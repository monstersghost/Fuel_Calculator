using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Routing;

namespace FuelCalculator.Core.Segmentation;

public sealed class PolylineCountrySegmenter(
    ICountryResolver countryResolver,
    RouteSegmentationOptions? options = null) : IRouteCountrySegmenter
{
    private readonly ICountryResolver _countryResolver = countryResolver;
    private readonly RouteSegmentationOptions _options = options ?? new RouteSegmentationOptions();

    public async Task<RouteCountrySegmentationResult> SegmentAsync(
        RouteResult route,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>
        {
            $"Country split is approximate; MVP samples decoded route points every {GetSampleEveryKm():0.##} km."
        };

        var points = PolylineCodec.Decode(route.EncodedPolyline).ToArray();

        if (points.Length < 2)
        {
            points = route.Points.ToArray();
        }

        if (points.Length < 2)
        {
            warnings.Add("Route polyline did not contain enough points for country segmentation.");
            return new RouteCountrySegmentationResult(
                [new CountryRouteSegment("UNKNOWN", Math.Max(0, route.TotalDistanceKm))],
                warnings);
        }

        var totalDistance = route.TotalDistanceKm > 0 ? route.TotalDistanceKm : GeoMath.TotalDistanceKm(points);

        if (totalDistance <= 0)
        {
            warnings.Add("Route distance was zero; no country distance could be estimated.");
            return new RouteCountrySegmentationResult([], warnings);
        }

        var breakpoints = BuildBreakpoints(totalDistance);
        var segments = new List<CountryRouteSegment>();

        for (var i = 1; i < breakpoints.Count; i++)
        {
            var startKm = breakpoints[i - 1];
            var endKm = breakpoints[i];
            var intervalDistance = endKm - startKm;

            if (intervalDistance <= 0)
            {
                continue;
            }

            var midpoint = GeoMath.PointAtDistance(points, startKm + intervalDistance / 2d);
            var countryCode = await _countryResolver.ResolveCountryCodeAsync(midpoint, cancellationToken) ?? "UNKNOWN";

            if (countryCode.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("One or more route samples could not be mapped to a country.");
            }

            AddOrMerge(segments, countryCode, intervalDistance);
        }

        return new RouteCountrySegmentationResult(segments, warnings.Distinct().ToArray());
    }

    private List<double> BuildBreakpoints(double totalDistanceKm)
    {
        var sampleEveryKm = GetSampleEveryKm();
        var breakpoints = new List<double> { 0d };

        for (var distance = sampleEveryKm; distance < totalDistanceKm; distance += sampleEveryKm)
        {
            breakpoints.Add(distance);
        }

        breakpoints.Add(totalDistanceKm);
        return breakpoints;
    }

    private double GetSampleEveryKm() => _options.SampleEveryKm > 0 ? _options.SampleEveryKm : 5d;

    private static void AddOrMerge(List<CountryRouteSegment> segments, string countryCode, double distanceKm)
    {
        var normalized = string.IsNullOrWhiteSpace(countryCode)
            ? "UNKNOWN"
            : countryCode.Trim().ToUpperInvariant();

        if (segments.Count > 0 && segments[^1].CountryCode.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            var previous = segments[^1];
            segments[^1] = previous with { DistanceKm = previous.DistanceKm + distanceKm };
            return;
        }

        segments.Add(new CountryRouteSegment(normalized, distanceKm));
    }
}
