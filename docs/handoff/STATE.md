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
- Polyline country segmentation, sampled every 5 km, using real borders (Natural Earth 1:50m, embedded,
  35 countries from Egypt/Turkey to Pakistan; see `src/FuelCalculator.Core/Segmentation/Data/README.md`).
  Points off the simplified coastline (causeways, coastal roads) snap to the nearest country within 25 km.
- Manual fuel price overrides with precedence over seed data. A blank currency means the country's own.
- `CountryCatalog`: local currency and the grades sold per country. A grade not sold (98 in SA, QA, BH, OM)
  is priced as the nearest grade sold, with a warning.
- Totals report `isTotalComplete` and `countriesMissingPrice`; a missing price is never silently 0.
- Route duration is returned (`durationMinutes`).
- Refuel estimate keeps a reserve (default 15%, `reservePercentage` in the request).
- Static seed fuel prices for GCC-oriented development scenarios.
- Static currency converter.
- Optional tank size/current fuel estimate for minimum refuel stops.
- In-memory vehicle profile storage and future vehicle data provider interface.
- Console test runner covering core calculator behavior (22 tests).
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
- Cost is still charged where fuel is burned, not where it is bought (proposal C1: refuel planning).
- The mock route draws straight lines; Kuwait→Muscat crosses open water off Sila and reports ~10 km UNKNOWN.
- Static FX rates are MVP-only.
- Frontend has no automated browser tests yet.
- Google Maps shortened links are not expanded or resolved.

## Preferred Next Work

See `docs/handoff/BACKLOG.md`.
