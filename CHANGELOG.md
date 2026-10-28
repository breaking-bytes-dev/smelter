# Changelog

Todos los cambios notables de Smelter se documentan acá. El formato se basa en
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Las versiones de la solución de Power
Platform se publicarán como GitHub Releases (roadmap: `docs/repo-maturity-roadmap.md`, Fase 2).

## [Unreleased]

### Added (UI bilingüe 1033/3082, íconos y nombre de la solución; solo DEV, `Smelter.Deploy` 4k-4n y 6a)
- **Etiquetas bilingües: inglés bajo LCID 1033 (idioma base de la org, el que leen las canvas apps) y español bajo 3082.**
  Antes la MDA tenía el español escrito bajo 1033. Cada usuario ve el idioma de su interfaz (si no tiene 3082, el 1033). Nuevo
  record compartido `Bilingual(En, Es)` + `Lcids`, `LabelEditor` (edita solo el 3082 de un `Label`, el 1033 existente no se
  toca) y `LocLabels` (`RetrieveLocLabels`/`SetLocLabels` para atributos de registros). Los mensajes que guardan plugins/flow
  (motivos de falla, validaciones, logs) siguen en inglés.
  - **4k `SiteMapSpec`**: cada Area/Group/SubArea con `Title` 1033 (inglés) + 3082 (español); `Violations` marca cualquier
    título que falte en alguno de los dos idiomas. Nombre de la app: `appmodule.name` (1033) = "Smelter — Import configuration"
    y el 3082 "Smelter — Configuración de importaciones" con `SetLocLabelsRequest` (`appmodule.name` es `IsLocalizable = True`
    según la referencia de la tabla AppModule de Microsoft Learn). La entrada del dashboard que crea el paso [6] nace con los
    dos títulos y su ícono.
  - **4j `FormPolishSpec`**: etiquetas de pestañas y secciones con `languagecode` 1033 + 3082 dentro del formxml
    (`FormXmlEditor.TrySetLabels`); la pestaña "Logs de importación" del Import Job pasa a "Import logs" (1033) y conserva el
    español en 3082 (se reconoce por su texto legacy, no se duplica).
  - **4i `ColumnDescriptionsSpec`**: las Description (tooltips) pasan a inglés 1033 + español 3082 (los textos actuales son el
    español); también las 3 columnas que crea `SchemaAdditionsSpec`.
  - **6a `ViewLabelsSpec`** (nuevo, corre después del [6]): las 3 vistas custom del Import Job pasan a inglés bajo 1033 ("Active
    jobs", "Completed with errors", "All jobs"; `UpsertViews` renombra las existentes en vez de duplicarlas) con nombre y
    descripción 3082; el dashboard conserva "Smelter Dashboard" y gana "Panel de Smelter" en 3082.
  - **4n `TableLabelsSpec`** (nuevo): nombres 3082 de las 6 tablas + la de keys HMAC (DisplayName y DisplayCollectionName), de
    todas sus columnas `sanic_smlt_*` y de las opciones de sus choices locales (Sí/No incluidos). El 1033 de tablas y columnas
    no se toca (se lee y se reenvía con `MergeLabels = true`).
- **Íconos (4m `IconsSpec`)**: 9 SVG Fluent-style 24x24 de un solo color (`Assets/icons/`, embebidos) como web resources SVG
  (`sanic_/smelter/icons/*.svg`, webresourcetype 11) DENTRO de `smlt_core_smelter`; `IconVectorName` de cada tabla de Smelter
  (incluida la de keys HMAC si existe), `VectorIcon="$webresource:..."` en cada SubArea del sitemap (reemplaza al
  `transparent_spacer.gif`) y `appmodule.webresourceid` con el ícono de la app.
- **Nombre de la solución (4l `SolutionNameSpec`)**: `solution.friendlyname` = "SMLT | Core | Smelter" (convención "CM | Core |
  Cobranza Movil" del proyecto hermano). El unique name `smlt_core_smelter` no cambia.
- `Smelter.Verify`: nuevos `[SOLNAME]`, `[ICONS]`, `[LABELS]` y `[VIEWS]`; `[SITEMAP]` y `[DESC]` ahora exigen los dos idiomas,
  los íconos y el nombre 3082 de la app.
- Tests de contrato: `BilingualTests`, `IconsSpecContractTests` (SVG parsea, viewBox, color, existe y está embebido),
  `TableLabelsSpecContractTests` (cada columna/opción de la spec existe en `solutions/unpacked`; sin duplicados ni textos
  vacíos; toda columna/opción viva tiene su 3082), `MiscLabelSpecsContractTests`; ampliados los de sitemap, formularios y
  descripciones.

### Fixed (guarda de re-encolado, Plugins 1.0.13)
- **Poner `Queued` sobre un job que está `Processing` ya no se permite** (`ValidateQueueAccess`, Pre-op Update): un
  Update por API durante la corrida disparaba otra corrida del flow cuyo claim veía `Queued` y reseteaba el job a
  mitad de camino. Rechaza con "El Import Job se está procesando: no se puede volver a poner en cola hasta que
  termine." antes del chequeo de acceso. `Queued -> Queued` (idempotente; lo resuelve el claim), estados finales y
  status vacío (reintento) siguen permitidos; el Create no cambia. El flow y `PrepareImport` nunca setean `Queued`
  (solo `Processing` y `Failed`), así que el pipeline del servidor no se ve afectado.
- `Smelter.Verify`: nuevo **7d** (Prepare directo deja el job `Processing`; `Queued` es rechazado con el mensaje del
  guard; ProcessChunk lo termina `Completed` con conteos exactos y 0 logs). 7c acepta que el segundo `Queued` sea
  rechazado (si el flow ya pasó el job a `Processing`) o aceptado (si seguía `Queued`).

### Fixed (corridas duplicadas del flow orquestador)
- **Un job encolado disparaba varias corridas del Cloud Flow en el mismo segundo** (verificado en Dev con el historial
  de `flowrun`): el trigger no tenía columnas filtro ni control de concurrencia, y los triggers de Dataverse son
  at-least-once. Cada corrida llamaba a `PrepareImport` (que resetea el job) y a `ProcessChunk` en paralelo, así que los
  upserts competían y fallaban con `DuplicateRecordEntityKey` (0x80060892): el job terminaba `CompletedWithErrors`, de
  forma intermitente. Arreglo:
  1. **Select columns** del trigger: `subscriptionRequest/filteringattributes = sanic_smlt_ij_status` (el Create sigue
     disparando; el Update solo si cambia el status).
  2. **Sin concurrencia global en el trigger**: los jobs **corren en paralelo** (una concurrencia `runs = 1`
     serializaría jobs distintos y cobranza encola varios "partes" a la vez); los duplicados del mismo job los
     neutraliza el claim (punto 3). El transformer quita cualquier `concurrency` que hubiera.
  3. **Claim atómico**: `PrepareImport` lee el job y solo lo reclama si está `Queued` (o sin status, para llamadas
     directas a la API); el reset que lo pasa a `Processing` va con `ConcurrencyBehavior.IfRowVersionMatches`, y ante
     `ConcurrencyVersionMismatch` (0x80060882) o status distinto devuelve `Claimed = false` sin resetear ni lanzar. Nueva
     response property `Claimed` (Boolean) en `sanic_capi_smelter_prepareimport`. El flow envuelve el `Scope` del loop
     en `Condition_claimed` (`Claimed = true`) y el handler `Failed` ahora cuelga de esa condición. Nuevo transformer
     idempotente `ClientDataClaimGate` (mismo Draft→Update→Activate que los demás) y el flow desempaquetado del repo
     ya lo trae. Plugins 1.0.12.
- `Smelter.Verify`: 7a exige además 0 Import Job Logs; nuevo **7c** (el mismo job encolado dos veces seguidas debe
  terminar `Completed` con conteos exactos). Las corridas directas de Prepare sobre un job ya corrido lo devuelven antes
  a "sin status".

### Added (auditoría de campos, tanda 2)
- **Import Job**: los campos de salida (Status, contadores, Processing Batch, Starttime/Endtime) pasan a **solo
  lectura** en el form; quedan editables nombre, template, archivo e Is Dry Run. Nueva pestaña **Logs de
  importación** con los Import Job Logs del job.
- **Motivo de la falla**: columna `sanic_smlt_ij_failurereason` (multilínea, 4000) en el Import Job, en solo lectura en
  el form. El Cloud Flow, además de `status = 4`, escribe el mensaje de la acción que falló (Prepare o ProcessChunk;
  PII-free). Nuevo transformer idempotente `ClientDataFailureReasonWriter` (paso 7 del deploy, mismo
  Draft→Update→Activate) y el flow desempaquetado del repo ya lo trae.
- **Import Job Log**: el main form muestra en solo lectura job, fila, tabla, estado, categoría y mensaje; la vista
  default suma Error Category (`FormPolishSpec`, paso 4j; `ViewXmlEditor`).
- **Relationship Mapping**: el form muestra Import Template y los lookups de Parent/Child Table Mapping se filtran por
  él (related-records filtering).
- **Description en TODAS las columnas custom** de las 6 tablas (`ColumnDescriptionsSpec`, paso 4i, solo toca la
  Description; sin cambiar display names). `Chunk Size` limitado a 0–10000 en la columna (`SchemaTuningSpec`).
- **Etiquetas de la MDA en español** (`SiteMapSpec`, paso 4k): app "Smelter — Configuración de importaciones"; área
  "Smelter"; grupos "Configuración", "Ejecución y monitoreo", "Diagnóstico"; subareas "Templates de importación",
  "Import Jobs", "Panel de Smelter", "Logs de importación". Sin prefijos `TBL |` / `SMLT |`.
- `Smelter.Verify`: `[DESC]`, `[POLISH]`, `[SITEMAP]`.

### Changed (auditoría de campos, tanda 2)
- **Export/import de templates formato 1.1** (compatible con 1.0): `Description` y `Status` viajan en el JSON. El
  import crea con el Status del JSON (Active si no viene) y, al actualizar, solo lo aplica si viene. Status fuera de
  0/1/2 se rechaza. Plugins 1.0.11.
- Docs: Solo inserción (`insertonly`), motivo de falla y formularios en `docs/02-solution-reference/tables/*.md` y
  `docs/05-user-guide/monitoring-and-retry.md`.

### Security
- **Encolar un Import Job ahora valida acceso al template del lado servidor**: nuevo
  `ValidateQueueAccessPlugin` (sync Pre-operation, Create y Update de `sanic_smlt_ij_status` sobre
  Import Job, registrado vía `StepSpec`/`StepProvisioner`). Cuando el status pasa a Queued, el usuario
  iniciador (`InitiatingUserId`) debe poder leer el Import Template (`PrincipalAccessChecker`, incluye
  teams); si no, falla con "No tenés acceso al Import Template de este job: no se puede poner en cola.".
  Cierra el hueco por el que cualquiera con Write sobre un job podía poner `status = 0` por API y que el
  flow lo ejecutara con su propia identidad. `Smelter.Verify` ([STEP]) asserta ambos steps.
- `PrepareImport` ahora limpia `sanic_smlt_ij_endtime` al resetear el job (un Reintentar arrastraba el fin
  de la corrida anterior). Plugins 1.0.9.

### Changed
- **Update If Exists nace en Sí** (`sanic_smlt_tm_updateifexists`, default del schema: antes No): alinea el
  schema con los docs y con el fallback del Loader (`?? true`). Solo cambia el default de mappings NUEVOS;
  los existentes conservan su valor. Aplicado por SDK (`SchemaTuningProvisioner`, paso 4h).
- **`Source Type` del Import Template**: default CSV y oculto del main form (el required level de formulario se mantiene; no aplica en servidor) (la columna se
  conserva y viaja en el export/import). El importer siempre escribe CSV (0).
- **`PrepareImport` valida la coherencia del template antes de bajar el archivo** (`TemplateValidator`, en
  `Smelter.Core`): cada Dedup Column debe ser el Target Column de un Column Mapping del mismo Table Mapping;
  `Data Type = Lookup` exige Lookup Table; una relación OneToMany exige "Lookup column on child", un padre
  con dedup columns y tantas Parent Dedup Columns como dedup columns tiene el padre. Los errores nombran el
  mapping y la columna (nunca valores). Antes cada caso fallaba fila por fila o en silencio.
- **Delimiter acepta alias de tabulador**: `\t`, `TAB` (sin distinguir mayúsculas) o el tab real; cualquier
  otro valor sigue siendo literal. Antes un TSV era inalcanzable desde la UI (no se puede tipear un tab).
- Plugins 1.0.10.
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
- **Paso 4h de `Smelter.Deploy`** (`SchemaTuningSpec`/`SchemaTuningProvisioner`, no destructivo) y
  `[TUNE]` en `Smelter.Verify`: ajustes declarativos de columnas/forms existentes (required level, defaults,
  campos ocultos). `FormXmlEditor.TryRemoveField` / `TrySetVisible`.
- **Forms y vistas fijados en la MDA** (paso 4g de `Smelter.Deploy`): main form, vista por defecto, lookup, asociada y Quick Find de cada tabla, más las 3 vistas propias de Import Job. ValidateApp ya no avisa "App users will see all forms and views".
- **Verify**: check `[MDA] 5` de los 33 componentes fijados; smoke `[CHUNK]` de 250 filas en 3 chunks por el cursor de bytes (newline embebido y multibyte en los bordes); modo `--bench` (100k filas en dry run, falla si algún chunk tarda ≥ 60 s) con input `bench` en `backend-verify`.
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
- **`Source Column` ya no distingue mayúsculas**: `PrepareImport` validaba los headers con `OrdinalIgnoreCase`
  pero el motor leía la celda con una comparación sensible (un CSV con `email` y un mapping `Email` pasaba
  Prepare y escribía vacío en silencio). `SourceRow.Values` es case-insensitive de punta a punta (también las
  referencias a columnas dentro de un Transform) y `SourceColumn` se recorta como los headers.
- **Dedup Columns**: cada nombre se recorta y se ignoran los vacíos (`"a, b"` generaba la key `" b"`, que nunca
  coincidía y dejaba el upsert sin key completa).
- **La key del padre ahora usa el mismo pipeline que el Table Mapping padre**: antes el hijo mandaba el valor
  CRUDO del CSV, por lo que un padre con key `HMAC(Pan, "k")` (o con un tipo coercionado, o con default) nunca
  resolvía para sus hijos. Ahora el motor aplica a las Parent Dedup Columns el transform / default /
  coerción del mapping del padre para esa columna destino.
- El Import Job queda `Failed` si `PrepareImport` falla o el loop del Cloud Flow hace timeout; el deploy
  ya no deja el flow apagado.

### Security
- `TypeCoercer`, lookups no resueltos y faults de `UpsertWriter` ya no incluyen el valor de la fila (PII)
  en el Import Job Log ni en traces.
- **Riesgo aceptado (HMAC)**: la key vive en Dataverse; cualquier System Administrator del entorno puede
  leerla, las copias/backups del entorno la arrastran y la columna del valor no se audita. Endurecimiento
  futuro posible: Azure Key Vault (mismas keys → mismos hashes, migración sin ruptura).

### Removed
- **`sanic_smlt_tm_isjunctiontable`** (Is Junction Table): sale del main form del Table Mapping y del schema
  (`SchemaCleanupSpec`, requiere `--allow-destructive`). Un JSON de export viejo que traiga el campo sigue
  importando (se ignora).
- **Opciones muertas de choices** (`SchemaCleanupSpec`/`OptionCleanupCatalogo`, `DeleteOptionValueRequest`,
  `--allow-destructive`; el provisioner se niega si alguna fila usa el valor): `Relationship Type = ManyToMany`
  (el motor lo ignoraba en silencio; ahora `PrepareImport` y el import de templates lo rechazan con un error
  claro), `Import Job Log Status = Created / Updated / Skipped` y `Error Category = Transient` (nunca se
  escriben). `ErrorCategory.Transient` sale del enum; `ImportLogStatus` conserva los valores para los
  contadores del job (solo `Failed` se persiste). Nuevo enum `RelationshipType`; `ChoicesContractTests` los ata.

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
