using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Fuel;

public interface IFuelPriceProvider
{
    Task<FuelPriceQuote?> GetPriceAsync(
        string countryCode,
        FuelType fuelType,
        CancellationToken cancellationToken = default);
}
