using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Segmentation;

public interface IRouteCountrySegmenter
{
    Task<RouteCountrySegmentationResult> SegmentAsync(RouteResult route, CancellationToken cancellationToken = default);
}

public interface IRouteCountryIntersectionSegmenter : IRouteCountrySegmenter
{
}

public interface ICountryResolver
{
    Task<string?> ResolveCountryCodeAsync(GeoPoint point, CancellationToken cancellationToken = default);
}

public sealed class RouteSegmentationOptions
{
    public double SampleEveryKm { get; init; } = 5d;
}
