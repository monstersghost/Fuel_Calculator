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
    ("Mock route and seed prices integration works", TestMockRouteIntegration)
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
        new StaticBoundingBoxCountryResolver(),
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
