[← Volver al índice de documentación](../../README.md)

# Import Job — `sanic_tbl_smlt_importjob`

Entidad de runtime — un registro por ejecución de import. Almacena el archivo CSV fuente, los contadores
de progreso, el offset actual y el estado del job.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Hijo de | [Import Template](import-template.md) | N:1 | `sanic_smlt_ij_importtemplateid` |
| Padre de | [Import Job Log](import-job-log.md) | 1:N | `sanic_smlt_ijl_id` (alternate key del log) |

## Columnas

| Columna | Tipo | Requerido | Descripción |
|---------|------|-----------|-------------|
| `sanic_smlt_ij_importjobid` | GUID (PK) | Auto | Clave primaria |
| `sanic_smlt_ij_importtemplateid` | Lookup | Sí | FK → Import Template |
| `sanic_smlt_ij_name` | String(200) | Sí | Nombre descriptivo (ej. `{template} - {fecha}`) |
| `sanic_smlt_ij_sourcefile` | File | Sí | Archivo CSV fuente (máx. 128 MB) |
| `sanic_smlt_ij_status` | OptionSet | Sí | Estado del job |
| `sanic_smlt_ij_totalrows` | Integer | No | Total de filas de datos en el CSV (cargado por `PrepareImport`) |
| `sanic_smlt_ij_processedrows` | Integer | No | Filas procesadas hasta ahora (acumulado) |
| `sanic_smlt_ij_successcount` | Integer | No | Operaciones exitosas (create + update) |
| `sanic_smlt_ij_failedcount` | Integer | No | Operaciones fallidas |
| `sanic_smlt_ij_skippedcount` | Integer | No | Filas omitidas por dedup (match sin update) |
| `sanic_smlt_ij_processingbatch` | Integer | No | **Offset actual** — número de filas ya procesadas; usado por `ProcessChunk` como punto de reanudación. Avanza después de cada chunk. |
| `sanic_smlt_ij_starttime` | DateTime | No | Inicio del procesamiento (seteado por `PrepareImport`) |
| `sanic_smlt_ij_endtime` | DateTime | No | Fin del procesamiento (seteado por `ProcessChunk` al completar) |
| `sanic_smlt_ij_isdryrun` | Boolean | No | Si es `true`, simula el procesamiento sin escribir en Dataverse |
| `sanic_smlt_ij_errorsummary` | Multiline | No | Resumen de errores fatales del job (ej. excepción del flow) |

## OptionSet — `sanic_smlt_ij_status`

| Valor | Label | Descripción |
|-------|-------|-------------|
| 0 | Queued | En cola — trigger del Cloud Flow |
| 1 | Processing | En proceso — `PrepareImport` lo setea; `ProcessChunk` lo mantiene |
| 2 | Completed | Todas las filas procesadas sin errores |
| 3 | CompletedWithErrors | Completado pero con al menos una fila fallida |
| 4 | Failed | Error fatal — el flow lo setea desde el scope catch |

## Reintentos / reprocesamiento

### Cómo funciona el retry real

Smelter tiene dos mecanismos de reintento, ambos automáticos:

1. **Retry policy del Cloud Flow sobre `ProcessChunk`** (errores transitorios de chunk): si una
   invocación del plugin falla con una excepción no capturada (timeout, error transitorio de BD,
   etc.), el Cloud Flow reintenta la llamada automáticamente según su política de retry exponencial.
   Como el offset (`sanic_smlt_ij_processingbatch`) no avanzó — el fallo ocurrió antes de la
   actualización del job — el chunk se re-procesa desde el mismo punto. El upsert idempotente
   garantiza que no se dupliquen registros.

2. **Re-correr el import completo** (botón Reintentar / volver el job a status `Queued`): vuelve
   a iniciar el procesamiento desde el principio. Es seguro porque el upsert por alternate key es
   idempotente: si un registro ya existe, se actualiza; si no, se crea. No hay duplicados.

### El campo `IsRetry` / retry por fila — no implementado

El campo `retry` a nivel de job existe en el modelo de datos pero **no está implementado en v2**.
`ImportContextLoader` no lo carga, y ni `ProcessChunk` ni `NormalizationEngine` lo leen.

**No existe el concepto de "reprocesar solo las filas fallidas"** en v2. Las filas con error se
registran en `ImportJobLog` para revisión manual. Si se necesita reintentar solo una fila, la
alternativa es corregir el CSV fuente y re-correr un import con ese subset, aprovechando la
idempotencia del upsert.

## Trigger del Cloud Flow

El Cloud Flow se dispara con el trigger **"When a row is added or modified"** sobre `sanic_tbl_smlt_importjob`,
filtrado por `sanic_smlt_ij_status = 0` (Queued). Ver [Cloud Flows](../cloud-flows.md).

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
