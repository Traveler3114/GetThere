using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Reflection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ── Database ──────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Identity ──────────────────────────────────────────────────────────────
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddHttpClient();

// ── Singletons (in-memory cache — must NOT be scoped) ─────────────────────
// Registered before the reflection loop so the loop skips them.
builder.Services.AddSingleton<StaticDataManager>();
builder.Services.AddSingleton<RealtimeManager>();
builder.Services.AddSingleton<MobilityManager>();

// Starts background polling loops when the server starts
builder.Services.AddHostedService(sp => sp.GetRequiredService<RealtimeManager>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MobilityManager>());

// ── All other managers (scoped — auto-registered by reflection) ───────────
var managerTypes = Assembly.GetExecutingAssembly()
    .GetTypes()
    .Where(t => t.Namespace == "GetThereAPI.Managers"
                && t.IsClass
                && !t.IsAbstract
                && t != typeof(StaticDataManager)   // already registered as singleton
                && t != typeof(RealtimeManager)     // already registered as singleton
                && t != typeof(MobilityManager));   // already registered as singleton

foreach (var managerType in managerTypes)
{
    builder.Services.AddScoped(managerType);
}

// ── JWT Authentication ────────────────────────────────────────────────────
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
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});

// ── CORS — allows the MAUI WebView to fetch map icons from this API ────────
// WebView2 (Windows) and Android WebView have unpredictable or null Origins,
// so we allow any origin specifically for the image assets endpoint.
// All other endpoints are protected by JWT so this is safe.
builder.Services.AddCors(options =>
{
    options.AddPolicy("MapAssets", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// ─────────────────────────────────────────────────────────────────────────
var app = builder.Build();
// ─────────────────────────────────────────────────────────────────────────

// ── Load GTFS data for all operators on startup ───────────────────────────
// Must run after app.Build() so the DI container is ready,
// but before app.Run() so data is in memory before first request arrives.
using (var scope = app.Services.CreateScope())
{
    var manager = scope.ServiceProvider.GetRequiredService<OperatorManager>();
    await manager.InitialiseAsync();
}

// ── Pre-fetch bike stations on startup ────────────────────────────────────
// MobilityManager is a singleton BackgroundService; calling InitialiseAsync()
// here ensures stations are cached before the first HTTP request arrives,
// matching the pattern used for OperatorManager above.
await app.Services.GetRequiredService<MobilityManager>().InitialiseAsync();

// ── Middleware pipeline ───────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("MapAssets");
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseAuthentication();   // "Who are you?"    — validates the JWT
app.UseAuthorization();    // "Are you allowed?" — checks [Authorize] attributes
app.MapControllers();

app.Run();

// https://localhost:7230/scalar/v1