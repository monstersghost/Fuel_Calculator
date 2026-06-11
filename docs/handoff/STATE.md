# Fuel Calculator - Project State

**Last updated:** 2026-06-11

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
- Approximate polyline country segmentation.
- Manual fuel price overrides with precedence over seed data.
- Static seed fuel prices for GCC-oriented development scenarios.
- Static currency converter.
- Optional tank size/current fuel estimate for minimum refuel stops.
- In-memory vehicle profile storage and future vehicle data provider interface.
- Console test runner covering core calculator behavior.

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
- Fuel price seed data is not authoritative.
- Static FX rates are MVP-only.
- Frontend has no automated browser tests yet.
- Google Maps shortened links are not expanded or resolved.

## Preferred Next Work

See `docs/handoff/BACKLOG.md`.
