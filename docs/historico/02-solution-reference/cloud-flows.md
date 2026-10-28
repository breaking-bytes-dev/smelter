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
| Select columns (`filteringattributes`) | `sanic_smlt_ij_status` (el Create dispara siempre; el Update solo si cambia el status) |
| Concurrencia del trigger | `runs = 1` (una corrida a la vez) |
| Filtro de fila | `sanic_smlt_ij_status eq 0` (Queued) |

El trigger dispara exactamente cuando un Import Job pasa al estado `Queued`. Este es el **único camino
de ejecución**: los botones **Ejecutar** y **Reintentar** del form de la MDA no procesan nada en el
navegador, solo ponen `sanic_smlt_ij_status = 0` (Queued) y el flow hace el resto. Por eso cerrar la
pestaña no afecta al job, y un error en `PrepareImport` deja el job en `Failed` (handler del flow).

> **Identidad y acceso:** el flow corre con la identidad de su conexión, por lo que `InitiatingUserId`
> en `PrepareImport` es esa identidad y no la persona que hizo click. El acceso del operador se
> controla antes de encolar: poner el job en Queued requiere Write sobre el job y el botón verifica
> que el usuario pueda leer el Import Template. Ver `docs/04-admin-guide/security-and-roles.md`.

## Estructura del Flow

```
TRIGGER: Import Job status = Queued   (filteringattributes = status; sin límite de concurrencia)
│
├─► Perform an unbound action: sanic_PrepareImport   ← CLAIM atómico (Queued → Processing)
│     In:  JobId = triggerOutputs()?['body/sanic_smlt_ij_importjobid']
│     Out: TotalRows, Claimed
│
├─► [Condition_claimed]  outputs('Perform_an_unbound_action_2')?['body/Claimed'] == true
│     │
│     ├─ Sí ─► [Scope: Import Execution]
│     │          └─► Do until: outputs('ProcessChunk')?['body/HasMore'] is equal to false
│     │                    │
│     │                    └─► Perform an unbound action: sanic_ProcessChunk  [retry policy]
│     │                          In:  JobId = <mismo JobId>
│     │                          Out: HasMore, ProcessedSoFar
│     │
│     └─ No ─► (vacío: otra corrida es dueña del job; esta termina Succeeded sin tocarlo)
│
└─► [Update a row: Failed handler]  ← corre si Condition_claimed falla, se omite (Skipped: PrepareImport lanzó) o hace TimedOut.
                                      Si no se reclamó el job, la condición termina Succeeded y el handler NO corre
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

## Concurrencia y corridas duplicadas

Un job encolado podía disparar **varias corridas del flow en el mismo segundo** (verificado en Dev con el
historial de `flowrun`; los triggers de Dataverse son at-least-once y cualquier Update de una fila `Queued`
re-dispara). Cada corrida reseteaba el job y procesaba chunks en paralelo, y los upserts competían con
`DuplicateRecordEntityKey` (0x80060892). Lo evitan tres piezas (1.0.12, la guarda de re-encolado en 1.0.13):

1. **Select columns** del trigger = `sanic_smlt_ij_status`: los Update que no tocan el status no disparan.
2. **Sin límite de concurrencia en el trigger** (a propósito): los jobs **corren en paralelo**. Una concurrencia
   global (`runs = 1`) serializaría jobs DISTINTOS y cobranza encola varios jobs de "partes" para procesarlos a la
   vez. Los duplicados del MISMO job los neutraliza el claim atómico (punto 3); el deploy quita cualquier
   `runtimeConfiguration.concurrency` que una versión anterior haya dejado.
3. **Claim atómico en `PrepareImport`**: solo reclama el job si está `Queued` (o sin status, para llamadas
   directas a la API) y el reset a `Processing` va con `IfRowVersionMatches`. Si otra corrida ya lo tomó
   devuelve `Claimed = false` y el flow no entra al loop de `ProcessChunk`. Es la única capa que cubre también
   la entrega duplicada (at-least-once) del trigger.

4. **Guarda de re-encolado** (`ValidateQueueAccess`, 1.0.13): poner `Queued` sobre un job que está `Processing`
   se rechaza ("El Import Job se está procesando..."). Sin esto, un Update por API durante la corrida disparaba
   otra corrida cuyo claim veía `Queued` y reseteaba el job a mitad de camino. `Queued -> Queued` y el reintento
   desde estados finales siguen permitidos; el flow y `PrepareImport` nunca setean `Queued`, así que no afecta al
   pipeline del servidor.

El deploy aplica los puntos 1 y 2 (`ClientDataClaimGate`, idempotente) y el flow desempaquetado del repo ya los trae.

## Connection Reference

El flow se conecta a Dataverse usando una **Connection Reference** vinculada al application user
dedicado de Smelter. Ver [Connections](connections.md).

## Volver a

- [Custom APIs](custom-apis.md)
- [Connections](connections.md)
- [Architecture](../01-overview/architecture.md)
