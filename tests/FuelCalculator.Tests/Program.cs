using FuelCalculator.Core.Calculation;
using FuelCalculator.Core.Currency;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Fuel;
using FuelCalculator.Core.Routing;
using FuelCalculator.Core.Segmentation;

var tests = new List<(string Name, Func<Task> Test)>
{
    ("L/100km conversion is unchanged", TestLPer100KmConversion),
    ("km/L converts to 100 divided by value", TestKmPerLiterConversion),
    ("US MPG converts with 235.214583 factor", TestUsMpgConversion),
    ("UK MPG converts with 282.480936 factor", TestUkMpgConversion),
    ("Fuel cost math uses distance, consumption, price, and FX", TestFuelCostMath),
    ("Country segments aggregate by country", TestPerCountryAggregation),
    ("Manual price override takes precedence", TestManualOverridePrecedence),
    ("Missing fuel price returns warning", TestMissingFuelPriceWarning),
    ("Mock route and seed prices integration works", TestMockRouteIntegration),
    ("Mock Syria route does not fall back to Doha", TestMockSyriaRoute),
    ("Google Maps full directions path parses stops", TestGoogleMapsPathParser),
    ("C3 missing price marks total incomplete", TestMissingPriceMarksTotalIncomplete),
    ("C4 manual price without currency uses country currency", TestManualPriceDefaultsToCountryCurrency),
    ("C5 seed prices report their real date, not startup time", TestSeedFetchedAtIsSeedDate),
    ("C6 route duration is returned", TestDurationIsReturned),
    ("C7 98 Octane in Saudi is priced as 95 with a note", TestUnsoldGradeFallsBack),
    ("C8 refuel stops keep a reserve", TestRefuelStopsKeepReserve),
    ("Short-link resolver rejects redirects off Google", TestResolverRejectsNonGoogleRedirect),
    ("C2 border towns resolve to the right country", TestBorderTownsResolve),
    ("C2 a point on the King Fahd Causeway snaps to SA or BH", TestCausewaySnapsToNearestCountry),
    ("C2 open sea far from land is unknown", TestOpenSeaIsUnknown),
    ("C2 Kuwait to Syria bills Saudi km to Saudi, not Jordan", TestKuwaitSyriaSplit)
};

var failures = new List<string>();

foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{failures.Count} test(s) failed:");

    foreach (var failure in failures)
    {
        Console.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
    return;
}

Console.WriteLine();
Console.WriteLine($"{tests.Count} tests passed.");

static Task TestLPer100KmConversion()
{
    AssertClose(8.5, ConsumptionConverter.ToLPer100Km(8.5, ConsumptionUnit.L_PER_100KM));
    return Task.CompletedTask;
}

static Task TestKmPerLiterConversion()
{
    AssertClose(10d, ConsumptionConverter.ToLPer100Km(10d, ConsumptionUnit.KM_PER_L));
    return Task.CompletedTask;
}

static Task TestUsMpgConversion()
{
    AssertClose(9.40858332, ConsumptionConverter.ToLPer100Km(25d, ConsumptionUnit.US_MPG));
    return Task.CompletedTask;
}

static Task TestUkMpgConversion()
{
    AssertClose(8.070883885714286, ConsumptionConverter.ToLPer100Km(35d, ConsumptionUnit.UK_MPG));
    return Task.CompletedTask;
}

static async Task TestFuelCostMath()
{
    var result = await CalculateAsync(
        [new CountryRouteSegment("KW", 100d)],
        new ManualFuelPriceOverride("KW", FuelType.GASOLINE_95, 0.100m, "KWD"));

    AssertClose(100d, result.TotalDistanceKm);
    AssertClose(10d, result.TotalFuelLiters);
    AssertEqual(1.000m, result.TotalCost);
    AssertEqual("Manual", result.Segments.Single().PriceSource);
}

static async Task TestPerCountryAggregation()
{
    var result = await CalculateAsync(
        [
            new CountryRouteSegment("KW", 50d),
            new CountryRouteSegment("SA", 100d),
            new CountryRouteSegment("KW", 50d)
        ],
        new ManualFuelPriceOverride("KW", FuelType.GASOLINE_95, 0.100m, "KWD"),
        new ManualFuelPriceOverride("SA", FuelType.GASOLINE_95, 2.000m, "SAR"));

    AssertEqual(2, result.Segments.Count);
    AssertClose(100d, result.Segments.Single(segment => segment.CountryCode == "KW").DistanceKm);
    AssertClose(100d, result.Segments.Single(segment => segment.CountryCode == "SA").DistanceKm);
}

static async Task TestManualOverridePrecedence()
{
    var manual = new ManualFuelPriceProvider(
    [
        new ManualFuelPriceOverride("KW", FuelType.GASOLINE_95, 0.200m, "KWD")
    ]);
    var provider = new CompositeFuelPriceProvider([manual, new StaticSeedFuelPriceProvider()]);
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput(
            [new CountryRouteSegment("KW", 100d)],
            FuelType.GASOLINE_95,
            10d,
            "KWD",
            null,
            null),
        provider,
        new StaticCurrencyConverter());

    var segment = result.Segments.Single();
    AssertEqual("Manual", segment.PriceSource);
    AssertEqual(2.000m, result.TotalCost);
    AssertTrue(segment.IsUserProvided, "Segment should be marked user-provided.");
}

static async Task TestMissingFuelPriceWarning()
{
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput(
            [new CountryRouteSegment("XX", 50d)],
            FuelType.GASOLINE_98,
            10d,
            "KWD",
            null,
            null),
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    AssertEqual(0m, result.TotalCost);
    AssertTrue(result.Warnings.Any(warning => warning.Contains("Missing fuel price", StringComparison.OrdinalIgnoreCase)));
}

static async Task TestMockRouteIntegration()
{
    var routeProvider = new MockRouteProvider();
    var route = await routeProvider.GetRouteAsync(new RouteRequest(
        "Kuwait City",
        "Doha, Qatar",
        []));
    var segmenter = new PolylineCountrySegmenter(
        PolygonCountryResolver.LoadEmbedded(),
        new RouteSegmentationOptions { SampleEveryKm = 25d });
    var segmentation = await segmenter.SegmentAsync(route);
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput(
            segmentation.Segments,
            FuelType.GASOLINE_95,
            8.5d,
            "KWD",
            70d,
            80d),
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter(),
        segmentation.Warnings);

    AssertTrue(route.TotalDistanceKm > 500d, "Mock route should have a realistic multi-country distance.");
    AssertTrue(result.Segments.Count >= 2, "Mock route should split across at least two countries.");
    AssertTrue(result.TotalFuelLiters > 0, "Fuel total should be positive.");
    AssertTrue(result.Warnings.Any(warning => warning.Contains("approximate", StringComparison.OrdinalIgnoreCase)));
    AssertTrue(result.FuelStops is not null, "Tank size should produce a fuel stop estimate.");
}

static async Task TestMockSyriaRoute()
{
    var routeProvider = new MockRouteProvider();
    var route = await routeProvider.GetRouteAsync(new RouteRequest(
        "Kuwait City",
        "Al Nabk, Syria",
        ["Hafar Al Batin Saudi Arabia", "Al Hadithah Saudi Arabia"]));
    var segmenter = new PolylineCountrySegmenter(
        PolygonCountryResolver.LoadEmbedded(),
        new RouteSegmentationOptions { SampleEveryKm = 25d });
    var segmentation = await segmenter.SegmentAsync(route);
    var countries = segmentation.Segments.Select(segment => segment.CountryCode).ToArray();

    AssertTrue(route.Points.Last().Latitude > 33d, "Syria route should end north of Jordan/Saudi, not in Doha.");
    AssertTrue(countries.Contains("SY"), "Syria route should include a Syria segment.");
    AssertTrue(!countries.Contains("QA"), "Syria route should not include Qatar/Doha fallback.");
}

static Task TestGoogleMapsPathParser()
{
    var parser = new GoogleMapsLinkParser();
    var parsedOk = parser.TryParse(
        "https://www.google.com/maps/dir/Home/Hafar+Al+Batin+Saudi+Arabia/Al+Hadithah+Saudi+Arabia/Al+Nabk,+Syria/@31.0,37.0,6z/data=!4m2",
        out var parsed);

    AssertTrue(parsedOk, "Full Google Maps directions path should parse.");
    AssertEqual("Home", parsed.Origin);
    AssertEqual("Al Nabk, Syria", parsed.Destination);
    AssertEqual(2, parsed.Waypoints.Count);
    AssertEqual("Hafar Al Batin Saudi Arabia", parsed.Waypoints[0]);
    AssertEqual("Al Hadithah Saudi Arabia", parsed.Waypoints[1]);
    return Task.CompletedTask;
}

static async Task TestMissingPriceMarksTotalIncomplete()
{
    var result = await CalculateAsync(
        [new CountryRouteSegment("KW", 100d), new CountryRouteSegment("XX", 100d)],
        new ManualFuelPriceOverride("KW", FuelType.GASOLINE_95, 0.100m, "KWD"));

    AssertTrue(!result.IsTotalComplete, "Total must be flagged incomplete when a country has no price.");
    AssertEqual("XX", result.CountriesMissingPrice.Single());
    AssertEqual(1.000m, result.TotalCost);
}

static async Task TestManualPriceDefaultsToCountryCurrency()
{
    var provider = new ManualFuelPriceProvider([new ManualFuelPriceOverride("SA", FuelType.GASOLINE_95, 2.33m, "")]);
    var quote = await provider.GetPriceAsync("SA", FuelType.GASOLINE_95);

    AssertEqual("SAR", quote!.Currency);
}

static async Task TestSeedFetchedAtIsSeedDate()
{
    var quote = await new StaticSeedFuelPriceProvider().GetPriceAsync("KW", FuelType.GASOLINE_95);

    AssertEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), quote!.FetchedAt);
}

static async Task TestDurationIsReturned()
{
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput([new CountryRouteSegment("KW", 100d)], FuelType.GASOLINE_95, 10d, "KWD", null, null)
        {
            DurationMinutes = 75d
        },
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    AssertEqual(75d, result.DurationMinutes);
}

static async Task TestUnsoldGradeFallsBack()
{
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput([new CountryRouteSegment("SA", 100d)], FuelType.GASOLINE_98, 10d, "SAR", null, null),
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    var segment = result.Segments.Single();
    AssertEqual(FuelType.GASOLINE_95, segment.FuelType);
    AssertEqual(23.300m, segment.LocalCost);
    AssertTrue(result.IsTotalComplete, "A substituted grade still gives a complete total.");
    AssertTrue(result.Warnings.Any(w => w.Contains("not sold in SA", StringComparison.Ordinal)), "Expected a substitution note.");
}

static async Task TestRefuelStopsKeepReserve()
{
    // 50 L tank at 10 L/100km = 500 km full range; 15% reserve leaves 425 km usable per tank.
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput([new CountryRouteSegment("KW", 900d)], FuelType.GASOLINE_95, 10d, "KWD", 50d, 100d),
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    AssertEqual(15d, result.FuelStops!.ReservePercentage);
    AssertClose(425d, result.FuelStops.UsableRangeKm);
    AssertEqual(2, result.FuelStops.EstimatedMinimumStops);
}

static async Task TestResolverRejectsNonGoogleRedirect()
{
    var handler = new RedirectingHandler(new Uri("https://evil.example/maps/dir/A/B"));
    var resolver = new GoogleMapsLinkResolver(new HttpClient(handler));

    var resolved = await resolver.ResolveAsync("https://maps.app.goo.gl/abc123");

    AssertEqual<string?>(null, resolved);
}

static async Task TestBorderTownsResolve()
{
    var resolver = PolygonCountryResolver.LoadEmbedded();
    (string Place, double Lat, double Lon, string Expected)[] places =
    [
        ("Kuwait City", 29.3759, 47.9774, "KW"),
        ("Al Khafji", 28.4328, 48.4913, "SA"),
        ("Hafar Al Batin", 28.4342, 45.9636, "SA"),
        ("Al Hadithah", 31.4700, 37.1400, "SA"),
        ("Irbid", 32.5556, 35.8500, "JO"),
        ("Amman", 31.9539, 35.9106, "JO"),
        ("Damascus", 33.5138, 36.2765, "SY"),
        ("Al-Qa'im", 34.3870, 41.0950, "IQ"),
        ("Beirut", 33.8938, 35.5018, "LB"),
        ("Doha", 25.2854, 51.5310, "QA"),
        ("Dubai", 25.2048, 55.2708, "AE"),
        ("Al Ain", 24.2075, 55.7447, "AE"),
        ("Muscat", 23.5880, 58.3829, "OM"),
        ("Riyadh", 24.7136, 46.6753, "SA")
    ];

    foreach (var (place, lat, lon, expected) in places)
    {
        var actual = await resolver.ResolveCountryCodeAsync(new GeoPoint(lat, lon));
        AssertTrue(actual == expected, $"{place}: expected {expected}, got {actual ?? "null"}.");
    }
}

static async Task TestCausewaySnapsToNearestCountry()
{
    var actual = await PolygonCountryResolver.LoadEmbedded().ResolveCountryCodeAsync(new GeoPoint(26.1850, 50.3600));
    AssertTrue(actual is "SA" or "BH", $"Causeway should snap to SA or BH, got {actual ?? "null"}.");
}

static async Task TestOpenSeaIsUnknown()
{
    var actual = await PolygonCountryResolver.LoadEmbedded().ResolveCountryCodeAsync(new GeoPoint(16.0, 62.0));
    AssertEqual<string?>(null, actual);
}

static async Task TestKuwaitSyriaSplit()
{
    var route = await new MockRouteProvider().GetRouteAsync(new RouteRequest(
        "Kuwait City",
        "Al Nabk, Syria",
        ["Hafar Al Batin Saudi Arabia", "Al Hadithah Saudi Arabia"]));
    var segmentation = await new PolylineCountrySegmenter(PolygonCountryResolver.LoadEmbedded()).SegmentAsync(route);
    var km = segmentation.Segments
        .GroupBy(segment => segment.CountryCode)
        .ToDictionary(group => group.Key, group => group.Sum(segment => segment.DistanceKm));

    AssertEqual("KW,SA,JO,SY", string.Join(",", segmentation.Segments.Select(segment => segment.CountryCode)));
    AssertTrue(km["SA"] > 900d, $"Hafar Al Batin to Al Hadithah is Saudi; got {km["SA"]:0} km of SA.");
    AssertTrue(km["JO"] < 200d, $"Only the Al Omari to Jaber stretch is Jordan; got {km["JO"]:0} km of JO.");
}

static async Task<TripEstimateResult> CalculateAsync(
    IReadOnlyList<CountryRouteSegment> segments,
    params ManualFuelPriceOverride[] overrides)
{
    var provider = new CompositeFuelPriceProvider([new ManualFuelPriceProvider(overrides), new StaticSeedFuelPriceProvider()]);

    return await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput(
            segments,
            FuelType.GASOLINE_95,
            10d,
            "KWD",
            null,
            null),
        provider,
        new StaticCurrencyConverter());
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void AssertClose(double expected, double actual, double tolerance = 0.000001d)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void AssertTrue(bool condition, string? message = null)
{
    if (!condition)
    {
        throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }
}

sealed class RedirectingHandler(Uri finalUri) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri)
        });
}
