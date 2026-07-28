# Proposal: Smelter Security + MDA UX (v2)

## Intent

Auditoría (2026-07-27) confirmó que el v1 nunca se desplegó: roles vacíos, cascade ausente, Custom APIs
públicas con plugins SYSTEM sin gating, typo que rompe el Cloud Flow, y UX nunca construida. Se extiende a
seguridad **+ UX**, 100% vía SDK (`tools/Smelter.Deploy`, idempotente, CI, sin manual), verificado con
`tools/Smelter.Verify`.

## Scope

### In Scope
- Roles **Configurador**/**Operador**, privilegios de tabla reutilizados (no hay custom, MS Learn):
  export/import-template → `prvWritesanic_tbl_smlt_importtemplate`; prepareimport/processchunk →
  `prvCreatesanic_tbl_smlt_importjob`.
- MDA solo Configurador (app↔rol). Templates owned by Team; Operador: Template Read User + AppendTo, mappings
  Read Org, ImportJob Create/Read/Write User, Log Read User.
- Cascade Share/Reparent Template→ImportJob.
- Único cambio de código: `PrepareImportPlugin` valida `InitiatingUserId` (RetrievePrincipalAccess); motor
  sigue SYSTEM. TDD.
- Fix typo `processchunk` → `sanic_capi_smelter_processchunk` + Cloud Flow.
- UX: vistas de Import Job, botones Ejecutar/Reintentar, dashboard en sitemap.

### Out of Scope
- Field-level security; privilegios custom; flujos de aprobación; pasos manuales en maker portal.

## Capabilities

### New Capabilities
- `smelter-access-control`: roles, ownership por team, cascade sharing, autorización runtime, app
  restringida al Configurador.
- `smelter-mda-ux`: vistas de Import Job, ribbon Ejecutar/Reintentar, dashboard en sitemap.

### Modified Capabilities
- None (nada mergeado a `openspec/specs/` main aún).

## Approach

`tools/Smelter.Deploy` provisiona roles/privilegios/app-rol/cascade/vistas/ribbon/sitemap idempotentemente
desde CI; typo y Cloud Flow se actualizan en el mismo release. Único cambio de motor: check en
`PrepareImportPlugin` (TDD). `tools/Smelter.Verify` valida post-deploy.

## Affected Areas

| Área | Impacto |
|------|---------|
| Roles + `AppModule.xml` + cascade (solución) | New/Modified |
| Custom API `processchunk` + Cloud Flow | Fix/Modified |
| `PrepareImportPlugin.cs` + tests | Modified/New, TDD |
| Import Job (vistas + ribbon) | New |
| `tools/Smelter.Deploy` / `Verify` | Modified |

## Risks

| Riesgo | Prob. | Mitigación |
|--------|-------|------------|
| Privilegios reutilizados dan granularidad gruesa | Low | Sin Write en Template, export/import bloqueadas; solo Create habilitado |
| Rename rompe Cloud Flow; `Deploy` no cubre hoy ribbon/sitemap/vistas | Med | Deploy+flow en el mismo release; extender `Deploy`, validar en CI antes de prod |

## Rollback Plan

Redeploy de la solución anterior (unmanaged, git) revierte roles/app-rol/sitemap/vistas/ribbon/cascade.
Rename y Cloud Flow se revierten juntos. Check en `PrepareImportPlugin`: `git revert` + redeploy — motor
sigue SYSTEM.

## Dependencies

- 6 tablas ya UserOwned; `Smelter.Deploy` a extender; secrets de CI OK (Fase 3).

## Success Criteria

- [ ] Operador con privilegios correctos; no invoca export/import-template (sin Write), sí prepareimport/processchunk.
- [ ] MDA solo Configurador; `PrepareImportPlugin` rechaza `InitiatingUserId` sin acceso (TDD).
- [ ] `sanic_capi_smelter_processchunk` resuelve; Cloud Flow corre end-to-end.
- [ ] Vistas de Import Job, botones Ejecutar/Reintentar y dashboard en sitemap, funcionales.
- [ ] Deploy reproducible desde CI limpio (cero manual); `tools/Smelter.Verify` en verde.
