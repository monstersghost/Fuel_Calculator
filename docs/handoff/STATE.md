# Fuel Calculator - Project State

**Last updated:** 2026-11-27 (branch `fix/correctness-pass`: correctness pass, refuel planner, worldwide data, OSRM routing, borders)

## Current MVP

The app estimates cross-border trip fuel usage and cost from route input, vehicle consumption, fuel type, country-specific prices, and output currency.

## Implemented

- .NET 8 ASP.NET Core API.
- React + Vite frontend.
- `POST /api/trips/estimate`.
- Fuel types: 91 Octane, 95 Octane, 98 Octane, Diesel.
- Consumption normalization for L/100km, km/L, US MPG, UK MPG.
- **Routes from OSRM** (OpenStreetMap roads, no key; the default). Stops are found with Nominatim, or given
  as `label | lat,lon`. Unknown places or unroutable stops return HTTP 422 with a readable message.
  `RouteProvider:Mode` can be `Osrm`, `Google` (needs `GoogleRoutes:ApiKey`) or `Mock` (fixed GCC corridors, for tests).
- **Border crossings** (`Borders/`, data in `Borders/Data/border-crossings.json`, hand-curated, LOW confidence):
  - Each change of country on the route is matched to the nearest known crossing for that pair of countries
    (19 crossings: the GCC, Levant, Iraq and Turkey).
  - Waits come from bands: normal, weekend peak (GCC Thursday 14:00 to Saturday; Europe Friday 15:00 to Sunday) and holiday windows.
  - The data also holds tolls (King Fahd Causeway SAR 35), entry/exit rules (Saudi Manafith insurance and the
    90-day rule, the UAE exit fee of AED 35 per person, the Jordan carnet, Oman insurance) and Schengen open borders.
- **Trip timing**: optional `departureTime` and `passengers` in the request. The result gives `borderCrossings`,
  `borderCostTotal` and `timing` (driving + border waits + 10 min per fuel stop, and the arrival time).
- **Live EU prices**: the EU Weekly Oil Bulletin is re-read every 12 h in the background (`EuBulletinRefresher`).
  Live prices rank above the seed; if a refresh fails, the last copy or the seed is used.
- **Price periods**: with a travel date, a warning appears when a country that sets prices monthly (AE, QA, OM, BH, SA, JO)
  or quarterly (KW, EG) will have a newer price by then.
- Polyline country segmentation, sampled every 5 km, using real borders for the whole world (Natural Earth 1:50m,
  embedded, 237 countries; see `src/FuelCalculator.Core/Segmentation/Data/README.md`).
  Points off the simplified coastline (causeways, coastal roads) snap to the nearest country within 25 km.
- Manual fuel price overrides with precedence over seed data. A blank currency means the country's own.
- `CountryCatalog`: local currency and the grades sold per country. A grade not sold (98 in SA, QA, BH, OM)
  is priced as the nearest grade sold, with a warning.
- Totals report `isTotalComplete` and `countriesMissingPrice`; a missing price is never silently 0.
- Route duration is returned (`durationMinutes`).
- Refuel estimate keeps a reserve (default 15%, `reservePercentage` in the request).
- **Refuel planner** (`Planning/RefuelPlanner.cs`), returned as `refuelPlan` with `refuelSaving`. It plans where to buy:
  - It is the gas-station greedy: drive on to an equal or cheaper station you can reach, otherwise buy just enough
    to reach cheaper fuel, otherwise fill up.
  - The trip cost is fuel bought + fuel you start with (at the origin price) − fuel left on arrival (at the last price paid).
  - Stations are virtual for now (`VirtualRefuelCandidates`): every 50 km, plus 1 km before and after each border.
  - Oman's 30 L cap within 50 km of a border is applied (`CountryRefuelRules`).
- **Worldwide data, built by `tools/data/build_world_data.py`** (see `tools/data/README.md`):
  - Seed prices for 151 countries, in priority order:
    1. Hand-checked prices in `tools/data/curated-prices.csv`: the Middle East plus the US, UK, CA, TR, AU, IN, CH, ZA,
       NO, RU, TW and others, each with its source.
    2. The EU Weekly Oil Bulletin for all 27 EU countries (week of 2026-11-23).
    3. The World Bank Global Fuel Prices Database (to April 2025, LOW).
  - Currencies for every ISO country.
  - An FX snapshot of 166 currencies (Exchange Rate API, 2026-11-27; credited in the UI).
  - Seed prices older than six months carry a warning.
- **Real fuel stations** from OpenStreetMap (Overpass API), fetched per 0.1° tile and cached on disk for 30 days
  (`%LOCALAPPDATA%/FuelCalculator/station-tiles`):
  - Estimates never wait. Cached tiles are used straight away; missing tiles are fetched in the background
    (`StationPrefetcher`, 25 tiles per request, one request at a time, second server if the first is busy).
  - Until a route's tiles are all cached, stand-in stations are used, with a progress note.
  - Stations are snapped to the route within 2 km. A station tagged as not selling the requested fuel is skipped,
    and `opening_hours=24/7` is shown.
  - `FuelStations:Mode`: `Auto` (on for real routes) / `Overpass` / `Virtual`.
- The planner buys at least 5 L per stop.
- When a grade has no price, the nearest priced grade is used with a warning ("No 91 Octane price for IQ; …").
- Static seed fuel prices for GCC-oriented development scenarios.
- Static currency converter.
- Optional tank size/current fuel estimate for minimum refuel stops.
- In-memory vehicle profile storage and future vehicle data provider interface.
- Console test runner covering core calculator behavior (73 tests).
- CORS limited to `Cors:AllowedOrigins` (dev: `http://localhost:5173`); route errors are logged, not
  echoed to the client; the short-link expander only accepts redirects that land on Google.

## Architecture Notes

- Core calculation lives in `FuelCalculator.Core`.
- HTTP validation and endpoint registration live in `FuelCalculator.Api`.
- Concrete MVP providers are intentionally replaceable.
- `IRouteCountryIntersectionSegmenter` is the placeholder for future PostGIS implementation.

## How to Start a Session

```powershell
cd C:\Repo\Fuel_Calculator
git status --short --branch
dotnet build FuelCalculator.sln
dotnet run --project tests\FuelCalculator.Tests\FuelCalculator.Tests.csproj
cd frontend
npm.cmd run build
```

## Known Gaps

- No persistent PostgreSQL/PostGIS database yet.
- Time zones are ignored: the departure time is treated as local time all the way.
- Border waits are estimates, not live data. Only the King Fahd Causeway publishes live times (JESR app), and there is no API for them.
- No station-level planning by design.
- Fuel price seed data is not authoritative, and there is none for JO, SY, IQ or LB.
- The public Overpass server was very slow on 2026-11-27 (40–60 s per request), so a new long route needs a few
  minutes before real stations appear. For production, host Overpass or preload tiles for the main corridors.
- The public OSRM and Nominatim servers are for light use only (Nominatim: one request per second, which is enforced).
- OpenStreetMap station coverage and opening hours vary, and a station mapped without fuel-type tags may not sell every grade.
- When a per-fill cap applies, the planner can add a very small extra stop (for example 0.6 L) to keep the full reserve.
- Seed prices go stale. Rebuild them with the data script (curated CSV + latest EU bulletin) about monthly. There is
  no live price feed yet.
- 86 map countries have no seed price (mostly territories; also Chad, Afghanistan, Morocco, Yemen and Cuba).
- The mock route draws straight lines; Kuwait→Muscat crosses open water off Sila and reports ~10 km UNKNOWN.
- Static FX rates are MVP-only.
- Frontend has no automated browser tests yet.
- Google Maps shortened links are not expanded or resolved.

## Preferred Next Work

See `docs/handoff/BACKLOG.md`.
