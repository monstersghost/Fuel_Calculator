# ADR-004 - Coding Conventions

**Status:** Accepted  
**Date:** 2026-06-11

---

## Context

The codebase is small, but its correctness depends on predictable conversions, price precedence, route segmentation warnings, and provider boundaries. Conventions should make the important logic easy to find and test.

---

## Decision

### Domain Models

- Use records for request/result data shapes in Core.
- Use enums for first-class domain choices such as `FuelType`, `ConsumptionUnit`, and `ConfidenceLevel`.
- Keep display names in catalog helpers rather than duplicating labels across the app.

### Calculations

- Normalize consumption to `L/100km` before cost calculation.
- Keep trip cost math in `TripFuelCostCalculator`.
- Round response numbers at the calculation boundary.
- Preserve warnings in the response instead of hiding missing/approximate data.

### Providers

- External data access must go behind interfaces.
- Manual provider data must take precedence over online or seed data.
- Future official adapters should be isolated and replaceable.

### API

- Keep endpoints thin.
- Validate unsupported fuel types, unsupported units, and zero/negative consumption before invoking the calculator.
- Return warnings for approximate country splitting and missing prices.

### Frontend

- Keep the MVP as a dense operational tool, not a landing page.
- Use controls that match the task: inputs for route and consumption, selects for fuel/currency/unit, table for country breakdown.
- Do not hide warnings; they are part of the estimate quality.

### Tests

- Add/maintain tests for conversion factors and price precedence whenever calculation behavior changes.
- Use mock/static providers for integration-style flow checks.

---

## Consequences

- The important business behavior is concentrated and testable.
- Provider additions should not require frontend rewrites.
- The frontend and backend currently duplicate some option labels; a future typed metadata endpoint can remove that duplication.

---

## Related Decisions

- ADR-002 - Project Architecture
- ADR-005 - Provider Strategy for Route, Fuel Price, and FX Data

---

**Author:** monstersghost
