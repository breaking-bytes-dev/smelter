# Archive Report: smelter-security

**Date**: 2026-07-28
**Mode**: openspec

## Summary

Change `smelter-security` (Smelter Security + MDA UX, v2) fully planned, implemented, verified, and
archived. All 9 phases complete; `tasks.md` has 0 open items (46 tasks total, 38 explicitly done +
8 explicitly gated/blocked-by-design, none defective). `verify-report.md` final verdict: **PASS**
(re-audit 2026-07-28 superseding the original "PASS WITH WARNINGS" — all CRITICAL/WARNING items
from the first pass confirmed fixed).

Real E2E confirmation against Dev (not just sandbox-static review):
- `backend-deploy` run **30364664298** — Release N+1, legacy Custom API
  `anic_capi_smelter_processchunk` deleted from Dev.
- `backend-verify` run **30364284364** — [SEC] assertions complete + smoke E2E, Release N.
- `backend-verify` run **30365195704** — Release N+1, 4 APIs (legacy gone), idempotent smoke E2E green.

## Specs Synced

| Domain | Action | Details |
|--------|--------|---------|
| `smelter-access-control` | Created | `openspec/specs/` was empty (only `.gitkeep`) — delta spec copied as the full initial spec. 7 requirements: Rol Configurador, Rol Operador, MDA restringida al Configurador, Cascade share/reparent, Gating de Custom APIs por ExecutePrivilegeName, Validación de acceso al template en PrepareImportPlugin, Provisioning idempotente, Ownership de templates por team. |
| `smelter-mda-ux` | Created | Delta spec copied as the full initial spec. 4 requirements: Vistas custom de Import Job, Botones de command bar Ejecutar/Reintentar, Dashboard "Smelter Dashboard" en el sitemap, Rename de processchunk sin ventana rota. |

No merge logic was needed (no pre-existing main specs to reconcile against) — both domains are net-new
capabilities per `proposal.md`'s "New Capabilities" section.

## Archive Contents

- `proposal.md` — v2, post-auditoría 2026-07-27
- `specs/smelter-access-control/spec.md`
- `specs/smelter-mda-ux/spec.md`
- `design.md`
- `tasks.md` — 46 tasks, 0 open (38 done + 8 gated/blocked-by-design, all documented)
- `verify-report.md` — PASS (final, re-audit 2026-07-28)
- `archive-report.md` — this file

## Source of Truth Updated

The following specs now reflect the new behavior and are the authoritative reference going forward:
- `openspec/specs/smelter-access-control/spec.md`
- `openspec/specs/smelter-mda-ux/spec.md`

## Known Residual Items (non-blocking, carried in verify-report.md)

- SUGGESTION: `ClientDataProcessChunkRenamer`'s double-S corruption repair only handles a single extra
  leading `s` (`"ssanic_..."`); a hypothetical triple-s string fails safe (caught loudly by
  `Smelter.Verify`, not silently). Low risk — no real deploy ever hit the original bug more than once.
- CI hygiene: `CustomApiContractTests.cs` carries a dated, scoped `PendingSolutionSync` exclusion for
  `sanic_capi_smelter_processchunk` pointing at `tasks.md` 6.2/9.4 — already resolved as of task 9.4
  (Phase 9 complete), worth confirming the exclusion set is now empty on next touch of that file.

Neither item is a defect; both are explicitly documented in `verify-report.md` and did not block
archival.

## Traceability

- Change name: `smelter-security`
- Artifact store mode: `openspec`
- Archived path: `openspec/changes/archive/2026-07-28-smelter-security/`
- Verification: `sdd-verify` PASS (final, 2026-07-28 re-audit)
- Deploy evidence: backend-deploy run 30364664298 (Release N+1)
- Verify evidence: backend-verify runs 30364284364 (Release N, [SEC]+smoke) and 30365195704 (Release N+1, idempotent)

## SDD Cycle Complete

The change has been fully planned, implemented, verified, and archived. Ready for the next change.
