using FuelCalculator.Core.Calculation;
using FuelCalculator.Core.Currency;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Fuel;
using FuelCalculator.Core.Planning;
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
    ("C5 seed prices report when they were checked, not startup time", TestSeedFetchedAtIsSeedDate),
    ("C6 route duration is returned", TestDurationIsReturned),
    ("C7 98 Octane in Qatar is priced as 95 with a note", TestUnsoldGradeFallsBack),
    ("C8 refuel stops keep a reserve", TestRefuelStopsKeepReserve),
    ("Short-link resolver rejects redirects off Google", TestResolverRejectsNonGoogleRedirect),
    ("C2 border towns resolve to the right country", TestBorderTownsResolve),
    ("C2 a point on the King Fahd Causeway snaps to SA or BH", TestCausewaySnapsToNearestCountry),
    ("C2 open sea far from land is unknown", TestOpenSeaIsUnknown),
    ("C2 Kuwait to Syria bills Saudi km to Saudi, not Jordan", TestKuwaitSyriaSplit),
    ("Seed carries November 2026 UAE prices from the Fuel Price Committee", TestSeedUaeNovember2026),
    ("Seed covers every route country with a petrol price", TestSeedCoversRouteCountries),
    ("Saudi 98 is now sold and priced", TestSaudi98IsPriced),
    ("A sold grade with no price uses the nearest priced grade", TestUnpricedGradeUsesNearestPriced),
    ("New Syrian pound converts at about 122 to the dollar", TestNewSyrianPoundRate),
    ("Planner fills up at the last cheap station before the border", TestPlannerFillsBeforeBorder),
    ("Planner makes no stop when the tank reaches the destination", TestPlannerNoStopWhenInRange),
    ("Planner buys only enough to reach cheaper fuel", TestPlannerBuysJustEnough),
    ("Planner respects a per-fill litre cap", TestPlannerRespectsCap),
    ("Planner flags a gap longer than the usable range", TestPlannerFlagsGap),
    ("Planner trip cost is purchases plus start fuel minus fuel left", TestPlannerTripCost),
    ("Virtual stations include the last point before and first after each border", TestVirtualStationsAtBorders),
    ("Virtual stations near Oman's borders carry the 30 litre cap", TestVirtualStationsOmanCap),
    ("Estimate includes a refuel plan and saving when a tank size is given", TestEstimateIncludesPlan),
    ("World borders resolve cities on every continent", TestWorldBorders),
    ("Seed has current EU Weekly Oil Bulletin prices", TestSeedEuBulletin),
    ("Seed prices at least 150 countries", TestSeedWorldCoverage),
    ("Every country knows its currency", TestCountryCurrencies),
    ("FX snapshot converts dollar pegs exactly", TestFxPegs),
    ("Old prices carry a staleness warning", TestStalePriceWarning),
    ("Route projection gives distance along and off the route", TestRouteProjection),
    ("Stations near the route are snapped to their km; far ones are dropped", TestStationSnapping),
    ("Overpass query asks for fuel stations in boxes along the route", TestOverpassQuery),
    ("Overpass response is parsed into stations, ways by their centre", TestOverpassParse),
    ("Estimate plans stops only at the stations it is given", TestEstimateUsesGivenStations),
    ("Real stations in Oman near a border get the 30 litre cap", TestRealStationsOmanCap),
    ("Planner top-up to the destination is not short by rounding", TestPlannerTopUpNotShortByRounding),
    ("Overpass falls back to the next server when one is busy", TestOverpassFallback)
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

    AssertEqual(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), quote!.FetchedAt);
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
        new TripFuelCostInput([new CountryRouteSegment("QA", 100d)], FuelType.GASOLINE_98, 10d, "QAR", null, null),
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    var segment = result.Segments.Single();
    AssertEqual(FuelType.GASOLINE_95, segment.FuelType);
    AssertEqual(21.000m, segment.LocalCost);
    AssertTrue(result.IsTotalComplete, "A substituted grade still gives a complete total.");
    AssertTrue(result.Warnings.Any(w => w.Contains("not sold in QA", StringComparison.Ordinal)), "Expected a substitution note.");
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

static async Task TestSeedUaeNovember2026()
{
    var quote = await new StaticSeedFuelPriceProvider().GetPriceAsync("AE", FuelType.GASOLINE_95);

    AssertEqual(4.04m, quote!.PricePerLiter);
    AssertEqual("AED", quote.Currency);
    AssertEqual(new DateOnly(2026, 11, 1), quote.EffectiveDate);
    AssertEqual(ConfidenceLevel.HIGH, quote.Confidence);
    AssertTrue(quote.SourceName.StartsWith("Seed", StringComparison.Ordinal), "Seed quotes say they are seed data.");
}

static async Task TestSeedCoversRouteCountries()
{
    var seed = new StaticSeedFuelPriceProvider();

    foreach (var country in new[] { "KW", "SA", "AE", "QA", "BH", "OM", "JO", "SY", "IQ", "LB", "EG" })
    {
        var quote = await seed.GetPriceAsync(country, FuelType.GASOLINE_95);
        AssertTrue(quote is not null, $"No seed 95 Octane price for {country}.");
    }
}

static async Task TestSaudi98IsPriced()
{
    var quote = await new StaticSeedFuelPriceProvider().GetPriceAsync("SA", FuelType.GASOLINE_98);

    AssertEqual(5.09m, quote!.PricePerLiter);
}

static async Task TestUnpricedGradeUsesNearestPriced()
{
    // Iraq sells regular petrol, but the only 2026 figure found is for 95.
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput([new CountryRouteSegment("IQ", 100d)], FuelType.GASOLINE_91, 10d, "IQD", null, null),
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    var segment = result.Segments.Single();
    AssertEqual(FuelType.GASOLINE_95, segment.FuelType);
    AssertTrue(result.IsTotalComplete, "An estimated grade still gives a complete total.");
    AssertTrue(result.Warnings.Any(w => w.Contains("No 91 Octane price for IQ", StringComparison.Ordinal)), "Expected an estimate note.");
}

static async Task TestNewSyrianPoundRate()
{
    var conversion = await new StaticCurrencyConverter().ConvertAsync(122m, "SYP", "USD");

    AssertTrue(conversion!.Amount is > 0.97m and < 1.03m, $"122 SYP should be about 1 USD, got {conversion.Amount}.");
}

// Shared planner scenario: 50 L tank, 10 L/100km (0.1 L/km), 10% reserve (5 L, 450 km usable range).
static RefuelPlanInput PlannerInput(
    double distanceKm,
    double startPercent,
    IReadOnlyList<RefuelCandidate> candidates,
    params (string Country, decimal Price)[] prices) =>
    new(
        distanceKm,
        10d,
        50d,
        startPercent,
        10d,
        candidates,
        prices.ToDictionary(
            price => price.Country,
            price => new RefuelPrice(price.Country, price.Price, "KWD", price.Price, "Test")));

static IReadOnlyList<RefuelCandidate> Stations(params (double Km, string Country)[] stations) =>
    stations.Select(station => new RefuelCandidate(station.Km, station.Country)).ToArray();

// KW (cheap) for the first 100 km, then SA (dear) to km 600.
static IReadOnlyList<RefuelCandidate> KuwaitThenSaudi() =>
    Stations((0, "KW"), (50, "KW"), (99, "KW"), (101, "SA"), (150, "SA"), (200, "SA"), (250, "SA"),
        (300, "SA"), (350, "SA"), (400, "SA"), (450, "SA"), (500, "SA"), (550, "SA"));

static Task TestPlannerFillsBeforeBorder()
{
    var plan = RefuelPlanner.Plan(PlannerInput(600d, 50d, KuwaitThenSaudi(), ("KW", 0.10m), ("SA", 0.25m)));

    AssertTrue(plan.IsFeasible, "Plan should be feasible.");
    AssertEqual(2, plan.Stops.Count);
    AssertClose(99d, plan.Stops[0].DistanceKm);
    AssertEqual("KW", plan.Stops[0].CountryCode);
    AssertClose(34.9d, plan.Stops[0].Liters, 0.001d);
    AssertClose(500d, plan.Stops[1].DistanceKm);
    AssertClose(5.1d, plan.Stops[1].Liters, 0.001d);
    AssertClose(5d, plan.ArrivalFuelLiters, 0.001d);
    return Task.CompletedTask;
}

static Task TestPlannerNoStopWhenInRange()
{
    var plan = RefuelPlanner.Plan(PlannerInput(300d, 100d, KuwaitThenSaudi(), ("KW", 0.10m), ("SA", 0.25m)));

    AssertEqual(0, plan.Stops.Count);
    AssertClose(20d, plan.ArrivalFuelLiters, 0.001d);
    return Task.CompletedTask;
}

static Task TestPlannerBuysJustEnough()
{
    // Dear SA first, cheap KW from km 100. Start with 10 L (50 km of usable range).
    var stations = Stations((0, "SA"), (50, "SA"), (101, "KW"), (150, "KW"), (200, "KW"));
    var plan = RefuelPlanner.Plan(PlannerInput(300d, 20d, stations, ("SA", 0.25m), ("KW", 0.10m)));

    var first = plan.Stops[0];
    AssertClose(50d, first.DistanceKm);
    AssertClose(5.1d, first.Liters, 0.001d);
    AssertTrue(first.Reason.Contains("cheaper", StringComparison.OrdinalIgnoreCase), $"Reason should mention cheaper fuel: {first.Reason}");
    AssertEqual("KW", plan.Stops[1].CountryCode);
    return Task.CompletedTask;
}

static Task TestPlannerRespectsCap()
{
    var stations = new[]
    {
        new RefuelCandidate(0d, "OM"),
        new RefuelCandidate(100d, "OM", MaxLiters: 30d),
        new RefuelCandidate(300d, "OM"),
        new RefuelCandidate(500d, "OM")
    };
    var plan = RefuelPlanner.Plan(PlannerInput(800d, 30d, stations, ("OM", 0.08m)));

    AssertTrue(plan.Stops.All(stop => stop.DistanceKm != 100d || stop.Liters <= 30d + 1e-9), "Capped station sold more than 30 L.");
    AssertTrue(plan.IsFeasible, "Plan should still be feasible using later stations.");
    return Task.CompletedTask;
}

static Task TestPlannerFlagsGap()
{
    var plan = RefuelPlanner.Plan(PlannerInput(900d, 100d, Stations((0, "SA"), (600, "SA")), ("SA", 0.25m)));

    AssertTrue(!plan.IsFeasible, "A 600 km gap with 450 km usable range is not feasible.");
    AssertTrue(plan.Warnings.Any(w => w.Contains("gap", StringComparison.OrdinalIgnoreCase)), "Expected a gap warning.");
    return Task.CompletedTask;
}

static Task TestPlannerTripCost()
{
    var plan = RefuelPlanner.Plan(PlannerInput(600d, 50d, KuwaitThenSaudi(), ("KW", 0.10m), ("SA", 0.25m)));

    // Bought 34.9 L x 0.10 + 5.1 L x 0.25 = 4.765; started with 25 L at the origin price 0.10 = 2.500;
    // arrive with 5 L valued at the last price paid 0.25 = 1.250. Trip cost = 4.765 + 2.5 - 1.25 = 6.015.
    AssertEqual(4.765m, plan.PurchasedCost);
    AssertEqual(2.500m, plan.StartFuelValue);
    AssertEqual(1.250m, plan.ArrivalFuelValue);
    AssertEqual(6.015m, plan.TripFuelCost);
    AssertTrue(plan.IsComplete, "All stops were priced.");
    return Task.CompletedTask;
}

static Task TestVirtualStationsAtBorders()
{
    var candidates = VirtualRefuelCandidates.FromSegments(
        [new CountryRouteSegment("KW", 100d), new CountryRouteSegment("SA", 500d)]);

    AssertTrue(candidates.Any(c => c.CountryCode == "KW" && Math.Abs(c.DistanceKm - 99d) < 1e-9), "Expected a last-chance KW station at km 99.");
    AssertTrue(candidates.Any(c => c.CountryCode == "SA" && Math.Abs(c.DistanceKm - 101d) < 1e-9), "Expected a first SA station at km 101.");
    AssertTrue(candidates.Any(c => c.DistanceKm == 0d), "Expected a station at the origin.");
    AssertTrue(candidates.All(c => c.DistanceKm < 600d), "No station at or past the destination.");
    AssertTrue(candidates.Zip(candidates.Skip(1)).All(pair => pair.Second.DistanceKm - pair.First.DistanceKm <= 50d + 1e-9), "Stations at most 50 km apart.");
    return Task.CompletedTask;
}

static Task TestVirtualStationsOmanCap()
{
    var candidates = VirtualRefuelCandidates.FromSegments(
        [new CountryRouteSegment("AE", 100d), new CountryRouteSegment("OM", 300d)]);

    AssertTrue(candidates.Where(c => c.CountryCode == "OM" && c.DistanceKm <= 150d).All(c => c.MaxLiters == 30d), "OM stations within 50 km of the border are capped.");
    AssertTrue(candidates.Where(c => c.CountryCode == "OM" && c.DistanceKm > 150d).All(c => c.MaxLiters is null), "OM stations further in are not capped.");
    AssertTrue(candidates.Where(c => c.CountryCode == "AE").All(c => c.MaxLiters is null), "Other countries are not capped.");
    return Task.CompletedTask;
}

static async Task TestEstimateIncludesPlan()
{
    // Kuwait 100 km then Saudi 500 km, 95 Octane at seed prices, 50 L tank half full, 10 L/100km.
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput(
            [new CountryRouteSegment("KW", 100d), new CountryRouteSegment("SA", 500d)],
            FuelType.GASOLINE_95,
            10d,
            "KWD",
            50d,
            50d)
        {
            ReservePercentage = 10d
        },
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    var plan = result.RefuelPlan!;
    AssertTrue(plan.Stops.Count > 0, "Expected at least one stop.");
    AssertEqual("KW", plan.Stops[0].CountryCode);
    AssertTrue(plan.TripFuelCost < result.TotalCost, $"Filling in Kuwait should beat paying Saudi prices for Saudi km: {plan.TripFuelCost} vs {result.TotalCost}.");
    AssertEqual(result.TotalCost - plan.TripFuelCost, result.RefuelSaving);
}

static async Task TestWorldBorders()
{
    var resolver = PolygonCountryResolver.LoadEmbedded();
    (string Place, double Lat, double Lon, string Expected)[] places =
    [
        ("Berlin", 52.5200, 13.4050, "DE"),
        ("Paris", 48.8566, 2.3522, "FR"),
        ("New York", 40.7128, -74.0060, "US"),
        ("Mexico City", 19.4326, -99.1332, "MX"),
        ("Sao Paulo", -23.5505, -46.6333, "BR"),
        ("Cape Town", -33.9249, 18.4241, "ZA"),
        ("Tokyo", 35.6762, 139.6503, "JP"),
        ("Sydney", -33.8688, 151.2093, "AU"),
        ("Delhi", 28.6139, 77.2090, "IN"),
        ("Kuwait City", 29.3759, 47.9774, "KW")
    ];

    foreach (var (place, lat, lon, expected) in places)
    {
        var actual = await resolver.ResolveCountryCodeAsync(new GeoPoint(lat, lon));
        AssertTrue(actual == expected, $"{place}: expected {expected}, got {actual ?? "null"}.");
    }
}

static async Task TestSeedEuBulletin()
{
    var quote = await new StaticSeedFuelPriceProvider().GetPriceAsync("DE", FuelType.GASOLINE_95);

    AssertEqual(1.964m, quote!.PricePerLiter);
    AssertEqual("EUR", quote.Currency);
    AssertEqual(new DateOnly(2026, 11, 23), quote.EffectiveDate);
    AssertEqual(ConfidenceLevel.HIGH, quote.Confidence);
}

static Task TestSeedWorldCoverage()
{
    var countries = new StaticSeedFuelPriceProvider().ListPrices().Select(quote => quote.CountryCode).Distinct().Count();

    AssertTrue(countries >= 150, $"Expected at least 150 priced countries, got {countries}.");
    return Task.CompletedTask;
}

static Task TestCountryCurrencies()
{
    AssertEqual("JPY", CountryCatalog.GetLocalCurrency("JP"));
    AssertEqual("EUR", CountryCatalog.GetLocalCurrency("DE"));
    AssertEqual("BRL", CountryCatalog.GetLocalCurrency("BR"));
    AssertEqual("KWD", CountryCatalog.GetLocalCurrency("KW"));
    return Task.CompletedTask;
}

static async Task TestFxPegs()
{
    var converter = new StaticCurrencyConverter();
    var sar = await converter.ConvertAsync(3.75m, "SAR", "USD");
    var aed = await converter.ConvertAsync(1m, "USD", "AED");

    AssertEqual(1.0000m, Math.Round(sar!.Amount, 4));
    AssertEqual(3.6725m, Math.Round(aed!.Amount, 4));
    AssertEqual(new DateOnly(2026, 11, 27), StaticCurrencyConverter.SnapshotDate);
}

static async Task TestStalePriceWarning()
{
    // Japan's only open price is the World Bank's, from early 2025.
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput([new CountryRouteSegment("JP", 100d)], FuelType.GASOLINE_95, 10d, "JPY", null, null),
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    AssertTrue(result.Warnings.Any(w => w.Contains("JP price is from", StringComparison.Ordinal) && w.Contains("2025", StringComparison.Ordinal)),
        "Expected a warning that Japan's price is old.");
}

// A straight route due east along the equator: 1 degree of longitude is about 111.2 km.
static IReadOnlyList<GeoPoint> EquatorRoute() => [new GeoPoint(0d, 0d), new GeoPoint(0d, 1d), new GeoPoint(0d, 2d)];

static Task TestRouteProjection()
{
    var projection = new RouteProjection(EquatorRoute());
    var (alongKm, offsetKm) = projection.Project(new GeoPoint(0.01d, 0.5d));

    AssertClose(222.39d, projection.TotalKm, 0.5d);
    AssertClose(55.6d, alongKm, 0.3d);
    AssertClose(1.11d, offsetKm, 0.05d);
    return Task.CompletedTask;
}

static async Task TestStationSnapping()
{
    var stations = new[]
    {
        new FuelStation(1, 0.01d, 0.5d, "Near", null),
        new FuelStation(2, 0.05d, 1.5d, "Far", null)
    };
    var candidates = await FuelStationSnapper.SnapAsync(
        new RouteProjection(EquatorRoute()),
        stations,
        corridorKm: 2d,
        new FixedCountryResolver("XX"));

    AssertEqual(1, candidates.Count);
    AssertEqual("Near", candidates[0].Name);
    AssertEqual("XX", candidates[0].CountryCode);
    AssertClose(55.6d, candidates[0].DistanceKm, 0.3d);
}

static Task TestOverpassQuery()
{
    // 222 km of route in pieces of at most 50 km: five boxes, each widened by the 2 km corridor.
    var query = OverpassFuelStationProvider.BuildQuery(EquatorRoute(), corridorKm: 2d);
    var boxes = query.Split("nwr[amenity=fuel](").Length - 1;

    AssertEqual(5, boxes);
    AssertTrue(query.Contains("nwr[amenity=fuel](-0.01809,-0.01797,0.01809,", StringComparison.Ordinal), query);
    AssertTrue(query.EndsWith(");out center tags;", StringComparison.Ordinal), query);
    return Task.CompletedTask;
}

static Task TestOverpassParse()
{
    const string json = """
        {"elements":[
          {"type":"node","id":11,"lat":29.1,"lon":47.9,"tags":{"amenity":"fuel","name":"KNPC 55","brand":"KNPC"}},
          {"type":"way","id":22,"center":{"lat":28.4,"lon":46.0},"tags":{"amenity":"fuel","brand":"Aldrees"}},
          {"type":"node","id":33,"lat":28.0,"lon":45.0}
        ]}
        """;
    var stations = OverpassFuelStationProvider.Parse(json);

    AssertEqual(3, stations.Count);
    AssertEqual("KNPC 55", stations[0].Name);
    AssertEqual("Aldrees", stations[1].Name);
    AssertClose(28.4d, stations[1].Latitude);
    AssertEqual<string?>(null, stations[2].Name);
    return Task.CompletedTask;
}

static async Task TestEstimateUsesGivenStations()
{
    var result = await new TripFuelCostCalculator().CalculateAsync(
        new TripFuelCostInput([new CountryRouteSegment("SA", 900d)], FuelType.GASOLINE_95, 10d, "SAR", 50d, 100d)
        {
            ReservePercentage = 10d,
            Stations = [new RefuelCandidate(300d, "SA", "Station A"), new RefuelCandidate(420d, "SA", "Station B")]
        },
        new StaticSeedFuelPriceProvider(),
        new StaticCurrencyConverter());

    var plan = result.RefuelPlan!;
    AssertTrue(plan.Stops.All(stop => stop.Name is "Station A" or "Station B"), "Stops must be at the given stations.");
    AssertTrue(plan.Stops.Count > 0, "A 900 km trip on a 450 km tank needs a stop.");
    AssertTrue(!plan.Warnings.Any(w => w.Contains("placed along the route by country", StringComparison.Ordinal)), "Real stations need no stand-in note.");
}

static Task TestRealStationsOmanCap()
{
    var candidates = CountryRefuelRules.Apply(
        [new RefuelCandidate(120d, "OM"), new RefuelCandidate(250d, "OM"), new RefuelCandidate(50d, "AE")],
        [new CountryRouteSegment("AE", 100d), new CountryRouteSegment("OM", 300d)]);

    AssertEqual(30d, candidates.Single(c => c.DistanceKm == 120d).MaxLiters);
    AssertEqual<double?>(null, candidates.Single(c => c.DistanceKm == 250d).MaxLiters);
    AssertEqual<double?>(null, candidates.Single(c => c.DistanceKm == 50d).MaxLiters);
    return Task.CompletedTask;
}

static Task TestPlannerTopUpNotShortByRounding()
{
    // Needs 10.01234 L at km 0 to arrive with the 5 L reserve; rounding that down would leave it short.
    var plan = RefuelPlanner.Plan(PlannerInput(300.1234d, 50d, Stations((0, "SA")), ("SA", 0.25m)));

    AssertTrue(plan.IsFeasible, $"Plan should be feasible: {string.Join(" ", plan.Warnings)}");
    AssertTrue(plan.ArrivalFuelLiters >= 5d - 1e-9, $"Should arrive with the reserve, got {plan.ArrivalFuelLiters} L.");
    return Task.CompletedTask;
}

static async Task TestOverpassFallback()
{
    var handler = new OverpassStubHandler();
    var provider = new OverpassFuelStationProvider(new HttpClient(handler), new OverpassOptions
    {
        Endpoints = ["https://busy.example/api/interpreter", "https://ok.example/api/interpreter"]
    });

    var stations = await provider.GetStationsAsync(EquatorRoute(), 2d);

    AssertEqual(1, stations.Count);
    AssertEqual("busy.example,ok.example", string.Join(",", handler.Hosts));
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

sealed class FixedCountryResolver(string code) : ICountryResolver
{
    public Task<string?> ResolveCountryCodeAsync(GeoPoint point, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(code);
}

sealed class OverpassStubHandler : HttpMessageHandler
{
    public List<string> Hosts { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Hosts.Add(request.RequestUri!.Host);

        return Task.FromResult(request.RequestUri.Host == "busy.example"
            ? new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"elements":[{"type":"node","id":1,"lat":0.001,"lon":0.5,"tags":{"name":"Equator Fuel"}}]}""")
            });
    }
}
