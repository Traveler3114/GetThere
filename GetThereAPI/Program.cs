using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Infrastructure;
using GetThereAPI.Managers;
using GetThereAPI.Parsers.Mobility;
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

builder.Services.AddScoped<OtpClient>();
builder.Services.AddScoped<ITransitProvider, OtpTransitProvider>();
builder.Services.AddScoped<ITransitRouter, TransitRouter>();
builder.Services.AddScoped<TransitOrchestrator>();

builder.Services.AddSingleton<NextbikeParser>();
builder.Services.AddKeyedSingleton<IMobilityParser>(MobilityFeedFormat.NEXTBIKE_API,
    (sp, _) => sp.GetRequiredService<NextbikeParser>());
builder.Services.AddKeyedSingleton<IMobilityParser>(MobilityFeedFormat.GBFS,
    (sp, _) => sp.GetRequiredService<NextbikeParser>());
builder.Services.AddKeyedSingleton<IMobilityParser>(MobilityFeedFormat.BOLT_API,
    (sp, _) => sp.GetRequiredService<NextbikeParser>());
builder.Services.AddKeyedSingleton<IMobilityParser>(MobilityFeedFormat.REST,
    (sp, _) => sp.GetRequiredService<NextbikeParser>());
builder.Services.AddSingleton<MobilityParserFactory>();

builder.Services.AddSingleton<MobilityManager>();
builder.Services.AddSingleton<IBikeStationCache>(sp => sp.GetRequiredService<MobilityManager>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MobilityManager>());

builder.Services.AddSingleton<IIconFileStore, WebRootIconFileStore>();
builder.Services.AddScoped<MockTicketPurchaseService>();
builder.Services.AddScoped<TicketableCatalogueService>();
builder.Services.AddScoped<TransitDataService>();

var managerTypes = Assembly.GetExecutingAssembly()
    .GetTypes()
    .Where(t => t.Namespace == "GetThereAPI.Managers"
                && t.IsClass
                && !t.IsAbstract
                && t != typeof(MobilityManager));

foreach (var managerType in managerTypes)
{
    builder.Services.AddScoped(managerType);
}

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

builder.Services.AddCors(options =>
{
    options.AddPolicy("MapAssets", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

_ = Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("[Startup] Background initialization started...");

    try
    {
        var mobilityManager = scope.ServiceProvider.GetRequiredService<MobilityManager>();
        await mobilityManager.InitialiseAsync();

        logger.LogInformation("[Startup] Background initialization completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Startup] Background initialization failed.");
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("MapAssets");
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
