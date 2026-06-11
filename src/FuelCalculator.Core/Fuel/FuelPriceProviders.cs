using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Fuel;

public sealed class ManualFuelPriceProvider(IEnumerable<ManualFuelPriceOverride> overrides) : IFuelPriceProvider
{
    private readonly Dictionary<(string CountryCode, FuelType FuelType), ManualFuelPriceOverride> _overrides = overrides
        .Where(item => !string.IsNullOrWhiteSpace(item.CountryCode) && item.PricePerLiter > 0)
        .ToDictionary(
            item => (NormalizeCountry(item.CountryCode), item.FuelType),
            item => item);

    public Task<FuelPriceQuote?> GetPriceAsync(
        string countryCode,
        FuelType fuelType,
        CancellationToken cancellationToken = default)
    {
        if (!_overrides.TryGetValue((NormalizeCountry(countryCode), fuelType), out var manualPrice))
        {
            return Task.FromResult<FuelPriceQuote?>(null);
        }

        FuelPriceQuote quote = new(
            NormalizeCountry(manualPrice.CountryCode),
            manualPrice.FuelType,
            manualPrice.PricePerLiter,
            NormalizeCurrency(manualPrice.Currency),
            "Manual",
            manualPrice.EffectiveDate,
            DateTimeOffset.UtcNow,
            ConfidenceLevel.MANUAL,
            true);

        return Task.FromResult<FuelPriceQuote?>(quote);
    }

    private static string NormalizeCountry(string countryCode) => countryCode.Trim().ToUpperInvariant();

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? "KWD" : currency.Trim().ToUpperInvariant();
}

public sealed class StaticSeedFuelPriceProvider : IFuelPriceProvider
{
    private readonly Dictionary<(string CountryCode, FuelType FuelType), FuelPriceQuote> _prices;

    public StaticSeedFuelPriceProvider()
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var effectiveDate = new DateOnly(2026, 1, 1);

        _prices = new Dictionary<(string CountryCode, FuelType FuelType), FuelPriceQuote>
        {
            [("KW", FuelType.GASOLINE_91)] = Seed("KW", FuelType.GASOLINE_91, 0.085m, "KWD", effectiveDate, fetchedAt),
            [("KW", FuelType.GASOLINE_95)] = Seed("KW", FuelType.GASOLINE_95, 0.105m, "KWD", effectiveDate, fetchedAt),
            [("KW", FuelType.GASOLINE_98)] = Seed("KW", FuelType.GASOLINE_98, 0.165m, "KWD", effectiveDate, fetchedAt),
            [("KW", FuelType.DIESEL)] = Seed("KW", FuelType.DIESEL, 0.115m, "KWD", effectiveDate, fetchedAt),

            [("SA", FuelType.GASOLINE_91)] = Seed("SA", FuelType.GASOLINE_91, 2.180m, "SAR", effectiveDate, fetchedAt),
            [("SA", FuelType.GASOLINE_95)] = Seed("SA", FuelType.GASOLINE_95, 2.330m, "SAR", effectiveDate, fetchedAt),
            [("SA", FuelType.DIESEL)] = Seed("SA", FuelType.DIESEL, 1.660m, "SAR", effectiveDate, fetchedAt),

            [("QA", FuelType.GASOLINE_91)] = Seed("QA", FuelType.GASOLINE_91, 1.950m, "QAR", effectiveDate, fetchedAt),
            [("QA", FuelType.GASOLINE_95)] = Seed("QA", FuelType.GASOLINE_95, 2.100m, "QAR", effectiveDate, fetchedAt),
            [("QA", FuelType.DIESEL)] = Seed("QA", FuelType.DIESEL, 2.050m, "QAR", effectiveDate, fetchedAt),

            [("AE", FuelType.GASOLINE_91)] = Seed("AE", FuelType.GASOLINE_91, 2.850m, "AED", effectiveDate, fetchedAt),
            [("AE", FuelType.GASOLINE_95)] = Seed("AE", FuelType.GASOLINE_95, 2.970m, "AED", effectiveDate, fetchedAt),
            [("AE", FuelType.GASOLINE_98)] = Seed("AE", FuelType.GASOLINE_98, 3.080m, "AED", effectiveDate, fetchedAt),
            [("AE", FuelType.DIESEL)] = Seed("AE", FuelType.DIESEL, 2.900m, "AED", effectiveDate, fetchedAt),

            [("BH", FuelType.GASOLINE_91)] = Seed("BH", FuelType.GASOLINE_91, 0.140m, "BHD", effectiveDate, fetchedAt),
            [("BH", FuelType.GASOLINE_95)] = Seed("BH", FuelType.GASOLINE_95, 0.200m, "BHD", effectiveDate, fetchedAt),
            [("BH", FuelType.DIESEL)] = Seed("BH", FuelType.DIESEL, 0.120m, "BHD", effectiveDate, fetchedAt),

            [("OM", FuelType.GASOLINE_91)] = Seed("OM", FuelType.GASOLINE_91, 0.229m, "OMR", effectiveDate, fetchedAt),
            [("OM", FuelType.GASOLINE_95)] = Seed("OM", FuelType.GASOLINE_95, 0.239m, "OMR", effectiveDate, fetchedAt),
            [("OM", FuelType.DIESEL)] = Seed("OM", FuelType.DIESEL, 0.258m, "OMR", effectiveDate, fetchedAt)
        };
    }

    public Task<FuelPriceQuote?> GetPriceAsync(
        string countryCode,
        FuelType fuelType,
        CancellationToken cancellationToken = default)
    {
        _prices.TryGetValue((NormalizeCountry(countryCode), fuelType), out var quote);
        return Task.FromResult<FuelPriceQuote?>(quote);
    }

    public IReadOnlyList<FuelPriceQuote> ListPrices() =>
        _prices.Values
            .OrderBy(quote => quote.CountryCode)
            .ThenBy(quote => quote.FuelType)
            .ToArray();

    private static FuelPriceQuote Seed(
        string countryCode,
        FuelType fuelType,
        decimal pricePerLiter,
        string currency,
        DateOnly effectiveDate,
        DateTimeOffset fetchedAt) =>
        new(
            countryCode,
            fuelType,
            pricePerLiter,
            currency,
            "Seed",
            effectiveDate,
            fetchedAt,
            ConfidenceLevel.LOW,
            false);

    private static string NormalizeCountry(string countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) ? "UNKNOWN" : countryCode.Trim().ToUpperInvariant();
}

public sealed class CompositeFuelPriceProvider(IEnumerable<IFuelPriceProvider> providers) : IFuelPriceProvider
{
    private readonly IReadOnlyList<IFuelPriceProvider> _providers = providers.ToArray();

    public async Task<FuelPriceQuote?> GetPriceAsync(
        string countryCode,
        FuelType fuelType,
        CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            var quote = await provider.GetPriceAsync(countryCode, fuelType, cancellationToken);

            if (quote is not null)
            {
                return quote;
            }
        }

        return null;
    }
}

public abstract class FutureFuelPriceProvider(string sourceName) : IFuelPriceProvider
{
    public string SourceName { get; } = sourceName;

    public Task<FuelPriceQuote?> GetPriceAsync(
        string countryCode,
        FuelType fuelType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<FuelPriceQuote?>(null);
}

public sealed class SaudiAramcoFuelPriceProvider() : FutureFuelPriceProvider("Saudi Aramco");

public sealed class AdnocFuelPriceProvider() : FutureFuelPriceProvider("ADNOC");

public sealed class QatarFuelPriceProvider() : FutureFuelPriceProvider("Qatar Energy");

public sealed class GlobalPetrolPricesProvider() : FutureFuelPriceProvider("GlobalPetrolPrices");
