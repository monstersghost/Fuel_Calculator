namespace FuelCalculator.Core.Planning;

/// <summary>A place on the route where fuel can be bought.</summary>
/// <param name="DistanceKm">Distance from the start of the route.</param>
/// <param name="MaxLiters">Most that may be bought in one fill here, e.g. Oman's 30 L cap near borders.</param>
public sealed record RefuelCandidate(
    double DistanceKm,
    string CountryCode,
    string? Name = null,
    double? MaxLiters = null,
    bool? Open24Hours = null);

/// <summary>The pump price in a country, locally and in the trip's output currency.</summary>
/// <param name="PricePerLiterInOutput">Used to compare countries; null when there is no price or no FX rate.</param>
public sealed record RefuelPrice(
    string CountryCode,
    decimal? PricePerLiter,
    string? Currency,
    decimal? PricePerLiterInOutput,
    string? Source);

public sealed record RefuelPlanInput(
    double TotalDistanceKm,
    double LPer100Km,
    double TankLiters,
    double StartFuelPercentage,
    double ReservePercentage,
    IReadOnlyList<RefuelCandidate> Candidates,
    IReadOnlyDictionary<string, RefuelPrice> Prices);

public sealed record PlannedStop(
    double DistanceKm,
    string CountryCode,
    string? Name,
    double Liters,
    decimal? PricePerLiter,
    string? Currency,
    decimal? LocalCost,
    decimal? ConvertedCost,
    double FuelBeforePercentage,
    double FuelAfterPercentage,
    string Reason,
    bool? Open24Hours = null);

/// <summary>
/// Where to buy fuel and what the trip's fuel really costs. <see cref="TripFuelCost"/> is what was bought
/// on the way, plus the fuel you set off with (at the origin price), minus the fuel left on arrival (at the
/// last price paid). Amounts are in the trip's output currency.
/// </summary>
public sealed record RefuelPlan(
    IReadOnlyList<PlannedStop> Stops,
    double StartFuelLiters,
    double ArrivalFuelLiters,
    double ArrivalFuelPercentage,
    double PurchasedLiters,
    decimal PurchasedCost,
    decimal StartFuelValue,
    decimal ArrivalFuelValue,
    decimal TripFuelCost,
    bool IsComplete,
    bool IsFeasible,
    IReadOnlyList<string> Warnings);
