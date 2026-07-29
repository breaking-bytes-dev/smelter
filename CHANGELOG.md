# Changelog

Todos los cambios notables de Smelter se documentan acá. El formato se basa en
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Las versiones de la solución de Power
Platform se publicarán como GitHub Releases (roadmap: `docs/repo-maturity-roadmap.md`, Fase 2).

## [Unreleased]

_Nada todavía._

## [[1.2.0.0](https://github.com/breaking-bytes-dev/smelter/releases/tag/sol-v1.2.0.0)] — 2026-07-28

**Ownership de Import Jobs por team** (`smelter-access-control`, Requirement "Ownership del Import Job
heredado del team del template"): los jobs creados contra un template owned por un team nacen owned por
ese MISMO team — todo el equipo ve y opera los jobs de cualquier miembro con la matriz de privilegios
tal como está (Read/Write Basic del rol Operador cubre lo owned por los teams del usuario).

### Added
- **`AssignJobOwnerPlugin` + `AssignJobOwnerHandler`** (`src/Smelter.Plugins/AssignJobOwnerPlugin.cs`):
  primer message step del assembly — sync, Pre-operation, Create de `sanic_tbl_smlt_importjob`. Si el
  template referenciado es owned por un team, setea `ownerid` del Target a ese team ANTES de persistir
  (sin `AssignRequest` posterior ni ventana owned-por-el-creador); pisa cualquier `ownerid` explícito
  del payload (regla determinista). Template owned por user o job sin template: owner default (creador).
- **Infraestructura declarativa de message steps** en el deploy, mismo patrón spec → provisioner →
  assertion de las Custom APIs: `tools/Smelter.Deploy/StepSpec.cs` (catálogo, fuente única de verdad),
  `StepProvisioner.cs` (upsert idempotente de `sdkmessageprocessingstep` por clave natural, paso `[4b]`,
  re-habilita steps deshabilitados a mano) y `tools/Smelter.Verify/StepAssertions.cs` (asserts E2E de
  stage/mode/rank/estado). El smoke de `Smelter.Verify` además verifica el comportamiento del ownership
  sobre el job de prueba.
- Tests: `AssignJobOwnerHandlerTests` (7 casos, NSubstitute) y `StepSpecContractTests` (contrato del
  step: sync + pre-op + Create de Import Job).

### Changed
- **Guía operativa de teams** (`docs/04-admin-guide/security-and-roles.md`): el paso recomendado ahora
  es **asignar** el template al team (ownership, `ownerid`) — `GrantAccess` queda para dar acceso a
  teams adicionales. Actualizados el modelo (5 piezas), checklist, troubleshooting y las referencias de
  tablas (`import-job.md`, `import-template.md`, `plugins.md` — que ya no dice "sin steps de mensaje").
- `Smelter.Plugins` bump a `1.0.3` (recarga del package en Dataverse).

## [[1.1.0.0](https://github.com/breaking-bytes-dev/smelter/releases/tag/sol-v1.1.0.0)] — 2026-07-28

**Modelo de seguridad por teams + UX de la MDA** (`smelter-security` v2). Todo el provisioning se
hace por SDK desde CI (`backend-deploy`) — cero configuración manual — y quedó verificado E2E contra
Dev (`backend-verify`: asserts de seguridad + smoke test de import idempotente).

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
  Nota de plataforma: `System Administrator` y `System Customizer` quedan asociados a toda MDA por
  Dataverse y no se pueden quitar (verificado contra Dev) — no afecta al modelo: los operadores no
  tienen esos roles.
- **UX de Import Job** (`smelter-mda-ux`): 3 vistas públicas (Jobs Activos, Completados con Errores,
  Todos los Jobs), botones de command bar **Ejecutar**/**Reintentar** (RibbonDiffXml + JS, invocan
  `prepareimport`/`processchunk`), y dashboard **"Smelter Dashboard"** con entrada en el sitemap.
- **`Smelter.Verify`**: `SecurityAssertions` — confirma roles/privilegios, cascade, `executeprivilegename`,
  restricción de la MDA y alineación del Cloud Flow, antes del smoke test funcional.

### Changed
- **Rename de la Custom API `processchunk` COMPLETADO**: corregido el typo histórico
  `anic_capi_smelter_processchunk` → `sanic_capi_smelter_processchunk`. Se ejecutó en dos pasos sin
  ventana rota: primero rename aditivo + migración del Cloud Flow al nombre nuevo en el mismo deploy
  (`CloudFlowUpdater`, reemplazo por token exacto e idempotente), y — tras confirmar `Smelter.Verify`
  en verde — la **entrada legacy fue eliminada de Dev** (`DeleteRequest` explícito en el deploy).
  Integraciones que invocaran el nombre viejo deben migrar a `sanic_capi_smelter_processchunk`.
- **`Smelter.Verify` más robusto**: imprime el `OrganizationServiceFault` completo (TraceText +
  InnerFaults) ante fallos, y reintenta acotadamente el 400 transitorio de Azure Blob Storage
  (`InvalidQueryParameterValue`) en la descarga del archivo del job — solo esa firma; cualquier otro
  error se propaga al primer intento.

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
