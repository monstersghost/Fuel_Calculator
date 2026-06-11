using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Calculation;

public static class ConsumptionConverter
{
    public static double ToLPer100Km(double value, ConsumptionUnit unit)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Consumption must be greater than zero.");
        }

        return unit switch
        {
            ConsumptionUnit.L_PER_100KM => value,
            ConsumptionUnit.KM_PER_L => 100d / value,
            ConsumptionUnit.US_MPG => 235.214583d / value,
            ConsumptionUnit.UK_MPG => 282.480936d / value,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), $"Unsupported consumption unit: {unit}")
        };
    }
}
