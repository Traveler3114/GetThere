using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

using GetThereAPI.Common;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Managers;
using GetThereAPI.Sdk;
using GetThereAPI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<TransitInfoApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["TransitInfoApi:BaseUrl"] ?? "http://localhost:5000");
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

app.UseRateLimiter();
app.UseCors("MapAssets");

app.UseAuthentication();
app.UseAuthorization();

// Protect /admin static files — reject unauthenticated requests
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin") &&
        context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = 401;
        return;
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseResponseCompression();

app.UseHttpsRedirection();
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
    var userPerms = PermissionKeys.All.Where(p =>
        p is PermissionKeys.TicketsView or PermissionKeys.TicketsCreate
        or PermissionKeys.WalletsView
        or PermissionKeys.ProfileView or PermissionKeys.ProfileManage
        or PermissionKeys.SettingsView
        or PermissionKeys.MapView);
    foreach (var perm in userPerms.Where(p => !userClaims.Any(c => c.Value == p)))
        await roleManager.AddClaimAsync(userRole!, new System.Security.Claims.Claim("permission", perm));

    // Seed admin user
    var admin = await userManager.FindByNameAsync("admin@getthere.local");
    if (admin is null)
    {
        var pwd = GenerateSecurePassword(24);
        admin = new AppUser { UserName = "admin@getthere.local", Email = "admin@getthere.local", FullName = "GetThere Admin" };
        await userManager.CreateAsync(admin, pwd);
        await userManager.AddToRoleAsync(admin, RoleNames.Admin);
        var credFile = Path.Combine(AppContext.BaseDirectory, ".admin-credentials");
        await File.WriteAllTextAsync(credFile,
            $"Email: admin@getthere.local\nPassword: {pwd}\n");
        Console.WriteLine($"Admin account created. Credentials saved to: {credFile}");
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