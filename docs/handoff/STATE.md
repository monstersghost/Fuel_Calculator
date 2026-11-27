# Fuel Calculator - Project State

**Last updated:** 2026-11-27 (correctness pass on branch `fix/correctness-pass`)

## Current MVP

The app estimates cross-border trip fuel usage and cost from route input, vehicle consumption, fuel type, country-specific prices, and output currency.

## Implemented

- .NET 8 ASP.NET Core API.
- React + Vite frontend.
- `POST /api/trips/estimate`.
- Fuel types: 91 Octane, 95 Octane, 98 Octane, Diesel.
- Consumption normalization for L/100km, km/L, US MPG, UK MPG.
- Mock route provider for local development.
- Google Routes provider adapter behind `IRouteProvider`.
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
- **Real fuel stations** from OpenStreetMap (Overpass API):
  - The route is queried as ~50 km bounding boxes, with a second public server tried if the first is busy.
  - Stations are snapped to the route within 2 km (`FuelStationSnapper`).
  - `FuelStations:Mode` is `Auto` (on with Google routes, off with mock routes), `Overpass` or `Virtual`.
  - If the lookup fails or finds nothing, the planner falls back to stand-in stations, with a warning.
- When a grade has no price, the nearest priced grade is used with a warning ("No 91 Octane price for IQ; …").
- Static seed fuel prices for GCC-oriented development scenarios.
- Static currency converter.
- Optional tank size/current fuel estimate for minimum refuel stops.
- In-memory vehicle profile storage and future vehicle data provider interface.
- Console test runner covering core calculator behavior (50 tests).
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
- No station-level planning by design.
- Fuel price seed data is not authoritative, and there is none for JO, SY, IQ or LB.
- The public Overpass server can be slow (about 45 s for a 740 km route) or busy. For production, host an
  Overpass instance or cache station extracts per region.
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
