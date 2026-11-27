namespace FuelCalculator.Core.Domain;

public sealed record GeoPoint(double Latitude, double Longitude);

public sealed record RouteRequest(
    string Origin,
    string Destination,
    IReadOnlyList<string> Waypoints);

public sealed record RouteStep(
    double DistanceKm,
    TimeSpan? Duration,
    string? EncodedPolyline,
    string? Instructions);

public sealed record RouteLeg(
    string StartLabel,
    string EndLabel,
    double DistanceKm,
    TimeSpan? Duration,
    string? EncodedPolyline,
    IReadOnlyList<RouteStep> Steps);

public sealed record RouteResult(
    double TotalDistanceKm,
    TimeSpan? Duration,
    string EncodedPolyline,
    IReadOnlyList<GeoPoint> Points,
    IReadOnlyList<RouteLeg> Legs);

public sealed record CountryRouteSegment(
    string CountryCode,
    double DistanceKm);

public sealed record RouteCountrySegmentationResult(
    IReadOnlyList<CountryRouteSegment> Segments,
    IReadOnlyList<string> Warnings);

public sealed record FuelPriceQuote(
    string CountryCode,
    FuelType FuelType,
    decimal PricePerLiter,
    string Currency,
    string SourceName,
    DateOnly? EffectiveDate,
    DateTimeOffset FetchedAt,
    ConfidenceLevel Confidence,
    bool IsUserProvided);

public sealed record ManualFuelPriceOverride(
    string CountryCode,
    FuelType FuelType,
    decimal PricePerLiter,
    string Currency,
    DateOnly? EffectiveDate = null);

public sealed record VehicleProfile(
    Guid Id,
    string Name,
    int? Year,
    string? Make,
    string? Model,
    FuelType FuelType,
    double ConsumptionValue,
    ConsumptionUnit ConsumptionUnit,
    double NormalizedConsumptionLPer100Km,
    double? TankSizeLiters);

public sealed record VehicleProfileDraft(
    string Name,
    int? Year,
    string? Make,
    string? Model,
    FuelType FuelType,
    double ConsumptionValue,
    ConsumptionUnit ConsumptionUnit,
    double? TankSizeLiters);

public sealed record CurrencyConversionResult(
    decimal Amount,
    string FromCurrency,
    string ToCurrency,
    decimal Rate);

public sealed record TripFuelCostInput(
    IReadOnlyList<CountryRouteSegment> CountrySegments,
    FuelType FuelType,
    double NormalizedConsumptionLPer100Km,
    string OutputCurrency,
    double? TankSizeLiters,
    double? CurrentFuelPercentage)
{
    public double? DurationMinutes { get; init; }

    /// <summary>Share of the tank kept in hand when planning refuels; never planned below this.</summary>
    public double ReservePercentage { get; init; } = 15d;

    /// <summary>Day of travel; used to warn when a regulated price may change before the trip.</summary>
    public DateOnly? TravelDate { get; init; }

    /// <summary>Real fuel stations on the route; when empty, stand-in stations are placed by country.</summary>
    public IReadOnlyList<FuelCalculator.Core.Planning.RefuelCandidate> Stations { get; init; } = [];
}

public sealed record TripSegmentCost(
    string CountryCode,
    double DistanceKm,
    FuelType FuelType,
    double FuelLiters,
    decimal? PricePerLiter,
    string? PriceCurrency,
    decimal? LocalCost,
    decimal? ConvertedCost,
    string? PriceSource,
    ConfidenceLevel? Confidence,
    bool IsUserProvided);

public sealed record FuelStopEstimate(
    double TankSizeLiters,
    double FullTankRangeKm,
    double StartingFuelPercentage,
    double StartingRangeKm,
    double ReservePercentage,
    double UsableRangeKm,
    bool RequiresRefuel,
    int EstimatedMinimumStops);

public sealed record TripEstimateResult(
    double TotalDistanceKm,
    double NormalizedConsumptionLPer100Km,
    double TotalFuelLiters,
    string OutputCurrency,
    decimal TotalCost,
    IReadOnlyList<TripSegmentCost> Segments,
    IReadOnlyList<string> Warnings,
    FuelStopEstimate? FuelStops,
    bool IsTotalComplete,
    IReadOnlyList<string> CountriesMissingPrice,
    double? DurationMinutes,
    FuelCalculator.Core.Planning.RefuelPlan? RefuelPlan = null,
    decimal? RefuelSaving = null,
    IReadOnlyList<FuelCalculator.Core.Borders.BorderCrossingEstimate>? BorderCrossings = null,
    decimal? BorderCostTotal = null,
    FuelCalculator.Core.Borders.TripTiming? Timing = null);

public sealed record ParsedRouteInput(
    string? Origin,
    string? Destination,
    IReadOnlyList<string> Waypoints);
