using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders().AddConsole().AddDebug();

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == "CHANGE-ME" || Encoding.UTF8.GetBytes(jwtKey).Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be configured and at least 32 characters long. " +
        "Run: dotnet user-secrets set \"Jwt:Key\" \"<64-char-key>\" --project TransitInfoAPI/TransitInfoAPI.csproj");

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddDbContext<TransitDbContext>(options =>
    options.UseSqlServer(connectionString, x => x.UseNetTopologySuite().CommandTimeout(120)));

builder.Services.AddHttpClient("gtfs", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddHttpClient("gtfsrt", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.Configure<FeedPollingOptions>(builder.Configuration.GetSection("FeedPolling"));
builder.Services.Configure<FeedImportOptions>(builder.Configuration.GetSection("FeedImport"));
builder.Services.Configure<RealtimePollingOptions>(builder.Configuration.GetSection("RealtimePolling"));
builder.Services.Configure<PlaceMatchingOptions>(builder.Configuration.GetSection("PlaceMatching"));

builder.Services.AddScoped<TransitInfoAPI.Services.GtfsParser>();
builder.Services.AddSingleton<OnestopIdManager>();
builder.Services.AddScoped<ReconciliationManager>();
builder.Services.AddScoped<ScheduleManager>();
builder.Services.AddScoped<PlaceMatchingManager>();
builder.Services.AddScoped<MobilityManager>();
builder.Services.AddScoped<StationManager>();
builder.Services.AddScoped<RouteManager>();
builder.Services.AddScoped<OperatorManager>();
builder.Services.AddScoped<FeedManager>();
builder.Services.AddScoped<CountryManager>();
builder.Services.AddSingleton<TransitInfoAPI.Services.ImportLogStore>();
builder.Services.AddSingleton<RealtimeManager>();

builder.Services.AddSingleton<TransitInfoAPI.Services.ExternalFeedSource>();

// Auth Managers
builder.Services.AddScoped<TokenManager>();
builder.Services.AddScoped<AuthManager>();
builder.Services.AddScoped<RolePermissionManager>();

builder.Services.AddHostedService<RealtimePollingWorker>();
builder.Services.AddHostedService<FeedPollingWorker>();
builder.Services.AddHostedService<MobilityPollingWorker>();
builder.Services.Configure<MobilityPollingOptions>(builder.Configuration.GetSection("MobilityPolling"));

// Identity
builder.Services.AddIdentityCore<AppUser>(opt =>
{
    opt.Password.RequiredLength = 12;
    opt.Password.RequireDigit = true;
    opt.Password.RequireUppercase = true;
    opt.Password.RequireNonAlphanumeric = true;
    opt.User.RequireUniqueEmail = true;
    opt.Lockout.MaxFailedAccessAttempts = 5;
    opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    opt.Lockout.AllowedForNewUsers = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<TransitDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Authentication
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

// Authorization Policies
builder.Services.AddAuthorization(options =>
{
    foreach (var perm in PermissionKeys.All)
    {
        options.AddPolicy(perm, p => p.RequireAssertion(ctx =>
            ctx.User.IsInRole(RoleNames.Admin) ||
            ctx.User.HasClaim("permission", perm)));
    }
});

builder.Services.AddTransient<IClaimsTransformation, TransitInfoAPI.Services.DynamicClaimsTransformation>();
builder.Services.AddHttpContextAccessor();

// Behind a reverse proxy, Connection.RemoteIpAddress is the proxy's address — every caller would
// otherwise share a single rate-limit partition. KnownNetworks/KnownProxies are cleared because the
// proxy address is not known at build time; only enable this where a trusted proxy terminates TLS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
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
    limiter.RejectionStatusCode = 429;
});

// CORS is intentionally not configured — all browser consumers (admin UI, map)
// are served from the same origin. Server-to-server callers don't need CORS.

builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        app.Logger.LogError(ex, "Unhandled exception");
        var pd = new Microsoft.AspNetCore.Mvc.ProblemDetails();
        if (ex is TransitInfoAPI.Exceptions.AppException appEx)
        {
            pd.Status = appEx.StatusCode;
            pd.Title = appEx.ErrorCode ?? "Error";
            pd.Detail = ex.Message;
        }
        else
        {
            pd.Status = 500;
            pd.Title = "Internal Server Error";
            pd.Detail = "An unexpected error occurred.";
        }
        context.Response.StatusCode = pd.Status ?? 500;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(pd);
    });
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// The /admin console is served as plain static files. It deliberately carries no authorization
// gate: authentication here is bearer-token based, and a browser navigation to an .html file
// cannot send an Authorization header — a gate on these paths 401s the login page itself and
// makes the console unreachable. The console holds no secrets; every byte of data it renders
// comes from API endpoints that are authorized per-endpoint.
app.UseHsts();
app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store";
        }

        if (!ctx.Context.Request.Path.StartsWithSegments("/admin")) return;

        var headers = ctx.Context.Response.Headers;
        headers["X-Robots-Tag"] = "noindex, nofollow";

        // Backstop for the escaping in these pages, which render feed- and operator-supplied text.
        // Map tiles and the Bootstrap/MapLibre CDNs the legacy pages still use are allowed
        // explicitly; 'unsafe-inline' stays until the inline scripts move to files.
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://unpkg.com; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://unpkg.com; " +
            "img-src 'self' data: blob: https:; connect-src 'self' https:; worker-src 'self' blob:; " +
            "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
    }
});
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })).AllowAnonymous();

app.MapControllers();

// Seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
    await db.Database.MigrateAsync();

    var stuck = await db.FeedVersions
        .Where(fv => fv.ImportStatus == FeedImportStatus.Importing)
        .ToListAsync();
    var stuckIds = stuck.Select(v => v.Id).ToList();
    foreach (var version in stuck)
    {
        version.ImportStatus = FeedImportStatus.Failed;
        version.ImportError = "Import interrupted by application restart";
    }
    await db.SaveChangesAsync();

    if (stuckIds.Count > 0)
    {
        await db.StopTimes.Where(st => stuckIds.Contains(st.Trip.FeedVersionId)).ExecuteDeleteAsync();
        await db.RawStops.Where(rs => stuckIds.Contains(rs.FeedVersionId)).ExecuteDeleteAsync();
        await db.Trips.Where(t => stuckIds.Contains(t.FeedVersionId)).ExecuteDeleteAsync();
        await db.Calendars.Where(c => stuckIds.Contains(c.FeedVersionId)).ExecuteDeleteAsync();
        await db.CalendarDates.Where(cd => stuckIds.Contains(cd.FeedVersionId)).ExecuteDeleteAsync();
        await db.Shapes.Where(s => stuckIds.Contains(s.FeedVersionId)).ExecuteDeleteAsync();
    }

    // Seed roles and users
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    // Ensure roles exist
    if (!await roleManager.RoleExistsAsync(RoleNames.Admin))
        await roleManager.CreateAsync(new IdentityRole(RoleNames.Admin));
    if (!await roleManager.RoleExistsAsync(RoleNames.Client))
        await roleManager.CreateAsync(new IdentityRole(RoleNames.Client));

    // Add permission claims to Admin role (all permissions)
    var adminRole = await roleManager.FindByNameAsync(RoleNames.Admin);
    var adminClaims = await roleManager.GetClaimsAsync(adminRole!);
    foreach (var perm in PermissionKeys.All.Where(p => !adminClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(adminRole!, new Claim("permission", perm));

    // Add permission claims to Client role (all .view permissions)
    var clientRole = await roleManager.FindByNameAsync(RoleNames.Client);
    var clientClaims = await roleManager.GetClaimsAsync(clientRole!);
    foreach (var perm in PermissionKeys.All.Where(p => p.EndsWith(".view", StringComparison.Ordinal) && !clientClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(clientRole!, new Claim("permission", perm));

    // Admin user
    var admin = await userManager.FindByNameAsync("admin@transit.local");
    if (admin is null)
    {
        var configuredPassword = app.Configuration["Seed:AdminPassword"];

        // Outside Development the password must be supplied through configuration. Generating one
        // and dropping it on disk in plaintext leaves a credential at rest on every deployment.
        if (string.IsNullOrWhiteSpace(configuredPassword) && !app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning(
                "No admin account exists and Seed:AdminPassword is not configured — skipping admin seed. " +
                "Set it via user-secrets or the environment to create admin@transit.local.");
        }
        else
        {
            var pwd = configuredPassword ?? GenerateSecurePassword(24);
            admin = new AppUser { UserName = "admin@transit.local", Email = "admin@transit.local", FullName = "Transit Admin" };
            await userManager.CreateAsync(admin, pwd);
            await userManager.AddToRoleAsync(admin, RoleNames.Admin);

            if (configuredPassword is null)
            {
                // Development only — the generated password has to reach the developer somehow.
                var credFile = Path.Combine(AppContext.BaseDirectory, ".admin-credentials");
                await File.WriteAllTextAsync(credFile,
                    $"Email: admin@transit.local\nPassword: {pwd}\n");
                Console.WriteLine($"Admin account created. Credentials saved to: {credFile}");
            }
            else
            {
                app.Logger.LogInformation("Admin account created from Seed:AdminPassword.");
            }
        }
    }

    // Service account for GetThereAPI
    var client = await userManager.FindByNameAsync("getthere-api");
    if (client is null)
    {
        // Must match GetThereAPI's TransitInfoApi:ClientSecret, so it is configuration-driven
        // outside Development rather than generated and written to disk.
        var configuredSecret = app.Configuration["Seed:ServiceAccountPassword"];

        if (string.IsNullOrWhiteSpace(configuredSecret) && !app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning(
                "No service account exists and Seed:ServiceAccountPassword is not configured — skipping. " +
                "GetThereAPI will not be able to authenticate until it is created.");
        }
        else
        {
            var pwd = configuredSecret ?? GenerateSecurePassword(32);
            client = new AppUser { UserName = "getthere-api", Email = "getthere-api@transit.local", FullName = "GetThere API Client" };
            await userManager.CreateAsync(client, pwd);
            await userManager.AddToRoleAsync(client, RoleNames.Client);

            if (configuredSecret is null)
            {
                var svcCredFile = Path.Combine(AppContext.BaseDirectory, ".service-account-credentials");
                await File.WriteAllTextAsync(svcCredFile,
                    $"Username: getthere-api\nPassword: {pwd}\n");
                Console.WriteLine($"Service account created. Credentials saved to: {svcCredFile}");
            }
            else
            {
                app.Logger.LogInformation("Service account created from Seed:ServiceAccountPassword.");
            }
        }
    }
}

static string GenerateSecurePassword(int length)
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
    var result = new char[length];
    for (int i = 0; i < length; i++) result[i] = chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)];
    return new string(result);
}

await app.RunAsync();
