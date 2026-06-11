using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

public static class GeoMath
{
    private const double EarthRadiusKm = 6371.0088d;

    public static double HaversineKm(GeoPoint first, GeoPoint second)
    {
        var dLat = ToRadians(second.Latitude - first.Latitude);
        var dLng = ToRadians(second.Longitude - first.Longitude);
        var lat1 = ToRadians(first.Latitude);
        var lat2 = ToRadians(second.Latitude);

        var a = Math.Pow(Math.Sin(dLat / 2d), 2d)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLng / 2d), 2d);

        return EarthRadiusKm * 2d * Math.Asin(Math.Sqrt(a));
    }

    public static double TotalDistanceKm(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count < 2)
        {
            return 0d;
        }

        var distance = 0d;

        for (var i = 1; i < points.Count; i++)
        {
            distance += HaversineKm(points[i - 1], points[i]);
        }

        return distance;
    }

    public static GeoPoint Interpolate(GeoPoint first, GeoPoint second, double fraction)
    {
        var clamped = Math.Clamp(fraction, 0d, 1d);

        return new GeoPoint(
            first.Latitude + (second.Latitude - first.Latitude) * clamped,
            first.Longitude + (second.Longitude - first.Longitude) * clamped);
    }

    public static GeoPoint PointAtDistance(IReadOnlyList<GeoPoint> points, double targetDistanceKm)
    {
        if (points.Count == 0)
        {
            return new GeoPoint(0, 0);
        }

        if (points.Count == 1 || targetDistanceKm <= 0)
        {
            return points[0];
        }

        var travelled = 0d;

        for (var i = 1; i < points.Count; i++)
        {
            var segmentDistance = HaversineKm(points[i - 1], points[i]);

            if (travelled + segmentDistance >= targetDistanceKm)
            {
                var remaining = targetDistanceKm - travelled;
                return Interpolate(points[i - 1], points[i], segmentDistance == 0 ? 0 : remaining / segmentDistance);
            }

            travelled += segmentDistance;
        }

        return points[^1];
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
