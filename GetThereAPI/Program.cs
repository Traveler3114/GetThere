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

using GetThereShared.Extraction;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using Scalar.AspNetCore;

using SharedAuth;

var builder = WebApplication.CreateBuilder(args);

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == "CHANGE-ME" || Encoding.UTF8.GetBytes(jwtKey).Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be configured and at least 32 characters long. " +
        "Run: dotnet user-secrets set \"Jwt:Key\" \"<64-char-key>\" --project GetThereAPI/GetThereAPI.csproj");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddControllers();
// SizeLimit keeps the cache bounded. It was sized for the map proxy, whose reads were keyed by
// viewport and so produced an unbounded set of distinct keys as a user panned; that proxy is gone
// and DynamicClaimsTransformation is the only consumer left. Every entry must still declare a Size,
// and it uses 1, so this is an entry count.
builder.Services.AddMemoryCache(options => options.SizeLimit = 2_000);
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Liveness answers "is this process running", readiness answers "can it serve a request". They must
// be separate: the old single /health returned 200 as long as the process was up, so an instance
// whose database was unreachable still looked healthy and a load balancer kept routing to it.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

// Inert unless Otel:Endpoint is configured. See SharedAuth.TelemetryRegistration.
builder.Services.AddSharedTelemetry(builder.Configuration, "GetThereAPI");

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
// Constructed by hand rather than by type, because the decoder moved to GetThereShared and no
// longer takes an ILogger — a shared library that also runs inside a MAUI app should not force a
// logging abstraction onto a phone for four warning lines. It takes a delegate, and this is where
// the server's logger gets attached to it.
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("BarcodeDecoder");
    return new BarcodeDecoder((message, error) =>
    {
        if (error is null) logger.LogInformation("{Message}", message);
        else logger.LogWarning(error, "{Message}", message);
    });
});
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

// Configurable rather than compiled in, with the previous values as defaults. A deployment behind a
// gateway that already throttles may want different numbers, and an in-process test host has every
// request arriving on one partition, so a 10/minute auth window rejects the fixture's own logins.
var globalPermitLimit = builder.Configuration.GetValue("RateLimits:GlobalPerMinute", 100);
var authPermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10);
var uploadPermitLimit = builder.Configuration.GetValue("RateLimits:UploadPerMinute", 10);

builder.Services.AddRateLimiter(limiter =>
{
    // Partition on the authenticated user first, and only fall back to the address for anonymous
    // callers. Keying everything on the address puts every subscriber behind one carrier-grade NAT
    // into a single 100/minute bucket, so a busy cell tower throttles real users who have done
    // nothing wrong. An authenticated caller is identifiable, so it gets its own allowance.
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.FindFirst(JwtClaimTypes.UserId)?.Value;

        var partitionKey = userId is not null
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    limiter.AddFixedWindowLimiter("Auth", opt =>
    {
        opt.PermitLimit = authPermitLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Each ticket upload can carry 10 MB and costs image decoding, barcode scanning and PDF
    // parsing, so the global 100/minute allowance is far too loose for this endpoint.
    limiter.AddFixedWindowLimiter("Upload", opt =>
    {
        opt.PermitLimit = uploadPermitLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    limiter.RejectionStatusCode = 429;
});

// CORS is deliberately not configured. The one policy that existed, "MapAssets", was an
// allow-any-origin rule scoped to /map so the map page could be embedded cross-origin; that page is
// served by TransitInfoAPI now and this API hosts no browser asset that a foreign origin reads. The
// admin console is same-origin.

builder.Services.AddResponseCompression(options =>
{
    // Without this the whole registration is inert in every environment that matters: it defaults
    // to false, UseHttpsRedirection sends every caller to https, and so nothing was ever compressed
    // outside a plain-http local run.
    //
    // The default exists because of BREACH, which needs a secret in the response body and an
    // attacker able to inject into the same body. Neither holds here: these responses carry the
    // caller's own data or public transit reference data, and the API is stateless bearer-token
    // authentication with no CSRF token or session cookie reflected anywhere in a payload.
    options.EnableForHttps = true;

    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Register a mock ticketing adapter so the purchasable-now path works end-to-end without a real
    // operator integration or payment provider. Development only — production purchases require a real
    // registered adapter (an unregistered one returns 503 by design).
    app.Services.GetRequiredService<GetThereAPI.Sdk.AdapterRegistry>()
        .Register(GetThereAPI.Sdk.MockTicketingAdapter.Type, new GetThereAPI.Sdk.MockTicketingAdapter());
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
        // /map used to be served from here too, with its own CSP allowing the MapLibre CDN and the
        // tile origin. That page belongs to TransitInfoAPI now, and so does its policy.
        if (!ctx.Context.Request.Path.StartsWithSegments("/admin")) return;

        var headers = ctx.Context.Response.Headers;
        headers["X-Robots-Tag"] = "noindex, nofollow";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";

        // Revalidate every console asset. Without this the only freshness signals are ETag and
        // Last-Modified, which a browser may skip checking, so a shipped change to admin.js or
        // style.css reaches an open tab only on a hard refresh. "no-cache" still permits caching —
        // it requires a conditional request first, which the ETag answers with a cheap 304.
        // TransitInfoAPI's console carries the same header for the same reason.
        headers.CacheControl = "no-cache";

        // The console renders operator-supplied text; a CSP is the backstop if an escaping bug slips
        // through. script-src no longer allows 'unsafe-inline': every page's script now lives in its
        // own file and none carries an inline handler, so injected script cannot execute even if
        // something unescaped reaches the DOM. That matters here because the refresh token is held
        // in sessionStorage, which any executing script could read.
        //
        // style-src keeps 'unsafe-inline' for the handful of style attributes still in the markup.
        // An injected style is a defacement risk, not a token-theft one.
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    }
});

app.UseAuthentication();

// After authentication, deliberately: the limiter partitions on the caller's user id when there is
// one, and context.User is not populated until the authentication middleware has run. Ordered the
// other way the claim is always absent and every authenticated caller silently falls back to being
// bucketed by IP address, which is the behaviour this partitioning exists to avoid.
app.UseRateLimiter();

app.UseAuthorization();

// Kept for anything already pointing at it, and still a pure liveness answer.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })).AllowAnonymous();

// Liveness: the process is up and the pipeline responds. No dependency is consulted, so a restart
// loop cannot be caused by a database blip.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Readiness: everything this instance needs in order to serve. Fails while the database is
// unreachable, so a load balancer stops routing to an instance that cannot answer.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapControllers();

// Seeding writes roles, permission claims and an admin account on every boot. That is fine for a
// single instance owning its database and wrong for anything else: scaled out, every instance races
// the others through the same writes on every deploy. Enabled by default so local and Development
// runs are unchanged; set Seed:Enabled=false on instances that must not do it.
if (app.Configuration.GetValue("Seed:Enabled", true))
{
    using var scope = app.Services.CreateScope();
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
            var pwd = configuredPassword ?? SeedPasswordGenerator.Generate(24);
            admin = new AppUser { UserName = "admin@getthere.local", Email = "admin@getthere.local", FullName = "GetThere Admin" };

            // Checked, because a Seed:AdminPassword that misses the 12-character/digit/upper/symbol
            // policy fails here silently: the account is never created, AddToRoleAsync then runs
            // against an unpersisted user, and the credentials file below advertises a login that
            // does not exist.
            var createResult = await userManager.CreateAsync(admin, pwd);
            if (!createResult.Succeeded)
            {
                startupLogger.LogError(
                    "Could not create the seed admin account: {Errors}. Check that Seed:AdminPassword meets the password policy.",
                    string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }
            else
            {
                var roleResult = await userManager.AddToRoleAsync(admin, RoleNames.Admin);
                if (!roleResult.Succeeded)
                {
                    startupLogger.LogError(
                        "Seed admin account was created but could not be given the {Role} role: {Errors}",
                        RoleNames.Admin, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
                }

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
}

app.Run();

