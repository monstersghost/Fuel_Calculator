using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

public static class PolylineCodec
{
    public static IReadOnlyList<GeoPoint> Decode(string? encodedPolyline)
    {
        if (string.IsNullOrWhiteSpace(encodedPolyline))
        {
            return [];
        }

        var points = new List<GeoPoint>();
        var index = 0;
        var latitude = 0;
        var longitude = 0;

        while (index < encodedPolyline.Length)
        {
            latitude += DecodeNextValue(encodedPolyline, ref index);
            longitude += DecodeNextValue(encodedPolyline, ref index);
            points.Add(new GeoPoint(latitude / 1E5, longitude / 1E5));
        }

        return points;
    }

    public static string Encode(IReadOnlyList<GeoPoint> points)
    {
        var encoded = new System.Text.StringBuilder();
        var previousLatitude = 0;
        var previousLongitude = 0;

        foreach (var point in points)
        {
            var latitude = (int)Math.Round(point.Latitude * 1E5);
            var longitude = (int)Math.Round(point.Longitude * 1E5);

            EncodeValue(latitude - previousLatitude, encoded);
            EncodeValue(longitude - previousLongitude, encoded);

            previousLatitude = latitude;
            previousLongitude = longitude;
        }

        return encoded.ToString();
    }

    private static int DecodeNextValue(string encoded, ref int index)
    {
        var shift = 0;
        var result = 0;
        int value;

        do
        {
            if (index >= encoded.Length)
            {
                throw new FormatException("Encoded polyline ended unexpectedly.");
            }

            value = encoded[index++] - 63;
            result |= (value & 0x1f) << shift;
            shift += 5;
        }
        while (value >= 0x20);

        return (result & 1) == 1 ? ~(result >> 1) : result >> 1;
    }

    private static void EncodeValue(int value, System.Text.StringBuilder encoded)
    {
        var shifted = value < 0 ? ~(value << 1) : value << 1;

        while (shifted >= 0x20)
        {
            encoded.Append((char)((0x20 | (shifted & 0x1f)) + 63));
            shifted >>= 5;
        }

        encoded.Append((char)(shifted + 63));
    }
}
