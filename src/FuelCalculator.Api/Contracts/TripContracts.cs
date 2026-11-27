namespace FuelCalculator.Api.Contracts;

public sealed record EstimateTripRequest
{
    public string? Origin { get; init; }

    public string? Destination { get; init; }

    public IReadOnlyList<string> Waypoints { get; init; } = [];

    public string? GoogleMapsLink { get; init; }

    public string FuelType { get; init; } = "GASOLINE_95";

    public double ConsumptionValue { get; init; }

    public string ConsumptionUnit { get; init; } = "L_PER_100KM";

    public string OutputCurrency { get; init; } = "KWD";

    public double? TankSizeLiters { get; init; }

    public double? CurrentFuelPercentage { get; init; }

    /// <summary>Share of the tank to keep in hand when estimating refuels. Defaults to 15%.</summary>
    public double? ReservePercentage { get; init; }

    /// <summary>Local departure time, used for border waits and the arrival time. Optional.</summary>
    public DateTime? DepartureTime { get; init; }

    /// <summary>People in the car, for per-person border fees. Defaults to 1.</summary>
    public int? Passengers { get; init; }

    public IReadOnlyList<ManualFuelPriceRequest> ManualFuelPrices { get; init; } = [];
}

public sealed record ManualFuelPriceRequest
{
    public string CountryCode { get; init; } = string.Empty;

    public string? FuelType { get; init; }

    public decimal PricePerLiter { get; init; }

    /// <summary>Price currency. When blank, the country's local currency is used.</summary>
    public string? Currency { get; init; }

    public DateOnly? EffectiveDate { get; init; }
}

public sealed record VehicleProfileRequest
{
    public string Name { get; init; } = string.Empty;

    public int? Year { get; init; }

    public string? Make { get; init; }

    public string? Model { get; init; }

    public string FuelType { get; init; } = "GASOLINE_95";

    public double ConsumptionValue { get; init; }

    public string ConsumptionUnit { get; init; } = "L_PER_100KM";

    public double? TankSizeLiters { get; init; }
}
