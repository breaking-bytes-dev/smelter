[← Volver al índice de documentación](../README.md)

# Smelter — Architecture

## Overview

Smelter transforma archivos CSV denormalizados en tablas Dataverse normalizadas. El motor central es una
cadena de invocaciones cortas orquestadas por un Cloud Flow (Power Automate): un plugin por chunk,
sin ninguna tabla de staging intermedia.

**Formato soportado**: solo CSV. El soporte a Excel fue descartado en el diseño v2 (ver más abajo).

## Flujo General

```
┌─────────────────────────────────────────────────────────────────┐
│  Canvas App / Model-Driven App                                  │
│  Usuario configura Import Template + sube CSV + crea Import Job │
│  ImportJob.Status = Queued                                      │
└────────────────────────────┬────────────────────────────────────┘
                             │ trigger: status = Queued
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│  Cloud Flow (Power Automate)                                    │
│  Connection: application user dedicado                          │
│                                                                 │
│  1. sanic_PrepareImport(JobId)                                  │
│     └─► cuenta filas, valida headers, resetea estado job        │
│         out: TotalRows                                          │
│                                                                 │
│  2. Do until HasMore == false                                   │
│       sanic_ProcessChunk(JobId)           [retry exponencial]   │
│       └─► descarga CSV, parse streaming + skip al offset        │
│           └─► toma chunk (chunkSize filas)                      │
│               └─► orden DAG por TableMapping.ProcessingOrder    │
│                   └─► upsert por alternate key (idempotente)    │
│                       └─► avanza offset (processingbatch)       │
│                           out: HasMore, ProcessedSoFar          │
│                                                                 │
│  Scope Catch: ImportJob.Status = Failed                         │
└─────────────────────────────────────────────────────────────────┘
                             │
                             ▼
            ┌────────────────────────────────┐
            │  Dataverse                     │
            │  ImportJob  (progreso/estado)  │
            │  ImportJobLog (solo fallidas)  │
            └────────────────────────────────┘
```

## Detalle del Motor por Chunk

Para cada invocación de `sanic_ProcessChunk`:

1. `ImportContextLoader` carga la configuración completa desde Dataverse (template, table mappings, column mappings, relationship mappings).
2. `FileDownloader` descarga el CSV desde la columna `File` del Import Job.
3. `CsvParserService.ReadRange` lee el rango `[offset, offset+chunkSize)` en streaming, sin materializar el archivo completo en memoria.
4. Para cada tabla en orden DAG (`ProcessingOrder` ascendente):
   - `TransformEngine` evalúa las expresiones DSL de las Column Mappings.
   - `TypeCoercer` convierte los valores string al tipo Dataverse de destino.
   - `NormalizationEngine` arma el `Entity` a partir de los resultados.
   - `UpsertWriter` ejecuta el upsert via `UpsertRequest`; en caso de fallo clasifica la categoría de error.
5. Los fallos (solo ellos) se loguean en `ImportJobLog` via `ExecuteMultipleRequest` con upsert idempotente.
6. El job se actualiza con el nuevo offset (`processingbatch`), contadores y status.

## Decisiones de Diseño

### Por qué chunked en lugar de plugin async monolítico

Los plugins en sandbox de Dataverse —síncronos y asíncronos— tienen un **límite de ejecución de 2 minutos**.
El mecanismo de auto-continue por re-encolado del sistema async acumulaba `plugin depth` y Dataverse lo
cancelaba cerca de depth 8 (~12 minutos de trabajo máximo).

La solución es dividir el procesamiento en invocaciones independientes orquestadas por el Cloud Flow:
- Cada llamada a `ProcessChunk` es un request fresco al plugin → el depth se reinicia en 1 y el
  timeout de 2 minutos se renueva.
- No hay límite práctico de volumen: el flow llama al plugin tantas veces como sea necesario.

### Por qué solo CSV (no Excel)

Los archivos `.xlsx` no son streameables de forma eficiente: el archivo ZIP interno contiene un `sharedStrings.xml`
que debe cargarse completo antes de leer cualquier celda. Esto hace imposible el skip-por-offset real.
CSV permite saltear filas sin materializarlas, que es el mecanismo que hace posible el modelo chunked.

### Por qué upsert por alternate key (no dedup en memoria)

El motor chunked implica que el mismo chunk puede re-procesarse (si el Cloud Flow reintenta tras un error).
Con dedup solo en memoria, un retry duplicaría registros. Usando `UpsertRequest` contra alternate keys
configuradas en las tablas destino, el re-procesamiento es idempotente: si el registro ya existe, se
actualiza; si no, se crea. No hay duplicados.

### Por qué el log solo registra las filas fallidas

Escribir un log por cada fila exitosa (write-amplification) podría consumir más tiempo que el propio
procesamiento en imports grandes. El `ImportJob` ya lleva los contadores agregados (`successcount`,
`failedcount`, `skippedcount`). El `ImportJobLog` captura solo lo que requiere revisión manual.

El log también usa upsert idempotente (alternate key: `importjobid` + `rownumber` + `targettable`), por
lo que un retry del chunk no duplica las entradas de error.

### Por qué los plugins corren SYSTEM

Los plugins crean `IOrganizationService` con `factory.CreateOrganizationService(null)`, lo que resulta
en un contexto de usuario `SYSTEM`. Esto garantiza que:
- Las operaciones no están sujetas a los privilegios de seguridad del usuario que disparó el flow.
- No se generan entradas en el audit log asociadas al application user por cada fila procesada.
- El procesamiento es consistente independientemente de quién inicia el import.

### Por qué un application user dedicado para el Cloud Flow

El Cloud Flow se conecta con un **application user dedicado** (Service Principal) en lugar de una cuenta
de usuario licenciada. Esto separa el pool de requests de API del entitlement diario de los usuarios
humanos, evitando que un import masivo sature las cuotas de los usuarios del equipo.

### Por qué DAG para el orden de procesamiento

Al normalizar desde un CSV denormalizado hacia múltiples tablas, los registros padre deben existir antes
que los hijos. Por ejemplo, no se puede crear un `OrderLine` con lookup a `Order` si el `Order` todavía
no existe. El DAG construido desde los `RelationshipMapping` determina el orden correcto (`ProcessingOrder`
ascendente en `TableMapping`) y resuelve cadenas complejas como País → Estado → Ciudad.

## Responsabilidades de los Servicios

| Servicio | Responsabilidad |
|----------|----------------|
| `ImportContextLoader` | Carga toda la configuración desde Dataverse (template, mappings) |
| `FileDownloader` | Descarga el CSV desde la columna `File` del Import Job |
| `CsvParserService` | Cuenta filas (`CountRows`) y lee rangos en streaming (`ReadRange`) |
| `NormalizationEngine` | Orquesta el pipeline: transform → coerce → upsert para cada tabla |
| `DependencyResolver` | Construye el DAG, detecta ciclos, ordena topológicamente |
| `TypeCoercer` | Convierte strings a tipos de atributo Dataverse |
| `TransformEngine` | Evalúa expresiones DSL de transform |
| `UpsertWriter` | Ejecuta `UpsertRequest`; clasifica faults en categoría de error |

## Manejo de Errores

- **Errores de fila**: se capturan, se categorizan (`ErrorCategory`) y se loguean en `ImportJobLog`. El procesamiento del resto del chunk continúa.
- **Errores transitorios de chunk** (excepción no capturada, timeout): la invocación falla y el retry policy del Cloud Flow reintenta la llamada a `ProcessChunk`. Como el offset no avanzó, el chunk se re-procesa sin duplicar (idempotencia por upsert).
- **Error fatal del flow** (scope catch): el job se marca `Failed`. El detalle del error se diagnostica desde el **Plugin Trace Log**; los errores por fila quedan en `ImportJobLog`.

## Limitaciones conocidas y configuración no soportada todavía

Estado a 1.3.0.0 de los campos que históricamente no tenían efecto: algunos fueron **eliminados del
schema**, uno se **implementó**, y otro sigue ignorado. Queda registrado a nivel arquitectura.

- **IsRetry (campo `retry` en Import Job):** campo **eliminado del schema en 1.3.0.0**. Nunca
  existió un mecanismo de "reprocesar solo las filas fallidas". El retry real opera a nivel de
  chunk (retry policy del Cloud Flow) o a nivel de job completo (volver a `Queued`), ambos seguros
  por la idempotencia del upsert.

- **IsJunctionTable (`sanic_smlt_tm_isjunctiontable`):** sigue sin implementarse. El campo existe
  en el schema pero el motor lo ignora, y desde 1.3.0.0 el export/import de templates tampoco lo
  toca. Está **pendiente de eliminación**. Era un placeholder para el soporte de relaciones N:M,
  que no está implementado.

- **CreateIfNotExists / UpdateIfExists:** **implementados en 1.3.0.0.** El `UpsertWriter` ahora
  honra ambos flags cuando hay dedup columns (alternate key): si el registro existe y
  `UpdateIfExists` = No → la fila se marca `Skipped` (no se escribe, cuenta en `skippedcount`);
  si no existe y `CreateIfNotExists` = No → `Skipped`. Con ambos flags en Sí (default) se mantiene
  el `UpsertRequest` de un solo round-trip. Sin dedup columns los flags se ignoran — no hay
  concepto de "existe". Las filas `Skipped` se cuentan pero no se loguean en `ImportJobLog`.

- **RelationshipType = ManyToMany / IntersectTable:** N:M sigue sin soporte. `NormalizationEngine`
  filtra con `.Where(r => r.IsOneToMany)`: cualquier Relationship Mapping configurado como
  `ManyToMany` se ignora en silencio, sin error. El campo `sanic_smlt_rm_intersecttable` fue
  **eliminado del schema en 1.3.0.0**. Implementar N:M requeriría `AssociateRequest` y desarrollo nuevo.

### Categorías de error (`sanic_smlt_ijl_errorcategory`)

| Valor | Categoría | Origen |
|-------|-----------|--------|
| 0 | SourceError | Fila malformada o columnas faltantes en el CSV |
| 1 | TransformError | Fallo en el DSL: función desconocida, error de sintaxis |
| 2 | TypeError | `TypeCoercer` no pudo convertir el valor al tipo destino |
| 3 | ValidationError | Campo requerido vacío o validación de negocio |
| 4 | LookupUnresolved | Lookup padre o externo no encontrado en Dataverse |
| 5 | TargetData | Dataverse rechazó la escritura (constraint, privilegio, duplicado) |
| 6 | Transient | Fallo transitorio de BD (deadlock/timeout/lock). En v1 se trata como TargetData. |
