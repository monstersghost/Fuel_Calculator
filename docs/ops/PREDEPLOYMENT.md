# Fuel Calculator - Pre-Deployment Checklist

## 1. Configuration

- [ ] Confirm route provider mode:
  - `Mock` for local/demo mode
  - `Google` only when `GoogleRoutes:ApiKey` is configured
- [ ] Confirm `RouteSegmentation:SampleEveryKm` is appropriate for the environment.
- [ ] Confirm CORS policy is restricted before public deployment.

## 2. Secrets

- [ ] No API keys committed to `appsettings.json`.
- [ ] Google Routes API key supplied through user secrets, environment variables, or deployment secret store.
- [ ] Future fuel price and FX provider credentials stored outside the repository.

## 3. Data Accuracy

- [ ] Seed fuel prices reviewed and labeled as seed/development data.
- [ ] Static FX rates reviewed or replaced with live provider.
- [ ] Approximate country splitting warning remains visible until PostGIS segmentation exists.

## 4. Backend Verification

- [ ] `dotnet build FuelCalculator.sln`
- [ ] `dotnet run --project tests\FuelCalculator.Tests\FuelCalculator.Tests.csproj`
- [ ] API smoke test passes for a multi-country route.

## 5. Frontend Verification

- [ ] `npm.cmd run build` succeeds in `frontend`.
- [ ] Manual estimate flow works against deployed API.
- [ ] Per-country table remains readable on desktop and mobile widths.

## 6. Deployment Readiness

- [ ] Decide whether frontend is deployed separately or built into the API static host.
- [ ] Configure health check for `/api/health`.
- [ ] Configure logs for API runtime.
- [ ] Configure rate limits if public API access is exposed.

## 7. PostGIS Readiness

- [ ] PostgreSQL connection string is configured only when persistence is enabled.
- [ ] Country boundary source is documented.
- [ ] Geospatial segmentation results are compared against mock/sampled results before replacing MVP segmenter.
