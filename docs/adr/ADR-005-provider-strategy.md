# ADR-005 - Provider Strategy for Route, Fuel Price, and FX Data

**Status:** Accepted  
**Date:** 2026-06-11

---

## Context

Fuel Calculator depends on data that may come from paid APIs, official country sources, user overrides, static seed values, or future databases. The MVP should not scrape random websites or block local development on credentials.

---

## Decision

Use provider interfaces for all external or replaceable data:

| Concern | Interface | MVP implementation | Future implementation |
|---------|-----------|--------------------|-----------------------|
| Route calculation | `IRouteProvider` | `MockRouteProvider` | `GoogleRoutesProvider` with Google Routes API |
| Country segmentation | `IRouteCountrySegmenter` | `PolylineCountrySegmenter` using sampled decoded polyline points | PostGIS route/polygon intersection |
| Country lookup | `ICountryResolver` | `StaticBoundingBoxCountryResolver` | PostGIS or geocoding-backed resolver |
| Fuel prices | `IFuelPriceProvider` | `ManualFuelPriceProvider`, `StaticSeedFuelPriceProvider` | Saudi Aramco, ADNOC, Qatar, GlobalPetrolPrices, official feeds |
| FX conversion | `ICurrencyConverter` | `StaticCurrencyConverter` | Live FX provider |
| Vehicle data | `IVehicleDataProvider` | `NullVehicleDataProvider` | FuelEconomy.gov or regional equivalent |

### Manual Override Rule

Manual fuel prices are user-provided, clearly marked as `Manual`, and take precedence over seed or online provider data.

### Warning Rule

Approximation, missing prices, missing FX, and unparsable Google Maps links must produce warnings in the API response.

---

## Rationale

- Local development remains useful without API keys.
- Provider order makes manual overrides deterministic.
- Future official adapters can be added without changing the calculator.
- Country splitting can start approximate and become geospatially correct later.

---

## Consequences

- Seed fuel prices are development data, not authoritative prices.
- Static FX rates are only suitable for MVP estimates.
- The route country split must be presented as approximate until PostGIS intersection is implemented.
- Provider failures should degrade into warnings when possible instead of breaking the full estimate.

---

## Alternatives Considered

| Option | Reason Rejected |
|--------|-----------------|
| Scrape public fuel price pages directly | Brittle and against the replaceable-provider requirement. |
| Hard-code all route/country behavior in the calculator | Would make the core math depend on geospatial implementation details. |
| Require Google API keys in MVP | Blocks local development and tests. |

---

## Related Decisions

- ADR-001 - Technology Stack Choice
- ADR-002 - Project Architecture
- ADR-004 - Coding Conventions

---

**Author:** monstersghost
