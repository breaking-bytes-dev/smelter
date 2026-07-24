# Proposal: Chunked Import Redesign (v2 — CSV-only, sin staging)

## Intent

El procesamiento corría íntegro en un plugin async (límite real **2 min**, no 2 h) con auto-continue por
status que acumulaba **plugin depth** → moría cerca de depth ~12. El v1 introdujo chunking pero apoyado
en una tabla de **staging**, y se descubrió que escribir 100k filas a staging en el `PrepareImport`
**tampoco entra en 2 min** (mismo timeout, escondido en la preparación). El v2 elimina ese cuello:
parseo CSV en streaming con skip al offset, sin staging, idempotente y reanudable.

## Scope

### In Scope
- Orquestación por **Power Automate**: `PrepareImport` (1 vez) + `Do until` de `ProcessChunk`.
- `ProcessChunk`: descarga CSV → **parse en streaming + skip al offset** → procesa la tajada (sin staging).
- `PrepareImport` liviano: cuenta filas (`TotalRows`), **valida headers**, resetea estado.
- `chunkSize` **variable por Import Template** (campo nuevo; default ~1000, rango 100–10000).
- **Upsert idempotente** por alternate key + **offset persistente** en el job (resume).
- **Log solo de filas fallidas**, idempotente (alt key + upsert) y **categorizado** por origen.
- **Retry policy** (PA) para transitorios; `Transient` re-lanza para reintentar el chunk.

### Out of Scope
- **Excel** (decisión: solo CSV; se elimina todo el soporte Excel).
- Tabla de **staging** (se elimina).
- Azure / Service Bus (stack 100% Power Platform).
- Chunk por-tabla y chunk auto-adaptativo (evoluciones futuras).

## Capabilities

### New Capabilities
- `error-handling`: categorización de errores por origen (7 categorías), log idempotente solo de fallidas, distinción transitorio/dato y retry.

### Modified Capabilities
- `chunked-orchestration`: el chunk se procesa parseando el CSV en streaming + skip (ya NO desde staging); `chunkSize` por template.
- `normalized-upsert`: sin cambios de fondo (upsert por alternate key + lookups por KeyAttributes).
- `import-staging`: **ELIMINADA** (la capability y su spec se quitan del cambio).

## Approach

Cada `ProcessChunk` es una invocación independiente (depth=1, 2 min frescos) que descarga el CSV, salta
`offset` filas en streaming, procesa `chunkSize` filas en orden DAG con upsert idempotente, y avanza el
offset en el job **después** de procesar. El resume = re-disparar (continúa desde el offset). Re-procesar
un chunk es seguro por la idempotencia. Errores: dato → log+seguir; transitorio → re-lanzar (PA reintenta);
fatal → scope-catch del flow marca `Failed`.

## Affected Areas

| Área | Impacto | Descripción |
|------|---------|-------------|
| `Services/StagingReader.cs`, `StagingPayloadSerializer.cs` (+ tests) | **Removed** | Sin staging |
| `Services/ExcelParserService.cs` (+ tests), dep `ExcelDataReader` | **Removed** | Solo CSV |
| Tabla `sanic_tbl_smlt_importstagingrow` | **Removed** | Sin staging |
| `Models/SourceRow` (`SheetName`), `TableMappingConfig` (`SourceSheet`), `NormalizationEngine` (`FilterBySheet`) | **Removed/Simplified** | Eran de Excel multi-hoja |
| `PrepareImportPlugin` | **Modified** | De "escribe staging" a "cuenta + valida headers + resetea" |
| `ProcessChunkPlugin` | **Modified** | De "lee staging" a "descarga + parse streaming + skip" |
| `Services/CsvParserService` | **Modified** | Soportar lectura en streaming + skip por offset |
| `UpsertWriter` / `ProcessingResult` | **Modified** | Clasificación de errores (transitorio vs dato) + categoría |
| Import Job Log: alt key + columna `errorcategory`; Import Template: `chunksize` | **New** | Esquema |
| `ImportContextLoader`, `FileDownloader`, `DependencyResolver` | **Reused** | Sin cambios mayores |

## Risks

| Riesgo | Prob. | Mitigación |
|--------|-------|------------|
| Re-descarga del CSV por chunk | Med | chunk grande = menos descargas; CSV liviano en bytes |
| Skip lineal (O(n²) acumulado) | Low | barato para 100k; vigilar >>100k |
| Chunk supera 2 min | Med | `chunkSize` por template, calibrado midiendo |
| Entitlement diario de API | Med | application user (pool separado); plugins exentos de service protection |

## Rollback Plan

El v1 y el código previo quedan en git. Revertir = volver al commit anterior y re-registrar lo que
corresponda. Las tablas de configuración y de negocio no se destruyen; eliminar la tabla staging es
aditivo-inverso (no borra datos de negocio). El Cloud Flow y las Custom APIs se quitan de la solución
no-publicada.

## Dependencies

- Alternate keys en tablas destino **y** en Import Job Log.
- Power Automate con conector Dataverse; connection con **application user** dedicado.

## Success Criteria

- [ ] Import de ~100k filas CSV completa sin tocar depth/timeout, ni en proceso ni en preparación.
- [ ] Reprocesar / reanudar no duplica registros ni logs (idempotencia + offset).
- [ ] Cada fila fallida queda logueada una vez, con su categoría de error.
- [ ] Headers inválidos fallan en `PrepareImport` con mensaje claro (no a mitad del proceso).
- [ ] Transitorios se reintentan automáticamente; fatales marcan el job `Failed`.
