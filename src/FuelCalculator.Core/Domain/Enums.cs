namespace FuelCalculator.Core.Domain;

public enum FuelType
{
    GASOLINE_91,
    GASOLINE_95,
    GASOLINE_98,
    DIESEL
}

public enum ConsumptionUnit
{
    L_PER_100KM,
    KM_PER_L,
    US_MPG,
    UK_MPG
}

public enum ConfidenceLevel
{
    LOW,
    MEDIUM,
    HIGH,
    MANUAL
}

public static class FuelTypeCatalog
{
    public static IReadOnlyList<FuelType> All { get; } =
    [
        FuelType.GASOLINE_91,
        FuelType.GASOLINE_95,
        FuelType.GASOLINE_98,
        FuelType.DIESEL
    ];

    public static string GetDisplayName(FuelType fuelType) => fuelType switch
    {
        FuelType.GASOLINE_91 => "91 Octane",
        FuelType.GASOLINE_95 => "95 Octane",
        FuelType.GASOLINE_98 => "98 Octane",
        FuelType.DIESEL => "Diesel",
        _ => fuelType.ToString()
    };

    public static bool TryParse(string? value, out FuelType fuelType)
    {
        fuelType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim()
            .ToUpperInvariant()
            .Replace(" ", string.Empty)
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);

        return normalized switch
        {
            "GASOLINE_91" or "GASOLINE91" or "91" or "91OCTANE" or "OCTANE91" => Set(FuelType.GASOLINE_91, out fuelType),
            "GASOLINE_95" or "GASOLINE95" or "95" or "95OCTANE" or "OCTANE95" => Set(FuelType.GASOLINE_95, out fuelType),
            "GASOLINE_98" or "GASOLINE98" or "98" or "98OCTANE" or "OCTANE98" => Set(FuelType.GASOLINE_98, out fuelType),
            "DIESEL" => Set(FuelType.DIESEL, out fuelType),
            _ => Enum.TryParse(normalized, ignoreCase: true, out fuelType)
        };
    }

    private static bool Set(FuelType value, out FuelType fuelType)
    {
        fuelType = value;
        return true;
    }
}

public static class ConsumptionUnitCatalog
{
    public static IReadOnlyList<ConsumptionUnit> All { get; } =
    [
        ConsumptionUnit.L_PER_100KM,
        ConsumptionUnit.KM_PER_L,
        ConsumptionUnit.US_MPG,
        ConsumptionUnit.UK_MPG
    ];

    public static string GetDisplayName(ConsumptionUnit unit) => unit switch
    {
        ConsumptionUnit.L_PER_100KM => "L/100km",
        ConsumptionUnit.KM_PER_L => "km/L",
        ConsumptionUnit.US_MPG => "US MPG",
        ConsumptionUnit.UK_MPG => "UK MPG",
        _ => unit.ToString()
    };

    public static bool TryParse(string? value, out ConsumptionUnit unit)
    {
        unit = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim()
            .ToUpperInvariant()
            .Replace(" ", string.Empty)
            .Replace("-", "_", StringComparison.Ordinal);

        return normalized switch
        {
            "L/100KM" or "L_PER_100KM" or "LPER100KM" or "L100KM" => Set(ConsumptionUnit.L_PER_100KM, out unit),
            "KM/L" or "KM_PER_L" or "KMPERL" => Set(ConsumptionUnit.KM_PER_L, out unit),
            "USMPG" or "US_MPG" or "MPG_US" => Set(ConsumptionUnit.US_MPG, out unit),
            "UKMPG" or "UK_MPG" or "MPG_UK" => Set(ConsumptionUnit.UK_MPG, out unit),
            _ => Enum.TryParse(normalized, ignoreCase: true, out unit)
        };
    }

    private static bool Set(ConsumptionUnit value, out ConsumptionUnit unit)
    {
        unit = value;
        return true;
    }
}
