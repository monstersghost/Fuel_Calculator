using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

public interface IRouteProvider
{
    Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken = default);
}
