using System.Text.Json.Serialization;
using FuelCalculator.Api.Contracts;
using FuelCalculator.Core.Calculation;
using FuelCalculator.Core.Currency;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Fuel;
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

builder.Services.AddSingleton<TripFuelCostCalculator>();
builder.Services.AddSingleton<StaticSeedFuelPriceProvider>();
builder.Services.AddSingleton<ICurrencyConverter, StaticCurrencyConverter>();
builder.Services.AddSingleton<ICountryResolver, StaticBoundingBoxCountryResolver>();
builder.Services.AddSingleton<IRouteCountrySegmenter>(sp =>
{
    var sampleEveryKm = builder.Configuration.GetValue<double?>("RouteSegmentation:SampleEveryKm") ?? 5d;
    return new PolylineCountrySegmenter(
        sp.GetRequiredService<ICountryResolver>(),
        new RouteSegmentationOptions { SampleEveryKm = sampleEveryKm });
});
builder.Services.AddSingleton<IGoogleMapsLinkParser, GoogleMapsLinkParser>();
builder.Services.AddSingleton<IVehicleProfileRepository, InMemoryVehicleProfileRepository>();
builder.Services.AddSingleton<IVehicleDataProvider, NullVehicleDataProvider>();
builder.Services.AddHttpClient<GoogleRoutesProvider>();
builder.Services.AddSingleton<IRouteProvider>(sp =>
{
    var mode = builder.Configuration.GetValue<string>("RouteProvider:Mode") ?? "Mock";

    if (!mode.Equals("Google", StringComparison.OrdinalIgnoreCase))
    {
        return new MockRouteProvider();
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

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

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
    [FromServices] ICurrencyConverter currencyConverter,
    [FromServices] IGoogleMapsLinkParser mapsLinkParser,
    [FromServices] TripFuelCostCalculator calculator,
    CancellationToken cancellationToken) =>
{
    var warnings = new List<string>();
    var errors = ValidateTripRequest(request, mapsLinkParser, warnings, out var normalizedRequest);

    if (errors.Count > 0 || normalizedRequest is null)
    {
        return Results.BadRequest(new { errors, warnings });
    }

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
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Route calculation failed.",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    var countrySegmentation = await countrySegmenter.SegmentAsync(route, cancellationToken);
    warnings.AddRange(countrySegmentation.Warnings);

    var manualProvider = new ManualFuelPriceProvider(normalizedRequest.ManualFuelPrices);
    var fuelPriceProvider = new CompositeFuelPriceProvider([manualProvider, seedFuelPriceProvider]);
    var result = await calculator.CalculateAsync(
        new TripFuelCostInput(
            countrySegmentation.Segments,
            normalizedRequest.FuelType,
            normalizedRequest.NormalizedConsumptionLPer100Km,
            normalizedRequest.OutputCurrency,
            normalizedRequest.TankSizeLiters,
            normalizedRequest.CurrentFuelPercentage),
        fuelPriceProvider,
        currencyConverter,
        warnings,
        cancellationToken);

    return Results.Ok(result);
});

app.Run();

static IReadOnlyList<string> ValidateTripRequest(
    EstimateTripRequest request,
    IGoogleMapsLinkParser mapsLinkParser,
    List<string> warnings,
    out NormalizedTripRequest? normalized)
{
    normalized = null;
    var errors = new List<string>();
    var origin = Clean(request.Origin);
    var destination = Clean(request.Destination);
    var waypoints = request.Waypoints
        .Where(waypoint => !string.IsNullOrWhiteSpace(waypoint))
        .Select(waypoint => waypoint.Trim())
        .ToList();

    if (!string.IsNullOrWhiteSpace(request.GoogleMapsLink))
    {
        if (mapsLinkParser.TryParse(request.GoogleMapsLink, out var parsed))
        {
            origin ??= Clean(parsed.Origin);
            destination ??= Clean(parsed.Destination);

            if (waypoints.Count == 0)
            {
                waypoints.AddRange(parsed.Waypoints.Where(waypoint => !string.IsNullOrWhiteSpace(waypoint)));
            }
        }
        else
        {
            warnings.Add("Google Maps link could not be parsed; enter origin and destination manually.");
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
            string.IsNullOrWhiteSpace(manualPrice.Currency) ? request.OutputCurrency : manualPrice.Currency.Trim().ToUpperInvariant(),
            manualPrice.EffectiveDate));
    }

    if (errors.Count > 0 || origin is null || destination is null)
    {
        return errors;
    }

    var normalizedConsumption = ConsumptionConverter.ToLPer100Km(request.ConsumptionValue, consumptionUnit);

    normalized = new NormalizedTripRequest(
        origin,
        destination,
        waypoints,
        fuelType,
        normalizedConsumption,
        string.IsNullOrWhiteSpace(request.OutputCurrency) ? "KWD" : request.OutputCurrency.Trim().ToUpperInvariant(),
        request.TankSizeLiters,
        request.CurrentFuelPercentage,
        manualPrices);

    return errors;
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
    IReadOnlyList<ManualFuelPriceOverride> ManualFuelPrices);
