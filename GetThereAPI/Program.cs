using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Infrastructure;
using GetThereAPI.Managers;
using GetThereAPI.Services;
using GetThereAPI.Transit;
using GetThereShared.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
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

builder.Services.AddHttpClient<TransitInfoApiClient>(client =>
{
    var baseUrl = builder.Configuration["TransitInfoApi:BaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<IIconFileStore, WebRootIconFileStore>();
builder.Services.AddScoped<MockTicketPurchaseService>();
builder.Services.AddScoped<TicketableCatalogueService>();
builder.Services.AddScoped<TransitDataService>();

var managerTypes = Assembly.GetExecutingAssembly()
    .GetTypes()
    .Where(t => t.Namespace == "GetThereAPI.Managers"
                && t.IsClass
                && !t.IsAbstract);

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

        if (error != null)
        {
            logger.LogError(error.Error, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 500;

        var message = isDev && error?.Error != null
            ? $"Unexpected error ({error.Error.GetType().Name}): {error.Error.Message}"
            : "An unexpected error occurred. Please try again later.";

        await context.Response.WriteAsJsonAsync(OperationResult<string>.Fail(message));
    });
});

app.UseCors("MapAssets");
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
