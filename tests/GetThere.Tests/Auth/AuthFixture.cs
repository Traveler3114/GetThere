using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GetThere.Tests.Auth;

/// <summary>
/// Real SQL Server plus a real Identity stack. Refresh-token rotation is a sequence of database
/// writes whose ordering is the thing under test, so a fake user store would prove nothing.
/// </summary>
public sealed class AuthFixture : IDisposable
{
    private static readonly string ConnectionString = TestDatabase.ConnectionStringFor("GetThereAuthTests");

    private readonly ServiceProvider _provider;

    public AuthFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-that-is-long-enough-to-be-valid-0123456789",
                ["Jwt:Issuer"] = "GetThereAPI",
                ["Jwt:Audience"] = "GetThereApp",
                ["Jwt:ExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "1",
                ["Jwt:RefreshTokenDaysRememberMe"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddIdentity<AppUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 12;
            options.User.RequireUniqueEmail = true;
            options.Lockout.AllowedForNewUsers = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<TokenManager>();
        services.AddScoped<AuthManager>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureDeleted();
        db.Database.Migrate();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        roleManager.CreateAsync(new IdentityRole("User")).GetAwaiter().GetResult();
    }

    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>Creates a confirmed account and returns its id.</summary>
    public async Task<string> CreateUserAsync(string email, string password = "Auditor!Probe#2026x")
    {
        using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser { UserName = email, Email = email, FullName = "Auth Probe" };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));

        return user.Id;
    }

    public void Dispose()
    {
        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeleted();
        }

        _provider.Dispose();
    }
}

[CollectionDefinition(Name)]
public class AuthCollection : ICollectionFixture<AuthFixture>
{
    public const string Name = "auth";
}
