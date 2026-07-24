[← Volver al índice de documentación](../../README.md)

# Import Job Log — `sanic_tbl_smlt_importjoblog`

Entidad de runtime — log detallado de filas **fallidas** por ejecución. Solo se registran las filas que
fallaron; las filas exitosas y omitidas se contabilizan en los contadores del Import Job, no aquí.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Hijo de | [Import Job](import-job.md) | N:1 | `sanic_smlt_ijl_id` (vía alternate key) |

## Columnas

| Columna | Tipo | Requerido | Descripción |
|---------|------|-----------|-------------|
| `sanic_smlt_ijl_importjoblogid` | GUID (PK) | Auto | Clave primaria |
| `sanic_smlt_ijl_id` | Lookup | Sí | FK → Import Job |
| `sanic_smlt_ijl_rownumber` | Integer | Sí | Número de fila en el CSV fuente |
| `sanic_smlt_ijl_targettable` | String(100) | Sí | Logical name de la tabla destino donde falló la operación |
| `sanic_smlt_ijl_status` | OptionSet | Sí | Resultado de la operación |
| `sanic_smlt_ijl_errormessage` | Multiline | No | Detalle del error (nulo en caso de éxito) |
| `sanic_smlt_ijl_errorcategory` | OptionSet | No | Categoría de error — etapa del pipeline donde ocurrió el fallo |

## OptionSet — `sanic_smlt_ijl_status`

| Valor | Label | Descripción |
|-------|-------|-------------|
| 0 | Created | Registro nuevo creado |
| 1 | Updated | Registro existente actualizado (dedup match + updateIfExists) |
| 2 | Skipped | Match de dedup encontrado, updateIfExists = false |
| 3 | Failed | Error durante create/update |

## OptionSet — `sanic_smlt_ijl_errorcategory`

Indica en qué etapa del pipeline ocurrió el fallo. Facilita el diagnóstico sin necesidad de leer el stack trace.

| Valor | Label | Origen |
|-------|-------|--------|
| 0 | SourceError | Fila malformada o columnas faltantes en el CSV |
| 1 | TransformError | Fallo en el DSL: función desconocida, error de sintaxis |
| 2 | TypeError | `TypeCoercer` no pudo convertir el valor al tipo destino |
| 3 | ValidationError | Campo requerido vacío o validación de negocio |
| 4 | LookupUnresolved | Lookup padre o externo no encontrado en Dataverse |
| 5 | TargetData | Dataverse rechazó la escritura (constraint, privilegio, duplicado) |
| 6 | Transient | Fallo transitorio de BD (deadlock/timeout/lock). En v1 se clasifica como TargetData. |

## Alternate Key (idempotencia de logs)

El Import Job Log tiene una **alternate key** compuesta por:

| Columna | Descripción |
|---------|-------------|
| `sanic_smlt_ijl_id` | Import Job (lookup) |
| `sanic_smlt_ijl_rownumber` | Número de fila en el CSV |
| `sanic_smlt_ijl_targettable` | Tabla destino |

Esta clave garantiza que si un chunk se re-procesa (retry del Cloud Flow), el log de la misma fila se
actualiza en lugar de duplicarse. El plugin usa `UpsertRequest` con esta alternate key para escribir los
logs, lo que hace que la operación sea idempotente.

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
