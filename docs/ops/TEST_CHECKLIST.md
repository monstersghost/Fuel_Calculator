# Fuel Calculator - Test Checklist

## 1. Backend Build

- [ ] `dotnet restore FuelCalculator.sln --configfile NuGet.Config`
- [ ] `dotnet build FuelCalculator.sln`

## 2. Core Tests

- [ ] `dotnet run --project tests\FuelCalculator.Tests\FuelCalculator.Tests.csproj`
- [ ] Verify conversion tests:
  - L/100km unchanged
  - km/L = `100 / value`
  - US MPG = `235.214583 / value`
  - UK MPG = `282.480936 / value`
- [ ] Verify calculator tests:
  - fuel cost math
  - per-country aggregation
  - manual override precedence
  - missing fuel price warning
  - mock route + seed fuel provider flow

## 3. API Smoke Test

- [ ] Start API:

```powershell
dotnet run --project src\FuelCalculator.Api\FuelCalculator.Api.csproj --urls http://localhost:5124
```

- [ ] `GET http://localhost:5124/api/health` returns `{ "status": "ok" }`.
- [ ] `POST /api/trips/estimate` returns:
  - total distance
  - normalized consumption
  - total liters
  - output currency total
  - per-country segments
  - warnings when split is approximate or data is missing

## 4. Frontend Build

- [ ] `cd frontend`
- [ ] `npm.cmd install`
- [ ] `npm.cmd run build`

## 5. Frontend Manual Check

- [ ] Start frontend with `npm.cmd run dev`.
- [ ] Open `http://127.0.0.1:5173`.
- [ ] Submit Kuwait City to Doha, Qatar with 95 Octane and 8.5 L/100km.
- [ ] Confirm result cards render distance, liters, cost, and fuel type.
- [ ] Confirm country breakdown table renders KW, SA, and QA in mock mode.
- [ ] Confirm warnings are visible.
- [ ] Add a KW manual price and confirm source displays as Manual.

## 6. Regression Risks

- [ ] Zero or negative consumption is rejected.
- [ ] Unsupported fuel type is rejected.
- [ ] Missing country fuel price returns a warning, not a crash.
- [ ] Tank size produces refuel estimate when provided.
- [ ] Google Maps link parse failure produces a warning and still allows manual origin/destination.
