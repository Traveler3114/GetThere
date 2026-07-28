using System.Net.Http.Json;

using GetThereAPI.Common;
using GetThereAPI.Data;
using GetThereAPI.Entities;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GetThere.Tests.Api;

/// <summary>
/// Boots the real GetThereAPI pipeline in-process.
/// <para>
/// Every existing test calls a manager directly, which leaves the entire HTTP layer unverified:
/// routing, the authorization policies, model validation and the shape of the error response. That
/// gap has already cost this codebase once — <c>SetRoleRequest</c> sat between
/// <c>[ApiController]/[Route]/[Authorize]</c> and the class it was meant to decorate, so the
/// attributes bound to the record instead and the entire admin surface silently moved from
/// <c>/admin/users</c> to <c>/users</c>. Nothing failed to compile and no unit test noticed.
/// </para>
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<GetThereAPI.ApiEntryPoint>, IAsyncLifetime
{
    public const string AdminEmail = "admin@getthere.local";
    public const string AdminPassword = "Integration!Admin#2026";
    public const string UserPassword = "Integration!User#2026";

    private static readonly string ConnectionString =
        TestDatabase.ConnectionStringFor("GetThereApiIntegrationTests");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Key", "integration-signing-key-long-enough-to-be-valid-0123456789");
        builder.UseSetting("Jwt:Issuer", "GetThereAPI");
        builder.UseSetting("Jwt:Audience", "GetThereApp");
        builder.UseSetting("Jwt:ExpiryMinutes", "15");

        // The startup seed creates the roles and permission claims the authorization tests depend
        // on, so it stays on and is given a known password rather than a generated one.
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:AdminPassword", AdminPassword);

        // Migrations are applied once by InitializeAsync below.
        builder.UseSetting("Database:MigrateOnStartup", "false");

        // Every request from an in-process client shares one rate-limit partition, so the shipped
        // 10-per-minute auth window rejects the fixture's own logins. Raised rather than disabled,
        // so the limiter middleware still sits in the pipeline being exercised.
        builder.UseSetting("RateLimits:GlobalPerMinute", "100000");
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:UploadPerMinute", "100000");

        builder.ConfigureServices(services =>
        {
            // The background sweeps would run against the test database on their own schedule and
            // make results depend on timing. Nothing here is testing them; they have their own
            // tests that call the managers directly.
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(hosted);
        });
    }

    /// <summary>
    /// A context built straight from the connection string, deliberately not resolved from
    /// <see cref="WebApplicationFactory{T}.Services"/>: touching that property starts the host, and
    /// the host's startup seed queries the database. The schema therefore has to exist before the
    /// first time anything asks the factory for a service.
    /// </summary>
    private static AppDbContext CreateStandaloneContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var db = CreateStandaloneContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await using (var db = CreateStandaloneContext())
        {
            await db.Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>An unauthenticated client.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();

    /// <summary>A client carrying a bearer token for a freshly created plain <c>User</c>.</summary>
    public async Task<HttpClient> CreateUserClientAsync()
    {
        var email = $"probe-{Guid.NewGuid():N}@example.com";

        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { UserName = email, Email = email, FullName = "Integration Probe" };

            var created = await users.CreateAsync(user, UserPassword);
            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(user, RoleNames.User);
        }

        return await CreateTokenClientAsync(email, UserPassword);
    }

    /// <summary>A client carrying a bearer token for the seeded admin.</summary>
    public Task<HttpClient> CreateAdminClientAsync() => CreateTokenClientAsync(AdminEmail, AdminPassword);

    /// <summary>
    /// Logs in over HTTP rather than minting a token directly, so the token these tests carry is
    /// one the login endpoint actually issues.
    /// </summary>
    private async Task<HttpClient> CreateTokenClientAsync(string email, string password)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Login for {email} failed: {(int)response.StatusCode} {body}");

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
