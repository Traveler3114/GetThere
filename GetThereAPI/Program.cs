using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;
using GetThereAPI.Transit;
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
builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection("Otp"));

// ── Transit provider stack ──────────────────────────────────────────────────
builder.Services.AddScoped<OtpClient>();
builder.Services.AddScoped<ITransitProvider, OtpTransitProvider>();
builder.Services.AddScoped<ITransitRouter, TransitRouter>();
builder.Services.AddScoped<TransitOrchestrator>();

// ── Singletons (in-memory cache — must NOT be scoped) ─────────────────────
builder.Services.AddSingleton<MobilityManager>();

// Starts background polling loops when the server starts
builder.Services.AddHostedService(sp => sp.GetRequiredService<MobilityManager>());

// ── All other managers (scoped — auto-registered by reflection) ───────────
var managerTypes = Assembly.GetExecutingAssembly()
    .GetTypes()
    .Where(t => t.Namespace == "GetThereAPI.Managers"
                && t.IsClass
                && !t.IsAbstract
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

// ── Non-blocking background initialization ───────────────────────────────
// We fire and forget these tasks so the server starts listening INSTANTLY.
// Data will populate in the background over the first few seconds.
_ = Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("[Startup] Background initialization started...");

    try
    {
        // Pre-fetch bike stations
        var mobilityManager = scope.ServiceProvider.GetRequiredService<MobilityManager>();
        await mobilityManager.InitialiseAsync();

        logger.LogInformation("[Startup] Background initialization completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Startup] Background initialization failed.");
    }
});

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
