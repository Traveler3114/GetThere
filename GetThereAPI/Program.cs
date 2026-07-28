using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;

using GetThereAPI.Common;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Managers;
using GetThereAPI.Sdk;
using GetThereAPI.Services;
using GetThereAPI.Services.Extraction;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == "CHANGE-ME" || Encoding.UTF8.GetBytes(jwtKey).Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be configured and at least 32 characters long. " +
        "Run: dotnet user-secrets set \"Jwt:Key\" \"<64-char-key>\" --project GetThereAPI/GetThereAPI.csproj");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddControllers();
// SizeLimit makes the cache bounded rather than "however much the map asks for": map reads are keyed
// by viewport, so a user panning around produces an unbounded set of distinct keys. Every entry must
// therefore declare a Size — both consumers (MapManager, DynamicClaimsTransformation) use 1, so this
// is an entry count.
builder.Services.AddMemoryCache(options => options.SizeLimit = 2_000);
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddHttpClient<TransitInfoApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["TransitInfoApi:BaseUrl"] ?? "https://localhost:5001");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.Configure<TransitInfoApiOptions>(builder.Configuration.GetSection("TransitInfoApi"));

builder.Services.AddSingleton<AdapterRegistry>();
builder.Services.AddHostedService<TicketExpiryWorker>();

// Settles purchases that were debited but never resolved. See PurchaseReconciliationWorker for why
// that state exists at all: the wallet debit is committed before the operator is called.
builder.Services.AddHostedService<PurchaseReconciliationWorker>();

// Ticket file import. Swapping local disk for object storage later means replacing the
// ITicketFileStore registration; replacing the no-op scanner enforces real malware scanning.
// Everything here is stateless, hence singleton.
builder.Services.AddSingleton<ITicketFileStore, LocalTicketFileStore>();
builder.Services.AddSingleton<ITicketFileScanner, NoOpTicketFileScanner>();
builder.Services.AddSingleton<BarcodeDecoder>();
builder.Services.AddSingleton<ITicketExtractor, PkPassTicketExtractor>();
builder.Services.AddSingleton<ITicketExtractor, PdfTicketExtractor>();
builder.Services.AddSingleton<ITicketExtractor, ImageTicketExtractor>();
builder.Services.AddSingleton<ITicketExtractor, ICalTicketExtractor>();
builder.Services.AddSingleton<TicketExtractorRegistry>();

var managerTypes = typeof(Program).Assembly.GetTypes()
    .Where(t => t.Namespace == "GetThereAPI.Managers" && t is { IsClass: true, IsAbstract: false });
foreach (var mt in managerTypes)
    builder.Services.AddScoped(mt);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        NameClaimType = "given_name",
        RoleClaimType = "role"
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole(RoleNames.Admin));

    foreach (var perm in PermissionKeys.All)
    {
        options.AddPolicy(perm, p => p.RequireAssertion(ctx =>
            ctx.User.IsInRole(RoleNames.Admin) ||
            ctx.User.HasClaim("permission", perm)));
    }
});

builder.Services.AddTransient<IClaimsTransformation, DynamicClaimsTransformation>();
builder.Services.AddHttpContextAccessor();

// Behind a reverse proxy, Connection.RemoteIpAddress is the proxy's address — every caller would
// otherwise share a single rate-limit partition. KnownNetworks/KnownProxies are cleared because the
// proxy address is not known at build time; only enable this where a trusted proxy terminates TLS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(limiter =>
{
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    limiter.AddFixedWindowLimiter("Auth", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Each ticket upload can carry 10 MB and costs image decoding, barcode scanning and PDF
    // parsing, so the global 100/minute allowance is far too loose for this endpoint.
    limiter.AddFixedWindowLimiter("Upload", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    limiter.RejectionStatusCode = 429;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("MapAssets", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var error = context.Features.Get<IExceptionHandlerFeature>();
        var isDev = app.Environment.IsDevelopment();

        if (error is not null)
        {
            logger.LogError(error.Error, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }

        context.Response.ContentType = "application/problem+json";

        var statusCode = 500;
        var title = "Internal Server Error";

        if (error?.Error is AppException appEx)
        {
            statusCode = appEx.StatusCode;
            title = appEx.ErrorCode ?? appEx.Message;
        }

        if (isDev && error?.Error is not null && error.Error is not AppException)
        {
            title = $"Unexpected error ({error.Error.GetType().Name}): {error.Error.Message}";
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title,
            status = statusCode
        });
    });
});

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseResponseCompression();

// The permissive policy exists so the map assets can be embedded cross-origin; it is scoped to
// /map rather than applied globally, so it never widens access to the authenticated API surface.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/map"),
    branch => branch.UseCors("MapAssets"));

// The /admin console is served as plain static files. It deliberately carries no authorization
// gate: authentication here is bearer-token based, and a browser navigation to an .html file
// cannot send an Authorization header — a gate on these paths 401s the login page itself and
// makes the console unreachable. The console holds no secrets; every byte of data it renders
// comes from API endpoints that are authorized per-endpoint (see AdminController).
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (!ctx.Context.Request.Path.StartsWithSegments("/admin")) return;

        var headers = ctx.Context.Response.Headers;
        headers["X-Robots-Tag"] = "noindex, nofollow";

        // The console renders operator-supplied text; a CSP is the backstop if an escaping bug slips
        // through. 'unsafe-inline' is required because the pages carry inline <script> and style
        // blocks — remove it once those move to files.
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
    }
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })).AllowAnonymous();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

    // Ensure roles exist
    foreach (var roleName in new[] { RoleNames.Admin, RoleNames.User })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole(roleName));
    }

    // Add permission claims to Admin role (all permissions)
    var adminRole = await roleManager.FindByNameAsync(RoleNames.Admin);
    var adminClaims = await roleManager.GetClaimsAsync(adminRole!);
    foreach (var perm in PermissionKeys.All.Where(p => !adminClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(adminRole!, new System.Security.Claims.Claim("permission", perm));

    // Add permission claims to User role (standard user permissions)
    var userRole = await roleManager.FindByNameAsync(RoleNames.User);
    var userClaims = await roleManager.GetClaimsAsync(userRole!);
    foreach (var perm in PermissionKeys.UserRoleDefaults.Where(p => !userClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(userRole!, new System.Security.Claims.Claim("permission", perm));

    // Seed admin user
    var admin = await userManager.FindByNameAsync("admin@getthere.local");
    if (admin is null)
    {
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var configuredPassword = app.Configuration["Seed:AdminPassword"];

        // Outside Development the password must be supplied through configuration. Generating one
        // and dropping it on disk in plaintext leaves a credential at rest on every deployment.
        if (string.IsNullOrWhiteSpace(configuredPassword) && !app.Environment.IsDevelopment())
        {
            startupLogger.LogWarning(
                "No admin account exists and Seed:AdminPassword is not configured — skipping admin seed. " +
                "Set it via user-secrets or the environment to create admin@getthere.local.");
        }
        else
        {
            var pwd = configuredPassword ?? GenerateSecurePassword(24);
            admin = new AppUser { UserName = "admin@getthere.local", Email = "admin@getthere.local", FullName = "GetThere Admin" };
            await userManager.CreateAsync(admin, pwd);
            await userManager.AddToRoleAsync(admin, RoleNames.Admin);

            if (configuredPassword is null)
            {
                // Development only — the generated password has to reach the developer somehow.
                var credFile = Path.Combine(AppContext.BaseDirectory, ".admin-credentials");
                await File.WriteAllTextAsync(credFile,
                    $"Email: admin@getthere.local\nPassword: {pwd}\n");
                Console.WriteLine($"Admin account created. Credentials saved to: {credFile}");
            }
            else
            {
                startupLogger.LogInformation("Admin account created from Seed:AdminPassword.");
            }
        }
    }
}

app.Run();

static string GenerateSecurePassword(int length)
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
    var result = new char[length];
    for (int i = 0; i < length; i++) result[i] = chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)];
    return new string(result);
}
