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

using SharedAuth;

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

// Liveness answers "is this process running", readiness answers "can it serve a request". They must
// be separate: the old single /health returned 200 as long as the process was up, so an instance
// whose database was unreachable still looked healthy and a load balancer kept routing to it.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<TransitDbContext>("database", tags: ["ready"]);

// Inert unless Otel:Endpoint is configured. See SharedAuth.TelemetryRegistration.
builder.Services.AddSharedTelemetry(builder.Configuration, "TransitInfoAPI");

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
// Missing here meant PlacesController could not be activated at all: every one of its four actions
// answered 500 from the DI container before reaching any code, so the admin Places page has never
// loaded. Nothing catches this at build time — the controller compiles fine.
builder.Services.AddScoped<PlaceManager>();
builder.Services.AddSingleton<TransitInfoAPI.Services.ImportLogStore>();
builder.Services.AddSingleton<RealtimeManager>();

builder.Services.AddSingleton<TransitInfoAPI.Services.ExternalFeedSource>();

// Scoped rather than singleton because GtfsParser is: registration order here decides which source
// claims a feed when more than one CanHandle it. CustomHttpSource is first because a feed carrying
// a CustomSourceId is a custom feed regardless of what else is set on it.
builder.Services.AddScoped<TransitInfoAPI.Core.ITransitSource, TransitInfoAPI.Services.CustomHttpSource>();
builder.Services.AddScoped<TransitInfoAPI.Core.ITransitSource, TransitInfoAPI.Services.GtfsZipSource>();
builder.Services.AddScoped<TransitInfoAPI.Core.TransitSourceResolver>();

// Register an ICustomExtractor to take over a source that configuration cannot describe.
builder.Services.AddScoped<TransitInfoAPI.Core.CustomExtractorRegistry>();
builder.Services.AddScoped<TransitInfoAPI.Services.CustomSourceEngine>();
builder.Services.AddScoped<TransitInfoAPI.Services.CustomHttpSource>();
builder.Services.AddScoped<TransitDocumentCompleter>();
builder.Services.AddScoped<CustomSourceManager>();

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

// Configurable rather than compiled in, matching GetThereAPI. An in-process test host has every
// request arriving on one partition, so a fixed 10/minute auth window rejects the fixture's own
// logins.
var globalPermitLimit = builder.Configuration.GetValue("RateLimits:GlobalPerMinute", 100);
var authPermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10);

builder.Services.AddRateLimiter(limiter =>
{
    // Partition on the authenticated caller first, falling back to the address only for anonymous
    // requests. Keying purely on the address put GetThereAPI's entire user base into a single
    // 100/minute bucket, because every map-proxy request reaches this service from one host: a
    // handful of people panning a map exhausted the window, and TransitInfoApiClient turns the
    // resulting 429 into "Transit information service is unavailable". This mirrors the
    // partitioning GetThereAPI already applies for the same reason.
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.FindFirst("sub")?.Value;

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

app.UseAuthentication();

// After authentication, deliberately: the limiter partitions on the caller's user id when there is
// one, and context.User is not populated until the authentication middleware has run. Ordered the
// other way the claim is always absent and every authenticated caller silently falls back to being
// bucketed by IP address, which is the behaviour this partitioning exists to avoid.
app.UseRateLimiter();

app.UseAuthorization();

// The /admin console is served as plain static files. It deliberately carries no authorization
// gate: authentication here is bearer-token based, and a browser navigation to an .html file
// cannot send an Authorization header — a gate on these paths 401s the login page itself and
// makes the console unreachable. The console holds no secrets; every byte of data it renders
// comes from API endpoints that are authorized per-endpoint.
// Guarded by environment, matching GetThereAPI. Sending HSTS from a Development run pins localhost
// to HTTPS in the developer's browser for the max-age, which then breaks every other local project
// served over plain HTTP on the same host.
if (!app.Environment.IsDevelopment())
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

        // The map pages here are the same pages GetThereAPI serves, and until now neither project
        // sent them any policy. They read an operator's session token out of sessionStorage, so an
        // escaping bug in the feed-supplied text they render reaches a credential. Their script now
        // lives in map/*.js with no inline handler, so script-src needs no 'unsafe-inline'.
        if (ctx.Context.Request.Path.StartsWithSegments("/map"))
        {
            var mapHeaders = ctx.Context.Response.Headers;
            mapHeaders["X-Robots-Tag"] = "noindex, nofollow";
            mapHeaders["X-Content-Type-Options"] = "nosniff";
            mapHeaders["Referrer-Policy"] = "no-referrer";
            // MapLibre is served from wwwroot/vendor now, so no external origin may execute script
            // here at all — the CDN that used to appear in script-src and style-src is gone. The
            // tile origin stays in img-src/connect-src: the basemap is still fetched from it.
            mapHeaders["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: blob: https://tiles.openfreemap.org; " +
                "connect-src 'self' https://tiles.openfreemap.org; " +
                // MapLibre builds its tile workers from blob URLs.
                "worker-src 'self' blob:; child-src 'self' blob:; " +
                "frame-ancestors 'self'; base-uri 'self'; form-action 'self'";
            return;
        }

        if (!ctx.Context.Request.Path.StartsWithSegments("/admin")) return;

        var headers = ctx.Context.Response.Headers;
        headers["X-Robots-Tag"] = "noindex, nofollow";

        // Revalidate every console asset. Without this the only freshness signals are ETag and
        // Last-Modified, which a browser is free to skip checking — so a shipped change to
        // admin-shell.js or style.css reaches a stale tab only on a hard refresh. That is not
        // hypothetical: a cached copy of admin-shell.js without Shell.mountLegacy made every legacy
        // page throw on mount. "no-cache" still allows caching, it just requires a conditional
        // request first, so the ETag above turns the usual case into a cheap 304.
        headers.CacheControl = "no-cache";

        // Backstop for the escaping in these pages, which render feed- and operator-supplied text.
        // Map tiles and the Bootstrap/MapLibre CDNs the legacy pages still use are allowed
        // explicitly.
        //
        // script-src still allows 'unsafe-inline', unlike GetThereAPI's console, which no longer
        // does. The inline <script> blocks have been moved into per-page .js files, but these pages
        // also wire behaviour through inline on* attributes — 48 in the markup and 63 more inside
        // generated HTML strings — and 'unsafe-inline' is what makes those run. Dropping it before
        // all 111 are converted to addEventListener would leave buttons that silently do nothing.
        // That conversion is the remaining work; it needs the pages exercised against a populated
        // database, since a missed handler is invisible until someone clicks it.
        // unpkg is no longer listed: it served MapLibre to the two admin map pages, which now load it
        // from wwwroot/vendor like everything else. jsdelivr stays for Bootstrap.
        //
        // Note that shape-editor.html also pulls mapbox-gl-draw from https://api.mapbox.com, an
        // origin this policy has never allowed — so that plugin is already blocked and its drawing
        // toolbar cannot be working. Vendoring it or allowing the origin is a separate decision.
        // font-src is listed explicitly because omitting it does not mean "unrestricted" — it falls
        // back to default-src 'self', which blocked the Bootstrap Icons webfont on jsdelivr. The
        // stylesheet loaded fine (style-src allows the CDN) and every one of the 59 <i class="bi">
        // glyphs in these pages rendered as nothing, with the failure visible only as a console
        // violation. Allowing the font here does not widen script execution: jsdelivr can already
        // serve script and style to this console.
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "font-src 'self' data: https://cdn.jsdelivr.net; " +
            "img-src 'self' data: blob: https:; connect-src 'self' https:; worker-src 'self' blob:; " +
            "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
    }
});
// Kept for anything already pointing at it, and still a pure liveness answer.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })).AllowAnonymous();

// Liveness: the process is up and the pipeline responds. No dependency is consulted, so a restart
// loop cannot be caused by a database blip.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Readiness: everything this instance needs in order to serve. Fails while the database is
// unreachable, so a load balancer stops routing to an instance that cannot answer.
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapControllers();

// Seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();

    // Applying migrations from application startup is a convenience for a single instance that owns
    // its database, and a hazard anywhere else: several instances coming up together race each other
    // through the same schema changes, and a destructive migration is applied by whichever pod
    // restarts first rather than by a reviewed deployment step. GetThereAPI never did this, so the
    // two services disagreed about how their schema arrives.
    //
    // Defaulting to Development keeps local runs working exactly as before. Set
    // Database:MigrateOnStartup=true to keep the old behaviour in a deployed environment, or run
    // `dotnet ef database update` as a deploy step (see docs/guides/ef-database-commands.md).
    if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
        await db.Database.MigrateAsync();

    // Discards the half-written data of any import the previous process died in the middle of.
    //
    // Both properties below are load-bearing and were both missing:
    //
    // 1. It is one transaction. The status write used to commit on its own and each delete ran in
    //    its own implicit transaction, so a failure partway through left a version marked Failed
    //    with its StopTimes already gone and everything else intact — which is exactly what
    //    happened to the active ZET version.
    //
    // 2. StopTimes are unlinked from the doomed RawStops first. The reconciliation backfill joins
    //    StopTimes to RawStops on the id *string* with no version scoping, so other versions' rows
    //    point at this version's stops; deleting them directly fails on
    //    FK_StopTimes_RawStops_RawStopEntityId. FeedManager.CleanupExistingDataAsync has always
    //    done this — this path never did.
    //
    // A failure here is logged and swallowed. Cleanup is housekeeping, and the previous behaviour
    // was an unhandled exception during startup, so the service crash-looped and could not be
    // started again until someone edited the database by hand.
    try
    {
        var stuckIds = await db.FeedVersions
            .Where(fv => fv.ImportStatus == FeedImportStatus.Importing)
            .Select(fv => fv.Id)
            .ToListAsync();

        if (stuckIds.Count > 0)
        {
            var recoveryLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("StuckImportRecovery");
            recoveryLogger.LogWarning("Discarding {Count} import(s) interrupted by an application restart: {Ids}",
                stuckIds.Count, string.Join(", ", stuckIds));

            await using var recoveryTx = await db.Database.BeginTransactionAsync();

            await db.FeedVersions
                .Where(fv => stuckIds.Contains(fv.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(fv => fv.ImportStatus, FeedImportStatus.Failed)
                    .SetProperty(fv => fv.ImportError, "Import interrupted by application restart"));

            await db.StopTimes.Where(st => stuckIds.Contains(st.Trip.FeedVersionId)).ExecuteDeleteAsync();

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE StopTimes SET RawStopEntityId = NULL, CanonicalStationId = NULL "
                + "WHERE RawStopEntityId IN (SELECT Id FROM RawStops WHERE FeedVersionId IN (SELECT value FROM STRING_SPLIT(@p0, ',')))",
                new object[] { string.Join(",", stuckIds) });

            await db.ReconciliationCandidates.Where(rc => stuckIds.Contains(rc.RawStop.FeedVersionId)).ExecuteDeleteAsync();
            await db.RawStops.Where(rs => stuckIds.Contains(rs.FeedVersionId)).ExecuteDeleteAsync();
            await db.Trips.Where(t => stuckIds.Contains(t.FeedVersionId)).ExecuteDeleteAsync();
            await db.Calendars.Where(c => stuckIds.Contains(c.FeedVersionId)).ExecuteDeleteAsync();
            await db.CalendarDates.Where(cd => stuckIds.Contains(cd.FeedVersionId)).ExecuteDeleteAsync();
            await db.Shapes.Where(s => stuckIds.Contains(s.FeedVersionId)).ExecuteDeleteAsync();
            await db.Agencies.Where(a => stuckIds.Contains(a.FeedVersionId)).ExecuteDeleteAsync();

            await recoveryTx.CommitAsync();
            recoveryLogger.LogInformation("Discarded {Count} interrupted import(s)", stuckIds.Count);
        }
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("StuckImportRecovery")
            .LogError(ex, "Could not discard interrupted imports. The service is starting anyway; "
                + "re-import the affected feeds to repair them.");
    }

    // Seed roles and users.
    //
    // Gated for the same reason as the migration above: scaled out, every instance races the others
    // through the same writes on every deploy. Enabled by default so local runs are unchanged; set
    // Seed:Enabled=false on instances that must not do it. The stuck-import recovery above stays
    // unconditional — it repairs this instance's own interrupted work and is safe to repeat.
    if (app.Configuration.GetValue("Seed:Enabled", true))
        await SeedIdentityAsync(app, scope);
}

static async Task SeedIdentityAsync(WebApplication app, IServiceScope scope)
{
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

            // Checked, because a configured password that misses the policy fails here silently:
            // no account is created, AddToRoleAsync runs against an unpersisted user, and the
            // credentials file below advertises a login that does not exist.
            var createResult = await userManager.CreateAsync(admin, pwd);
            if (!createResult.Succeeded)
            {
                app.Logger.LogError(
                    "Could not create the seed admin account: {Errors}. Check that Seed:AdminPassword meets the password policy.",
                    string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }
            else
            {
                var roleResult = await userManager.AddToRoleAsync(admin, RoleNames.Admin);
                if (!roleResult.Succeeded)
                {
                    app.Logger.LogError("Seed admin account could not be given the {Role} role: {Errors}",
                        RoleNames.Admin, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
                }

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

            // A silent failure here is the worst of the three: GetThereAPI cannot authenticate at
            // all, and every map read it proxies answers 502 with nothing in this service's log to
            // say why.
            var createResult = await userManager.CreateAsync(client, pwd);
            if (!createResult.Succeeded)
            {
                app.Logger.LogError(
                    "Could not create the getthere-api service account: {Errors}. GetThereAPI will not be able to authenticate.",
                    string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }
            else
            {
                var roleResult = await userManager.AddToRoleAsync(client, RoleNames.Client);
                if (!roleResult.Succeeded)
                {
                    app.Logger.LogError("Service account could not be given the {Role} role: {Errors}",
                        RoleNames.Client, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
                }

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
}

static string GenerateSecurePassword(int length)
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
    var result = new char[length];
    for (int i = 0; i < length; i++) result[i] = chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)];
    return new string(result);
}

await app.RunAsync();
