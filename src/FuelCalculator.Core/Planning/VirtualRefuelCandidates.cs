using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Planning;

/// <summary>
/// Stand-in fuel stations until real station data is loaded: one at the start, then every
/// <c>spacingKm</c> through each country, plus a last chance 1 km before each border and a first
/// chance 1 km after it, so the planner can fill up just before fuel gets dearer. Stretches whose
/// country is unknown get no stations. Country refuel rules (such as Oman's cap) are applied here.
/// </summary>
public static class VirtualRefuelCandidates
{
    public static IReadOnlyList<RefuelCandidate> FromSegments(
        IReadOnlyList<CountryRouteSegment> orderedSegments,
        double spacingKm = 50d)
    {
        var segments = orderedSegments.Where(segment => segment.DistanceKm > 0).ToArray();
        var total = segments.Sum(segment => segment.DistanceKm);
        var candidates = new List<RefuelCandidate>();
        var start = 0d;

        for (var i = 0; i < segments.Length; i++)
        {
            var country = segments[i].CountryCode.Trim().ToUpperInvariant();
            var end = start + segments[i].DistanceKm;
            var isFirst = i == 0;
            var isLast = i == segments.Length - 1;

            if (country != "UNKNOWN")
            {
                var first = isFirst ? start : Math.Min(start + 1d, (start + end) / 2d);
                var last = isLast ? end : Math.Max(end - 1d, (start + end) / 2d);
                var points = new List<double>();

                for (var km = first; km < last - 1e-9; km += spacingKm)
                {
                    points.Add(km);
                }

                if (!isLast)
                {
                    points.Add(last);
                }

                foreach (var km in points.Where(km => km < total).Distinct())
                {
                    var cap = CountryRefuelRules.MaxLitersNearBorder(country, km, start, end, borderAtStart: !isFirst, borderAtEnd: !isLast);
                    candidates.Add(new RefuelCandidate(Math.Round(km, 3), country, MaxLiters: cap));
                }
            }

            start = end;
        }

        return candidates.OrderBy(candidate => candidate.DistanceKm).ToArray();
    }
}

/// <summary>Country rules that limit how much fuel can be bought. Checked 2026-11-27; see docs/ops/FUEL_PRICE_SEED.md.</summary>
public static class CountryRefuelRules
{
    // Oman: temporary 30 L per fill at stations within 50 km of a border crossing (since late October 2026).
    private const string OmanCode = "OM";
    private const double OmanCapLiters = 30d;
    private const double OmanBorderZoneKm = 50d;

    /// <summary>
    /// Applies the rules to stations placed on a route, using the route's ordered country segments to
    /// know where each border is. A station keeps any cap it already had when no rule applies.
    /// </summary>
    public static IReadOnlyList<RefuelCandidate> Apply(
        IReadOnlyList<RefuelCandidate> candidates,
        IReadOnlyList<CountryRouteSegment> orderedSegments)
    {
        var segments = orderedSegments.Where(segment => segment.DistanceKm > 0).ToArray();
        var zones = new List<(double Start, double End, bool BorderAtStart, bool BorderAtEnd)>();
        var start = 0d;

        for (var i = 0; i < segments.Length; i++)
        {
            zones.Add((start, start + segments[i].DistanceKm, i > 0, i < segments.Length - 1));
            start += segments[i].DistanceKm;
        }

        return candidates
            .Select(candidate =>
            {
                var zone = zones.FirstOrDefault(z => candidate.DistanceKm >= z.Start && candidate.DistanceKm < z.End);

                if (zone == default)
                {
                    return candidate;
                }

                var cap = MaxLitersNearBorder(candidate.CountryCode, candidate.DistanceKm, zone.Start, zone.End, zone.BorderAtStart, zone.BorderAtEnd);
                return cap is null ? candidate : candidate with { MaxLiters = cap };
            })
            .OrderBy(candidate => candidate.DistanceKm)
            .ToArray();
    }

    public static double? MaxLitersNearBorder(string countryCode, double km, double countryStartKm, double countryEndKm, bool borderAtStart, bool borderAtEnd)
    {
        if (!countryCode.Equals(OmanCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var nearEntry = borderAtStart && km - countryStartKm <= OmanBorderZoneKm;
        var nearExit = borderAtEnd && countryEndKm - km <= OmanBorderZoneKm;
        return nearEntry || nearExit ? OmanCapLiters : null;
    }
}
