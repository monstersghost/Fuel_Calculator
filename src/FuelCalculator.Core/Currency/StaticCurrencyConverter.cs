using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Currency;

/// <summary>
/// Converts with a dated snapshot of rates against the US dollar (Exchange Rate API, 166 currencies),
/// embedded as Currency/Data/fx-usd.json. Rates By Exchange Rate API: https://www.exchangerate-api.com.
/// </summary>
public sealed class StaticCurrencyConverter : ICurrencyConverter
{
    private static readonly Lazy<(DateOnly AsOf, IReadOnlyDictionary<string, decimal> Rates)> Snapshot = new(LoadSnapshot);

    private readonly IReadOnlyDictionary<string, decimal> _perUsd;

    /// <param name="ratesPerUsd">Units of each currency per one US dollar; the embedded snapshot when null.</param>
    public StaticCurrencyConverter(IReadOnlyDictionary<string, decimal>? ratesPerUsd = null)
    {
        _perUsd = ratesPerUsd ?? Snapshot.Value.Rates;
    }

    /// <summary>The day the embedded rates were published.</summary>
    public static DateOnly SnapshotDate => Snapshot.Value.AsOf;

    /// <summary>Every currency code in the embedded snapshot, sorted.</summary>
    public static IReadOnlyList<string> Currencies => Snapshot.Value.Rates.Keys.Order(StringComparer.Ordinal).ToArray();

    public Task<CurrencyConversionResult?> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default)
    {
        var from = Normalize(fromCurrency);
        var to = Normalize(toCurrency);

        if (!_perUsd.TryGetValue(from, out var fromPerUsd)
            || !_perUsd.TryGetValue(to, out var toPerUsd)
            || fromPerUsd == 0)
        {
            return Task.FromResult<CurrencyConversionResult?>(null);
        }

        var rate = toPerUsd / fromPerUsd;

        return Task.FromResult<CurrencyConversionResult?>(new CurrencyConversionResult(
            amount * rate,
            from,
            to,
            rate));
    }

    private static (DateOnly, IReadOnlyDictionary<string, decimal>) LoadSnapshot()
    {
        using var document = EmbeddedData.Load("fx-usd.json");
        var root = document.RootElement;
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var rate in root.GetProperty("rates").EnumerateObject())
        {
            rates[rate.Name] = rate.Value.GetDecimal();
        }

        return (DateOnly.Parse(root.GetProperty("asOf").GetString()!), rates);
    }

    private static string Normalize(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
}
