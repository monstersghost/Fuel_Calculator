# Fuel Calculator Documentation

This folder follows the same documentation shape used by the GaragePos repo, scaled down for this MVP.

## Structure

- `adr/` - Architecture Decision Records. These capture decisions that should not be rediscovered later.
- `handoff/` - Current state and backlog notes for future development sessions.
- `ops/` - Build, test, and deployment readiness checklists.
- `releases/` - Release notes by version.

## Current Status

The project is an MVP with mock/local provider mode, static seed prices, static FX rates, and a React UI. The architecture keeps external data sources behind interfaces so Google Routes, PostGIS, official fuel price feeds, and live FX providers can be added later without rewriting the calculation engine.
