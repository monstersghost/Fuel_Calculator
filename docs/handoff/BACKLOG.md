# Fuel Calculator - Backlog

## Currently in Flight

- Documentation structure aligned with GaragePos: ADRs, handoff, ops, and release notes.

## P0 - Keep MVP Correct

- Keep conversion tests green for all supported units.
- Keep manual price override precedence covered by tests.
- Keep missing price and approximate split warnings visible in API and UI.

## P1 - Persistence and Geospatial Accuracy

- Add PostgreSQL setup.
- Add PostGIS country boundary tables.
- Implement `IRouteCountryIntersectionSegmenter`.
- Store vehicle profiles persistently.
- Add migrations and database configuration documentation.

## P1 - Provider Integrations

- Add Google Routes API configuration documentation and error handling.
- Add live FX provider behind `ICurrencyConverter`.
- Add official or licensed fuel price adapters behind `IFuelPriceProvider`.
- Add caching and fetched timestamp persistence for provider data.

## P2 - Product UX

- Saved trips.
- Reusable vehicle profiles in the frontend.
- Route summary with duration and route legs.
- More explicit confidence labels in country breakdown.
- Better Google Maps link import for non-shortened direction URLs.

## P2 - Testing and Quality

- Replace console test runner with xUnit or NUnit if package policy allows.
- Add ASP.NET Core integration tests for `/api/trips/estimate`.
- Add frontend component tests or Playwright smoke tests.
- Add GitHub Actions for backend build/test and frontend build.

## Out of Scope for MVP

- Station-level fuel stop optimization.
- Scraping random fuel-price websites directly.
- EV charging support.
