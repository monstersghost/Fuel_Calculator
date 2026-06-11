using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Currency;

public interface ICurrencyConverter
{
    Task<CurrencyConversionResult?> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default);
}
