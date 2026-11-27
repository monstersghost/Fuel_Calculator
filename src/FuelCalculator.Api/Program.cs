using System.Text.Json.Serialization;
using FuelCalculator.Api;
using FuelCalculator.Api.Contracts;
using FuelCalculator.Core.Borders;
using FuelCalculator.Core.Calculation;
using FuelCalculator.Core.Currency;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Fuel;
using FuelCalculator.Core.Planning;
using FuelCalculator.Core.Routing;
using FuelCalculator.Core.Segmentation;
using FuelCalculator.Core.Vehicles;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithHeaders("Content-Type")
            .WithMethods("GET", "POST");
    });
});

builder.Services.AddSingleton<TripFuelCostCalculator>();
builder.Services.AddSingleton(_ => BorderCatalog.LoadEmbedded());
builder.Services.AddSingleton<BorderPlanner>();
builder.Services.AddSingleton<StaticSeedFuelPriceProvider>();
builder.Services.AddHttpClient("eu-bulletin", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FuelCalculator/0.1 (route fuel planning)");
});
builder.Services.AddSingleton(sp => new EuOilBulletinPriceProvider(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("eu-bulletin"),
    builder.Configuration["FuelPrices:EuBulletinCacheFile"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelCalculator", "eu-oil-bulletin.json")));
if (builder.Configuration.GetValue<bool?>("FuelPrices:EuBulletinLive") ?? true)
{
    builder.Services.AddHostedService<EuBulletinRefresher>();
}
builder.Services.AddSingleton<ICurrencyConverter, StaticCurrencyConverter>();
builder.Services.AddSingleton<ICountryResolver>(_ => PolygonCountryResolver.LoadEmbedded());
builder.Services.AddSingleton<IRouteCountrySegmenter>(sp =>
{
    var sampleEveryKm = builder.Configuration.GetValue<double?>("RouteSegmentation:SampleEveryKm") ?? 5d;
    return new PolylineCountrySegmenter(
        sp.GetRequiredService<ICountryResolver>(),
        new RouteSegmentationOptions { SampleEveryKm = sampleEveryKm });
});
builder.Services.AddSingleton<IGoogleMapsLinkParser, GoogleMapsLinkParser>();
builder.Services.AddHttpClient<IGoogleMapsLinkResolver, GoogleMapsLinkResolver>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IVehicleProfileRepository, InMemoryVehicleProfileRepository>();
builder.Services.AddSingleton<IVehicleDataProvider, NullVehicleDataProvider>();
builder.Services.AddHttpClient<GoogleRoutesProvider>();

// Routes: "Osrm" (OpenStreetMap roads, no key; the default), "Google" (needs GoogleRoutes:ApiKey) or "Mock".
// Fuel stations: "Overpass" (OpenStreetMap), "Virtual" (stand-ins by country) or "Auto" (Overpass for real
// road routes, Virtual for the mock routes, whose straight lines don't follow roads).
var routeMode = builder.Configuration.GetValue<string>("RouteProvider:Mode") ?? "Osrm";
var stationsMode = builder.Configuration.GetValue<string>("FuelStations:Mode") ?? "Auto";
var useOverpass = stationsMode.Equals("Overpass", StringComparison.OrdinalIgnoreCase)
    || (stationsMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) && !routeMode.Equals("Mock", StringComparison.OrdinalIgnoreCase));
var stationOptions = new FuelStationSettings(
    useOverpass,
    builder.Configuration.GetValue<double?>("FuelStations:CorridorKm") ?? 2d,
    TimeSpan.FromSeconds(builder.Configuration.GetValue<double?>("FuelStations:TimeoutSeconds") ?? 60d));
builder.Services.AddSingleton(stationOptions);
builder.Services.AddHttpClient("overpass", client => client.Timeout = stationOptions.Timeout);
builder.Services.AddSingleton(sp => new OverpassFuelStationProvider(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("overpass"),
    new OverpassOptions
    {
        CacheFolder = builder.Configuration["FuelStations:CacheFolder"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelCalculator", "station-tiles"),
        Endpoints = builder.Configuration.GetSection("FuelStations:Endpoints").Get<string[]>() is { Length: > 0 } endpoints
            ? endpoints
            : new OverpassOptions().Endpoints
    }));
builder.Services.AddSingleton(sp =>
{
    var prefetcher = new StationPrefetcher(sp.GetRequiredService<OverpassFuelStationProvider>());
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("FuelStations");
    prefetcher.Failed += ex => log.LogWarning(ex, "Background fuel station fetch failed; the tiles will be retried on the next estimate.");
    return prefetcher;
});
builder.Services.AddHttpClient("osm", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IGeocoder>(sp => new NominatimGeocoder(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("osm"),
    new NominatimOptions { Endpoint = builder.Configuration["Osrm:GeocoderEndpoint"] ?? new NominatimOptions().Endpoint }));
builder.Services.AddSingleton<IRouteProvider>(sp =>
{
    if (routeMode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
    {
        return new MockRouteProvider();
    }

    if (routeMode.Equals("Osrm", StringComparison.OrdinalIgnoreCase))
    {
        return new OsrmRouteProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("osm"),
            sp.GetRequiredService<IGeocoder>(),
            new OsrmOptions { Endpoint = builder.Configuration["Osrm:Endpoint"] ?? new OsrmOptions().Endpoint });
    }

    var options = new GoogleRoutesOptions
    {
        ApiKey = builder.Configuration["GoogleRoutes:ApiKey"],
        Endpoint = builder.Configuration["GoogleRoutes:Endpoint"] ?? new GoogleRoutesOptions().Endpoint
    };

    return new GoogleRoutesProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleRoutesProvider)), options);
});

var app = builder.Build();

app.UseCors("Frontend");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", routeProvider = routeMode, fuelStations = useOverpass ? "OpenStreetMap" : "Virtual" }));

app.MapGet("/api/fuel-types", () => FuelTypeCatalog.All.Select(fuelType => new
{
    value = fuelType.ToString(),
    displayName = FuelTypeCatalog.GetDisplayName(fuelType)
}));

app.MapGet("/api/consumption-units", () => ConsumptionUnitCatalog.All.Select(unit => new
{
    value = unit.ToString(),
    displayName = ConsumptionUnitCatalog.GetDisplayName(unit)
}));

app.MapGet("/api/currencies", () => Results.Ok(new
{
    asOf = StaticCurrencyConverter.SnapshotDate,
    attribution = "Rates By Exchange Rate API",
    attributionUrl = "https://www.exchangerate-api.com",
    currencies = StaticCurrencyConverter.Currencies
}));

app.MapGet("/api/fuel-prices/seed", ([FromServices] StaticSeedFuelPriceProvider provider) =>
    Results.Ok(provider.ListPrices()));

app.MapGet("/api/vehicle-profiles", async (
    [FromServices] IVehicleProfileRepository repository,
    CancellationToken cancellationToken) =>
    Results.Ok(await repository.ListAsync(cancellationToken)));

app.MapGet("/api/vehicle-profiles/{id:guid}", async (
    Guid id,
    [FromServices] IVehicleProfileRepository repository,
    CancellationToken cancellationToken) =>
{
    var profile = await repository.GetAsync(id, cancellationToken);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
});

app.MapPost("/api/vehicle-profiles", async (
    VehicleProfileRequest request,
    [FromServices] IVehicleProfileRepository repository,
    CancellationToken cancellationToken) =>
{
    var validation = ValidateVehicleProfile(request, out var draft);

    if (validation.Count > 0 || draft is null)
    {
        return Results.BadRequest(new { errors = validation });
    }

    var profile = await repository.SaveAsync(draft, cancellationToken);
    return Results.Created($"/api/vehicle-profiles/{profile.Id}", profile);
});

app.MapPost("/api/trips/estimate", async (
    EstimateTripRequest request,
    [FromServices] IRouteProvider routeProvider,
    [FromServices] IRouteCountrySegmenter countrySegmenter,
    [FromServices] StaticSeedFuelPriceProvider seedFuelPriceProvider,
    [FromServices] EuOilBulletinPriceProvider euBulletinPriceProvider,
    [FromServices] ICurrencyConverter currencyConverter,
    [FromServices] IGoogleMapsLinkParser mapsLinkParser,
    [FromServices] IGoogleMapsLinkResolver mapsLinkResolver,
    [FromServices] TripFuelCostCalculator calculator,
    [FromServices] ILogger<Program> logger,
    [FromServices] OverpassFuelStationProvider stationProvider,
    [FromServices] StationPrefetcher stationPrefetcher,
    [FromServices] IHostApplicationLifetime lifetime,
    [FromServices] FuelStationSettings stationSettings,
    [FromServices] ICountryResolver countryResolver,
    [FromServices] BorderPlanner borderPlanner,
    CancellationToken cancellationToken) =>
{
    var warnings = new List<string>();
    var validation = await ValidateTripRequestAsync(
        request,
        mapsLinkParser,
        mapsLinkResolver,
        warnings,
        cancellationToken);

    if (validation.Errors.Count > 0 || validation.NormalizedRequest is null)
    {
        return Results.BadRequest(new { errors = validation.Errors, warnings });
    }

    var normalizedRequest = validation.NormalizedRequest;

    RouteResult route;

    try
    {
        route = await routeProvider.GetRouteAsync(
            new RouteRequest(
                normalizedRequest.Origin,
                normalizedRequest.Destination,
                normalizedRequest.Waypoints),
            cancellationToken);
    }
    catch (RouteNotFoundException ex)
    {
        return Results.Problem(title: "Route not found.", detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogError(ex, "Route calculation failed.");
        return Results.Problem(
            title: "Route calculation failed.",
            detail: "The route provider could not calculate this route. Check the stops and try again.",
            statusCode: StatusCodes.Status502BadGateway);
    }

    var countrySegmentation = await countrySegmenter.SegmentAsync(route, cancellationToken);
    warnings.AddRange(countrySegmentation.Warnings);

    IReadOnlyList<RefuelCandidate> stations = [];

    if (stationSettings.UseOverpass && normalizedRequest.TankSizeLiters is not null && route.Points.Count >= 2)
    {
        // Never wait on Overpass: plan with what is cached, and fetch the rest in the background.
        var lookup = stationProvider.Lookup(route.Points, stationSettings.CorridorKm);

        if (lookup.IsComplete)
        {
            stations = await FuelStationSnapper.SnapAsync(
                new RouteProjection(route.Points), lookup.Stations, stationSettings.CorridorKm, countryResolver, normalizedRequest.FuelType, cancellationToken);

            if (stations.Count == 0)
            {
                warnings.Add("No fuel stations were found near the route in OpenStreetMap; stops are placed by country instead.");
            }
        }
        else
        {
            stationPrefetcher.Queue(lookup.Missing, lifetime.ApplicationStopping);
            var ready = lookup.TotalTiles - lookup.Missing.Count;
            warnings.Add($"Real fuel stations are loading from OpenStreetMap ({ready} of {lookup.TotalTiles} map tiles ready); stops are placed by country for now. Recalculate in a minute or two.");
        }
    }

    var manualProvider = new ManualFuelPriceProvider(normalizedRequest.ManualFuelPrices);
    var fuelPriceProvider = new CompositeFuelPriceProvider([manualProvider, euBulletinPriceProvider, seedFuelPriceProvider]);
    var result = await calculator.CalculateAsync(
        new TripFuelCostInput(
            countrySegmentation.Segments,
            normalizedRequest.FuelType,
            normalizedRequest.NormalizedConsumptionLPer100Km,
            normalizedRequest.OutputCurrency,
            normalizedRequest.TankSizeLiters,
            normalizedRequest.CurrentFuelPercentage)
        {
            DurationMinutes = route.Duration?.TotalMinutes,
            ReservePercentage = normalizedRequest.ReservePercentage,
            Stations = stations,
            TravelDate = request.DepartureTime is { } departure ? DateOnly.FromDateTime(departure) : null
        },
        fuelPriceProvider,
        currencyConverter,
        warnings,
        cancellationToken);

    if (route.Points.Count >= 2)
    {
        var transitions = BorderDetector.Detect(countrySegmentation.Segments, new RouteProjection(route.Points));
        var drivingMinutes = route.Duration?.TotalMinutes ?? result.TotalDistanceKm / 80d * 60d;
        var crossings = await borderPlanner.PlanAsync(
            transitions,
            result.OutputCurrency,
            request.Passengers ?? 1,
            request.DepartureTime,
            drivingMinutes,
            result.TotalDistanceKm,
            cancellationToken);
        var allCostsConverted = crossings.SelectMany(crossing => crossing.Costs).All(cost => cost.ConvertedAmount is not null);

        result = result with
        {
            BorderCrossings = crossings,
            BorderCostTotal = allCostsConverted
                ? Math.Round(crossings.SelectMany(crossing => crossing.Costs).Sum(cost => cost.ConvertedAmount!.Value), 3)
                : null,
            Timing = TripTiming.Create(
                request.DepartureTime,
                drivingMinutes,
                crossings.Sum(crossing => crossing.DelayMinutes),
                result.RefuelPlan?.Stops.Count ?? 0)
        };
    }

    return Results.Ok(result);
});

app.Run();

static async Task<TripRequestValidationResult> ValidateTripRequestAsync(
    EstimateTripRequest request,
    IGoogleMapsLinkParser mapsLinkParser,
    IGoogleMapsLinkResolver mapsLinkResolver,
    List<string> warnings,
    CancellationToken cancellationToken)
{
    var errors = new List<string>();
    var origin = Clean(request.Origin);
    var destination = Clean(request.Destination);
    var waypoints = request.Waypoints
        .Where(waypoint => !string.IsNullOrWhiteSpace(waypoint))
        .Select(waypoint => waypoint.Trim())
        .ToList();

    if (!string.IsNullOrWhiteSpace(request.GoogleMapsLink))
    {
        var linkToParse = request.GoogleMapsLink;

        try
        {
            linkToParse = await mapsLinkResolver.ResolveAsync(request.GoogleMapsLink, cancellationToken) ?? request.GoogleMapsLink;
        }
        catch
        {
            warnings.Add("Google Maps short link could not be expanded; enter route stops manually or paste a full Google Maps directions URL.");
        }

        if (mapsLinkParser.TryParse(linkToParse, out var parsed))
        {
            origin = Clean(parsed.Origin);
            destination = Clean(parsed.Destination);
            waypoints = parsed.Waypoints
                .Where(waypoint => !string.IsNullOrWhiteSpace(waypoint))
                .Select(waypoint => waypoint.Trim())
                .ToList();
        }
        else if (!warnings.Any(warning => warning.StartsWith("Google Maps", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Google Maps link could not be parsed; enter route stops manually or paste a full Google Maps directions URL.");
        }
    }

    if (string.IsNullOrWhiteSpace(origin))
    {
        errors.Add("Origin is required.");
    }

    if (string.IsNullOrWhiteSpace(destination))
    {
        errors.Add("Destination is required.");
    }

    if (!FuelTypeCatalog.TryParse(request.FuelType, out var fuelType))
    {
        errors.Add("Unsupported fuel type.");
    }

    if (!ConsumptionUnitCatalog.TryParse(request.ConsumptionUnit, out var consumptionUnit))
    {
        errors.Add("Unsupported consumption unit.");
    }

    if (request.ConsumptionValue <= 0)
    {
        errors.Add("Consumption value must be greater than zero.");
    }

    if (request.TankSizeLiters is <= 0)
    {
        errors.Add("Tank size must be greater than zero when provided.");
    }

    if (request.CurrentFuelPercentage is < 0 or > 100)
    {
        errors.Add("Current fuel percentage must be between 0 and 100.");
    }

    if (request.ReservePercentage is < 0 or >= 100)
    {
        errors.Add("Reserve percentage must be at least 0 and below 100.");
    }

    if (request.Passengers is < 1 or > 50)
    {
        errors.Add("Passengers must be between 1 and 50.");
    }

    var manualPrices = new List<ManualFuelPriceOverride>();

    foreach (var manualPrice in request.ManualFuelPrices)
    {
        var priceFuelType = fuelType;

        if (!string.IsNullOrWhiteSpace(manualPrice.FuelType)
            && !FuelTypeCatalog.TryParse(manualPrice.FuelType, out priceFuelType))
        {
            errors.Add($"Unsupported fuel type for manual price in {manualPrice.CountryCode}.");
            continue;
        }

        if (string.IsNullOrWhiteSpace(manualPrice.CountryCode))
        {
            errors.Add("Manual fuel price country code is required.");
            continue;
        }

        if (manualPrice.PricePerLiter <= 0)
        {
            errors.Add($"Manual fuel price for {manualPrice.CountryCode} must be greater than zero.");
            continue;
        }

        manualPrices.Add(new ManualFuelPriceOverride(
            manualPrice.CountryCode.Trim().ToUpperInvariant(),
            priceFuelType,
            manualPrice.PricePerLiter,
            string.IsNullOrWhiteSpace(manualPrice.Currency) ? string.Empty : manualPrice.Currency.Trim().ToUpperInvariant(),
            manualPrice.EffectiveDate));
    }

    if (errors.Count > 0 || origin is null || destination is null)
    {
        return new TripRequestValidationResult(errors, null);
    }

    var normalizedConsumption = ConsumptionConverter.ToLPer100Km(request.ConsumptionValue, consumptionUnit);

    var normalized = new NormalizedTripRequest(
        origin,
        destination,
        waypoints,
        fuelType,
        normalizedConsumption,
        string.IsNullOrWhiteSpace(request.OutputCurrency) ? "KWD" : request.OutputCurrency.Trim().ToUpperInvariant(),
        request.TankSizeLiters,
        request.CurrentFuelPercentage,
        request.ReservePercentage ?? 15d,
        manualPrices);

    return new TripRequestValidationResult(errors, normalized);
}

static IReadOnlyList<string> ValidateVehicleProfile(VehicleProfileRequest request, out VehicleProfileDraft? draft)
{
    draft = null;
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        errors.Add("Vehicle profile name is required.");
    }

    if (!FuelTypeCatalog.TryParse(request.FuelType, out var fuelType))
    {
        errors.Add("Unsupported fuel type.");
    }

    if (!ConsumptionUnitCatalog.TryParse(request.ConsumptionUnit, out var consumptionUnit))
    {
        errors.Add("Unsupported consumption unit.");
    }

    if (request.ConsumptionValue <= 0)
    {
        errors.Add("Consumption value must be greater than zero.");
    }

    if (request.TankSizeLiters is <= 0)
    {
        errors.Add("Tank size must be greater than zero when provided.");
    }

    if (errors.Count > 0)
    {
        return errors;
    }

    draft = new VehicleProfileDraft(
        request.Name,
        request.Year,
        request.Make,
        request.Model,
        fuelType,
        request.ConsumptionValue,
        consumptionUnit,
        request.TankSizeLiters);

    return errors;
}

static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

internal sealed record NormalizedTripRequest(
    string Origin,
    string Destination,
    IReadOnlyList<string> Waypoints,
    FuelType FuelType,
    double NormalizedConsumptionLPer100Km,
    string OutputCurrency,
    double? TankSizeLiters,
    double? CurrentFuelPercentage,
    double ReservePercentage,
    IReadOnlyList<ManualFuelPriceOverride> ManualFuelPrices);

internal sealed record FuelStationSettings(bool UseOverpass, double CorridorKm, TimeSpan Timeout);

internal sealed record TripRequestValidationResult(
    IReadOnlyList<string> Errors,
    NormalizedTripRequest? NormalizedRequest);
