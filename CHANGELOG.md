# Changelog

Todos los cambios notables de Smelter se documentan acá. El formato se basa en
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Las versiones de la solución de Power
Platform se publicarán como GitHub Releases (roadmap: `docs/repo-maturity-roadmap.md`, Fase 2).

## [Unreleased]

### Added
- **Modelo de seguridad provisionado por SDK** (`smelter-security`, `tools/Smelter.Deploy/SecurityProvisioner.cs`):
  roles `Smelter Configurador` (CRUD Org en las 6 tablas `sanic_tbl_smlt_*`) y `Smelter Operador`
  (Template Read+AppendTo Basic, mappings Read Org, Import Job Create Org + Read/Write Basic, Log Read
  Basic) — reemplaza el modelo config-only manual de la primera versión. Idempotente: re-correr el
  deploy no duplica roles ni privilegios.
- **`ExecutePrivilegeName` en las Custom APIs**: `export`/`importtemplate` gateadas por Write sobre
  Template (solo Configurador); `prepareimport`/`processchunk` por Create sobre Import Job (Operador
  también puede).
- **Validación de acceso en runtime** (`PrepareImportPlugin`/`PrepareImportHandler`): valida que
  `InitiatingUserId` tenga Read sobre el Template del job antes de prepararlo
  (`IAccessChecker`/`PrincipalAccessChecker`, `RetrievePrincipalAccessRequest`) — defensa en profundidad
  además de la matriz de privilegios. Sin acceso, `InvalidPluginExecutionException`, el import no arranca.
- **Cascade sharing Template→Import Job** (Share=Cascade, Reparent=Cascade) provisionado por SDK
  (`UpdateRelationshipRequest`) — los jobs nuevos heredan el acceso del team del template.
- **MDA restringida al Configurador** vía `AppModuleRoles` — el Operador no ve la app en su selector.
- **UX de Import Job** (`smelter-mda-ux`): 3 vistas públicas (Jobs Activos, Completados con Errores,
  Todos los Jobs), botones de command bar **Ejecutar**/**Reintentar** (RibbonDiffXml + JS, invocan
  `prepareimport`/`processchunk`), y dashboard **"Smelter Dashboard"** con entrada en el sitemap.
- **`Smelter.Verify`**: `SecurityAssertions` — confirma roles/privilegios, cascade, `executeprivilegename`,
  restricción de la MDA y alineación del Cloud Flow, antes del smoke test funcional.

### Changed
- **Rename aditivo de la Custom API `processchunk`**: corrige el typo histórico
  `anic_capi_smelter_processchunk` → `sanic_capi_smelter_processchunk`. Release N agrega la entrada
  nueva y actualiza el Cloud Flow al nombre correcto en el mismo deploy (`CloudFlowUpdater`) SIN borrar
  la entrada vieja — ambas coexisten y apuntan al mismo `ProcessChunkPlugin` durante la transición
  (`ProcessChunkRenameContractTests`). La entrada legacy se remueve en Release N+1, gateada a que
  `Smelter.Verify` confirme el flow migrado en verde.

## [[1.0.0.0](https://github.com/breaking-bytes-dev/smelter/releases/tag/sol-v1.0.0.0)] — 2026-07-25

🎉 **Primer release de la solución** (`smlt_core_smelter`, managed + unmanaged, exportada de Dev por
el workflow `solution-release`). Incluye todo el desarrollo previo al primer commit:

### Added
- **Motor de import v2 (chunked, CSV-only)**: Custom APIs `sanic_PrepareImport` (valida headers, cuenta
  filas, resetea el job) y `sanic_ProcessChunk` (parse en streaming con skip al offset, procesamiento en
  orden DAG, upsert por alternate key, log idempotente de filas fallidas con categoría de error),
  orquestadas por un Cloud Flow con retry policy. Offset persistente → reanudación segura.
- **Export/Import de templates** (`sanic_ExportTemplate` / `sanic_ImportTemplate`): portabilidad de la
  configuración entre ambientes en JSON, con remapeo de GUIDs por refs estables y upsert por nombre.
- **Modelo de seguridad** (config-only): roles Configurador/Operador, visibilidad de templates por team
  y herencia de acceso a los jobs vía cascade sharing (sin código).
- **Model-Driven App** de administración/operación (diseño): forms, vistas, dashboard, botones
  Ejecutar/Reintentar, export/import de templates desde la command bar.
- **Documentación corporativa** (`docs/`): árbol por audiencia (overview, solution reference, developer,
  admin, user, examples) con índice maestro, XML docs en todo el código, y limitaciones conocidas
  documentadas ("no soportado todavía": IsRetry por fila, IsJunctionTable, Create/UpdateIfExists, N:M).
- **Planes SDD** (`openspec/`): chunked-import-redesign, template-export-import, smelter-security.
- ~60 unit tests (xUnit + NSubstitute + FluentAssertions), strict TDD.

### Changed
- Rediseño completo respecto del concepto v1: se eliminó el plugin async monolítico con auto-continue
  (moría por plugin depth), la tabla de staging, el soporte de Excel y la deduplicación en memoria —
  reemplazados por chunking orquestado, parse en streaming y upsert por alternate key.
