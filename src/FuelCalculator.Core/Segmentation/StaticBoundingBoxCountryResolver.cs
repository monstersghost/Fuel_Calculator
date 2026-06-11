using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Segmentation;

public sealed class StaticBoundingBoxCountryResolver : ICountryResolver
{
    private static readonly IReadOnlyList<CountryBounds> Bounds =
    [
        new("KW", 28.4, 30.2, 46.4, 49.2),
        new("QA", 24.3, 26.4, 50.6, 52.2),
        new("BH", 25.5, 26.5, 50.2, 50.9),
        new("AE", 22.4, 26.5, 51.4, 56.7),
        new("OM", 16.0, 26.6, 52.0, 60.5),
        new("SA", 16.0, 32.5, 34.0, 56.8)
    ];

    public Task<string?> ResolveCountryCodeAsync(GeoPoint point, CancellationToken cancellationToken = default)
    {
        var match = Bounds.FirstOrDefault(bounds => bounds.Contains(point));
        return Task.FromResult(match?.CountryCode);
    }

    private sealed record CountryBounds(
        string CountryCode,
        double MinLatitude,
        double MaxLatitude,
        double MinLongitude,
        double MaxLongitude)
    {
        public bool Contains(GeoPoint point) =>
            point.Latitude >= MinLatitude
            && point.Latitude <= MaxLatitude
            && point.Longitude >= MinLongitude
            && point.Longitude <= MaxLongitude;
    }
}
