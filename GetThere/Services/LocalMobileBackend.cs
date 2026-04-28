using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GetThereShared.Common;
using GetThereShared.Dtos;
using GetThereShared.Enums;

namespace GetThere.Services;

public sealed class LocalMobileBackendStore
{
    private const string UsersKey = "local_backend_users";
    private const string TransactionsKey = "local_backend_transactions";
    private const string TicketsKey = "local_backend_tickets";
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class LocalUserRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public decimal Balance { get; set; } = 25m;
    }

    public OperationResult Register(RegisterDto dto)
    {
        lock (_gate)
        {
            var users = LoadUsers();
            if (users.Any(u => string.Equals(u.Email, dto.Email, StringComparison.OrdinalIgnoreCase)))
                return OperationResult.Fail("Email is already registered.");

            users.Add(new LocalUserRecord
            {
                Email = dto.Email.Trim(),
                Password = dto.Password,
                FullName = dto.FullName?.Trim() ?? string.Empty,
                Balance = 25m,
            });
            SaveUsers(users);
            return OperationResult.Ok("Registration successful.");
        }
    }

    public LoginResponseDto? Login(LoginDto dto)
    {
        lock (_gate)
        {
            var user = LoadUsers().FirstOrDefault(u =>
                string.Equals(u.Email, dto.Email, StringComparison.OrdinalIgnoreCase)
                && u.Password == dto.Password);

            if (user == null)
                return null;

            var accessToken = CreateFakeJwt(user);
            return new LoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = "offline-refresh-token",
                User = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    FullName = string.IsNullOrWhiteSpace(user.FullName) ? null : user.FullName,
                    Token = accessToken,
                }
            };
        }
    }

    public RefreshTokenResponseDto RefreshToken() => new()
    {
        AccessToken = CreateFakeJwt(new LocalUserRecord { Id = "offline-user", Email = "offline@getthere.local", FullName = "Offline User" }),
        RefreshToken = "offline-refresh-token"
    };

    public OperationResult<List<CountryDto>> GetCountries() => OperationResult<List<CountryDto>>.Ok(
    [
        new CountryDto { Id = 1, Name = "Croatia" },
        new CountryDto { Id = 2, Name = "Slovenia" },
        new CountryDto { Id = 3, Name = "Austria" }
    ]);

    public OperationResult<List<TicketableOperatorDto>> GetOperators(int? countryId)
    {
        var all = new List<TicketableOperatorDto>
        {
            new() { Id = 1, Name = "ZET", Type = "TRANSIT", Color = "#1264AB", Description = "Zagreb's tram and bus network.", City = "Zagreb", Country = "Croatia", IsMock = true },
            new() { Id = 2, Name = "HZPP", Type = "TRAIN", Color = "#6B21A8", Description = "Croatian national railway.", City = "Croatia", Country = "Croatia", IsMock = true },
            new() { Id = 3, Name = "Bajs", Type = "BIKE", Color = "#F97316", Description = "Nextbike city bike sharing.", City = "Multiple", Country = "Croatia", IsMock = true },
            new() { Id = 4, Name = "LPP", Type = "TRANSIT", Color = "#E30613", Description = "Ljubljana public transport.", City = "Ljubljana", Country = "Slovenia", IsMock = true },
        };

        if (countryId == 1)
            return OperationResult<List<TicketableOperatorDto>>.Ok(all.Where(o => o.Id is 1 or 2 or 3).ToList());
        if (countryId == 2)
            return OperationResult<List<TicketableOperatorDto>>.Ok(all.Where(o => o.Id is 3 or 4).ToList());
        return OperationResult<List<TicketableOperatorDto>>.Ok(all);
    }

    public OperationResult<List<OperatorDto>> GetPublicOperators() => OperationResult<List<OperatorDto>>.Ok(
    [
        new OperatorDto { Id = 1, Name = "ZET", City = "Zagreb", Country = "Croatia" },
        new OperatorDto { Id = 2, Name = "HZPP", City = null, Country = "Croatia" },
        new OperatorDto { Id = 3, Name = "LPP", City = "Ljubljana", Country = "Slovenia" }
    ]);

    public OperationResult<List<TransportTypeDto>> GetTransportTypes() => OperationResult<List<TransportTypeDto>>.Ok(
    [
        new TransportTypeDto { GtfsRouteType = 0, Name = "Tram", IconFile = "tram.png", Color = "#1264AB" },
        new TransportTypeDto { GtfsRouteType = 3, Name = "Bus", IconFile = "bus.png", Color = "#126400" },
        new TransportTypeDto { GtfsRouteType = 2, Name = "Train", IconFile = "train.png", Color = "#FF6B00" }
    ]);

    public OperationResult<List<StopDto>> GetStops(int? countryId) => OperationResult<List<StopDto>>.Ok(
        (countryId == 2
            ? new List<StopDto>
            {
                new() { StopId = "lpp-center", Name = "Ljubljana Center", Lat = 46.0569, Lon = 14.5058, RouteType = 3 },
                new() { StopId = "lpp-station", Name = "Main Station", Lat = 46.0583, Lon = 14.5102, RouteType = 3 }
            }
            : new List<StopDto>
            {
                new() { StopId = "zet-ban", Name = "Ban Jelacic Square", Lat = 45.8131, Lon = 15.9775, RouteType = 0 },
                new() { StopId = "zet-main", Name = "Main Station", Lat = 45.8058, Lon = 15.9794, RouteType = 0 },
                new() { StopId = "zet-savski", Name = "Savski Most", Lat = 45.7865, Lon = 15.9575, RouteType = 3 }
            }).ToList());

    public OperationResult<List<RouteDto>> GetRoutes(int? countryId) => OperationResult<List<RouteDto>>.Ok(
        (countryId == 2
            ? new List<RouteDto>
            {
                new()
                {
                    RouteId = "lpp-6",
                    ShortName = "6",
                    LongName = "Center - Station",
                    Color = "E30613",
                    RouteType = 3,
                    Shape = [ [14.5058,46.0569], [14.5080,46.0572], [14.5102,46.0583] ]
                }
            }
            : new List<RouteDto>
            {
                new()
                {
                    RouteId = "zet-6",
                    ShortName = "6",
                    LongName = "Ban Jelacic Square - Main Station",
                    Color = "1264AB",
                    RouteType = 0,
                    Shape = [ [15.9775,45.8131], [15.9786,45.8109], [15.9794,45.8058] ]
                },
                new()
                {
                    RouteId = "zet-109",
                    ShortName = "109",
                    LongName = "Savski Most Loop",
                    Color = "126400",
                    RouteType = 3,
                    Shape = [ [15.9575,45.7865], [15.9590,45.7895], [15.9614,45.7924] ]
                }
            }).ToList());

    public OperationResult<List<BikeStationDto>> GetBikeStations(int? countryId) => OperationResult<List<BikeStationDto>>.Ok(
        (countryId == 2
            ? new List<BikeStationDto>
            {
                new() { StationId = "bike-lj-1", Name = "Tivoli", Lat = 46.0562, Lon = 14.4968, AvailableBikes = 6, Capacity = 12, ProviderId = 1, ProviderName = "Bajs / Nextbike", CountryName = "Slovenia" }
            }
            : new List<BikeStationDto>
            {
                new() { StationId = "bike-zg-1", Name = "Zrinjevac", Lat = 45.8105, Lon = 15.9790, AvailableBikes = 8, Capacity = 14, ProviderId = 1, ProviderName = "Bajs / Nextbike", CountryName = "Croatia" },
                new() { StationId = "bike-zg-2", Name = "Mimara", Lat = 45.8074, Lon = 15.9688, AvailableBikes = 5, Capacity = 12, ProviderId = 1, ProviderName = "Bajs / Nextbike", CountryName = "Croatia" }
            }).ToList());

    public OperationResult<StopScheduleDto> GetStopSchedule(string stopId) => OperationResult<StopScheduleDto>.Ok(
        new StopScheduleDto
        {
            StopId = stopId,
            StopName = stopId switch
            {
                "zet-ban" => "Ban Jelacic Square",
                "zet-main" => "Main Station",
                "zet-savski" => "Savski Most",
                "lpp-center" => "Ljubljana Center",
                "lpp-station" => "Main Station",
                _ => stopId
            },
            Groups =
            [
                new DepartureGroupDto
                {
                    RouteId = stopId.StartsWith("lpp") ? "lpp-6" : "zet-6",
                    ShortName = "6",
                    Headsign = stopId.StartsWith("lpp") ? "Station" : "Main Station",
                    Departures =
                    [
                        new DepartureDto { TripId = "t1", ScheduledTime = DateTime.Now.AddMinutes(3).ToString("HH:mm"), EstimatedTime = DateTime.Now.AddMinutes(4).ToString("HH:mm"), DelayMinutes = 1, IsRealtime = true },
                        new DepartureDto { TripId = "t2", ScheduledTime = DateTime.Now.AddMinutes(12).ToString("HH:mm"), EstimatedTime = DateTime.Now.AddMinutes(12).ToString("HH:mm"), DelayMinutes = 0, IsRealtime = false },
                        new DepartureDto { TripId = "t3", ScheduledTime = DateTime.Now.AddMinutes(19).ToString("HH:mm"), EstimatedTime = DateTime.Now.AddMinutes(21).ToString("HH:mm"), DelayMinutes = 2, IsRealtime = true }
                    ]
                }
            ]
        });

    public OperationResult<List<MockTicketOptionDto>> GetTicketOptions(int operatorId)
    {
        var map = new Dictionary<int, List<MockTicketOptionDto>>
        {
            [1] =
            [
                new() { OptionId = "zet-single", Name = "Single Ride", Description = "Valid for 90 minutes on any ZET tram or bus.", Price = 0.80m, Validity = "90 minutes" },
                new() { OptionId = "zet-day", Name = "Day Pass", Description = "Unlimited rides all day on the ZET network.", Price = 4.00m, Validity = "24 hours" },
                new() { OptionId = "zet-10ride", Name = "10-Ride Card", Description = "10 single rides to use at any time.", Price = 6.50m, Validity = "Per ride" },
            ],
            [2] =
            [
                new() { OptionId = "hzpp-single", Name = "Single Ride", Description = "Valid for one train journey.", Price = 2.50m, Validity = "Single use" },
                new() { OptionId = "hzpp-day", Name = "Day Pass", Description = "Unlimited rail travel for one day.", Price = 8.50m, Validity = "24 hours" },
            ],
            [3] =
            [
                new() { OptionId = "bajs-unlock", Name = "Bike Unlock", Description = "Unlock one city bike.", Price = 1.00m, Validity = "30 minutes" },
                new() { OptionId = "bajs-day", Name = "Bike Day Pass", Description = "Ride city bikes all day.", Price = 5.00m, Validity = "24 hours" },
            ],
            [4] =
            [
                new() { OptionId = "lpp-single", Name = "Single Ride", Description = "Valid for 90 minutes on any LPP bus.", Price = 1.30m, Validity = "90 minutes" },
                new() { OptionId = "lpp-day", Name = "Day Pass", Description = "Unlimited rides all day on LPP buses.", Price = 5.00m, Validity = "24 hours" },
            ]
        };

        return map.TryGetValue(operatorId, out var options)
            ? OperationResult<List<MockTicketOptionDto>>.Ok(options)
            : OperationResult<List<MockTicketOptionDto>>.Fail("Operator not found.");
    }

    public OperationResult<IEnumerable<PaymentProviderDto>> GetProviders() => OperationResult<IEnumerable<PaymentProviderDto>>.Ok(
    [
        new PaymentProviderDto { Id = 1, Name = "MockPay" },
        new PaymentProviderDto { Id = 2, Name = "TestPay" }
    ]);

    public OperationResult<WalletDto> TopUp(string userId, TopUpDto dto)
    {
        lock (_gate)
        {
            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return OperationResult<WalletDto>.Fail("User not found.");

            user.Balance += dto.Amount;
            SaveUsers(users);

            var transactions = LoadTransactions();
            transactions.Add(new WalletTransactionDto
            {
                Id = NextId(transactions.Select(t => t.Id)),
                WalletId = 1,
                Type = WalletTransactionType.TopUp,
                Amount = dto.Amount,
                Timestamp = DateTime.Now,
                Description = "Manual top-up"
            });
            SaveTransactions(transactions);

            return OperationResult<WalletDto>.Ok(ToWallet(user));
        }
    }

    public OperationResult<WalletDto> GetWallet(string userId)
    {
        lock (_gate)
        {
            var user = LoadUsers().FirstOrDefault(u => u.Id == userId);
            return user == null
                ? OperationResult<WalletDto>.Fail("User not found.")
                : OperationResult<WalletDto>.Ok(ToWallet(user));
        }
    }

    public OperationResult<IEnumerable<WalletTransactionDto>> GetTransactions() =>
        OperationResult<IEnumerable<WalletTransactionDto>>.Ok(LoadTransactions().OrderByDescending(t => t.Timestamp));

    public OperationResult<IEnumerable<TicketDto>> GetTickets() =>
        OperationResult<IEnumerable<TicketDto>>.Ok(LoadTickets().OrderByDescending(t => t.PurchasedAt));

    public OperationResult<MockTicketResultDto> Purchase(string userId, int operatorId, MockTicketPurchaseRequest body)
    {
        lock (_gate)
        {
            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return OperationResult<MockTicketResultDto>.Fail("User not authenticated.");

            var optionsResult = GetTicketOptions(operatorId);
            var option = optionsResult.Data?.FirstOrDefault(o => o.OptionId == body.OptionId);
            if (option == null)
                return OperationResult<MockTicketResultDto>.Fail("Ticket option not found.");

            var total = option.Price * Math.Max(1, body.Quantity);
            if (user.Balance < total)
                return OperationResult<MockTicketResultDto>.Fail("Insufficient wallet balance.");

            user.Balance -= total;
            SaveUsers(users);

            var now = DateTime.UtcNow;
            var validUntil = option.Validity switch
            {
                "90 minutes" => now.AddMinutes(90),
                "24 hours" => now.AddHours(24),
                _ => now.AddDays(365)
            };

            var result = new MockTicketResultDto
            {
                TicketId = Guid.NewGuid().ToString("N"),
                OperatorName = GetOperators(null).Data!.First(o => o.Id == operatorId).Name,
                TicketName = option.Name,
                Price = total,
                ValidFrom = now.ToString("O"),
                ValidUntil = validUntil.ToString("O"),
                QrCodeData = $"mock:{operatorId}:{option.OptionId}:{Guid.NewGuid():N}",
                IsMock = true,
            };

            var tickets = LoadTickets();
            tickets.Add(new TicketDto
            {
                Id = NextId(tickets.Select(t => t.Id)),
                UserId = user.Id,
                TicketType = $"{result.OperatorName} — {result.TicketName}",
                PurchasedAt = now,
                ValidFrom = now,
                ValidUntil = validUntil,
                Format = TicketFormat.QrCode,
                Payload = result.QrCodeData,
                DisplayInstructions = "Mock ticket stored locally on this device.",
                Status = TicketStatus.Active,
                TransitOperatorId = operatorId
            });
            SaveTickets(tickets);

            var transactions = LoadTransactions();
            transactions.Add(new WalletTransactionDto
            {
                Id = NextId(transactions.Select(t => t.Id)),
                WalletId = 1,
                TicketId = tickets.Last().Id,
                Type = WalletTransactionType.TicketPurchase,
                Amount = total,
                Timestamp = DateTime.Now,
                Description = result.TicketName
            });
            SaveTransactions(transactions);

            return OperationResult<MockTicketResultDto>.Ok(result, "Purchase successful.");
        }
    }

    public static string? TryGetUserId(HttpRequestMessage request)
    {
        var bearer = request.Headers.Authorization?.Parameter;
        if (string.IsNullOrWhiteSpace(bearer))
            return null;

        try
        {
            var payload = bearer.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static WalletDto ToWallet(LocalUserRecord user) => new()
    {
        Id = 1,
        Balance = user.Balance,
        LastUpdated = DateTime.Now
    };

    private List<LocalUserRecord> LoadUsers() => Load(UsersKey, SeedUsers());
    private List<WalletTransactionDto> LoadTransactions() => Load<WalletTransactionDto>(TransactionsKey, []);
    private List<TicketDto> LoadTickets() => Load<TicketDto>(TicketsKey, []);

    private void SaveUsers(List<LocalUserRecord> users) => Save(UsersKey, users);
    private void SaveTransactions(List<WalletTransactionDto> items) => Save(TransactionsKey, items);
    private void SaveTickets(List<TicketDto> items) => Save(TicketsKey, items);

    private static List<LocalUserRecord> SeedUsers() =>
    [
        new LocalUserRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = "admin@gmail.com",
            Password = "admin1234",
            FullName = "Admin User",
            Balance = 25m
        }
    ];

    private static List<T> Load<T>(string key, List<T> fallback)
    {
        var json = Preferences.Default.Get(key, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return fallback;
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void Save<T>(string key, List<T> value) =>
        Preferences.Default.Set(key, JsonSerializer.Serialize(value, JsonOptions));

    private static int NextId(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;

    private static string CreateFakeJwt(LocalUserRecord user)
    {
        var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode(JsonSerializer.Serialize(new
        {
            sub = user.Id,
            email = user.Email,
            given_name = string.IsNullOrWhiteSpace(user.FullName) ? user.Email.Split('@')[0] : user.FullName,
            exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
        }));

        return $"{header}.{payload}.offline";
    }

    private static string Base64UrlEncode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class LocalMobileApiHandler : DelegatingHandler
{
    private readonly LocalMobileBackendStore _store;

    public LocalMobileApiHandler(LocalMobileBackendStore store)
    {
        _store = store;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HandleLocal(request);
        }
    }

    private HttpResponseMessage HandleLocal(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath.Trim('/').ToLowerInvariant() ?? string.Empty;
        var query = request.RequestUri?.Query ?? string.Empty;

        if (request.Method == HttpMethod.Post && path == "auth/login")
        {
            var dto = request.Content!.ReadFromJsonAsync<LoginDto>().GetAwaiter().GetResult()!;
            var result = _store.Login(dto);
            return result == null ? Status(HttpStatusCode.Unauthorized, OperationResult.Fail("Invalid credentials")) : Ok(result);
        }

        if (request.Method == HttpMethod.Post && path == "auth/register")
        {
            var dto = request.Content!.ReadFromJsonAsync<RegisterDto>().GetAwaiter().GetResult()!;
            var result = _store.Register(dto);
            return result.Success ? Ok(result) : Status(HttpStatusCode.BadRequest, result);
        }

        if (request.Method == HttpMethod.Post && path == "auth/refresh")
            return Ok(_store.RefreshToken());

        if (request.Method == HttpMethod.Post && path == "auth/logout")
            return Ok(OperationResult.Ok());

        if (request.Method == HttpMethod.Get && path == "countries")
            return Ok(_store.GetCountries());

        if (request.Method == HttpMethod.Get && path == "operator")
            return Ok(_store.GetPublicOperators());

        if (request.Method == HttpMethod.Get && path == "operator/transport-types")
            return Ok(_store.GetTransportTypes());

        if (request.Method == HttpMethod.Get && path == "operator/stops")
            return Ok(_store.GetStops(ParseCountryId(query)));

        if (request.Method == HttpMethod.Get && path == "operator/routes")
            return Ok(_store.GetRoutes(ParseCountryId(query)));

        if (request.Method == HttpMethod.Get && path == "map/bike-stations")
            return Ok(_store.GetBikeStations(ParseCountryId(query)));

        if (request.Method == HttpMethod.Get && path.StartsWith("operator/stops/") && path.EndsWith("/schedule"))
        {
            var segments = path.Split('/');
            var stopId = Uri.UnescapeDataString(segments[2]);
            return Ok(_store.GetStopSchedule(stopId));
        }

        if (request.Method == HttpMethod.Get && path == "operator/ticketable")
        {
            return Ok(_store.GetOperators(ParseCountryId(query)));
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("mock-tickets/") && path.EndsWith("/options"))
        {
            var operatorId = int.Parse(path.Split('/')[1]);
            return Ok(_store.GetTicketOptions(operatorId));
        }

        if (request.Method == HttpMethod.Post && path.StartsWith("mock-tickets/") && path.EndsWith("/purchase"))
        {
            var userId = LocalMobileBackendStore.TryGetUserId(request);
            if (string.IsNullOrWhiteSpace(userId))
                return Status(HttpStatusCode.Unauthorized, OperationResult<MockTicketResultDto>.Fail("Please log in to purchase tickets."));

            var operatorId = int.Parse(path.Split('/')[1]);
            var body = request.Content!.ReadFromJsonAsync<MockTicketPurchaseRequest>().GetAwaiter().GetResult()!;
            var result = _store.Purchase(userId, operatorId, body);
            return result.Success ? Ok(result) : Status(HttpStatusCode.BadRequest, result);
        }

        if (request.Method == HttpMethod.Get && path == "payment/providers")
            return Ok(_store.GetProviders());

        if (request.Method == HttpMethod.Post && path == "payment/topup")
        {
            var userId = LocalMobileBackendStore.TryGetUserId(request);
            if (string.IsNullOrWhiteSpace(userId))
                return Status(HttpStatusCode.Unauthorized, OperationResult<WalletDto>.Fail("Please log in."));

            var body = request.Content!.ReadFromJsonAsync<TopUpDto>().GetAwaiter().GetResult()!;
            var result = _store.TopUp(userId, body);
            return result.Success ? Ok(result) : Status(HttpStatusCode.BadRequest, result);
        }

        if (request.Method == HttpMethod.Get && path == "wallet")
        {
            var userId = LocalMobileBackendStore.TryGetUserId(request);
            return string.IsNullOrWhiteSpace(userId)
                ? Status(HttpStatusCode.Unauthorized, OperationResult<WalletDto>.Fail("Please log in."))
                : Ok(_store.GetWallet(userId));
        }

        if (request.Method == HttpMethod.Get && path == "wallet/transactions")
            return Ok(_store.GetTransactions());

        if (request.Method == HttpMethod.Get && path == "ticket")
            return Ok(_store.GetTickets());

        return Status(HttpStatusCode.ServiceUnavailable, OperationResult.Fail("Local mobile fallback does not implement this endpoint."));
    }

    private static HttpResponseMessage Ok<T>(T value) => Status(HttpStatusCode.OK, value);

    private static HttpResponseMessage Status<T>(HttpStatusCode code, T value)
    {
        var response = new HttpResponseMessage(code)
        {
            Content = JsonContent.Create(value)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    private static int? ParseCountryId(string query)
    {
        if (!query.Contains("countryId=", StringComparison.OrdinalIgnoreCase))
            return null;

        var raw = query.TrimStart('?').Split('&').FirstOrDefault(p => p.StartsWith("countryId=", StringComparison.OrdinalIgnoreCase))?.Split('=').LastOrDefault();
        return int.TryParse(raw, out var parsed) ? parsed : null;
    }
}
