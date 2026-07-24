# Design: Chunked Import Redesign (v2 — CSV-only, sin staging)

## Technical Approach

Procesamiento en invocaciones cortas e independientes orquestadas por Power Automate, **sin tabla de
staging**. Cada `ProcessChunk` descarga el CSV, lo parsea en streaming saltando al offset, procesa una
tajada en orden DAG con upsert idempotente, loguea solo las fallidas (categorizadas) y avanza el offset
**después** de procesar. Implementa los specs `chunked-orchestration`, `normalized-upsert` y `error-handling`.

## Architecture Decisions

| Decisión | Elegido | Alternativa | Rationale |
|----------|---------|-------------|-----------|
| Materialización | Re-parse CSV streaming + skip por chunk | Tabla de staging | Escribir 100k a staging NO entra en 2 min (timeout en Prepare). Re-parsear es barato y distribuido. |
| Formato | Solo CSV | CSV + Excel | .xlsx no es streameable (shared strings se cargan enteras); skip re-parsea XML. CSV permite skip real. |
| chunkSize | Variable por Import Template | Por tabla / auto-adaptativo | El chunk se mide en filas-del-archivo; cada fila alimenta TODAS las tablas → el costo es del template. Por-tabla = multi-pasada compleja; adaptativo = futuro. |
| Estado/offset | `processingbatch` = watermark; avanza DESPUÉS de procesar | Avanzar antes / en memoria | Si el chunk muere, el offset no avanzó → resume re-procesa ese chunk. |
| Idempotencia | Upsert por alternate key | Dedup en memoria | Re-procesar un chunk no duplica → resume y retry seguros. |
| Log | Solo fallidas, idempotente (alt key + upsert) | Log por fila / sin log | Trazabilidad sin write-amplification (#8); el resume no duplica logs. |
| Errores | 7 categorías + transitorio vs dato | Mensaje plano | Diagnóstico por etapa; `Transient` re-lanza → PA reintenta; el resto loguea y sigue. |

## Data Flow

```
Canvas App ─ crea ImportJob(Queued)+CSV ─► Dataverse
                                             │ (trigger: status=Queued)
                                             ▼
                                       Cloud Flow (connection = application user)
                                         │ 1. PrepareImport(JobId) → cuenta filas, valida headers,
                                         │                            resetea estado → TotalRows
                                         │ 2. Do until HasMore==false  [retry policy en la acción]:
                                         │      ProcessChunk(JobId) → descarga CSV → parse streaming
                                         │        + skip(offset) → take(chunkSize) → DAG + upsert
                                         │        → log fallidas (upsert) → offset+=n → HasMore
                                         ▼
                                       ImportJob (offset/contadores/status) + ImportJobLog (solo fallidas)
```

## Schema Changes

| Objeto | Cambio |
|--------|--------|
| Import Template | + `sanic_smlt_it_chunksize` (Whole Number; default ~1000, rango 100–10000) |
| Import Job Log | + `sanic_smlt_ijl_errorcategory` (OptionSet, 7 valores) + **alternate key** (`importjobid`+`rownumber`+`targettable`) |
| Tablas destino | Alternate keys (= DedupColumns) — ya requerido |
| `sanic_tbl_smlt_importstagingrow` | **ELIMINADA** |

## Custom API Contracts

- `sanic_PrepareImport` — In: `JobId` (Guid). Out: `TotalRows` (int).
- `sanic_ProcessChunk` — In: `JobId` (Guid), `ChunkSize` (int, opcional override). Out: `HasMore` (bool), `ProcessedSoFar` (int).

## File Changes

| Archivo | Acción | Descripción |
|---------|--------|-------------|
| `Services/StagingReader.cs`, `StagingPayloadSerializer.cs` (+tests) | Delete | Sin staging |
| `Services/ExcelParserService.cs` (+tests), dep `ExcelDataReader` | Delete | Solo CSV |
| `Models/SourceRow.SheetName`, `TableMappingConfig.SourceSheet`, `NormalizationEngine.FilterBySheet` | Delete | Eran de Excel multi-hoja |
| `Services/CsvParserService.cs` | Modify | Lectura por rango: skip `offset` (sin materializar) + take `chunkSize` |
| `PrepareImportPlugin.cs` | Modify | Cuenta filas + valida headers + resetea (sin staging) |
| `ProcessChunkPlugin.cs` | Modify | Descarga + parse streaming + skip; ya no lee staging |
| `Services/UpsertWriter.cs`, `Models/ProcessingResult.cs` | Modify | Clasifica faults (Transient vs TargetData); categoría en el resultado |
| `Services/NormalizationEngine.cs` | Modify | Mapear excepciones a categorías (Transform/Type/Validation/Lookup) |
| `ImportContextLoader` (lee `chunksize`), `FileDownloader`, `DependencyResolver`, `TypeCoercer`, `TransformEngine` | Reuse | Ajustes menores |

## Interfaces

```csharp
public interface ICsvReader {
    int CountRows(byte[] csv, bool hasHeader, string delimiter);            // PrepareImport
    IReadOnlyList<SourceRow> ReadRange(byte[] csv, bool hasHeader,
        string delimiter, int skip, int take);                             // ProcessChunk (streaming skip)
}

public enum ErrorCategory {
    SourceError, TransformError, TypeError, ValidationError,
    LookupUnresolved, TargetData, Transient
}
// UpsertWriter: clasifica OrganizationServiceFault.ErrorCode dentro del plugin → Transient (SQL
// deadlock/timeout/lock) | TargetData. OJO: service protection (-2147015902/-903/-898, 429) NO aplica
// a operaciones de plugin (exento) → lo cubre el retry policy de PA a nivel de la invocación ProcessChunk.
```

## Testing Strategy

| Capa | Qué | Cómo |
|------|-----|------|
| Unit | `CsvReader.ReadRange` (skip/take, borde offset>total, header) | bytes CSV en memoria; assert filas/rownumbers |
| Unit | `CsvReader.CountRows` | idem |
| Unit | `UpsertWriter` clasifica fault transitorio vs dato | mock service devuelve fault con ErrorCode; assert categoría / re-lanza |
| Unit | `NormalizationEngine` mapea excepción → categoría | inyectar fila que rompe coerción/transform |
| Unit | `ProcessChunkPlugin` avanza offset y reanuda | NSubstitute (ya cubierto, ajustar a sin-staging) |

Strict TDD: test primero. Lo nuevo (CsvReader streaming+skip, clasificación de errores) NO tiene tests aún.

## Migration / Rollout

Aditivo en esquema (campos nuevos) + eliminación de la tabla staging (sin datos de negocio). Desregistrar
nada del v1 que dependa de staging. Alternate keys (destino + log) deben estar activas antes del primer run.

## Resolved Decisions (cierre de open questions)

### Service protection NO aplica dentro del plugin (verificado, Microsoft Learn)
Los upserts del `UpsertWriter` corren dentro del sandbox del plugin y están **exentos** de service
protection — los códigos `-2147015902`/`-903`/`-898` (hex `0x80072322`/`21`/`26`) **no aparecen ahí**.
- **Service protection** (429 a nivel de invocación PA→ProcessChunk) → lo maneja el **retry policy de PA**.
- **Transient dentro del plugin** = SQL **deadlock / timeout / lock** (los que SÍ ocurren en escritura
  masiva concurrente) → re-lanzar → PA reintenta.
Fuente: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/api-limits

### chunkSize: default 1000, rango 100–10000
Default conservador 1000; el operador calibra por template midiendo. Acotar fuera de rango al default.

### CountRows en PrepareImport
Se mantiene (una pasada de lectura barata) por la UX de progreso (X de Y) y para `totalBatches`.

### Transient interno: DIFERIDO al v1 (justificado por procesamiento secuencial)
El flow procesa los chunks **secuencialmente** (`Do until`, uno por vez) → sin concurrencia de
escritura → deadlocks/timeouts raros. Por eso:
- El enum `ErrorCategory` **incluye** `Transient` (contrato estable), pero en v1 los faults de escritura
  se clasifican como `TargetData` + log + seguir; los fallos de chunk completo (excepción) los reintenta PA.
- La detección fina de códigos SQL transitorios se agrega **solo si la medición en carga muestra**
  deadlocks/timeouts reales (evitar optimización prematura).

## Open Questions

- Ninguna. Todas resueltas (ver arriba). Plan v2 listo para implementación.
