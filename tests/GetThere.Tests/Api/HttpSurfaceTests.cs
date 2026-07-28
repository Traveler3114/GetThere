using System.Net;
using System.Net.Http.Json;

namespace GetThere.Tests.Api;

/// <summary>
/// The HTTP layer itself: that routes are where they are meant to be, that the authorization
/// policies actually deny, that model validation rejects bad input, and that errors come back in
/// the documented shape.
/// <para>
/// None of this is reachable from a manager-level test, which is how the admin surface once ended
/// up served from <c>/users</c> instead of <c>/admin/users</c> without a single test failing.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class HttpSurfaceTests
{
    private readonly ApiFactory _factory;

    public HttpSurfaceTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_is_anonymous()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_the_database()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/health/ready");

        // The test database is reachable, so readiness must pass. If this ever fails while /health
        // still returns 200, that is the exact situation the split was added for.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/admin/stats")]
    [InlineData("/admin/purchases")]
    [InlineData("/admin/adapters")]
    [InlineData("/admin/audit")]
    public async Task Admin_routes_are_served_where_they_are_declared(string path)
    {
        var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync(path);

        // 404 here means the route is not where it is declared — the failure mode that moved this
        // whole surface to the application root when an attribute bound to the wrong type.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/admin/stats")]
    [InlineData("/admin/purchases")]
    [InlineData("/admin/audit")]
    [InlineData("/wallet")]
    [InlineData("/tickets")]
    public async Task Anonymous_callers_are_challenged(string path)
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/admin/stats")]
    [InlineData("/admin/purchases")]
    [InlineData("/admin/audit")]
    public async Task A_plain_user_cannot_reach_the_admin_surface(string path)
    {
        var client = await _factory.CreateUserClientAsync();

        var response = await client.GetAsync(path);

        // /admin/stats and /admin/purchases were once gated on tickets.view, which the User role
        // holds, so any account could read every user's purchases and the wallet float.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_plain_user_can_reach_its_own_resources()
    {
        var client = await _factory.CreateUserClientAsync();

        var response = await client.GetAsync("/tickets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Registration_rejects_a_password_the_server_would_not_accept()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = $"short-{Guid.NewGuid():N}@example.com",
            password = "Short1!",
            fullName = "Too Short"
        });

        // The shared contract carries [MinLength(12)], matching Identity's RequiredLength, so this
        // is refused by model validation before any of it reaches the manager.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_rejects_a_malformed_email()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "not-an-email",
            password = "Integration!User#2026",
            fullName = "Bad Address"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected_without_saying_which_part_was_wrong()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = ApiFactory.AdminEmail,
            password = "NotTheAdminPassword#1"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_account_answers_exactly_as_a_wrong_password_does()
    {
        var client = _factory.CreateAnonymousClient();

        var unknown = await client.PostAsJsonAsync("/auth/login", new
        {
            email = $"nobody-{Guid.NewGuid():N}@example.com",
            password = "Integration!User#2026"
        });

        var wrongPassword = await client.PostAsJsonAsync("/auth/login", new
        {
            email = ApiFactory.AdminEmail,
            password = "NotTheAdminPassword#1"
        });

        // Any difference here — status or body — tells an attacker which addresses have accounts.
        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registering_an_address_that_already_exists_is_indistinguishable_from_success()
    {
        var client = _factory.CreateAnonymousClient();
        var email = $"dupe-{Guid.NewGuid():N}@example.com";
        var payload = new { email, password = "Integration!User#2026", fullName = "Duplicate Probe" };

        var first = await client.PostAsJsonAsync("/auth/register", payload);
        var second = await client.PostAsJsonAsync("/auth/register", payload);

        Assert.True(first.IsSuccessStatusCode, $"First registration failed: {(int)first.StatusCode}");
        Assert.Equal(first.StatusCode, second.StatusCode);
    }

    [Fact]
    public async Task An_unknown_path_is_a_404_and_not_something_else()
    {
        var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/admin/definitely-not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
