using FuelCalculator.Core.Calculation;
using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Vehicles;

public interface IVehicleDataProvider
{
    Task<VehicleProfileDraft?> LookupAsync(
        int? year,
        string? make,
        string? model,
        FuelType fuelType,
        CancellationToken cancellationToken = default);
}

public interface IVehicleProfileRepository
{
    Task<IReadOnlyList<VehicleProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<VehicleProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<VehicleProfile> SaveAsync(VehicleProfileDraft draft, CancellationToken cancellationToken = default);
}

public sealed class NullVehicleDataProvider : IVehicleDataProvider
{
    public Task<VehicleProfileDraft?> LookupAsync(
        int? year,
        string? make,
        string? model,
        FuelType fuelType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<VehicleProfileDraft?>(null);
}

public sealed class InMemoryVehicleProfileRepository : IVehicleProfileRepository
{
    private readonly List<VehicleProfile> _profiles = [];
    private readonly object _gate = new();

    public Task<IReadOnlyList<VehicleProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<VehicleProfile>>(_profiles.OrderBy(profile => profile.Name).ToArray());
        }
    }

    public Task<VehicleProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_profiles.FirstOrDefault(profile => profile.Id == id));
        }
    }

    public Task<VehicleProfile> SaveAsync(VehicleProfileDraft draft, CancellationToken cancellationToken = default)
    {
        var normalizedConsumption = ConsumptionConverter.ToLPer100Km(draft.ConsumptionValue, draft.ConsumptionUnit);
        var profile = new VehicleProfile(
            Guid.NewGuid(),
            draft.Name.Trim(),
            draft.Year,
            string.IsNullOrWhiteSpace(draft.Make) ? null : draft.Make.Trim(),
            string.IsNullOrWhiteSpace(draft.Model) ? null : draft.Model.Trim(),
            draft.FuelType,
            draft.ConsumptionValue,
            draft.ConsumptionUnit,
            normalizedConsumption,
            draft.TankSizeLiters);

        lock (_gate)
        {
            _profiles.Add(profile);
        }

        return Task.FromResult(profile);
    }
}
