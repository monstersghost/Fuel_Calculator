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
            NormalizeCurrency(manualPrice.Currency, manualPrice.CountryCode),
            "Manual",
            manualPrice.EffectiveDate,
            DateTimeOffset.UtcNow,
            ConfidenceLevel.MANUAL,
            true);

        return Task.FromResult<FuelPriceQuote?>(quote);
    }

    private static string NormalizeCountry(string countryCode) => countryCode.Trim().ToUpperInvariant();

    private static string NormalizeCurrency(string? currency, string countryCode) =>
        string.IsNullOrWhiteSpace(currency)
            ? CountryCatalog.GetLocalCurrency(countryCode) ?? "KWD"
            : currency.Trim().ToUpperInvariant();
}

/// <summary>
/// Last-resort pump prices for countries with no live or official feed. Every row says where it came
/// from, the date the price took effect and how sure we are of it. Checked on <see cref="CheckedOn"/>;
/// sources and caveats are listed in docs/ops/FUEL_PRICE_SEED.md. Update that file with this table.
/// </summary>
public sealed class StaticSeedFuelPriceProvider : IFuelPriceProvider
{
    public static readonly DateOnly CheckedOn = new(2026, 11, 27);

    private static readonly SeedRow[] Rows =
    [
        // Kuwait: subsidies committee, Q1 2026 decision; later listings report no change. Ultra 98 is set
        // quarterly and later figures conflict (200 vs 275 fils), so 98 is LOW.
        new("KW", FuelType.GASOLINE_91, 0.085m, "KWD", new(2026, 1, 1), ConfidenceLevel.MEDIUM, "Kuwait subsidies committee"),
        new("KW", FuelType.GASOLINE_95, 0.105m, "KWD", new(2026, 1, 1), ConfidenceLevel.MEDIUM, "Kuwait subsidies committee"),
        new("KW", FuelType.GASOLINE_98, 0.200m, "KWD", new(2026, 1, 1), ConfidenceLevel.LOW, "Kuwait subsidies committee"),
        new("KW", FuelType.DIESEL, 0.115m, "KWD", new(2026, 1, 1), ConfidenceLevel.MEDIUM, "Kuwait subsidies committee"),

        // Saudi Arabia: Aramco retail fuels page, prices for November 2026.
        new("SA", FuelType.GASOLINE_91, 2.18m, "SAR", new(2026, 11, 1), ConfidenceLevel.HIGH, "Aramco"),
        new("SA", FuelType.GASOLINE_95, 2.33m, "SAR", new(2026, 11, 1), ConfidenceLevel.HIGH, "Aramco"),
        new("SA", FuelType.GASOLINE_98, 5.09m, "SAR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Aramco"),
        new("SA", FuelType.DIESEL, 1.79m, "SAR", new(2026, 11, 1), ConfidenceLevel.HIGH, "Aramco"),

        // UAE: Fuel Price Committee, November 2026 (monthly; next set announced on the last day of the month).
        new("AE", FuelType.GASOLINE_91, 3.97m, "AED", new(2026, 11, 1), ConfidenceLevel.HIGH, "UAE Fuel Price Committee"),
        new("AE", FuelType.GASOLINE_95, 4.04m, "AED", new(2026, 11, 1), ConfidenceLevel.HIGH, "UAE Fuel Price Committee"),
        new("AE", FuelType.GASOLINE_98, 4.16m, "AED", new(2026, 11, 1), ConfidenceLevel.HIGH, "UAE Fuel Price Committee"),
        new("AE", FuelType.DIESEL, 4.75m, "AED", new(2026, 11, 1), ConfidenceLevel.HIGH, "UAE Fuel Price Committee"),

        // Qatar: QatarEnergy, November 2026 unchanged from October (figures via YallaMotor). No 98 sold.
        new("QA", FuelType.GASOLINE_91, 1.95m, "QAR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "QatarEnergy"),
        new("QA", FuelType.GASOLINE_95, 2.10m, "QAR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "QatarEnergy"),
        new("QA", FuelType.DIESEL, 2.05m, "QAR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "QatarEnergy"),

        // Bahrain: Fuel Price Committee, November 2026 (Jayyid 91, Mumtaz 95, Super 98).
        new("BH", FuelType.GASOLINE_91, 0.235m, "BHD", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Bahrain Fuel Price Committee"),
        new("BH", FuelType.GASOLINE_95, 0.277m, "BHD", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Bahrain Fuel Price Committee"),
        new("BH", FuelType.GASOLINE_98, 0.410m, "BHD", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Bahrain Fuel Price Committee"),
        new("BH", FuelType.DIESEL, 0.260m, "BHD", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Bahrain Fuel Price Committee"),

        // Oman: Ministry of Energy and Minerals monthly prices, November 2026 (secondary sources).
        new("OM", FuelType.GASOLINE_91, 0.229m, "OMR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Oman Ministry of Energy"),
        new("OM", FuelType.GASOLINE_95, 0.239m, "OMR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Oman Ministry of Energy"),
        new("OM", FuelType.GASOLINE_98, 0.399m, "OMR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Oman Ministry of Energy"),
        new("OM", FuelType.DIESEL, 0.258m, "OMR", new(2026, 11, 1), ConfidenceLevel.MEDIUM, "Oman Ministry of Energy"),

        // Jordan: fuel pricing committee, October 2026; November not found. Octane 90 is listed as 91.
        new("JO", FuelType.GASOLINE_91, 1.050m, "JOD", new(2026, 10, 1), ConfidenceLevel.LOW, "Jordan fuel pricing committee"),
        new("JO", FuelType.GASOLINE_95, 1.360m, "JOD", new(2026, 10, 1), ConfidenceLevel.LOW, "Jordan fuel pricing committee"),
        new("JO", FuelType.DIESEL, 0.900m, "JOD", new(2026, 10, 1), ConfidenceLevel.LOW, "Jordan fuel pricing committee"),

        // Syria: Ministry of Energy, 13 September 2026, in new Syrian pounds (two zeros dropped on 1 Jan 2026).
        // Octane 90 is listed as 91. Diesel has three tiers (115/150/175); 175 is the standard one.
        new("SY", FuelType.GASOLINE_91, 185m, "SYP", new(2026, 9, 13), ConfidenceLevel.LOW, "Syria Ministry of Energy"),
        new("SY", FuelType.GASOLINE_95, 195m, "SYP", new(2026, 9, 13), ConfidenceLevel.LOW, "Syria Ministry of Energy"),
        new("SY", FuelType.DIESEL, 175m, "SYP", new(2026, 9, 13), ConfidenceLevel.LOW, "Syria Ministry of Energy"),

        // Iraq: federal price for 95 as tracked by GlobalPetrolPrices, August 2026. Kurdistan Region stations
        // charge two to three times more. Diesel sources disagree (250-1,000 IQD), so diesel is not seeded.
        new("IQ", FuelType.GASOLINE_95, 850m, "IQD", new(2026, 8, 10), ConfidenceLevel.LOW, "GlobalPetrolPrices"),

        // Lebanon: ministry price per 20 L reported by LBCI, August 2026, divided by 20. Prices have fallen since.
        new("LB", FuelType.GASOLINE_95, 122750m, "LBP", new(2026, 8, 11), ConfidenceLevel.LOW, "Lebanon Ministry of Energy"),
        new("LB", FuelType.GASOLINE_98, 123700m, "LBP", new(2026, 8, 18), ConfidenceLevel.LOW, "Lebanon Ministry of Energy"),
        new("LB", FuelType.DIESEL, 118750m, "LBP", new(2026, 8, 18), ConfidenceLevel.LOW, "Lebanon Ministry of Energy"),

        // Egypt: price set on 10 March 2026 and frozen to the end of December 2026. Octane 92 is listed as 91.
        new("EG", FuelType.GASOLINE_91, 22.25m, "EGP", new(2026, 3, 10), ConfidenceLevel.MEDIUM, "Egypt fuel pricing committee"),
        new("EG", FuelType.GASOLINE_95, 24.00m, "EGP", new(2026, 3, 10), ConfidenceLevel.MEDIUM, "Egypt fuel pricing committee"),
        new("EG", FuelType.DIESEL, 20.50m, "EGP", new(2026, 3, 10), ConfidenceLevel.MEDIUM, "Egypt fuel pricing committee")
    ];

    private readonly Dictionary<(string CountryCode, FuelType FuelType), FuelPriceQuote> _prices;

    public StaticSeedFuelPriceProvider()
    {
        var checkedAt = new DateTimeOffset(CheckedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        _prices = Rows.ToDictionary(
            row => (row.CountryCode, row.FuelType),
            row => new FuelPriceQuote(
                row.CountryCode,
                row.FuelType,
                row.PricePerLiter,
                row.Currency,
                $"Seed: {row.Source}",
                row.EffectiveDate,
                checkedAt,
                row.Confidence,
                false));
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

    private static string NormalizeCountry(string countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) ? "UNKNOWN" : countryCode.Trim().ToUpperInvariant();

    private sealed record SeedRow(
        string CountryCode,
        FuelType FuelType,
        decimal PricePerLiter,
        string Currency,
        DateOnly EffectiveDate,
        ConfidenceLevel Confidence,
        string Source);
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
