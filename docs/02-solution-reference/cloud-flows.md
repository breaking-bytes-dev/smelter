[← Volver al índice de documentación](../README.md)

# Cloud Flows — Smelter

## Flow orquestador de imports

Nombre sugerido: **Smelter — Orchestrate Import Job**

Este es el único Cloud Flow de Smelter. Su responsabilidad es orquestar el procesamiento de un Import
Job: llama a `PrepareImport` una vez y luego itera `ProcessChunk` hasta que el job esté completo.

El flow es **agnóstico del offset** — no guarda ni interpreta el progreso. El offset (campo
`sanic_smlt_ij_processingbatch`) es mantenido exclusivamente por el plugin `ProcessChunkPlugin`.
Esto hace que el flow sea stateless y no necesite variables de iteración para el progreso.

## Trigger

| Propiedad | Valor |
|-----------|-------|
| Conector | Dataverse |
| Acción | When a row is added or modified |
| Tabla | Import Job (`sanic_tbl_smlt_importjob`) |
| Filtro de columna | `sanic_smlt_ij_status` |
| Filtro de fila | `sanic_smlt_ij_status eq 0` (Queued) |

El trigger dispara exactamente cuando un Import Job pasa al estado `Queued`. Tanto la creación del job
(Canvas App) como un eventual retry manual (cambiar status a Queued desde MDA) disparan el mismo flow.

## Estructura del Flow

```
TRIGGER: Import Job status = Queued
│
├─► [Scope: Import Execution]
│     │
│     ├─► Perform an unbound action: sanic_PrepareImport
│     │     In:  JobId = triggerOutputs()?['body/sanic_smlt_ij_importjobid']
│     │     Out: TotalRows
│     │
│     └─► Do until: outputs('ProcessChunk')?['body/HasMore'] is equal to false
│               │
│               └─► Perform an unbound action: sanic_ProcessChunk  [retry policy]
│                     In:  JobId = <mismo JobId>
│                     Out: HasMore, ProcessedSoFar
│
└─► [Scope: Error Handler]  ← catch del scope anterior
      │
      └─► Perform an unbound action: sanic_ProcessChunk  [NO — acá se marca Failed]
            Patch Import Job:
              sanic_smlt_ij_status = 4 (Failed)
```

## Retry Policy en ProcessChunk

La acción `sanic_ProcessChunk` dentro del `Do until` debe tener configurada una **retry policy exponencial**:

| Parámetro | Valor recomendado |
|-----------|------------------|
| Tipo | Exponential interval |
| Count | 4 reintentos |
| Interval | PT10S (10 segundos inicial) |
| Minimum | PT10S |
| Maximum | PT1H |

Esto cubre los escenarios de:
- **Service protection limits** (HTTP 429) en la invocación del flow hacia Dataverse — el retry
  espera y reintenta automáticamente.
- **Timeout o excepción transitoria** en el plugin — como el offset no avanzó hasta que el chunk
  se completó, el retry re-procesa el mismo rango de filas sin duplicados (idempotencia por upsert).

> **Importante**: los service protection limits (429) aplican a la invocación del Cloud Flow hacia
> Dataverse, no a las operaciones internas del plugin. Las escrituras que el plugin hace dentro del
> sandbox están exentas de service protection. Ver [Connections](connections.md).

## Concurrencia

El flow debe configurarse con **concurrency = 1** (o un valor bajo) por job. No tiene sentido procesar
el mismo job en paralelo porque el offset es una variable secuencial. Los distintos jobs son
independientes entre sí y pueden correr en paralelo sin conflicto.

## Connection Reference

El flow se conecta a Dataverse usando una **Connection Reference** vinculada al application user
dedicado de Smelter. Ver [Connections](connections.md).

## Volver a

- [Custom APIs](custom-apis.md)
- [Connections](connections.md)
- [Architecture](../01-overview/architecture.md)
