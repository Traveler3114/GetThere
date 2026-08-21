using System.Security.Claims;
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
// Bounded, matching GetThereAPI. An unbounded IMemoryCache has no eviction pressure at all: it grows
// until the GC's memory-pressure heuristics happen to trim it, which is not a policy. Both consumers
// here — ScheduleManager's service-calendar sets and SharedAuth's claims cache — already declare
// Size = 1 on their entries in anticipation of this, so the limit is an entry count.
builder.Services.AddMemoryCache(options => options.SizeLimit = 2_000);
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

// Both clients fetch operator-supplied URLs, so both connect through the SSRF guard.
//
// The guard lives on the handler rather than only in front of the call because a pre-flight DNS
// check cannot close the window between resolving a name and connecting to it — see
// ExternalFeedSource.ConnectToPublicOnlyAsync. Putting it here also covers redirect hops, which the
// pre-flight check could only catch after the response had already been fetched.
//
// Feeds:AllowPrivateNetworkUrls is the development escape hatch, and it has to be honoured here as
// well as in the callers, or a locally hosted GTFS zip becomes unreachable.
var allowPrivateFeedNetworks = builder.Configuration.GetValue("Feeds:AllowPrivateNetworkUrls", false);

static void ConfigureFeedHandler(IHttpClientBuilder http, bool allowPrivateNetworks, bool followRedirects = true) =>
    http.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = followRedirects,
        ConnectCallback = allowPrivateNetworks
            ? null
            : TransitInfoAPI.Services.ExternalFeedSource.ConnectToPublicOnlyAsync
    });

ConfigureFeedHandler(builder.Services.AddHttpClient("gtfs", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
}), allowPrivateFeedNetworks);

ConfigureFeedHandler(builder.Services.AddHttpClient("gtfsrt", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
}), allowPrivateFeedNetworks);

// Feeds whose host carries a Feeds:BasicAuth credential (the Croatian NAP). Redirects are followed
// by hand in ExternalFeedSource.FetchWithBasicAuthAsync so the credential never leaves that host,
// which requires the handler not to auto-follow — the same arrangement as "customsource".
ConfigureFeedHandler(builder.Services.AddHttpClient("gtfs-basic", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
}), allowPrivateFeedNetworks, followRedirects: false);

// Custom sources are the one feed path that carries an operator's own credential, and this client is
// separate from "gtfs" for exactly that reason: AllowAutoRedirect is off.
//
// It has to be off. SocketsHttpHandler follows redirects itself, inside SendAsync, before any code
// here sees a response — and while HttpClient does strip Authorization when a redirect crosses
// origins, it strips nothing else. CustomSourceEngine.ApplyAuth's `header` mode sets an arbitrary
// header name, typically an API key, so an auto-followed redirect handed that key to wherever it
// pointed. Checking the final URI afterwards, which is what this used to do, refuses the data but
// cannot un-send the credential.
//
// With redirects off, CustomSourceEngine follows them itself and decides per hop whether the
// credential goes along. See SendFollowingRedirectsAsync.
//
// Two minutes rather than the "gtfs" client's ten: these are pages of rows, not GTFS archives, and
// the timeout is per request across up to MaxPages of them.
ConfigureFeedHandler(builder.Services.AddHttpClient("customsource", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
}), allowPrivateFeedNetworks, followRedirects: false);

builder.Services.Configure<FeedPollingOptions>(builder.Configuration.GetSection("FeedPolling"));
builder.Services.Configure<FeedImportOptions>(builder.Configuration.GetSection("FeedImport"));
builder.Services.Configure<RealtimePollingOptions>(builder.Configuration.GetSection("RealtimePolling"));
builder.Services.Configure<PlaceMatchingOptions>(builder.Configuration.GetSection("PlaceMatching"));
builder.Services.Configure<TransitInfoAPI.Routing.RoutingOptions>(builder.Configuration.GetSection(TransitInfoAPI.Routing.RoutingOptions.SectionName));

builder.Services.AddScoped<TransitInfoAPI.Services.GtfsParser>();
builder.Services.AddScoped<TransitInfoAPI.Routing.Export.GtfsBundleExporter>();
builder.Services.AddScoped<TransitInfoAPI.Routing.Export.GbfsExporter>();
builder.Services.AddScoped<TransitInfoAPI.Routing.Export.GtfsRealtimeExporter>();
builder.Services.AddScoped<TransitInfoAPI.Routing.GbfsRefreshService>();
builder.Services.AddSingleton<TransitInfoAPI.Routing.RoutingExportCache>();
builder.Services.AddSingleton<TransitInfoAPI.Routing.GraphRebuildSignal>();
builder.Services.AddSingleton<TransitInfoAPI.Routing.IGraphRebuildSignal>(sp => sp.GetRequiredService<TransitInfoAPI.Routing.GraphRebuildSignal>());
builder.Services.AddHostedService<TransitInfoAPI.Routing.RoutingRebuildWorker>();
builder.Services.AddHttpClient("otp", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<TransitInfoAPI.Routing.OsmExtractDownloader>();
builder.Services.AddHostedService<TransitInfoAPI.Routing.OsmExtractRefreshWorker>();
builder.Services.AddHttpClient("osm-extract", client =>
{
    // Extract downloads stream to disk; give the slowest Geofabrik night a generous ceiling.
    client.Timeout = TimeSpan.FromHours(1);
});
builder.Services.AddScoped<TransitInfoAPI.Routing.Otp.OtpGraphQlClient>();
builder.Services.Configure<GeocodingOptions>(builder.Configuration.GetSection("Geocoding"));
builder.Services.AddHttpClient("azuremaps", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<GeocodingManager>();
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

// The operator catalog seeder — idempotent, gated by Seed:Operators (see the seed block below).
builder.Services.AddScoped<TransitInfoAPI.Data.TransitDataSeeder>();

// Encrypts the operator credentials on CustomSource.AuthConfig, which were previously written to
// the column in plaintext and returned over the API. See TransitInfoAPI.Services.SecretProtector —
// note its warning about key-ring persistence before scaling this service out.
builder.Services.AddDataProtection();
builder.Services.AddSingleton<TransitInfoAPI.Services.SecretProtector>();

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
var allowAnonymousExport = builder.Configuration.GetValue("Routing:AllowAnonymousExport", false);
builder.Services.AddAuthorization(options =>
{
    foreach (var perm in PermissionKeys.All)
    {
        options.AddPolicy(perm, p => p.RequireAssertion(ctx =>
            ctx.User.IsInRole(RoleNames.Admin) ||
            ctx.User.HasClaim("permission", perm)));
    }

    // The routing export endpoints are polled by OTP, which is not internet-exposed. They are
    // authenticated by default, but Routing:AllowAnonymousExport opens them for a local/isolated OTP
    // that cannot present a token. Never enable it on a public deployment — the bundle aggregates
    // feeds whose licences may forbid redistribution (ROADMAP Phase 7).
    options.AddPolicy("RoutingExport", p => p.RequireAssertion(ctx =>
        allowAnonymousExport || ctx.User.Identity?.IsAuthenticated == true));
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

    // Geocoding hits a paid Azure Maps quota per call, so it gets its own tighter per-caller window
    // on top of the global limiter.
    var geocodePermitLimit = builder.Configuration.GetValue("RateLimits:GeocodePerMinute", 20);
    limiter.AddPolicy("Geocode", context =>
    {
        var userId = context.User.FindFirst("sub")?.Value;
        var partitionKey = userId is not null
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = geocodePermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    limiter.RejectionStatusCode = 429;
});

// CORS is intentionally not configured — all browser consumers (admin UI, map)
// are served from the same origin. Server-to-server callers don't need CORS.

builder.Services.AddResponseCompression(options =>
{
    // See GetThereAPI's identical note: this defaults to false, so with UseHttpsRedirection in the
    // pipeline nothing was ever compressed. It matters more here than there — the anonymous station
    // and route GeoJSON reads return up to 5000 features each, straight to the public map page.
    options.EnableForHttps = true;

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

        // A client that disconnects mid-request (the map cancelling an in-flight /routes fetch when it
        // reloads or pans) cancels RequestAborted; EF/SqlClient surface that as an
        // OperationCanceledException, or from SQL Server as a SqlException "Operation cancelled by
        // user". The caller is already gone, so this is not a server fault — log it at debug and
        // return 499 ("client closed request") without a body, rather than dumping a 500 stack trace.
        // Writing to an aborted connection would just throw again, so skip the response body.
        if (context.RequestAborted.IsCancellationRequested)
        {
            app.Logger.LogDebug(ex, "Request aborted by client");
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
            return;
        }

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

// Transport and static assets before authentication, matching GetThereAPI.
//
// The order here used to be the other way round — authentication, rate limiting and authorization
// all ran, and only then did the pipeline decide to redirect the caller to https. That meant a
// bearer token presented over plain http was parsed, validated and used to look up claims before
// the request was told to go away and come back over TLS, and every static asset was fetched
// through the whole auth stack for nothing.
//
// Guarded by environment, matching GetThereAPI. Sending HSTS from a Development run pins localhost
// to HTTPS in the developer's browser for the max-age, which then breaks every other local project
// served over plain HTTP on the same host.
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Also guarded by environment: a Development run serves over plain HTTP so local server-to-server
// callers (the OTP updaters polling routing/*) reach the endpoints without a 307 to a self-signed
// HTTPS cert they cannot verify. Production still redirects to HTTPS.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseResponseCompression();

// The /admin console is served as plain static files. It deliberately carries no authorization
// gate: authentication here is bearer-token based, and a browser navigation to an .html file
// cannot send an Authorization header — a gate on these paths 401s the login page itself and
// makes the console unreachable. The console holds no secrets; every byte of data it renders
// comes from API endpoints that are authorized per-endpoint.
// The map has a single page, public.html — the legacy index.html/index.js map was removed. Add it
// as a default document so /map/ serves it; index.html still wins for /admin and the site root, which
// have their own.
var defaultFiles = new DefaultFilesOptions();
defaultFiles.DefaultFileNames.Add("public.html");
app.UseDefaultFiles(defaultFiles);
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
        // also wire behaviour through inline on* attributes and 'unsafe-inline' is what makes those
        // run. Dropping it before they are all converted to addEventListener would leave buttons
        // that silently do nothing.
        //
        // Recounted 2026-08-10: 56 in the markup and 156 more inside generated HTML strings, across
        // 17 pages — 212, not the 111 recorded here previously. The generated-string ones are the
        // awkward half: they are built inside render functions, so converting them means moving to
        // event delegation on the container rather than a mechanical find-and-replace.
        // operators.page.js and custom-sources.page.js already do this with data-* attributes and a
        // single delegated listener, and are the pattern to follow.
        //
        // Why this is worth finishing rather than living with: the console holds the operator's
        // refresh token in sessionStorage, which any executing script can read. So an escaping miss
        // anywhere in these pages — which render feed- and operator-supplied text — is not a
        // defacement, it is a full admin session handed over. The escaping itself is now in one
        // place (Shell.esc) rather than copy-pasted per page, which shrinks the surface but does not
        // close it.
        //
        // It needs the pages exercised against a populated database: a missed handler is invisible
        // until someone clicks it, and no test in this repo covers the console.
        // unpkg is no longer listed: it served MapLibre to the two admin map pages, which now load it
        // from wwwroot/vendor like everything else. jsdelivr stays for Bootstrap.
        //
        // shape-editor.html used to pull mapbox-gl-draw from https://api.mapbox.com, an origin this
        // policy has never allowed, so MapboxDraw was undefined and its drawing toolbar never worked.
        // It is now vendored under /vendor/mapbox-gl-draw and loads from 'self' like MapLibre.
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

    // The Croatian operator catalog: real-data feeds (Autotrolej, Šibenik) and gazetteer-backed
    // stops for the name-only operators. Idempotent by natural key, so a re-run repairs rather
    // than duplicates. Defaults to Development for the same reason as Seed:Enabled above; set
    // Seed:Operators=true on instances that should own the catalog.
    if (app.Configuration.GetValue<bool>("Seed:Operators", app.Environment.IsDevelopment()))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<TransitInfoAPI.Data.TransitDataSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }
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

    // Add permission claims to Client role (transit-data .view permissions).
    //
    // Narrowed from "every .view permission". Client is the read-only console role, and account
    // administration is not transit data: users.view returns the user list of this service, and
    // roles.view its permission model. Neither is something a read-only data account needs, and
    // granting them by a blanket suffix match meant any future *.view permission — on anything —
    // was handed to this role automatically the moment it was added to PermissionKeys.All.
    var clientRole = await roleManager.FindByNameAsync(RoleNames.Client);
    var clientClaims = await roleManager.GetClaimsAsync(clientRole!);
    string[] clientExcluded = [PermissionKeys.UsersView, PermissionKeys.RolesView];
    foreach (var perm in PermissionKeys.All.Where(p =>
                 p.EndsWith(".view", StringComparison.Ordinal)
                 && !clientExcluded.Contains(p)
                 && !clientClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(clientRole!, new Claim("permission", perm));

    // Removes the two claims from a role seeded before the narrowing above. Without this the grant
    // persists on every existing database, since the loop only ever adds.
    foreach (var stale in clientClaims.Where(c => c.Type == "permission" && clientExcluded.Contains(c.Value)))
        await roleManager.RemoveClaimAsync(clientRole!, stale);

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
            var pwd = configuredPassword ?? SeedPasswordGenerator.Generate(24);
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

    // The `getthere-api` service account that used to be seeded here is gone, deliberately.
    //
    // It existed for the map proxy: GetThereAPI authenticated to this service as that account and
    // proxied the map's reads. The proxy was removed on 2026-08-02 — the map page is served from
    // here and calls this service anonymously — and with it went TransitInfoApiClient,
    // MapProxyController, MapManager and the map.view permission. The seeding was missed, so every
    // boot kept re-creating a Client-role account with read access to the whole dataset that
    // nothing had called since. In Development it also wrote its password to
    // .service-account-credentials on disk.
    //
    // AGENTS.md records this as a regression to watch for: do not reintroduce it. GetThereAPI makes
    // no call to this service. Existing deployments should delete the row — see
    // docs/changelog.md for the query.
    //
    // Seed:ServiceAccountPassword is no longer read anywhere and can be removed from any
    // configuration that still sets it.
}


await app.RunAsync();
