[← Volver al índice de documentación](../../README.md)

# Import Job — `sanic_tbl_smlt_importjob`

Entidad de runtime — un registro por ejecución de import. Almacena el archivo CSV fuente, los contadores
de progreso, el offset actual y el estado del job.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Hijo de | [Import Template](import-template.md) | N:1 | `sanic_smlt_ij_importtemplateid` |
| Padre de | [Import Job Log](import-job-log.md) | 1:N | `sanic_smlt_ijl_importjobid` |

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
| `sanic_smlt_ij_skippedcount` | Integer | No | Filas omitidas por los flags del Table Mapping (desde 1.3.0.0: existe + `UpdateIfExists` = No, o no existe + `CreateIfNotExists` = No). Las filas Skipped se cuentan pero no se loguean. |
| `sanic_smlt_ij_processingbatch` | Integer | No | **Offset actual** — número de filas ya procesadas; usado por `ProcessChunk` como punto de reanudación. Avanza después de cada chunk. |
| `sanic_smlt_ij_starttime` | DateTime | No | Inicio del procesamiento (seteado por `PrepareImport`) |
| `sanic_smlt_ij_endtime` | DateTime | No | Fin del procesamiento (seteado por `ProcessChunk` al completar) |
| `sanic_smlt_ij_isdryrun` | Boolean | No | Si es `true`, valida parseo, tipos y transforms de todas las filas **sin escribir** en Dataverse ni simular omitidos (Skipped) o claves existentes |
| `sanic_smlt_ij_failurereason` | Multiline (4000) | No | **Motivo de la falla** (desde 1.0.11). Lo escribe el handler `Failed` del Cloud Flow con el mensaje de la acción que falló (`PrepareImport`/`ProcessChunk`, o un texto genérico que remite al historial del flujo). Mensajes PII-free (excepciones de plugin, sin valores del CSV); truncado a 4000. Vacío si el job no falló |
| `sanic_smlt_ij_bytecursor` | Decimal (sin form) | No | Byte del próximo registro sin leer; lo gobierna el motor, no está en el form |
| `ownerid` | Owner (user/team) | Auto | Owner del job. Si el template referenciado es owned por un **team**, `AssignJobOwnerPlugin` (step sync pre-operation del Create) lo setea a ese mismo team; si no, queda el default de la plataforma (el creador). Ver [Ownership](#ownership). |

## Form (desde 1.0.11)

- **Solo lectura** en el main form (`disabled`): Status, Total/Processed Rows, Success/Skipped/Fail Count,
  Processing Batch, Starttime/Endtime y Failure Reason. Es UX, no seguridad: el motor y el Cloud Flow escriben por API.
  Editables: Name, Import Template, Source File e Is Dry Run.
- Pestaña **Logs de importación**: subgrid de los Import Job Logs del job (vista default del log).
- El Cloud Flow, ante una falla, setea `status = 4` **y** `failurereason` (ver [Monitoreo](../../05-user-guide/monitoring-and-retry.md)).

## Ownership

La tabla es user/team-owned. Al **crear** un job, el step `AssignJobOwnerPlugin` (sync, Pre-operation)
resuelve el owner del Import Template referenciado:

- **Template owned por un team** → el job nace owned por ese MISMO team. Cualquier miembro del team
  (rol Smelter Operador, Read/Write Basic sobre Import Job) ve y opera los jobs creados por cualquier
  otro miembro. Un `ownerid` explícito en el payload del Create se pisa — la regla es determinista.
- **Template owned por un user** (o job sin template) → el owner queda en el default de la plataforma:
  el usuario creador.

Detalle completo del modelo en [Seguridad y roles](../../04-admin-guide/security-and-roles.md).

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

### Retry por fila — no existe (campo `IsRetry` eliminado)

El campo `retry` a nivel de job fue **eliminado del schema en 1.3.0.0** — nunca estuvo implementado
y ningún componente del motor lo leía.

**No existe el concepto de "reprocesar solo las filas fallidas"**: el reintento es re-correr el
import completo (volver el job a `Queued`), que es seguro por la idempotencia del upsert. Las filas
con error se registran en `ImportJobLog` para revisión manual; si se necesita reintentar solo un
subset, la alternativa es corregir el CSV fuente y re-correr un import con ese subset.

## Trigger del Cloud Flow

El Cloud Flow se dispara con el trigger **"When a row is added or modified"** sobre `sanic_tbl_smlt_importjob`,
filtrado por `sanic_smlt_ij_status = 0` (Queued). Ver [Cloud Flows](../cloud-flows.md).

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
