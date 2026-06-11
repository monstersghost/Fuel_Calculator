# ADR-002 - Project Architecture

**Status:** Accepted  
**Date:** 2026-06-11

---

## Context

The app is small today, but it has integration-heavy seams:

- Route calculation
- Country segmentation
- Fuel price lookup
- Currency conversion
- Vehicle profile and future vehicle data lookup

The design should stay understandable for an MVP without hiding integration concerns inside controllers or UI code.

---

## Decision

Use a small modular backend with explicit API and Core projects:

### Layer Overview

**1. API (`src/FuelCalculator.Api`)**

- Hosts ASP.NET Core endpoints.
- Exposes `POST /api/trips/estimate`.
- Owns HTTP contracts and validation of incoming request shape.
- Registers providers and calculator dependencies.
- Does not contain trip cost math.

**2. Core (`src/FuelCalculator.Core`)**

- Contains domain models and enums.
- Contains calculation logic, provider interfaces, provider implementations for MVP mode, route helpers, and segmentation.
- Keeps the calculator independent from HTTP.

**3. Frontend (`frontend`)**

- React + Vite form UI.
- Calls `/api/trips/estimate`.
- Displays result cards, warnings, fuel stop estimate, and per-country breakdown table.

**4. Tests (`tests/FuelCalculator.Tests`)**

- Console test runner for MVP unit and integration-style checks.
- Covers conversion factors, fuel cost math, aggregation, manual price precedence, missing fuel price warning, and mock route flow.

### Dependency Direction

```text
Frontend -> API -> Core
Tests -> Core
```

Core does not depend on API or frontend.

---

## Rationale

- The API remains a thin transport layer.
- The calculation engine can be tested without Kestrel, Google, a database, or frontend code.
- Provider interfaces make future data sources replaceable.
- A single Core project is enough for this MVP; splitting into Domain/Application/Infrastructure can happen when persistence and external adapters grow.

---

## Consequences

- The repo is smaller than GaragePos and does not need separate Domain/Application/Infrastructure projects yet.
- Some provider implementations live beside interfaces in Core for now.
- A future refactor can move concrete adapters into `FuelCalculator.Infrastructure` without changing API contracts.

---

## Alternatives Considered

| Option | Reason Rejected |
|--------|-----------------|
| Full Clean Architecture split immediately | Adds folders and project references before the app has enough use cases to justify them. |
| Single API project only | Would mix HTTP, providers, and cost math, making later tests and adapters harder. |
| Frontend-only calculator | Cannot cleanly hide Google Routes, fuel providers, and FX integrations behind server-side interfaces. |

---

## Related Decisions

- ADR-001 - Technology Stack Choice
- ADR-004 - Coding Conventions
- ADR-005 - Provider Strategy for Route, Fuel Price, and FX Data

---

**Author:** monstersghost
