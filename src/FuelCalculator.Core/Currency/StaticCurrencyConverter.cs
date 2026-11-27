using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Currency;

public sealed class StaticCurrencyConverter : ICurrencyConverter
{
    private readonly IReadOnlyDictionary<string, decimal> _valueInKwd;

    public StaticCurrencyConverter(IReadOnlyDictionary<string, decimal>? valueInKwd = null)
    {
        _valueInKwd = valueInKwd ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            // Value of one unit in KWD, from USD = 0.3075 KWD. SAR, AED, QAR, BHD, OMR and JOD are pegged
            // to the dollar, so their rows are exact. The rest are static estimates checked 2026-11-27:
            // EGP ~53.7/USD (bank quotes, 15 Nov 2026), IQD 1,310/USD (official), LBP ~89,600/USD,
            // SYP ~115/USD in new pounds (two zeros dropped on 1 Jan 2026), EUR approximate.
            ["KWD"] = 1.0000m,
            ["USD"] = 0.3075m,
            ["SAR"] = 0.3075m / 3.75m,
            ["AED"] = 0.3075m / 3.6725m,
            ["QAR"] = 0.3075m / 3.64m,
            ["BHD"] = 0.3075m / 0.376m,
            ["OMR"] = 0.3075m / 0.3845m,
            ["JOD"] = 0.3075m / 0.709m,
            ["EUR"] = 0.3560m,
            ["EGP"] = 0.3075m / 53.7m,
            ["IQD"] = 0.3075m / 1310m,
            ["LBP"] = 0.3075m / 89600m,
            ["SYP"] = 0.3075m / 115m
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
