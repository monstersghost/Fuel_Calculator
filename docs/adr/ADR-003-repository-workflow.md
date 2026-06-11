# ADR-003 - Repository and Commit Workflow

**Status:** Accepted  
**Date:** 2026-06-11

---

## Context

The repository is public on GitHub and mirrored locally under `C:\Repo\Fuel_Calculator`. The project is still MVP-stage, so workflow should stay lightweight while preserving a clean history.

---

## Decision

### Branch Structure

- `main` is the stable branch.
- Short-lived branches can be used for larger changes.
- Local Codex-generated branches should use the `codex/` prefix when a branch is needed.

### Workflow

1. Keep `main` buildable.
2. Make focused commits with tests or verification notes.
3. Push to `origin/main` for small MVP changes when no separate review branch is needed.
4. Use a PR branch for broader refactors, data persistence, or live provider integrations.

### Commit Message Convention

Use short imperative messages:

```text
Implement fuel calculator MVP
Add architecture decision records
Wire PostGIS segmenter placeholder
```

### Repository Locations

- Main local repo: `C:\Repo\Fuel_Calculator`
- Original working copy from initial build: `the local project directory`
- Remote: `https://github.com/monstersghost/Fuel_Calculator`

---

## Consequences

- The workflow stays simple while the project is small.
- Docs and ADRs should be updated in the same commit as decisions they describe.
- The duplicate local working copy means future work should prefer `C:\Repo\Fuel_Calculator` to avoid drift.

---

## Alternatives Considered

| Option | Reason Rejected |
|--------|-----------------|
| GitFlow | Too much process for an MVP with one main contributor. |
| Commit everything directly without ADRs | Decisions around providers and geospatial design are important enough to record. |
| Keep only the original Documents working copy | User requested the main local repo under `C:\Repo`. |

---

## Related Decisions

- ADR-001 - Technology Stack Choice
- ADR-002 - Project Architecture

---

**Author:** monstersghost
