using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// SQL Server — unchanged
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity — unchanged
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

// Register our TokenService so controllers can inject it
builder.Services.AddScoped<TokenService>();

// Tell ASP.NET "use JWT Bearer as the default way to authenticate"
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // These rules tell the server how to validate incoming tokens
    options.TokenValidationParameters = new TokenValidationParameters
    {
        // Check the token was made by our API
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],

        // Check the token was meant for our app
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],

        // Reject expired tokens
        ValidateLifetime = true,

        // Verify the signature using our secret key
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

// Order matters! Authentication must come before Authorization
app.UseAuthentication();  // "Who are you?" — reads and validates the JWT
app.UseAuthorization();   // "Are you allowed here?" — checks [Authorize] attributes
app.MapControllers();
app.Run();


//https://localhost:7230/scalar/v1