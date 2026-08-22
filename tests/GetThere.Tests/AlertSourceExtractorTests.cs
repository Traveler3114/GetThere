using TransitInfoAPI.Entities;
using TransitInfoAPI.Services;

namespace GetThere.Tests;

public class AlertSourceExtractorTests
{
    [Fact]
    public async Task Html_fixture_produces_expected_rows()
    {
        var html = """
            <html><body>
            <article class="c-article-card"><h2>Closure on line 3</h2><p>Due to works</p></article>
            <article class="c-article-card"><h2>Delay on line 5</h2><p>10 min delay</p></article>
            </body></html>
            """;
        // Use extractor via direct ParseHtmlAlerts simulation — we test the extractor's HTML path
        // by feeding it a synthetic HTTP response via a custom handler
        var source = new AlertSource
        {
            SourceKey = "test-html",
            Kind = "Transit",
            Format = "Html",
            Url = "https://example.test/notices",
            ItemSelector = "article.c-article-card",
            TitleSelector = "h2",
            DescriptionSelector = "p"
        };
        var (rows, warnings) = await RunExtractorWithBody(source, html, "text/html");
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r["Title"]?.ToString() == "Closure on line 3");
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task GeoJson_fixture_produces_expected_rows()
    {
        var geoJson = """
            {"type":"FeatureCollection","features":[
                {"type":"Feature","properties":{"title":"Road closed"},"geometry":{"type":"Point","coordinates":[16,45]}},
                {"type":"Feature","properties":{"title":"Works"},"geometry":{"type":"Point","coordinates":[16.1,45.1]}}
            ]}
            """;
        var source = new AlertSource
        {
            SourceKey = "test-geojson",
            Kind = "Road",
            Format = "GeoJson",
            Url = "https://example.test/events.geojson",
            ItemSelector = "features"
        };
        var (rows, _) = await RunExtractorWithBody(source, geoJson, "application/json");
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Selector_matching_nothing_returns_zero_rows_and_does_not_throw()
    {
        var html = "<html><body><div>No articles here</div></body></html>";
        var source = new AlertSource
        {
            SourceKey = "test-empty",
            Kind = "Transit",
            Format = "Html",
            Url = "https://example.test/notices",
            ItemSelector = "article.does-not-exist"
        };
        var (rows, warnings) = await RunExtractorWithBody(source, html, "text/html");
        Assert.Empty(rows);
        Assert.Contains(warnings, w => w.Contains("No element matched"));
    }

    private static async Task<(List<ExtractedRow> Rows, List<string> Warnings)> RunExtractorWithBody(AlertSource source, string body, string contentType)
    {
        var handler = new FakeHandler(body, contentType);
        var factory = new FakeFactory(handler);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var extractor = new AlertSourceExtractor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AlertSourceExtractor>.Instance,
            factory,
            config);
        return await extractor.ExtractAsync(source, CancellationToken.None);
    }

    private sealed class FakeHandler(string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
            };
            return Task.FromResult(resp);
        }
    }

    private sealed class FakeFactory(FakeHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
