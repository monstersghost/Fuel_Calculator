namespace FuelCalculator.Core.Planning;

/// <summary>
/// Plans where to buy fuel along a fixed route so the trip costs as little as possible without
/// dropping below the reserve. It is the classic gas-station greedy, with two tweaks for real driving:
/// <list type="bullet">
/// <item>If a station at the same or a lower price is reachable on the fuel in the tank, drive on and
/// decide there. This avoids topping up at every station of an equally priced country.</item>
/// <item>Otherwise, buy just enough to reach the nearest cheaper station in full-tank range. If there
/// is none, buy enough for the destination when it is in range, or else fill up and drive to the
/// cheapest (then furthest) station in range.</item>
/// </list>
/// A per-station litre cap (Oman near its borders) limits a purchase. Prices are compared in the
/// output currency; a country with no price is treated as the dearest option, and fuel bought there
/// leaves the plan incomplete.
/// </summary>
public static class RefuelPlanner
{
    private const double Epsilon = 1e-9;

    public static RefuelPlan Plan(RefuelPlanInput input)
    {
        if (input.LPer100Km <= 0 || input.TankLiters <= 0 || input.TotalDistanceKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Consumption and tank size must be positive and distance non-negative.");
        }

        var litresPerKm = input.LPer100Km / 100d;
        var capacity = input.TankLiters;
        var reserve = capacity * Math.Clamp(input.ReservePercentage, 0d, 99d) / 100d;
        var startFuel = capacity * Math.Clamp(input.StartFuelPercentage, 0d, 100d) / 100d;
        var destination = input.TotalDistanceKm;

        var stations = input.Candidates
            .Where(station => station.DistanceKm >= 0 && station.DistanceKm < destination)
            .OrderBy(station => station.DistanceKm)
            .ToArray();

        var warnings = new List<string>();
        var stops = new List<PlannedStop>();
        var feasible = true;
        var fuel = startFuel;
        var position = 0d;
        var here = stations.FirstOrDefault(station => station.DistanceKm <= Epsilon);

        if (startFuel < reserve - Epsilon && here is null)
        {
            warnings.Add("You start below the reserve and there is no fuel at the start of the route.");
        }

        for (var guard = 0; guard < stations.Length * 4 + 8; guard++)
        {
            if (CanReach(destination))
            {
                break;
            }

            var ahead = stations.Where(station => station.DistanceKm > position + Epsilon).ToArray();
            var reachableNow = ahead.Where(station => CanReach(station.DistanceKm)).ToArray();

            if (here is null)
            {
                // At the origin with nowhere to buy: drive to the best station the tank can reach.
                var next = Best(reachableNow);

                if (next is null)
                {
                    feasible = false;
                    warnings.Add(GapWarning(ahead));
                    break;
                }

                Drive(next);
                continue;
            }

            var hereCost = Price(here);
            var postpone = Best(reachableNow.Where(station => Price(station) <= hereCost));

            if (postpone is not null)
            {
                Drive(postpone);
                continue;
            }

            var fullReach = position + (capacity - reserve) / litresPerKm;
            var inFullRange = ahead.Where(station => station.DistanceKm <= fullReach + Epsilon).ToArray();
            var cheaper = inFullRange.FirstOrDefault(station => Price(station) < hereCost);
            double need;
            string reason;
            RefuelCandidate? target;

            if (cheaper is not null)
            {
                need = reserve + (cheaper.DistanceKm - position) * litresPerKm - fuel;
                reason = $"Buy just enough to reach cheaper fuel in {cheaper.CountryCode} at km {cheaper.DistanceKm:0}.";
                target = cheaper;
            }
            else if (destination <= fullReach + Epsilon)
            {
                need = reserve + (destination - position) * litresPerKm - fuel;
                reason = "Top up to reach the destination with the reserve in the tank.";
                target = null;
            }
            else
            {
                need = capacity - fuel;
                reason = inFullRange.Length == 0
                    ? "Fill up: there is no fuel within range."
                    : inFullRange.Any(station => Price(station) > hereCost)
                        ? "Fill up: fuel ahead costs more."
                        : "Fill up.";
                target = Best(inFullRange);
            }

            var bought = Buy(here, Math.Max(0d, need), reason);

            if (bought < need - Epsilon && here.MaxLiters is not null)
            {
                // Capped: aim for the best station the smaller fill can reach instead.
                target = CanReach(destination) ? null : Best(ahead.Where(station => CanReach(station.DistanceKm)));

                if (target is null && !CanReach(destination))
                {
                    feasible = false;
                    warnings.Add($"The {here.MaxLiters:0} L limit at km {here.DistanceKm:0} ({here.CountryCode}) is not enough to reach the next fuel.");
                    break;
                }
            }

            if (target is null)
            {
                if (!CanReach(destination))
                {
                    feasible = false;
                    warnings.Add(GapWarning(ahead));
                    break;
                }

                continue;
            }

            if (!CanReach(target.DistanceKm))
            {
                feasible = false;
                warnings.Add(GapWarning(ahead));
                break;
            }

            Drive(target);
        }

        var arrivalFuel = feasible ? fuel - (destination - position) * litresPerKm : 0d;
        var originPrice = stations.Length > 0 ? input.Prices.GetValueOrDefault(stations[0].CountryCode)?.PricePerLiterInOutput : null;
        var lastPaid = stops.LastOrDefault(stop => stop.ConvertedCost is not null);
        var lastPaidPrice = lastPaid is null ? originPrice : input.Prices.GetValueOrDefault(lastPaid.CountryCode)?.PricePerLiterInOutput;

        var complete = stops.All(stop => stop.ConvertedCost is not null)
            && (startFuel <= Epsilon || originPrice is not null)
            && (arrivalFuel <= Epsilon || lastPaidPrice is not null);

        var purchasedCost = Money(stops.Sum(stop => stop.ConvertedCost ?? 0m));
        var startValue = Money((decimal)Round(startFuel) * (originPrice ?? 0m));
        var arrivalValue = Money((decimal)Round(Math.Max(0d, arrivalFuel)) * (lastPaidPrice ?? 0m));

        if (stops.Any(stop => stop.ConvertedCost is null))
        {
            warnings.Add("Some fuel is bought where there is no price; the trip cost leaves it out.");
        }

        return new RefuelPlan(
            stops,
            Round(startFuel),
            Round(Math.Max(0d, arrivalFuel)),
            Round(Math.Max(0d, arrivalFuel) / capacity * 100d),
            Round(stops.Sum(stop => stop.Liters)),
            purchasedCost,
            startValue,
            arrivalValue,
            Money(purchasedCost + startValue - arrivalValue),
            complete,
            feasible,
            warnings.Distinct().ToArray());

        bool CanReach(double km) => (km - position) * litresPerKm <= fuel - reserve + Epsilon;

        decimal Price(RefuelCandidate station) =>
            input.Prices.GetValueOrDefault(station.CountryCode)?.PricePerLiterInOutput ?? decimal.MaxValue;

        // Lowest price first, then the furthest along: fewer stops for the same money.
        RefuelCandidate? Best(IEnumerable<RefuelCandidate> options) => options
            .OrderBy(Price)
            .ThenByDescending(station => station.DistanceKm)
            .FirstOrDefault();

        void Drive(RefuelCandidate station)
        {
            fuel -= (station.DistanceKm - position) * litresPerKm;
            position = station.DistanceKm;
            here = station;
        }

        double Buy(RefuelCandidate station, double litres, string why)
        {
            var space = capacity - fuel;
            var amount = Math.Min(litres, space);

            if (station.MaxLiters is { } cap && amount > cap)
            {
                amount = cap;
                why += $" Limited to {cap:0} L per fill here.";
            }

            // Round up to the millilitre so the plan is never short of the reserve, but never past a full tank.
            amount = Math.Min(Math.Ceiling(amount * 1000d - 1e-6) / 1000d, Round(space));

            if (amount <= 0.0005d)
            {
                return 0d;
            }

            var price = input.Prices.GetValueOrDefault(station.CountryCode);
            var before = fuel;
            fuel += amount;

            stops.Add(new PlannedStop(
                Round(station.DistanceKm),
                station.CountryCode,
                station.Name,
                amount,
                price?.PricePerLiter,
                price?.Currency,
                price?.PricePerLiter is { } local ? Money((decimal)amount * local) : null,
                price?.PricePerLiterInOutput is { } converted ? Money((decimal)amount * converted) : null,
                Round(before / capacity * 100d),
                Round(fuel / capacity * 100d),
                why));

            return amount;
        }

        string GapWarning(IReadOnlyList<RefuelCandidate> remaining)
        {
            var nextKm = remaining.Count > 0 ? remaining[0].DistanceKm : destination;
            var usable = (capacity - reserve) / litresPerKm;
            return $"There is a gap of {nextKm - position:0} km with no fuel after km {position:0}; a full tank covers {usable:0} km while keeping the reserve.";
        }
    }

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static decimal Money(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
