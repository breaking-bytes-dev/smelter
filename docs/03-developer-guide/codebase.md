[← Volver al índice de documentación](../README.md)

# Codebase — Estructura y responsabilidades

> Referencia técnica para desarrolladores que trabajan sobre el assembly `Smelter.Plugins`.
> Organizá la lectura por capa: Plugins → Services → Models.

---

## Estructura de directorios

```
src/Smelter.Plugins/
├── PrepareImportPlugin.cs        # IPlugin + PrepareImportHandler
├── ProcessChunkPlugin.cs         # IPlugin + ProcessChunkHandler
├── ExportTemplatePlugin.cs       # IPlugin + ExportTemplateHandler
├── ImportTemplatePlugin.cs       # IPlugin + ImportTemplateHandler
├── Services/
│   ├── IFileParser.cs            # Contrato genérico de parseo
│   ├── CsvParserService.cs       # ICsvReader + IFileParser (CSV)
│   ├── NormalizationEngine.cs    # INormalizationEngine — motor de normalización
│   ├── UpsertWriter.cs           # IUpsertWriter — escritura idempotente en Dataverse
│   ├── ImportContextLoader.cs    # IImportContextLoader — carga de configuración
│   ├── FileDownloader.cs         # IFileDownloader — descarga de columnas File
│   ├── DependencyResolver.cs     # Kahn topological sort (estático)
│   ├── TypeCoercer.cs            # Coerción de tipos string → CRM types
│   ├── TransformEngine.cs        # DSL: lexer + parser + evaluador
│   ├── TemplateSerializer.cs     # ITemplateSerializer — JSON portable
│   ├── TemplateExporter.cs       # ITemplateExporter — export desde Dataverse
│   └── TemplateImporter.cs       # ITemplateImporter — import hacia Dataverse
└── Models/
    ├── ImportContext.cs           # ImportContext + config classes
    ├── SourceRow.cs
    ├── TargetRecord.cs
    ├── ProcessingResult.cs
    ├── TemplateDto.cs
    ├── ColumnDataType.cs
    ├── ErrorCategory.cs
    ├── ImportJobStatus.cs
    └── ImportLogStatus.cs
```

---

## Patrón transversal: handler testeable + wrapper IPlugin fino

Todo plugin de Smelter sigue el mismo patrón estructural:

```
[IPlugin wrapper] → extrae params del IPluginExecutionContext
                  → construye el handler con dependencias concretas
                  → delega al handler
                  → escribe OutputParameters

[Handler testeable] → recibe interfaces inyectables (IImportContextLoader, ICsvReader, etc.)
                    → contiene TODA la lógica de negocio
                    → no tiene ninguna referencia a IPluginExecutionContext
                    → es instanciable en tests con NSubstitute
```

El wrapper `IPlugin` es deliberadamente fino: solo existe para satisfacer el contrato del sandbox de Dataverse. Todo lo que se puede verificar vive en el handler.

---

## Capa de Plugins (entry points / Custom API backings)

### `PrepareImportPlugin` + `PrepareImportHandler`

**Custom API:** `sanic_PrepareImport`
**In:** `JobId` (Guid) **Out:** `TotalRows` (int)

Responsabilidad del **plugin**: extraer `JobId` del contexto, instanciar el handler con `ImportContextLoader`, `FileDownloader`, `CsvParserService` y `IOrganizationService`, y escribir `TotalRows` en `OutputParameters`.

Responsabilidad del **handler** (`PrepareImportHandler.Run`):
1. Carga el `ImportContext` completo vía `IImportContextLoader.LoadContext`.
2. Descarga el archivo CSV de la columna `sanic_smlt_ij_sourcefile` vía `IFileDownloader.Download`.
3. Valida que todas las columnas mapeadas existan en el header del CSV (lanza `InvalidPluginExecutionException` si alguna falta). Las columnas con `TransformExpression` configurado se excluyen de la validación porque no leen del CSV directamente.
4. Cuenta las filas de datos vía `ICsvReader.CountRows` (sin materializar las filas).
5. Resetea el Import Job: status → `Processing`, `TotalRows`, `ProcessingBatch = 0`, `ProcessedRows = 0`, `SuccessCount = 0`, `FailedCount = 0`, `SkippedCount = 0`, `StartTime = UtcNow`.

**Constantes relevantes:**
- `ImportJobTable = "sanic_tbl_smlt_importjob"`
- `SourceFileAttribute = "sanic_smlt_ij_sourcefile"`

---

### `ProcessChunkPlugin` + `ProcessChunkHandler`

**Custom API:** `sanic_ProcessChunk`
**In:** `JobId` (Guid), `ChunkSize` (int, opcional — override del template)
**Out:** `HasMore` (bool), `ProcessedSoFar` (int)

Responsabilidad del **plugin**: extraer `JobId` y el override opcional de `ChunkSize`, instanciar el handler con todas las dependencias del pipeline (`ImportContextLoader`, `FileDownloader`, `CsvParserService`, `NormalizationEngine`, `UpsertWriter`, `IOrganizationService`), y escribir los outputs.

Responsabilidad del **handler** (`ProcessChunkHandler.Run`):
1. Carga el `ImportContext` (que incluye el offset actual en `InitialProcessingBatch`).
2. Resuelve el `ChunkSize` efectivo: override > template > default (1.000). Clampea entre `MinChunkSize = 100` y `MaxChunkSize = 10.000`.
3. Descarga el archivo CSV.
4. Lee exactamente su slice vía `ICsvReader.ReadRange(bytes, hasHeader, delimiter, offset, chunkSize)`. Las filas skipped se avanzan sin materializarse.
5. Si `rows.Count == 0`: no llama al engine. Actualiza el job como `Completed` (o `CompletedWithErrors` si ya hay fallos acumulados). Devuelve `HasMore = false`.
6. Llama a `INormalizationEngine.ProcessBatch(context, rows, writer)`.
7. Agrega los contadores acumulados (success/failed/skipped).
8. Loguea solo las filas fallidas en `sanic_tbl_smlt_importjoblog` vía `ExecuteMultiple` con `ContinueOnError = true`. Cada log entry usa `UpsertRequest` con alternate key (`sanic_smlt_ijl_id` + `sanic_smlt_ijl_rownumber` + `sanic_smlt_ijl_targettable`) para que sea idempotente.
9. Determina `HasMore`: `rows.Count >= chunkSize`. Si no hay más, determina el status final: `Completed` o `CompletedWithErrors`.
10. Actualiza el Import Job con el nuevo offset, contadores y (si es la última iteración) `EndTime`.

**Sin staging, sin dedup en memoria.** La idempotencia viene del upsert por alternate key. Cada invocación es independiente.

**`ProcessChunkResult`**: DTO interno con `HasMore` y `ProcessedSoFar`.

---

### `ExportTemplatePlugin` + `ExportTemplateHandler`

**Custom API:** `sanic_ExportTemplate`
**In:** `TemplateId` (Guid) **Out:** `Json` (string)

El **handler** es trivial: `_serializer.Serialize(_exporter.Export(templateId))`. Toda la complejidad está en `TemplateExporter` y `TemplateSerializer`.

---

### `ImportTemplatePlugin` + `ImportTemplateHandler`

**Custom API:** `sanic_ImportTemplate`
**In:** `Json` (string) **Out:** `TemplateId` (Guid), `Warnings` (string, newline-separated)

El **handler**: `_serializer.Deserialize(json)` → `_importer.Import(dto)`. La deserialización ya valida el JSON; si es inválido lanza `InvalidPluginExecutionException` antes de tocar Dataverse.

---

## Capa de Services

### `CsvParserService` — `ICsvReader` + `IFileParser`

**Propósito:** parseo streaming de CSV, sin materializar el archivo entero cuando no es necesario.

**Interfaz `ICsvReader`:**

| Método | Descripción |
|--------|-------------|
| `CountRows(bytes, hasHeaderRow, delimiter)` | Cuenta filas de datos sin materializarlas. O(n) en tiempo, O(1) en memoria. |
| `ReadHeaders(bytes, hasHeaderRow, delimiter)` | Devuelve los nombres de columna del header. Sin header: genera `Column0`, `Column1`, ... |
| `ReadRange(bytes, hasHeaderRow, delimiter, skip, take)` | Lee `take` filas a partir de `skip`, sin materializar las primeras `skip`. RowNumber es físico (header = 1). |

**Implementación:** usa `CsvHelper 30.0.1` con `CsvConfiguration` configurado para `TrimOptions.Trim`, `MissingFieldFound = null`, `BadDataFound = null`, detección de BOM. El `CsvReader` disposa el stream subyacente al salir del `using`.

**`IFileParser`** es el contrato genérico heredado; `CsvParserService.Parse` delega a `ReadRange(skip=0, take=MaxValue)`.

---

### `NormalizationEngine` — `INormalizationEngine`

**Propósito:** transforma filas fuente en `TargetRecord`s y los escribe vía `IUpsertWriter`, respetando el orden topológico del DAG.

**Interfaz:** `IList<ProcessingResult> ProcessBatch(ImportContext context, IList<SourceRow> rows, IUpsertWriter writer)`

**Flujo de `ProcessBatch`:**
1. Llama a `DependencyResolver.Resolve(tableMappings, relationshipMappings)` para obtener las tablas en orden padre → hijo.
2. Para cada tabla en orden:
   a. Llama a `BuildBatchRecords` para construir los `TargetRecord`s de todas las filas.
   b. Dentro de `BuildBatchRecords`, por cada fila: aplica column mappings (`ApplyColumnMappings`) y luego parent lookups (`ApplyParentLookups`).
   c. Llama a `writer.Write(records, tableMapping, isDryRun)`.
3. Acumula todos los `ProcessingResult`s (incluyendo los skipped del build) y los devuelve.

**`ApplyColumnMappings`:**
- Si `TransformExpression` está configurado, evalúa el DSL vía `TransformEngine.Evaluate`.
- Aplica `DefaultValue` antes de la validación de `IsRequired` (fix #6).
- Para `OptionSet`: aplica el mapeo JSON de `OptionSetMapping` sobre el valor raw.
- Delega la coerción de tipos a `TypeCoercer.Coerce`.
- Para `Lookup`: si el valor es GUID lo convierte a `EntityReference(table, guid)`; si no y hay `LookupMatchColumn`, construye un `EntityReference` por alternate key `(table, matchColumn, value)` (fix #7); si no tiene ninguno, registra fallo con `ErrorCategory.LookupUnresolved`.

**`ApplyParentLookups`:**
- Busca las relaciones 1:N donde esta tabla es el hijo.
- Para cada relación, construye un `EntityReference` con `KeyAttributeCollection` usando los valores de las columnas fuente (`ParentDedupColumns`) como claves del alternate key del padre.
- Dataverse resuelve el GUID en el momento del upsert — no hay preload ni caché en memoria.

**Stateless.** No guarda estado entre invocaciones. No hay dedup en memoria, no hay timeout, no hay auto-continue.

---

### `UpsertWriter` — `IUpsertWriter`

**Propósito:** escribe lotes de `TargetRecord`s en Dataverse de forma idempotente cuando la tabla tiene alternate key configurado.

**Interfaz:** `IList<ProcessingResult> Write(IList<TargetRecord> records, TableMappingConfig tableMapping, bool isDryRun)`

**Comportamiento:**
- Si `tableMapping.DedupColumns` tiene valor: emite `UpsertRequest` con `KeyAttributes` poblados (idempotente).
- Si `DedupColumns` está vacío: degrada a `CreateRequest`/`UpdateRequest` (no idempotente — traza advertencia).
- Los registros se envían en batches de `BatchSize = 1.000` mediante `ExecuteMultipleRequest` con `ContinueOnError = true` y `ReturnResponses = true`.
- Respuestas procesadas por índice: si `item.Fault != null` → `ProcessingResult.Failure`; si no → `ProcessingResult.Success` (detecta create vs update vía `UpsertResponse.RecordCreated`).
- `isDryRun = true`: no llama a Dataverse. Devuelve resultados de éxito simulados para todas las filas.

**Los parent lookups llegan como `EntityReference` con `KeyAttributes`** — Dataverse los resuelve en el upsert sin GUID.

---

### `ImportContextLoader` — `IImportContextLoader`

**Propósito:** carga la configuración completa desde Dataverse hacia los objetos del modelo en memoria.

**Interfaz:**

| Método | Descripción |
|--------|-------------|
| `LoadContext(importJobId)` | Contexto completo: template + table/column/relationship mappings + contadores actuales del job. |
| `LoadTemplateConfig(templateId)` | Solo la config del template. Recibe un **template id**, NO un job id. |
| `LoadRelationshipMappings(templateId)` | Lista de `RelationshipMappingConfig` para un template. Devuelve lista vacía si no hay ninguno. |

**Nota crítica sobre `LoadTemplateConfig`:** recibe un `templateId` y hace `Retrieve` directamente sobre la tabla de templates. Si se le pasa un job id y se intenta recuperar un template, Dataverse lanzará "Does Not Exist". Esta separación fue un bug histórico (pre-v2) — no mezclar los IDs.

**`LoadContext` incluye las relaciones** (era el bug #1 original: no se cargaban, el DAG nunca recibía aristas y procesaba siempre en orden arbitrario).

**Constantes de nombres de tabla:**
- `ImportJobTable = "sanic_tbl_smlt_importjob"`
- `ImportTemplateTable = "sanic_tbl_smlt_importtemplate"`
- `TableMappingTable = "sanic_tbl_smlt_tablemapping"`
- `ColumnMappingTable = "sanic_tbl_smlt_columnmapping"`
- `RelationshipMappingTable = "sanic_tbl_smlt_relationshipmapping"`

---

### `FileDownloader` — `IFileDownloader`

**Propósito:** descarga el contenido de una columna File de Dataverse en bloques de 4 MB.

**Interfaz:** `byte[] Download(EntityReference target, string fileAttributeName)`

**Implementación:** usa los mensajes de SDK `InitializeFileBlocksDownload` / `DownloadBlock`. Lee el token de continuación y el tamaño total del archivo, luego itera descargando bloques hasta completar. Retorna todos los bytes concatenados.

---

### `DependencyResolver`

**Propósito:** ordena las `TableMappingConfig`s en orden topológico (padres antes que hijos) usando el algoritmo de Kahn (BFS sobre in-degree).

**Firma:** `static List<TableMappingConfig> Resolve(List<TableMappingConfig> tableMappings, List<RelationshipMappingConfig> relationships)`

**Comportamiento:**
- Sin relaciones: devuelve los mappings en el orden original.
- Con relaciones: construye el grafo dirigido (padre → hijo) y aplica Kahn.
- Self-reference o ciclo: lanza `InvalidOperationException` con los nombres de las tablas involucradas.
- Las relaciones con ids que no corresponden a ningún table mapping son ignoradas silenciosamente.

Es una clase **estática**, sin estado, sin dependencias. Completamente testeable con datos puros.

---

### `TypeCoercer`

**Propósito:** convierte valores string crudos del CSV al tipo CRM correcto según `ColumnDataType`.

**Firma:** `object Coerce(string value, ColumnDataType dataType)`

**Contrato:**
- String nulo o vacío → retorna `null` (la validación de `IsRequired` ocurre en el engine, antes de llamar a este método).
- Fallo de parseo → lanza `FormatException` con mensaje descriptivo.

| ColumnDataType | Tipo retornado | Notas |
|---------------|---------------|-------|
| `String` | `string` (trimmed) | |
| `Integer` | `int` | InvariantCulture |
| `Decimal` | `decimal` | InvariantCulture |
| `DateTime` | `DateTime` | Parseo estándar; fallback para OADate serial de Excel en rango [2, 73415] |
| `Boolean` | `bool` | Acepta true/false, 1/0, yes/no (case-insensitive) |
| `Money` | `Money` | |
| `Guid` | `Guid` | |
| `OptionSet` | `OptionSetValue` | Solo acepta int directamente; usar `MAP()` para mapear strings |
| `Lookup` | `string` (raw) | El engine construye la `EntityReference` usando LookupTable + LookupMatchColumn |

---

### `TransformEngine`

**Propósito:** evalúa expresiones DSL definidas en `ColumnMappingConfig.TransformExpression`.

Ver [`transform-dsl.md`](transform-dsl.md) para la referencia completa del lenguaje. Internamente implementa un lexer (`Lex`) que produce tokens y un parser recursivo descendente que construye un AST (`StringLiteralNode`, `NumberLiteralNode`, `ColumnReferenceNode`, `FunctionCallNode`). La evaluación es inmediata sobre el AST.

**Funciones soportadas:** `CONCAT`, `UPPER`, `LOWER`, `TRIM`, `LEFT`, `RIGHT`, `DATEPARSE`, `REPLACE`, `SPLIT`, `IF`, `COALESCE`, `STATIC`, `MAP`.

---

### `TemplateSerializer` — `ITemplateSerializer`

**Propósito:** serializa/deserializa el DTO portable (`TemplateExportDto`) a/desde JSON, y valida el JSON en la deserialización.

**Usa `DataContractJsonSerializer`** (no `System.Text.Json` ni `Newtonsoft`) porque es el único serializador en la whitelist del sandbox de Dataverse.

**Versión de formato:** `"1.0"`. La validación rechaza versiones con major > 1. En la deserialización verifica además que todas las referencias (`ParentRef`, `ChildRef`) en las relaciones correspondan a `Ref`s existentes en los table mappings.

---

### `TemplateExporter` — `ITemplateExporter`

**Propósito:** lee el árbol completo de un Import Template desde Dataverse y construye el `TemplateExportDto`.

**Firma:** `TemplateExportDto Export(Guid templateId)`

Asigna a cada `TableMappingDto` un `Ref` estable (slug) derivado del nombre (`"tm-" + slug(name)`), deduplicado con sufijo numérico si hay colisiones. Las relaciones se expresan con `ParentRef`/`ChildRef` en lugar de GUIDs — los GUIDs no sobreviven entre ambientes.

---

### `TemplateImporter` — `ITemplateImporter`

**Propósito:** recrea el árbol de un template en el ambiente destino a partir de un `TemplateExportDto`.

**Firma:** `ImportResult Import(TemplateExportDto dto)` → `ImportResult { TemplateId, Warnings }`

**Estrategia:** busca el template por nombre. Si existe, elimina todos sus mappings actuales (relaciones → column mappings → table mappings, en ese orden para respetar FKs) y los recrea desde el DTO. Si no existe, crea el template y los mappings. Reasigna GUIDs locales para las relaciones via el mapa `refToGuid`.

**Advertencias (no bloquean):** si una tabla destino referenciada no existe en el ambiente se agrega un warning al resultado — el import continúa.

El constructor acepta un `Func<string, bool> entityExists` inyectable (por defecto usa `RetrieveEntityRequest`), lo que permite testearlo sin Dataverse.

---

## Capa de Models

### `ImportContext`

El objeto de contexto que se construye una vez por invocación y circula por todo el pipeline. Contiene:

- `ImportJobId`, `IsDryRun` — identidad y modo de ejecución.
- `InitialProcessedRows`, `InitialSuccessCount`, `InitialFailedCount`, `InitialSkippedCount`, `InitialProcessingBatch` — estado acumulado del job al comienzo de esta invocación (para sumar los contadores del chunk).
- `Template` (`ImportTemplateConfig`) — config del template (HasHeaderRow, Delimiter, ChunkSize, SourceType).
- `TableMappings` (`List<TableMappingConfig>`) — mappings de tabla con sus column mappings anidados.
- `RelationshipMappings` (`List<RelationshipMappingConfig>`) — aristas del DAG.

**`ImportTemplateConfig`:** Id, Name, SourceType (0=CSV, 1=Excel), HasHeaderRow, Delimiter, ChunkSize. `IsCsv`/`IsExcel` son propiedades calculadas.

**`TableMappingConfig`:** incluye `GetDedupColumnNames()` que parsea el string `DedupColumns` separado por comas.

**`ColumnMappingConfig`:** incluye propiedades calculadas `HasTransform`, `IsLookup`, `IsOptionSet`.

**`RelationshipMappingConfig`:** incluye `IsOneToMany` / `IsManyToMany`.

---

### `SourceRow`

Una fila del CSV. Contiene `RowNumber` (1-based, físico — el header es la fila 1) y `Values` (`Dictionary<string, string>`) con los valores crudos keyed por nombre de columna.

---

### `TargetRecord`

Un registro listo para escribir en Dataverse. Contiene `TableLogicalName`, `Attributes` (valores ya coercionados, tipos CRM), `SourceRowNumber`, `ExistingRecordId` (para updates explícitos — no usado en el path de upsert por alternate key).

---

### `ProcessingResult`

Resultado de procesar una fila para una tabla destino. Contiene `RowNumber`, `TargetTable`, `Status` (`ImportLogStatus`), `RecordId`, `ErrorMessage`, `Category` (`ErrorCategory`).

Factories estáticas: `Success(rowNumber, targetTable, recordId, isUpdate)`, `Skipped(...)`, `Failure(rowNumber, targetTable, errorMessage, category)`.

`IsSuccess` devuelve `true` para `Created` y `Updated`.

---

### `ErrorCategory`

Enum que clasifica el origen del fallo para diagnóstico en el Import Job Log:

| Valor | Código | Descripción |
|-------|--------|-------------|
| `SourceError` | 0 | Fila malformada / columna faltante |
| `TransformError` | 1 | Error en el DSL (función desconocida, sintaxis) |
| `TypeError` | 2 | TypeCoercer no pudo convertir el valor |
| `ValidationError` | 3 | Campo requerido vacío |
| `LookupUnresolved` | 4 | Lookup externo no resoluble (sin GUID ni LookupMatchColumn) |
| `TargetData` | 5 | Dataverse rechazó la escritura (constraint, privilegio, duplicado) |
| `Transient` | 6 | Fault transitorio de Dataverse (deadlock/timeout). Reservado para v2. |

---

### `ImportJobStatus` / `ImportLogStatus` / `ColumnDataType`

Enums que mapean directamente a los valores de los OptionSets correspondientes en Dataverse. Cambiar estos valores rompe la sincronía con la solución.

| Enum | Tabla | Columna |
|------|-------|---------|
| `ImportJobStatus` | `sanic_tbl_smlt_importjob` | `sanic_smlt_ij_status` |
| `ImportLogStatus` | `sanic_tbl_smlt_importjoblog` | `sanic_smlt_ijl_status` |
| `ColumnDataType` | `sanic_tbl_smlt_columnmapping` | `sanic_smlt_cm_datatype` |

---

### `TemplateDto` (`TemplateExportDto` y familia)

DTOs anotados con `[DataContract]` / `[DataMember]` para serialización con `DataContractJsonSerializer`. La jerarquía es:

```
TemplateExportDto
├── FormatVersion (string)
├── Template (TemplateInfoDto)
├── TableMappings[] (TableMappingDto)
│   ├── Ref (slug estable — reemplaza el GUID en las relaciones)
│   └── ColumnMappings[] (ColumnMappingDto)
└── RelationshipMappings[] (RelationshipMappingDto)
    ├── ParentRef (Ref de la tabla padre)
    └── ChildRef  (Ref de la tabla hijo)
```

**El campo `Ref` es el mecanismo de portabilidad.** Los GUIDs de `TableMappingConfig` son locales al ambiente; el `Ref` es un slug derivado del nombre que sobrevive la exportación.

---

## Componentes que NO existen (v1 eliminados en rediseño v2)

Los siguientes servicios existieron en la versión anterior y fueron eliminados completamente en el rediseño chunked-v2. No hay que buscarlos ni recrearlos:

| Servicio eliminado | Razón |
|-------------------|-------|
| `StagingTable` / staging Dataverse | Reemplazado por streaming directo `ReadRange` |
| `ExcelParser` / `ExcelParserService` | Excel descartado del roadmap actual |
| `DedupService` | Reemplazado por upsert por alternate key (idempotente, sin preload) |
| `BatchProcessor` | Absorbido por `ProcessChunkHandler` + `NormalizationEngine` |
| `ProgressReporter` | Absorbido por la lógica de `ProcessChunkHandler.UpdateJob` |
