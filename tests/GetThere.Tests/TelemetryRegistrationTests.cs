using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SharedAuth;

namespace GetThere.Tests;

/// <summary>
/// Telemetry has to stay out of the way when nothing is configured to receive it — otherwise every
/// developer has to run a collector locally just to start the app.
/// </summary>
public class TelemetryRegistrationTests
{
    private static IConfiguration ConfigurationWith(string? endpoint)
    {
        var values = new Dictionary<string, string?>();
        if (endpoint is not null)
            values["Otel:Endpoint"] = endpoint;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void No_endpoint_registers_nothing()
    {
        var services = new ServiceCollection();

        services.AddSharedTelemetry(ConfigurationWith(null), "TestService");

        Assert.Empty(services);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_endpoint_is_treated_as_absent(string endpoint)
    {
        var services = new ServiceCollection();

        services.AddSharedTelemetry(ConfigurationWith(endpoint), "TestService");

        Assert.Empty(services);
    }

    [Fact]
    public void A_configured_endpoint_registers_the_pipeline()
    {
        var services = new ServiceCollection();

        services.AddSharedTelemetry(ConfigurationWith("http://localhost:4317"), "TestService");

        Assert.NotEmpty(services);
    }

    [Theory]
    // Missing scheme. Uri.TryCreate accepts this as an absolute URI whose scheme is "localhost",
    // so an absolute-URI check alone lets it through and telemetry then exports nowhere.
    [InlineData("localhost:4317")]
    [InlineData("not a uri")]
    [InlineData("/relative/path")]
    public void A_malformed_endpoint_fails_at_startup_rather_than_silently_dropping_telemetry(string endpoint)
    {
        // Someone who sets the value clearly wants telemetry. Quietly ignoring a typo means finding
        // out during the incident that nothing was ever exported.
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddSharedTelemetry(ConfigurationWith(endpoint), "TestService"));

        Assert.Contains("http or https", ex.Message, StringComparison.Ordinal);
    }
}
