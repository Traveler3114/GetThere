using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
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

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
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
})
.AddRoles<IdentityRole<int>>()
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
        RoleClaimType = "role"
    };
});

// Authorization Policies
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

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
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
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store";
        }
    }
});
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

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
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

    // Ensure roles exist
    if (!await roleManager.RoleExistsAsync(RoleNames.Admin))
        await roleManager.CreateAsync(new IdentityRole<int>(RoleNames.Admin));
    if (!await roleManager.RoleExistsAsync(RoleNames.Client))
        await roleManager.CreateAsync(new IdentityRole<int>(RoleNames.Client));

    // Add permission claims to Admin role (all permissions)
    var adminRole = await roleManager.FindByNameAsync(RoleNames.Admin);
    var adminClaims = await roleManager.GetClaimsAsync(adminRole!);
    foreach (var perm in PermissionKeys.All.Where(p => !adminClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(adminRole!, new Claim("permission", perm));

    // Add permission claims to Client role (all .view permissions)
    var clientRole = await roleManager.FindByNameAsync(RoleNames.Client);
    var clientClaims = await roleManager.GetClaimsAsync(clientRole!);
    foreach (var perm in PermissionKeys.All.Where(p => p.EndsWith(".view") && !clientClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(clientRole!, new Claim("permission", perm));

    // Admin user
    var admin = await userManager.FindByNameAsync("admin@transit.local");
    if (admin is null)
    {
        var pwd = GenerateSecurePassword(24);
        admin = new AppUser { UserName = "admin@transit.local", Email = "admin@transit.local", FullName = "Transit Admin" };
        await userManager.CreateAsync(admin, pwd);
        await userManager.AddToRoleAsync(admin, RoleNames.Admin);
        Console.WriteLine("=== ADMIN ACCOUNT CREATED ===");
        Console.WriteLine($"Email: admin@transit.local");
        Console.WriteLine($"Password: {pwd}");
        Console.WriteLine("=== SAVE THIS PASSWORD ===");
    }

    // Service account for GetThereAPI
    var client = await userManager.FindByNameAsync("getthere-api");
    if (client is null)
    {
        var pwd = GenerateSecurePassword(32);
        client = new AppUser { UserName = "getthere-api", Email = "getthere-api@transit.local", FullName = "GetThere API Client" };
        await userManager.CreateAsync(client, pwd);
        await userManager.AddToRoleAsync(client, RoleNames.Client);
        Console.WriteLine("=== SERVICE ACCOUNT CREATED ===");
        Console.WriteLine($"Username: getthere-api");
        Console.WriteLine($"Password: {pwd}");
        Console.WriteLine("=== ADD TO GETTHEREAPI CONFIG ===");
    }
}

static string GenerateSecurePassword(int length)
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
    var bytes = RandomNumberGenerator.GetBytes(length);
    var result = new char[length];
    for (int i = 0; i < length; i++) result[i] = chars[bytes[i] % chars.Length];
    return new string(result);
}

await app.RunAsync();