# ADR-001 - Technology Stack Choice

**Status:** Accepted  
**Date:** 2026-06-11

---

## Context

Fuel Calculator estimates route fuel usage and cost for cross-border road trips. The MVP needs a type-safe backend, a simple browser UI, and room for future integrations such as Google Routes, PostGIS country geometry, official fuel price providers, and live exchange rates.

The app should work locally without paid API keys, while still keeping the production path clear.

---

## Decision

| Layer | Technology | Reason |
|-------|------------|--------|
| Language and framework | C# / .NET 8 / ASP.NET Core Web API | Stable LTS runtime, strong typing, first-class dependency injection, good API hosting model. |
| Frontend | React + Vite | Small app surface, fast local development, simple API proxying, low ceremony. |
| Database | PostgreSQL + PostGIS planned | Best fit for future country polygon and route geometry work. |
| MVP persistence | In-memory/mock mode | Keeps the MVP runnable without database setup. |
| Route provider | Google Routes API behind `IRouteProvider`; mock provider by default | Production-grade future route source without blocking local development. |
| Tests | .NET console test runner for MVP | Avoids external test package dependency while still verifying core behavior. |
| Repository | GitHub | Public repo, simple collaboration, future GitHub Actions path. |

---

## Rationale

### .NET 8

- LTS runtime, appropriate for a small but long-lived API.
- Built-in dependency injection supports provider-based architecture without extra framework code.
- Strong domain model and enums help catch unsupported fuel and consumption units early.

### React + Vite

- Fast setup and local feedback loop.
- The frontend only needs form input, result cards, warnings, and a breakdown table.
- Vite's proxy keeps local frontend/API development straightforward.

### PostgreSQL + PostGIS Later

- The route/country split problem is geographic, not just relational.
- PostGIS can eventually replace approximate sampling with route and polygon intersection.
- The current `IRouteCountryIntersectionSegmenter` placeholder keeps that path visible.

---

## Consequences

- The MVP can run without Google API keys or a database.
- Production integrations are isolated behind interfaces.
- The current console test runner is intentionally simple; a future xUnit/NUnit test project can replace it when package policy allows.
- The frontend currently has no generated API client, so request/response drift must be checked manually until a typed client is introduced.

---

## Alternatives Considered

| Option | Reason Rejected |
|--------|-----------------|
| Next.js | More framework than needed for a form-based MVP. |
| Node.js API | Weaker alignment with the requested .NET backend and provider interfaces. |
| SQL Server | Good general database, but less natural for future PostGIS route/country work. |
| Google Routes only, no mock | Would make local development depend on paid API configuration. |

---

## Related Decisions

- ADR-002 - Project Architecture
- ADR-005 - Provider Strategy for Route, Fuel Price, and FX Data

---

**Author:** monstersghost
