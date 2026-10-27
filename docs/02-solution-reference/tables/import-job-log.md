[← Volver al índice de documentación](../../README.md)

# Import Job Log — `sanic_tbl_smlt_importjoblog`

Entidad de runtime — log detallado de filas **fallidas** por ejecución. Solo se registran las filas que
fallaron; las filas exitosas y omitidas se contabilizan en los contadores del Import Job, no aquí.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Hijo de | [Import Job](import-job.md) | N:1 | `sanic_smlt_ijl_importjobid` |

## Columnas

| Columna | Tipo | Requerido | Descripción |
|---------|------|-----------|-------------|
| `sanic_tbl_smlt_importjoblogid` | GUID (PK) | Auto | Clave primaria |
| `sanic_smlt_ijl_id` | Autonumber (String 16) | Auto | **Nombre primario** autonumérico (`IJL-{SEQNUM:4}`). ⚠️ NO es un lookup — confundirlo con el FK al job causó el bug de logs vacíos corregido en 1.2.1.0 |
| `sanic_smlt_ijl_importjobid` | Lookup | Sí | FK → Import Job |
| `sanic_smlt_ijl_rownumber` | Integer | Sí | Número de fila en el CSV fuente |
| `sanic_smlt_ijl_targettable` | String(255) | Sí | Logical name de la tabla destino donde falló la operación |
| `sanic_smlt_ijl_status` | OptionSet | Sí | Resultado de la operación |
| `sanic_smlt_ijl_errormessage` | Multiline | No | Detalle del error, incluyendo la columna que falló (nulo en caso de éxito) |
| `sanic_smlt_ijl_errorcategory` | OptionSet | No | Categoría de error — etapa del pipeline donde ocurrió el fallo |

## OptionSet — `sanic_smlt_ijl_status`

| Valor | Label | Descripción |
|-------|-------|-------------|
| 3 | Failed | Error durante create/update |

> **Desde 1.0.10** el choice tiene **solo `3: Failed`**: el log registra únicamente fallos. Las opciones
> `Created (0)`, `Updated (1)` y `Skipped (2)` nunca se escribían y se eliminaron del schema por SDK
> (`SchemaCleanupSpec`); esos resultados viven en los contadores del Import Job (`successcount`,
> `skippedcount`).

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

> `Transient (6)` se eliminó del choice en 1.0.10: nunca se producía (un fallo transitorio de BD se clasifica
> como `TargetData`).

## Alternate Key (idempotencia de logs)

El Import Job Log tiene la **alternate key** `sanic_smlt_ijl_key_logidempotente`, compuesta por:

| Columna | Descripción |
|---------|-------------|
| `sanic_smlt_ijl_importjobid` | Import Job (lookup) |
| `sanic_smlt_ijl_rownumber` | Número de fila en el CSV |
| `sanic_smlt_ijl_targettable` | Tabla destino |

Esta clave garantiza que si un chunk se re-procesa (retry del Cloud Flow), el log de la misma fila se
actualiza en lugar de duplicarse. El plugin usa `UpsertRequest` con esta alternate key para escribir los
logs, lo que hace que la operación sea idempotente.

> **Historia (1.2.1.0):** la key original incluía `sanic_smlt_ijl_id` — que es el nombre primario
> **autonumber**, no el lookup al job — y esta doc lo documentaba como lookup. Un autonumber generado
> en cada create jamás puede dar idempotencia, y el plugin (escrito contra esta doc) le pasaba un
> `EntityReference` → cada upsert fallaba con "Incorrect type of attribute value" y el log quedaba
> VACÍO. La key se recrea con la composición correcta vía `KeyProvisioner`
> (`tools/Smelter.Deploy/KeySpec.cs`), verificada por `KeyAssertions` y por el smoke de
> `Smelter.Verify` (fila fallida real + re-corrida idempotente).

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
