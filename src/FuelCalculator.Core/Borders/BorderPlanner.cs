using System.Text.Json;
using FuelCalculator.Core.Currency;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Planning;
using FuelCalculator.Core.Routing;

namespace FuelCalculator.Core.Borders;

/// <summary>A point on the route where it leaves one country and enters another.</summary>
public sealed record BorderTransition(double DistanceKm, string FromCountry, string ToCountry, GeoPoint Point);

public sealed record BorderCost(string Label, decimal Amount, string Currency, decimal? ConvertedAmount);

/// <summary>What to expect at one border: where, when, how long, what it costs and what you need.</summary>
public sealed record BorderCrossingEstimate(
    double DistanceKm,
    string FromCountry,
    string ToCountry,
    string Name,
    bool IsKnownCrossing,
    bool IsOpenBorder,
    double? DistanceFromRouteKm,
    string Hours,
    DateTime? ArrivalAtBorder,
    string Band,
    int DelayMinutes,
    IReadOnlyList<BorderCost> Costs,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Sources,
    ConfidenceLevel Confidence);

/// <summary>Driving time plus border waits and fuel stops, and the arrival time when a departure is given.</summary>
public sealed record TripTiming(
    DateTime? Departure,
    double DrivingMinutes,
    int BorderMinutes,
    int FuelStopMinutes,
    double TotalMinutes,
    DateTime? Arrival)
{
    public const int MinutesPerFuelStop = 10;

    public static TripTiming Create(DateTime? departure, double drivingMinutes, int borderMinutes, int fuelStops)
    {
        var fuelMinutes = fuelStops * MinutesPerFuelStop;
        var total = Math.Round(drivingMinutes + borderMinutes + fuelMinutes, 1);
        return new TripTiming(departure, Math.Round(drivingMinutes, 1), borderMinutes, fuelMinutes, total, RoundToMinute(departure?.AddMinutes(total)));
    }

    /// <summary>Times shown to people are rounded to the minute.</summary>
    public static DateTime? RoundToMinute(DateTime? time) =>
        time is { } value ? new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind).AddMinutes(value.Second >= 30 ? 1 : 0) : null;
}

public static class BorderDetector
{
    /// <summary>
    /// Finds where the route changes country, from the ordered country segments. A stretch of unknown country
    /// (usually sea under a causeway) between two countries counts as one crossing at its middle.
    /// </summary>
    public static IReadOnlyList<BorderTransition> Detect(IReadOnlyList<CountryRouteSegment> orderedSegments, RouteProjection route)
    {
        var known = new List<(string Country, double Start, double End)>();
        var position = 0d;

        foreach (var segment in orderedSegments.Where(segment => segment.DistanceKm > 0))
        {
            var country = segment.CountryCode.Trim().ToUpperInvariant();

            if (country != "UNKNOWN")
            {
                if (known.Count > 0 && known[^1].Country == country && Math.Abs(known[^1].End - position) < 1e-9)
                {
                    known[^1] = (country, known[^1].Start, position + segment.DistanceKm);
                }
                else
                {
                    known.Add((country, position, position + segment.DistanceKm));
                }
            }

            position += segment.DistanceKm;
        }

        var transitions = new List<BorderTransition>();

        for (var i = 1; i < known.Count; i++)
        {
            if (known[i].Country == known[i - 1].Country)
            {
                continue;
            }

            var km = (known[i - 1].End + known[i].Start) / 2d;
            transitions.Add(new BorderTransition(Math.Round(km, 3), known[i - 1].Country, known[i].Country, GeoMath.PointAtDistance(route.Points, km)));
        }

        return transitions;
    }
}

/// <summary>Curated border data from Borders/Data/border-crossings.json.</summary>
public sealed class BorderCatalog
{
    private static readonly Lazy<BorderCatalog> Embedded = new(Load);

    public required DateOnly CheckedOn { get; init; }
    public required IReadOnlyList<Crossing> Crossings { get; init; }
    public required IReadOnlyList<HolidayPeak> Holidays { get; init; }
    public required IReadOnlyDictionary<string, CountryRule> EntryRules { get; init; }
    public required IReadOnlyDictionary<string, CountryRule> ExitRules { get; init; }
    public required IReadOnlyList<OpenGroup> OpenGroups { get; init; }
    public required Delay DefaultDelay { get; init; }

    public static BorderCatalog LoadEmbedded() => Embedded.Value;

    public sealed record Delay(int Normal, int Peak, int Holiday);
    public sealed record Cost(string Label, decimal Amount, string Currency, bool PerPerson);
    public sealed record Crossing(string Name, IReadOnlyList<string> Countries, GeoPoint Point, string Hours, string Peaks, Delay Delay, IReadOnlyList<Cost> Costs, IReadOnlyList<string> Notes, IReadOnlyList<string> Sources);
    public sealed record HolidayPeak(string Name, DateOnly From, DateOnly To, string Region);
    public sealed record CountryRule(IReadOnlyList<string> Requirements, IReadOnlyList<Cost> Costs, IReadOnlyList<string> Sources);
    public sealed record OpenGroup(string Name, IReadOnlySet<string> Countries, string Note);

    private static BorderCatalog Load()
    {
        using var document = EmbeddedData.Load("border-crossings.json");
        var root = document.RootElement;

        return new BorderCatalog
        {
            CheckedOn = DateOnly.Parse(root.GetProperty("checkedOn").GetString()!),
            Crossings = root.GetProperty("crossings").EnumerateArray().Select(c => new Crossing(
                c.GetProperty("name").GetString()!,
                Strings(c.GetProperty("countries")),
                new GeoPoint(c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble()),
                c.GetProperty("hours").GetString()!,
                c.GetProperty("peaks").GetString()!,
                ReadDelay(c.GetProperty("delay")),
                Costs(c.GetProperty("costs")),
                Strings(c.GetProperty("notes")),
                Strings(c.GetProperty("sources")))).ToArray(),
            Holidays = root.GetProperty("holidayPeaks").EnumerateArray().Select(h => new HolidayPeak(
                h.GetProperty("name").GetString()!,
                DateOnly.Parse(h.GetProperty("from").GetString()!),
                DateOnly.Parse(h.GetProperty("to").GetString()!),
                h.GetProperty("region").GetString()!)).ToArray(),
            EntryRules = Rules(root.GetProperty("entryRules")),
            ExitRules = Rules(root.GetProperty("exitRules")),
            OpenGroups = root.GetProperty("openBorderGroups").EnumerateArray().Select(g => new OpenGroup(
                g.GetProperty("name").GetString()!,
                Strings(g.GetProperty("countries")).ToHashSet(StringComparer.OrdinalIgnoreCase),
                g.GetProperty("note").GetString()!)).ToArray(),
            DefaultDelay = ReadDelay(root.GetProperty("defaultDelay"))
        };

        static IReadOnlyList<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToArray();

        static Delay ReadDelay(JsonElement delay) =>
            new(delay.GetProperty("normal").GetInt32(), delay.GetProperty("peak").GetInt32(), delay.GetProperty("holiday").GetInt32());

        static IReadOnlyList<Cost> Costs(JsonElement array) => array.EnumerateArray().Select(cost => new Cost(
            cost.GetProperty("label").GetString()!,
            cost.GetProperty("amount").GetDecimal(),
            cost.GetProperty("currency").GetString()!,
            cost.GetProperty("perPerson").GetBoolean())).ToArray();

        static IReadOnlyDictionary<string, CountryRule> Rules(JsonElement rules) =>
            rules.EnumerateObject().ToDictionary(
                rule => rule.Name,
                rule => new CountryRule(Strings(rule.Value.GetProperty("requirements")), Costs(rule.Value.GetProperty("costs")), Strings(rule.Value.GetProperty("sources"))),
                StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Turns border transitions into crossing estimates: the named crossing (nearest curated one for the country
/// pair), the expected wait for the arrival time, tolls and fees in the trip currency, and entry/exit rules.
/// </summary>
public sealed class BorderPlanner(BorderCatalog catalog, ICurrencyConverter currencyConverter)
{
    private const double FarFromKnownCrossingKm = 60d;

    public async Task<IReadOnlyList<BorderCrossingEstimate>> PlanAsync(
        IReadOnlyList<BorderTransition> transitions,
        string outputCurrency,
        int passengers,
        DateTime? departure,
        double drivingMinutes,
        double totalKm,
        CancellationToken cancellationToken = default)
    {
        var estimates = new List<BorderCrossingEstimate>();
        var earlierWaits = 0;
        passengers = Math.Max(1, passengers);

        foreach (var transition in transitions)
        {
            var arrival = TripTiming.RoundToMinute(departure?.AddMinutes((totalKm > 0 ? drivingMinutes * transition.DistanceKm / totalKm : 0) + earlierWaits));
            var openGroup = catalog.OpenGroups.FirstOrDefault(group => group.Countries.Contains(transition.FromCountry) && group.Countries.Contains(transition.ToCountry));
            var crossing = openGroup is null ? NearestCrossing(transition) : null;
            var costs = new List<BorderCost>();
            var requirements = new List<string>();
            var notes = new List<string>();
            var sources = new List<string>();

            string name, hours, band;
            int delay;
            bool known;
            double? distance = null;
            ConfidenceLevel confidence;

            if (openGroup is not null)
            {
                name = $"{transition.FromCountry} → {transition.ToCountry} (open border)";
                hours = "Always open";
                band = "open";
                delay = 0;
                known = false;
                confidence = ConfidenceLevel.MEDIUM;
                notes.Add(openGroup.Note);
            }
            else
            {
                var peaks = crossing?.Peaks ?? "none";
                band = BandFor(arrival, peaks);
                var table = crossing?.Delay ?? catalog.DefaultDelay;
                delay = band switch { "holiday" => table.Holiday, "peak" => table.Peak, _ => table.Normal };
                known = crossing is not null;
                confidence = ConfidenceLevel.LOW;

                if (crossing is not null)
                {
                    name = crossing.Name;
                    hours = crossing.Hours;
                    distance = Math.Round(GeoMath.HaversineKm(crossing.Point, transition.Point), 1);
                    notes.AddRange(crossing.Notes);
                    sources.AddRange(crossing.Sources);
                    await AddCostsAsync(crossing.Costs);

                    if (distance > FarFromKnownCrossingKm)
                    {
                        notes.Add($"The route crosses about {distance:0} km from {crossing.Name}, the nearest crossing on file; check which crossing it really uses.");
                    }
                }
                else
                {
                    name = $"{transition.FromCountry} → {transition.ToCountry} border";
                    hours = "Unknown";
                    notes.Add("No data on file for this border; the wait is a generic estimate.");
                }
            }

            if (catalog.ExitRules.TryGetValue(transition.FromCountry, out var exit))
            {
                requirements.AddRange(exit.Requirements);
                sources.AddRange(exit.Sources);
                await AddCostsAsync(exit.Costs);
            }

            if (catalog.EntryRules.TryGetValue(transition.ToCountry, out var entry))
            {
                requirements.AddRange(entry.Requirements);
                sources.AddRange(entry.Sources);
                await AddCostsAsync(entry.Costs);
            }

            estimates.Add(new BorderCrossingEstimate(
                transition.DistanceKm,
                transition.FromCountry,
                transition.ToCountry,
                name,
                known,
                openGroup is not null,
                distance,
                hours,
                arrival,
                band,
                delay,
                costs,
                requirements.Distinct().ToArray(),
                notes.Distinct().ToArray(),
                sources.Distinct().ToArray(),
                confidence));

            earlierWaits += delay;

            async Task AddCostsAsync(IEnumerable<BorderCatalog.Cost> items)
            {
                foreach (var item in items)
                {
                    var amount = item.PerPerson ? item.Amount * passengers : item.Amount;
                    var converted = await currencyConverter.ConvertAsync(amount, item.Currency, outputCurrency, cancellationToken);
                    var label = item.PerPerson && passengers > 1 ? $"{item.Label} (x{passengers} people)" : item.Label;
                    costs.Add(new BorderCost(label, amount, item.Currency, converted is null ? null : Math.Round(converted.Amount, 3)));
                }
            }
        }

        return estimates;
    }

    private BorderCatalog.Crossing? NearestCrossing(BorderTransition transition) =>
        catalog.Crossings
            .Where(crossing => crossing.Countries.Contains(transition.FromCountry, StringComparer.OrdinalIgnoreCase)
                && crossing.Countries.Contains(transition.ToCountry, StringComparer.OrdinalIgnoreCase))
            .OrderBy(crossing => GeoMath.HaversineKm(crossing.Point, transition.Point))
            .FirstOrDefault();

    private string BandFor(DateTime? arrival, string peaks)
    {
        if (arrival is not { } when)
        {
            return "normal";
        }

        var day = DateOnly.FromDateTime(when);

        if (catalog.Holidays.Any(holiday => day >= holiday.From && day <= holiday.To && (holiday.Region == "all" || holiday.Region == peaks)))
        {
            return "holiday";
        }

        var isPeak = peaks switch
        {
            // GCC weekend: Thursday afternoon to Saturday night.
            "gcc" => (when.DayOfWeek == DayOfWeek.Thursday && when.Hour >= 14) || when.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday,
            // European weekend: Friday afternoon to Sunday night.
            "europe" => (when.DayOfWeek == DayOfWeek.Friday && when.Hour >= 15) || when.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            _ => false
        };

        return isPeak ? "peak" : "normal";
    }
}
