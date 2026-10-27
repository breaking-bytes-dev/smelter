# Changelog

Todos los cambios notables de Smelter se documentan acá. El formato se basa en
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Las versiones de la solución de Power
Platform se publicarán como GitHub Releases (roadmap: `docs/repo-maturity-roadmap.md`, Fase 2).

## [Unreleased]

### Changed
- **Botones Ejecutar/Reintentar de Import Job: un solo camino de ejecución (Cloud Flow)**: antes corrían
  `PrepareImport` + el loop de `ProcessChunk` desde el navegador (cerrar la pestaña dejaba el job en
  Processing para siempre, y un fallo de Prepare desde el botón no marcaba Failed). Ahora solo guardan el
  form, validan archivo + template (y que el usuario pueda leer el template), ponen el job en Queued y
  muestran un aviso; el flow orquestador hace Prepare + ProcessChunk y su handler marca Failed. El form se
  auto-refresca mientras el job está Queued/Processing. Enable rules: Ejecutar solo con status vacío
  (nunca corrió), Reintentar solo en Completed/CompletedWithErrors/Failed, ambos deshabilitados en
  Queued/Processing y en registros sin guardar. `Smelter.Verify` falla si el web resource desplegado
  vuelve a referenciar processchunk/prepareimport o faltan las enable rules.
- **Nota de seguridad**: como el flow corre con la identidad de su conexión, `InitiatingUserId` en
  `PrepareImport` es esa identidad; el control por usuario se hace al encolar (Write sobre el job +
  lectura del template bajo el usuario que hace click).

### Added
- **Función `HMAC(val, "nombreKey")` en el Transform DSL**: HMAC-SHA256 en hex minúsculas (64 chars)
  sobre el valor sin espacios; vacío → vacío. La key se resuelve por nombre vía `IHmacKeyProvider`
  (el nombre es una etiqueta, nunca el material de la key); los mensajes de error nunca incluyen el
  valor de entrada ni la key. `TransformEngine` y `NormalizationEngine` aceptan el provider
  (constructores existentes intactos).
- **`DataverseHmacKeyProvider`**: lee las keys de la tabla `sanic_tbl_smlt_hmackey` (Organization-owned,
  audit de tabla ON, columna del valor sin audit y fuera de Advanced Find, fuera de todo rol de Smelter).
  La tabla es **componente de `smlt_core_smelter`**: el schema viaja a Test/Prod con el managed; el
  **valor de la key se carga a mano en cada entorno** (nunca por CI, repo ni GitHub secret). La crea (o,
  si ya existía en Default, la agrega a la solución) `Smelter.Deploy` (`HmacKeyTableProvisioner`) y la
  verifica `Smelter.Verify` (`HmacKeyAssertions`, incluida la pertenencia a la solución).
- **Cursor de bytes en ProcessChunk (O(N) en vez de O(N²))**: antes cada chunk descargaba el CSV entero y lo
  re-parseaba desde la fila 0 saltando `offset` filas. Ahora el Import Job guarda en
  `sanic_smlt_ij_bytecursor` el **byte** del próximo registro sin leer y cada chunk baja solo su rango
  (`InitializeFileBlocksDownload` + `DownloadBlock`), extendiendo bloque a bloque hasta tener `chunkSize`
  registros **completos** (un registro puede cruzar bloques y traer saltos de línea entre comillas; el
  último registro de un segmento que no llega al EOF no se consume). `PrepareImport` fija el cursor en el
  primer byte de datos (después del BOM y la cabecera). Los offsets son bytes UTF-8 calculados sobre
  `Parser.CharCount` (no `CountBytes`, que cuenta mal los pares subrogados: 😀 = 6 en vez de 4); el BOM no
  desplaza nada. Compatibilidad: cursor vacío (job preparado por una versión anterior) o archivo no
  recorrible por bytes (UTF-16/32, UTF-8 inválido) → ProcessChunk usa el camino anterior (descarga completa
  + skip de filas). `SourceRowNumber` no cambia. Lógica pura en `Smelter.Core` (`CsvParserService.ReadHeaderInfo`
  / `ReadChunk`) con tests de CRLF/LF, comillas con saltos y delimitadores, multibyte, BOM, sin newline final,
  borde de chunk a mitad y justo al final de un registro, y una propiedad "chunks por cursor == ReadAll" para
  varios tamaños de chunk y de bloque (hasta 1 byte).
- **Columna `sanic_smlt_ij_bytecursor`** (Import Job, Decimal precisión 0 — un Whole Number es int32, ~2.1 GB, y
  un File column llega a 10 GB) y **`sanic_smlt_tm_insertonly`** (Table Mapping, Sí/No, default No), provisionadas
  por SDK en `smlt_core_smelter` (`SchemaAdditionsSpec`/`SchemaAdditionsProvisioner`) y verificadas en
  `Smelter.Verify` (`AdditionsAssertions`). El cursor no va al form del job; "Solo inserción" va al main form
  del Table Mapping, después de "Update If Exists".
- **Gate de idempotencia**: `PrepareImport` y `ProcessChunk` rechazan un template cuyo Table Mapping no tiene
  dedup columns, salvo que tenga el opt-in **"Solo inserción (permite duplicados)"** (Create sin alternate key,
  como el camino sin key de antes; la advertencia en el trace se mantiene). El mensaje nombra el mapping y la
  tabla, nunca valores. Entra en el export/import de templates (`InsertOnly`; los JSON viejos importan `false`).
  `Smelter.Verify` comprueba que sin dedup y sin flag se rechaza y con el flag se acepta.

- **Workflow `solution-import`** para importar la solución a Test/Prod con GitHub Environments y
  deployment settings (`deploy/README.md`).
- Guía "Clave HMAC" en `docs/04-admin-guide/security-and-roles.md`: tabla de riesgo aceptado,
  procedimiento manual de carga, custodia, rotación y política de keys por entorno.

### Changed
- **Los templates con un Table Mapping sin dedup columns ya no corren** salvo que se marque "Solo inserción
  (permite duplicados)": revisar los templates existentes antes de actualizar (el job falla en
  `PrepareImport` con un mensaje que indica el mapping).
- `Smelter.Plugins` bump a `1.0.8` (cursor de bytes + gate de idempotencia; recarga del package). Antes: `1.0.7` (1.0.6: HMAC; 1.0.7: fixes de prod-readiness) (recarga del package en Dataverse). Sin el bump, algunos workers del
  sandbox seguían con 1.0.5 y la carga fallaba de forma intermitente con `Unknown function: HMAC`.
- `Smelter.Deploy` exige `SMELTER_ENV=dev` y los pasos destructivos quedan detrás de `--allow-destructive`;
  los workflows de Dev corren bajo el environment `dev`.
- `ProcessChunk` re-valida que el template esté `Active` y el job en `Processing` antes de descargar o
  escribir.
- El fallo de una key HMAC se cachea por key y el fault de tabla inexistente se traduce a `TransformError`.
- La función `HMAC` exige el nombre de la key como literal con forma de etiqueta, validado antes de evaluar.

### Fixed
- El Import Job queda `Failed` si `PrepareImport` falla o el loop del Cloud Flow hace timeout; el deploy
  ya no deja el flow apagado.

### Security
- `TypeCoercer`, lookups no resueltos y faults de `UpsertWriter` ya no incluyen el valor de la fila (PII)
  en el Import Job Log ni en traces.
- **Riesgo aceptado (HMAC)**: la key vive en Dataverse; cualquier System Administrator del entorno puede
  leerla, las copias/backups del entorno la arrastran y la columna del valor no se audita. Endurecimiento
  futuro posible: Azure Key Vault (mismas keys → mismos hashes, migración sin ruptura).

## [[1.3.0.0](https://github.com/breaking-bytes-dev/smelter/releases/tag/sol-v1.3.0.0)] — 2026-07-29

**Audit de campos: semántica implementada + limpieza de schema.** Resultado del audit completo
metadata↔código: los campos con semántica valiosa ahora FUNCIONAN, y los muertos se eliminaron del
schema (por SDK, `SchemaCleanupProvisioner`).

### Added
- **Gate de estado del template**: `sanic_smlt_it_status` ahora gatea la ejecución — `PrepareImport`
  rechaza templates en `Draft`/`Archived` ("Import Template ... is not Active"). Antes, un template a
  medio configurar corría igual. Los templates importados vía `sanic_capi_smelter_importtemplate`
  nacen `Active` (en update no se toca el status). Registros legacy sin el atributo poblado se tratan
  como `Active` (no se bloquea nada retroactivamente).
- **`CreateIfNotExists` / `UpdateIfExists` implementados** (`UpsertWriter`): existe + Update=No →
  fila **Skipped** (no se escribe, suma a `skippedcount`); existe + Update=Sí → Update; no existe +
  Create=No → Skipped; no existe + Create=Sí → Create. Con ambos en Sí (el default) sigue el upsert
  de un solo round-trip; el probe de existencia por alternate key solo se paga cuando algún flag está
  apagado. Sin dedup columns los flags se ignoran (no hay "existe"). El smoke de `Smelter.Verify`
  cubre el gate de status ([3b]) y el skip ([6c]).

### Removed
- **6 campos muertos eliminados del schema** (audit: nadie los cargaba, leía ni escribía; el deploy
  depura forms/vistas y borra por `DeleteAttributeRequest`, verificado por `CleanupAssertions`):
  `sanic_smlt_tm_sourcesheet` (residuo Excel), `sanic_smlt_ij_isretry` (retry por fila nunca
  implementado), `sanic_smlt_ij_totalbatches`, `sanic_smlt_ij_errorsummary` (el Cloud Flow nunca lo
  escribió — el detalle de fallas vive en la pestaña Errores y el Plugin Trace Log),
  `sanic_smlt_rm_intersecttable` (mitad de la feature N:M nunca implementada) y
  `sanic_smlt_ijl_createdrecordid` (el log solo registra fallos).
- **`IsJunctionTable` fuera del motor y del export/import de templates** (la otra mitad de N:M):
  el JSON exportado ya no lo incluye (los JSONs viejos se importan igual — el member se ignora).
  El campo `sanic_smlt_tm_isjunctiontable` sigue en el schema, marcado como ignorado.
- Código muerto asociado: `ImportContext.IsRetry`/`SuccessfulAndSkippedRowKeys`, `IsCsv`/`IsExcel`,
  `HasTransform`/`IsLookup`/`IsOptionSet`, `IsManyToMany`, `IntersectTable`. Firma de
  `IImportContextLoader.LoadTemplateConfig` corregida (`templateId`, no `importJobId`).

### Changed
- ⚠️ **Comportamiento**: templates con `Update If Exists = No` en algún Table Mapping ahora SKIPEAN
  las filas existentes en vez de actualizarlas (el flag por fin significa lo que dice). Revisá el
  valor del flag en tus templates: el default de columna es el que manda. `skippedcount` deja de ser
  siempre 0.
- Documentación alineada de punta a punta (13 archivos: referencia de tablas, arquitectura, guías de
  admin/usuario — sin menciones a `Error Summary` ni campos eliminados).
- `Smelter.Plugins` bump a `1.0.5` (recarga del package en Dataverse).

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
