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
/// Last-resort pump prices for countries with no live or official feed, read from Fuel/Data/seed-prices.json.
/// That file is built by tools/data/build_world_data.py from, in priority order:
/// <list type="number">
/// <item>tools/data/curated-prices.csv: prices checked by hand, each with its source.</item>
/// <item>The EU Weekly Oil Bulletin (all 27 EU countries, weekly, official).</item>
/// <item>The World Bank Global Fuel Prices Database (about 140 countries, monthly to April 2025).</item>
/// </list>
/// Every quote carries its source, the date the price took effect and a confidence level.
/// </summary>
public sealed class StaticSeedFuelPriceProvider : IFuelPriceProvider
{
    private static readonly Lazy<SeedData> Data = new(Load);

    /// <summary>The day the seed sources were checked.</summary>
    public static DateOnly CheckedOn => Data.Value.CheckedOn;

    public Task<FuelPriceQuote?> GetPriceAsync(
        string countryCode,
        FuelType fuelType,
        CancellationToken cancellationToken = default)
    {
        Data.Value.Prices.TryGetValue((NormalizeCountry(countryCode), fuelType), out var quote);
        return Task.FromResult<FuelPriceQuote?>(quote);
    }

    public IReadOnlyList<FuelPriceQuote> ListPrices() =>
        Data.Value.Prices.Values
            .OrderBy(quote => quote.CountryCode)
            .ThenBy(quote => quote.FuelType)
            .ToArray();

    private static SeedData Load()
    {
        using var document = EmbeddedData.Load("seed-prices.json");
        var root = document.RootElement;
        var checkedOn = DateOnly.Parse(root.GetProperty("checkedOn").GetString()!);
        var checkedAt = new DateTimeOffset(checkedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var prices = new Dictionary<(string CountryCode, FuelType FuelType), FuelPriceQuote>();

        foreach (var row in root.GetProperty("prices").EnumerateArray())
        {
            var country = NormalizeCountry(row.GetProperty("country").GetString()!);
            var fuel = Enum.Parse<FuelType>(row.GetProperty("fuel").GetString()!);

            prices[(country, fuel)] = new FuelPriceQuote(
                country,
                fuel,
                row.GetProperty("price").GetDecimal(),
                row.GetProperty("currency").GetString()!,
                $"Seed: {row.GetProperty("source").GetString()}",
                DateOnly.Parse(row.GetProperty("effective").GetString()!),
                checkedAt,
                Enum.Parse<ConfidenceLevel>(row.GetProperty("confidence").GetString()!),
                false);
        }

        return new SeedData(checkedOn, prices);
    }

    private static string NormalizeCountry(string countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) ? "UNKNOWN" : countryCode.Trim().ToUpperInvariant();

    private sealed record SeedData(
        DateOnly CheckedOn,
        IReadOnlyDictionary<(string CountryCode, FuelType FuelType), FuelPriceQuote> Prices);
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
