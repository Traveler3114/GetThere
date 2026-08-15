using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace GetThere.Tests.Gtfs;

/// <summary>
/// A custom source carries the operator's own credential — a bearer token, a basic-auth password, or
/// an arbitrary API-key header, whichever <c>ApplyAuth</c> is configured for. These pin the rule that
/// the credential goes only to the host the operator configured.
/// <para>
/// The guard this replaced was a check on <c>response.RequestMessage.RequestUri</c> after the send.
/// It correctly detected an off-host redirect and refused the body, but it could not have prevented
/// anything: <c>SocketsHttpHandler</c> follows redirects inside <c>SendAsync</c>, and while
/// <c>HttpClient</c> strips <c>Authorization</c> across origins it strips no other header, so the
/// API-key case had already been delivered. That is why these tests assert on <em>what was sent</em>
/// rather than on what came back — a check that only looks at the response cannot tell the
/// difference between the two versions.
/// </para>
/// </summary>
public class CustomSourceRedirectTests
{
    private const string ApiKeyHeader = "X-Operator-Key";
    private const string ApiKeyValue = "s3cret-key";

    private static readonly string HeaderAuth =
        $$"""{"type":"header","name":"{{ApiKeyHeader}}","value":"{{ApiKeyValue}}"}""";

    [Fact]
    public async Task A_credentialed_source_redirected_off_host_never_sends_the_key_to_the_new_host()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host switch
        {
            "operator.test" => Redirect(HttpStatusCode.Found, "https://attacker.test/feed"),
            _ => Json("""[{"id":"leaked"}]""")
        });

        var result = await ExecuteAsync(handler, "https://operator.test/feed", HeaderAuth);

        Assert.DoesNotContain(handler.Sent, r => r.Host == "attacker.test");
        Assert.Equal("operator.test", Assert.Single(handler.Sent).Host);
        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("refusing to send it off-host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_credentialed_source_redirected_within_its_own_host_is_followed_with_the_key()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/feed"
            ? Redirect(HttpStatusCode.MovedPermanently, "https://operator.test/v2/feed")
            : Json("""[{"id":"A"}]"""));

        var result = await ExecuteAsync(handler, "https://operator.test/feed", HeaderAuth);

        Assert.Equal(2, handler.Sent.Count);
        Assert.Equal("/feed", handler.Sent[0].AbsolutePath);
        Assert.Equal("/v2/feed", handler.Sent[1].AbsolutePath);
        Assert.All(handler.Sent, r => Assert.Equal(ApiKeyValue, r.KeyHeader));
        Assert.Single(result.Rows);
    }

    [Fact]
    public async Task An_unauthenticated_source_may_redirect_anywhere_because_there_is_nothing_to_leak()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "operator.test"
            ? Redirect(HttpStatusCode.Found, "https://mirror.test/feed")
            : Json("""[{"id":"A"}]"""));

        var result = await ExecuteAsync(handler, "https://operator.test/feed", authConfig: null);

        Assert.Equal(2, handler.Sent.Count);
        Assert.Equal("operator.test", handler.Sent[0].Host);
        Assert.Equal("mirror.test", handler.Sent[1].Host);
        Assert.All(handler.Sent, r => Assert.Null(r.KeyHeader));
        Assert.Single(result.Rows);
    }

    [Fact]
    public async Task A_credentialed_source_downgraded_to_http_on_the_same_host_is_refused()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Scheme == "https"
            ? Redirect(HttpStatusCode.Found, "http://operator.test/feed")
            : Json("""[{"id":"A"}]"""));

        var result = await ExecuteAsync(handler, "https://operator.test/feed", HeaderAuth);

        Assert.Equal("https", Assert.Single(handler.Sent).Scheme);
        Assert.Contains(result.Warnings, w => w.Contains("refusing to send it in clear", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_credentialed_source_redirected_to_another_port_on_the_same_host_is_refused()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Port == 443
            ? Redirect(HttpStatusCode.Found, "https://operator.test:8443/feed")
            : Json("""[{"id":"A"}]"""));

        var result = await ExecuteAsync(handler, "https://operator.test/feed", HeaderAuth);

        Assert.Equal(443, Assert.Single(handler.Sent).Port);
        Assert.Contains(result.Warnings, w => w.Contains("other than 443", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_plain_http_to_https_upgrade_is_the_one_origin_change_a_credential_survives()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Scheme == "http"
            ? Redirect(HttpStatusCode.MovedPermanently, "https://operator.test/feed")
            : Json("""[{"id":"A"}]"""));

        var result = await ExecuteAsync(handler, "http://operator.test/feed", HeaderAuth);

        Assert.Equal(2, handler.Sent.Count);
        Assert.Equal("http", handler.Sent[0].Scheme);
        Assert.Equal("https", handler.Sent[1].Scheme);
        Assert.Single(result.Rows);
    }

    [Fact]
    public async Task A_redirect_loop_stops_rather_than_running_forever()
    {
        var handler = new RecordingHandler(_ => Redirect(HttpStatusCode.Found, "https://operator.test/feed"));

        var result = await ExecuteAsync(handler, "https://operator.test/feed", HeaderAuth);

        // MaxRedirects is 5, so six requests go out: the original plus five hops.
        Assert.Equal(6, handler.Sent.Count);
        Assert.Contains(result.Warnings, w => w.Contains("gave up after 5 redirects", StringComparison.Ordinal));
    }

    /// <summary>
    /// The engine must ask for the client whose handler has <c>AllowAutoRedirect = false</c>. Asking
    /// for "gtfs" would compile, pass every other test here — the stub factory ignores the name — and
    /// silently restore the original defect, because the handler would follow the redirect before any
    /// of the code above ran.
    /// </summary>
    [Fact]
    public async Task The_engine_uses_the_client_that_does_not_follow_redirects_by_itself()
    {
        var handler = new RecordingHandler(_ => Json("[]"));
        var factory = new StubHttpClientFactory(handler);

        await ExecuteAsync(handler, "https://operator.test/feed", HeaderAuth, factory);

        Assert.Equal("customsource", Assert.Single(factory.NamesRequested));
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────

    private static async Task<ExtractionResult> ExecuteAsync(
        RecordingHandler handler, string url, string? authConfig, StubHttpClientFactory? factory = null)
    {
        // AllowPrivateNetworkUrls skips the DNS-backed SSRF pre-check, which would otherwise fail to
        // resolve the .test hosts before any of this got a chance to run. It does not affect the
        // redirect decisions under test.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Feeds:AllowPrivateNetworkUrls"] = "true" })
            .Build();

        var engine = new CustomSourceEngine(
            factory ?? new StubHttpClientFactory(handler),
            configuration,
            new StubEnvironment(),
            NullLogger<CustomSourceEngine>.Instance);

        var request = new CustomSourceRequest
        {
            Url = url,
            HttpMethod = "GET",
            Format = CustomSourceFormat.Json,
            TargetSection = TransitSection.Stops
        };

        return await engine.ExecuteAsync(request, authConfig);
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location) =>
        new(status) { Headers = { Location = new Uri(location) } };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed record SentRequest(string Host, string Scheme, int Port, string AbsolutePath, string? KeyHeader);

    /// <summary>
    /// Stands in for the primary handler, so it sees exactly what went on the wire. It follows no
    /// redirects of its own — the same posture as the "customsource" client's
    /// <c>AllowAutoRedirect = false</c>.
    /// </summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Sent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = request.Headers.TryGetValues(ApiKeyHeader, out var values)
                ? string.Join(",", values)
                : null;

            Sent.Add(new SentRequest(
                request.RequestUri!.Host, request.RequestUri.Scheme, request.RequestUri.Port,
                request.RequestUri.AbsolutePath, key));

            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubHttpClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public List<string> NamesRequested { get; } = [];

        public HttpClient CreateClient(string name)
        {
            NamesRequested.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "GetThere.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }
}
