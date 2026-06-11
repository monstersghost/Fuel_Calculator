using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Currency;

public sealed class StaticCurrencyConverter : ICurrencyConverter
{
    private readonly IReadOnlyDictionary<string, decimal> _valueInKwd;

    public StaticCurrencyConverter(IReadOnlyDictionary<string, decimal>? valueInKwd = null)
    {
        _valueInKwd = valueInKwd ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["KWD"] = 1.0000m,
            ["SAR"] = 0.0818m,
            ["AED"] = 0.0837m,
            ["QAR"] = 0.0844m,
            ["BHD"] = 0.8150m,
            ["OMR"] = 0.7990m,
            ["USD"] = 0.3075m,
            ["EUR"] = 0.3560m,
            ["JOD"] = 0.4337m,
            ["SYP"] = 0.000023m
        };
    }

    public Task<CurrencyConversionResult?> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default)
    {
        var from = Normalize(fromCurrency);
        var to = Normalize(toCurrency);

        if (!_valueInKwd.TryGetValue(from, out var fromValueInKwd)
            || !_valueInKwd.TryGetValue(to, out var toValueInKwd)
            || toValueInKwd == 0)
        {
            return Task.FromResult<CurrencyConversionResult?>(null);
        }

        var rate = fromValueInKwd / toValueInKwd;
        var converted = amount * rate;

        return Task.FromResult<CurrencyConversionResult?>(new CurrencyConversionResult(
            converted,
            from,
            to,
            rate));
    }

    private static string Normalize(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? "KWD" : currency.Trim().ToUpperInvariant();
}
