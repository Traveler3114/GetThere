using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SharedAuth;

/// <summary>
/// Traces and metrics for both APIs.
/// <para>
/// Neither service emitted anything beyond console logging, so a failed purchase or a stalled feed
/// import left nothing to look at after the fact. This wires the standard ASP.NET Core, HttpClient
/// and runtime instrumentation and exports over OTLP.
/// </para>
/// <para>
/// It is inert unless <c>Otel:Endpoint</c> is set. Adding the packages therefore changes nothing
/// about how the services run until somewhere exists to receive the data, and no developer has to
/// run a collector locally to start the app.
/// </para>
/// </summary>
public static class TelemetryRegistration
{
    public static IServiceCollection AddSharedTelemetry(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var endpoint = configuration["Otel:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return services;

        // The scheme has to be checked explicitly: Uri.TryCreate happily accepts "localhost:4317"
        // as an absolute URI whose *scheme* is "localhost", so an endpoint missing http:// passes
        // an absolute-URI test and then exports nowhere.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Otel:Endpoint is set to '{endpoint}', which is not an absolute http or https URI. " +
                "Use a value like http://localhost:4317, or remove the setting to disable telemetry.");
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes run constantly and say nothing about real traffic; leaving them
                    // in buries the requests worth looking at.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health");
                })
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = endpointUri))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = endpointUri));

        return services;
    }
}
