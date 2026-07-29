# Changelog

Todos los cambios notables de Smelter se documentan acá. El formato se basa en
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Las versiones de la solución de Power
Platform se publicarán como GitHub Releases (roadmap: `docs/repo-maturity-roadmap.md`, Fase 2).

## [Unreleased]

_Nada todavía._

## [[1.2.1.0](https://github.com/breaking-bytes-dev/smelter/releases/tag/sol-v1.2.1.0)] — 2026-07-29

**Fix crítico: el Import Job Log quedaba VACÍO ante filas fallidas.** La alternate key
`sanic_smlt_ijl_key_logidempotente` nació mal definida — incluía `sanic_smlt_ijl_id`, que es el nombre
primario **autonumber** (`IJL-{SEQNUM:4}`), no el lookup al job (la doc lo documentaba como lookup y el
plugin se escribió contra la doc). Resultado: cada upsert de log fallaba con
`Incorrect type of attribute value Microsoft.Xrm.Sdk.EntityReference` y el operador se quedaba sin saber
qué filas ni qué columnas fallaron. El path nunca se había ejercitado E2E: el smoke solo cubría imports
sin errores.

### Fixed
- **`ProcessChunkHandler.LogFailures`** (`src/Smelter.Plugins/ProcessChunkPlugin.cs`): la key del upsert
  ahora usa el FK real `sanic_smlt_ijl_importjobid` (+ `rownumber` + `targettable`) y setea el lookup
  required como atributo. Los logs de filas fallidas se escriben y son idempotentes ante retries.
- **Alternate key recreada por SDK**: nuevo `KeyProvisioner` (`tools/Smelter.Deploy/KeySpec.cs` +
  `KeyProvisioner.cs`, paso `[4c]`) — detecta la composición incorrecta, elimina y recrea la key con
  las columnas correctas, y espera el índice Active. Si el índice queda Failed por filas duplicadas
  preexistentes (basura de la era del bug), se auto-repara con el flujo documentado de la plataforma:
  deduplica los grupos exactos (conserva la fila más reciente de cada uno) + `ReactivateEntityKeyRequest`.
- **Doc corregida** (`docs/02-solution-reference/tables/import-job-log.md`): `sanic_smlt_ijl_id` es el
  autonumber del nombre primario, NO un lookup; el FK real `sanic_smlt_ijl_importjobid` ahora figura en
  la tabla de columnas y en la composición de la key (con la historia del bug).

### Added
- **El mensaje de error ahora NOMBRA la columna** (`NormalizationEngine`): los fallos de coerción de
  tipo se registran como `Column '<destino>' (source '<fuente>'): Cannot convert '<valor>' ...` — antes
  solo traían el valor, imposible saber cuál de las columnas mapeadas falló.
- **Cobertura del path de errores en `Smelter.Verify`**: el smoke ahora incluye un mapping Integer
  (`numberofchildren`) y un segundo job con una fila no numérica — asserts de `CompletedWithErrors`,
  del registro en Import Job Log (fila, tabla, categoría TypeError, mensaje con la columna) y de la
  idempotencia del log al re-correr. `KeyAssertions` valida composición y estado del índice de la key.

### Changed
- `Smelter.Plugins` bump a `1.0.4` (recarga del package en Dataverse).

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
