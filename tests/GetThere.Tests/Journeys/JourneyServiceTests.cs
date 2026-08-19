using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using GetThere.Services;

using GetThereShared.Contracts;

namespace GetThere.Tests.Journeys;

/// <summary>
/// The client's <see cref="JourneyService"/> buy flow, against a stub <see cref="HttpMessageHandler"/>:
/// that quote and book hit their routes with the right body, that book carries an idempotency key
/// (it charges the wallet, and the auth handler replays after a 401 token refresh), that cancel hits
/// its route, and that the responses deserialize into the shared contracts.
/// </summary>
public class JourneyServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((req, _) => Task.FromResult(respond(req))) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await _respond(request, cancellationToken);
        }
    }

    private static JourneyService NewService(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });

    private static QuoteLegDto Leg(string? @operator = "gt-zet") =>
        new(@operator, "TRAM", true,
            new DateTime(2026, 8, 19, 8, 0, 0),
            new DateTime(2026, 8, 19, 8, 30, 0),
            45.1, 15.5, 45.3, 15.6);

    [Fact]
    public async Task QuoteAsync_posts_the_legs_to_journeys_quote_and_deserializes_the_response()
    {
        string? body = null;
        var handler = new StubHandler(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/journeys/quote", req.RequestUri!.AbsolutePath);

            // Read here: the service disposes the request message once the call returns.
            body = await req.Content!.ReadAsStringAsync(ct);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new JourneyQuoteResponse(
                    [
                        new OperatorOfferDto("gt-zet", "ZET", "30 min", 0.55m, "EUR",
                            FulfillmentModes.PurchasableNow, 1, 11, null)
                    ],
                    0.55m, "EUR"))
            };
        });
        var service = NewService(handler);

        var result = await service.QuoteAsync(new JourneyQuoteRequest([Leg()]));

        Assert.True(result.Success, result.Message);

        var json = JsonNode.Parse(body);
        Assert.Equal("gt-zet", (string?)json!["legs"]![0]!["operatorGlobalId"]);
        Assert.Equal(true, (bool?)json["legs"]![0]!["isTransit"]);
        Assert.Equal("TRAM", (string?)json["legs"]![0]!["mode"]);
        Assert.Equal(45.1, (double?)json["legs"]![0]!["fromLat"]);

        Assert.Equal(0.55m, result.Data!.Total);
        var offer = Assert.Single(result.Data.Offers);
        Assert.Equal("gt-zet", offer.OperatorGlobalId);
        Assert.Equal("30 min", offer.ProductName);
        Assert.Equal(0.55m, offer.Price);
        Assert.Equal(FulfillmentModes.PurchasableNow, offer.FulfillmentMode);
    }

    [Fact]
    public async Task BookAsync_posts_to_journeys_book_with_an_idempotency_key()
    {
        string? idempotencyKey = null;
        string? body = null;
        var handler = new StubHandler(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/journeys/book", req.RequestUri!.AbsolutePath);

            idempotencyKey = Assert.Single(req.Headers.GetValues("Idempotency-Key"));
            body = await req.Content!.ReadAsStringAsync(ct);

            // The server answers 201 Created with the booking — a success for the client.
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new JourneyBookingResponse(
                    42, "Trip to town",
                    [
                        new BookedOfferDto("gt-zet", "ZET", "30 min", 0.55m, BookingOutcomes.Purchased)
                    ],
                    0.55m, 0.55m, 0m))
            };
        });
        var service = NewService(handler);

        var result = await service.BookAsync(new BookJourneyRequest("Trip to town", [Leg()]));

        Assert.True(result.Success, result.Message);

        // Two hex pairs per byte — 16 bytes, 32 characters, inside the server's 8..64 range.
        Assert.NotNull(idempotencyKey);
        Assert.Equal(32, idempotencyKey!.Length);
        Assert.All(idempotencyKey, c => Assert.True(Uri.IsHexDigit(c)));

        var json = JsonNode.Parse(body);
        Assert.Equal("Trip to town", (string?)json!["name"]);
        Assert.Equal("gt-zet", (string?)json["legs"]![0]!["operatorGlobalId"]);

        Assert.Equal(42, result.Data!.JourneyId);
        Assert.Equal("Trip to town", result.Data.Name);
        Assert.Equal(0.55m, result.Data.Charged);
        Assert.Equal(0m, result.Data.Reserved);
        var booked = Assert.Single(result.Data.Items);
        Assert.Equal(BookingOutcomes.Purchased, booked.Outcome);
    }

    [Fact]
    public async Task CancelBookingAsync_posts_to_the_cancel_route_and_reports_204_as_success()
    {
        var handler = new StubHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/journeys/7/cancel", req.RequestUri!.AbsolutePath);
            Assert.Null(req.Content);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var service = NewService(handler);

        var result = await service.CancelBookingAsync(7);

        Assert.True(result.Success, result.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task QuoteAsync_surfaces_the_problem_title_on_failure()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"title":"Nothing to price"}""",
                    System.Text.Encoding.UTF8, "application/problem+json")
            });
        var service = NewService(handler);

        var result = await service.QuoteAsync(new JourneyQuoteRequest([Leg()]));

        Assert.False(result.Success);
        Assert.Equal("Nothing to price", result.Message);
    }

    [Fact]
    public async Task BookAsync_failure_deserializes_the_problem_and_sends_no_key_twice()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"title":"Journey already booked"}""",
                    System.Text.Encoding.UTF8, "application/problem+json")
            });
        var service = NewService(handler);

        var result = await service.BookAsync(new BookJourneyRequest("Trip", [Leg()]));

        Assert.False(result.Success);
        Assert.Equal("Journey already booked", result.Message);
        Assert.Single(handler.Requests);
    }
}