using FuelCalculator.Core.Currency;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Fuel;
using FuelCalculator.Core.Planning;

namespace FuelCalculator.Core.Calculation;

public sealed class TripFuelCostCalculator
{
    /// <summary>A seed or provider price older than this gets a warning.</summary>
    public const int StalePriceMonths = 6;

    public async Task<TripEstimateResult> CalculateAsync(
        TripFuelCostInput input,
        IFuelPriceProvider fuelPriceProvider,
        ICurrencyConverter currencyConverter,
        IEnumerable<string>? existingWarnings = null,
        CancellationToken cancellationToken = default)
    {
        if (input.NormalizedConsumptionLPer100Km <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Normalized consumption must be greater than zero.");
        }

        var warnings = existingWarnings?.Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().ToList() ?? [];
        var outputCurrency = NormalizeCurrency(input.OutputCurrency);
        var segments = AggregateByCountry(input.CountrySegments);
        var segmentCosts = new List<TripSegmentCost>();
        var refuelPrices = new Dictionary<string, RefuelPrice>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in segments)
        {
            var fuelLiters = segment.DistanceKm * input.NormalizedConsumptionLPer100Km / 100d;
            var pricedFuelType = input.FuelType;
            var quote = await fuelPriceProvider.GetPriceAsync(segment.CountryCode, pricedFuelType, cancellationToken);

            if (quote is null && CountryCatalog.GetSubstituteGrade(segment.CountryCode, input.FuelType) is { } substitute)
            {
                quote = await fuelPriceProvider.GetPriceAsync(segment.CountryCode, substitute, cancellationToken);

                if (quote is not null)
                {
                    pricedFuelType = substitute;
                    warnings.Add($"{FuelTypeCatalog.GetDisplayName(input.FuelType)} is not sold in {segment.CountryCode}; priced as {FuelTypeCatalog.GetDisplayName(substitute)}.");
                }
            }

            if (quote is null)
            {
                foreach (var nearest in CountryCatalog.NearestPetrolGrades(input.FuelType))
                {
                    quote = await fuelPriceProvider.GetPriceAsync(segment.CountryCode, nearest, cancellationToken);

                    if (quote is not null)
                    {
                        pricedFuelType = nearest;
                        warnings.Add($"No {FuelTypeCatalog.GetDisplayName(input.FuelType)} price for {segment.CountryCode}; used the {FuelTypeCatalog.GetDisplayName(nearest)} price as an estimate.");
                        break;
                    }
                }
            }

            decimal? localCost = null;
            decimal? convertedCost = null;

            if (quote is { IsUserProvided: false, EffectiveDate: { } effective }
                && effective < DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-StalePriceMonths))
            {
                warnings.Add($"{segment.CountryCode} price is from {effective:MMM yyyy} and may be out of date; enter a manual price if you know a newer one.");
            }

            if (quote is { IsUserProvided: false, EffectiveDate: { } priceDate }
                && input.TravelDate is { } travel
                && CountryCatalog.GetPriceCycle(segment.CountryCode) is { } cycle
                && Period(travel, cycle) > Period(priceDate, cycle))
            {
                warnings.Add($"{segment.CountryCode} sets fuel prices {cycle.ToString().ToLowerInvariant()}; the price used is for {priceDate:MMM yyyy} and may change before your trip in {travel:MMM yyyy}.");
            }

            if (quote is null)
            {
                warnings.Add($"Missing fuel price for {segment.CountryCode} {FuelTypeCatalog.GetDisplayName(input.FuelType)}; it is left out of the total.");
            }
            else
            {
                localCost = RoundMoney((decimal)fuelLiters * quote.PricePerLiter);
                var conversion = await currencyConverter.ConvertAsync(localCost.Value, quote.Currency, outputCurrency, cancellationToken);

                if (conversion is null)
                {
                    warnings.Add($"Missing FX rate from {quote.Currency} to {outputCurrency}; {segment.CountryCode} is left out of the total.");
                }
                else
                {
                    convertedCost = RoundMoney(conversion.Amount);
                }

                var perLiter = await currencyConverter.ConvertAsync(quote.PricePerLiter, quote.Currency, outputCurrency, cancellationToken);
                refuelPrices[segment.CountryCode] = new RefuelPrice(
                    segment.CountryCode,
                    quote.PricePerLiter,
                    quote.Currency,
                    perLiter is null ? null : Math.Round(perLiter.Amount, 6, MidpointRounding.AwayFromZero),
                    quote.SourceName);
            }

            segmentCosts.Add(new TripSegmentCost(
                segment.CountryCode,
                RoundDouble(segment.DistanceKm),
                pricedFuelType,
                RoundDouble(fuelLiters),
                quote?.PricePerLiter,
                quote?.Currency,
                localCost,
                convertedCost,
                quote?.SourceName,
                quote?.Confidence,
                quote?.IsUserProvided ?? false));
        }

        var totalDistance = segments.Sum(segment => segment.DistanceKm);
        var totalFuel = totalDistance * input.NormalizedConsumptionLPer100Km / 100d;
        var totalCost = RoundMoney(segmentCosts.Sum(segment => segment.ConvertedCost ?? 0m));
        var fuelStops = CreateFuelStopEstimate(input, totalDistance, warnings);
        var countriesMissingPrice = segmentCosts
            .Where(segment => segment.ConvertedCost is null)
            .Select(segment => segment.CountryCode)
            .ToArray();
        RefuelPlan? refuelPlan = null;
        decimal? refuelSaving = null;

        if (fuelStops is not null)
        {
            refuelPlan = RefuelPlanner.Plan(new RefuelPlanInput(
                totalDistance,
                input.NormalizedConsumptionLPer100Km,
                fuelStops.TankSizeLiters,
                fuelStops.StartingFuelPercentage,
                fuelStops.ReservePercentage,
                input.Stations.Count > 0
                    ? CountryRefuelRules.Apply(input.Stations, input.CountrySegments)
                    : VirtualRefuelCandidates.FromSegments(input.CountrySegments),
                refuelPrices));

            var stationNote = input.Stations.Count > 0
                ? $"Fuel stops use {input.Stations.Count} OpenStreetMap fuel stations near the route; check opening hours before relying on remote ones."
                : "Fuel stops are placed along the route by country; real station locations are not checked.";

            refuelPlan = refuelPlan with { Warnings = [stationNote, .. refuelPlan.Warnings] };

            if (refuelPlan.IsComplete && refuelPlan.IsFeasible && countriesMissingPrice.Length == 0)
            {
                refuelSaving = RoundMoney(totalCost - refuelPlan.TripFuelCost);
            }
        }

        return new TripEstimateResult(
            RoundDouble(totalDistance),
            RoundDouble(input.NormalizedConsumptionLPer100Km),
            RoundDouble(totalFuel),
            outputCurrency,
            totalCost,
            segmentCosts,
            warnings.Distinct().ToArray(),
            fuelStops,
            countriesMissingPrice.Length == 0,
            countriesMissingPrice,
            input.DurationMinutes is { } minutes ? RoundDouble(minutes) : null,
            refuelPlan,
            refuelSaving);
    }

    private static IReadOnlyList<CountryRouteSegment> AggregateByCountry(IReadOnlyList<CountryRouteSegment> segments)
    {
        var ordered = new List<CountryRouteSegment>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in segments.Where(segment => segment.DistanceKm > 0))
        {
            var countryCode = NormalizeCountry(segment.CountryCode);

            if (indexes.TryGetValue(countryCode, out var index))
            {
                var existing = ordered[index];
                ordered[index] = existing with { DistanceKm = existing.DistanceKm + segment.DistanceKm };
                continue;
            }

            indexes[countryCode] = ordered.Count;
            ordered.Add(new CountryRouteSegment(countryCode, segment.DistanceKm));
        }

        return ordered;
    }

    private static FuelStopEstimate? CreateFuelStopEstimate(
        TripFuelCostInput input,
        double totalDistanceKm,
        List<string> warnings)
    {
        if (input.TankSizeLiters is null)
        {
            return null;
        }

        if (input.TankSizeLiters <= 0)
        {
            warnings.Add("Tank size must be greater than zero to estimate refuel stops.");
            return null;
        }

        var startingFuelPercentage = input.CurrentFuelPercentage ?? 100d;

        if (startingFuelPercentage < 0 || startingFuelPercentage > 100)
        {
            warnings.Add("Current fuel percentage must be between 0 and 100; assuming a full tank.");
            startingFuelPercentage = 100d;
        }

        var reservePercentage = input.ReservePercentage;

        if (reservePercentage < 0 || reservePercentage >= 100)
        {
            warnings.Add("Reserve percentage must be at least 0 and below 100; using 15%.");
            reservePercentage = 15d;
        }

        var fullTankRange = input.TankSizeLiters.Value * 100d / input.NormalizedConsumptionLPer100Km;
        var startingRange = fullTankRange * startingFuelPercentage / 100d;
        var reserveRange = fullTankRange * reservePercentage / 100d;
        var usableRange = fullTankRange - reserveRange;
        var startingUsableRange = Math.Max(0d, startingRange - reserveRange);
        var remainingDistance = Math.Max(0d, totalDistanceKm - startingUsableRange);
        var estimatedStops = remainingDistance <= 0 ? 0 : (int)Math.Ceiling(remainingDistance / usableRange);

        return new FuelStopEstimate(
            RoundDouble(input.TankSizeLiters.Value),
            RoundDouble(fullTankRange),
            RoundDouble(startingFuelPercentage),
            RoundDouble(startingRange),
            RoundDouble(reservePercentage),
            RoundDouble(usableRange),
            estimatedStops > 0,
            estimatedStops);
    }

    private static int Period(DateOnly date, PriceCycle cycle) =>
        cycle == PriceCycle.Quarterly ? date.Year * 4 + (date.Month - 1) / 3 : date.Year * 12 + date.Month - 1;

    private static string NormalizeCountry(string countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) ? "UNKNOWN" : countryCode.Trim().ToUpperInvariant();

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? "KWD" : currency.Trim().ToUpperInvariant();

    private static double RoundDouble(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static decimal RoundMoney(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
