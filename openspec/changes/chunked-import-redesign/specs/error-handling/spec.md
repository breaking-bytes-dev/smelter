# Error Handling Specification

## Purpose

Dar trazabilidad de errores a nivel fila sin write-amplification: loguear solo las filas fallidas, de
forma idempotente, categorizadas por origen, y distinguir errores transitorios (reintentables) de
errores de dato (definitivos).

## Requirements

### Requirement: Log solo de filas fallidas

El sistema MUST registrar en Import Job Log únicamente las filas que fallan; las filas exitosas
MUST NOT generar un registro de log. Los contadores agregados (éxito/fallo/omitido) se mantienen en el job.

#### Scenario: Import con algunas fallas

- GIVEN un chunk de 1000 filas donde 3 fallan
- WHEN se procesa el chunk
- THEN el sistema SHALL crear 3 registros de Import Job Log (uno por fila fallida)
- AND MUST NOT crear log para las 997 exitosas

### Requirement: Log idempotente

El sistema MUST escribir los logs de fallidas vía upsert por una alternate key
(`importjobid` + `rownumber` + `targettable`), de modo que reanudar/reprocesar no duplique logs.

#### Scenario: Re-proceso de un chunk no duplica logs

- GIVEN una fila que ya fue logueada como fallida en un intento previo
- WHEN el chunk se re-procesa (resume) y vuelve a fallar igual
- THEN el sistema SHALL actualizar el log existente (no crear uno nuevo)

### Requirement: Categorización de errores por origen

Cada fila fallida MUST registrar una categoría de error que identifique su etapa de origen: SourceError,
TransformError, TypeError, ValidationError, LookupUnresolved, TargetData o Transient. El job SHALL
resumir los conteos por categoría en `errorsummary`.

#### Scenario: Error de coerción de tipo

- GIVEN una fila con `"abc"` en una columna Integer
- WHEN se procesa
- THEN el log de esa fila SHALL tener categoría `TypeError`

#### Scenario: Lookup padre no resuelto

- GIVEN una fila hija cuyo padre (por alternate key) no existe
- WHEN se procesa
- THEN el log de esa fila SHALL tener categoría `LookupUnresolved`

#### Scenario: Resumen por categoría

- GIVEN un import con 23 TypeError y 5 LookupUnresolved
- WHEN finaliza
- THEN `errorsummary` SHALL reflejar los conteos agrupados por categoría

### Requirement: Distinción transitorio vs dato y retry

El sistema MUST clasificar los faults de Dataverse: los transitorios (límites de service protection,
HTTP 429) como `Transient` y el resto como `TargetData`. Ante al menos un `Transient` en un chunk, el
sistema SHALL re-lanzar la excepción para que la retry policy de Power Automate reintente el chunk
(seguro por idempotencia). Los errores no transitorios SHALL loguearse y el procesamiento continúa.

#### Scenario: Throttling transitorio se reintenta

- GIVEN un chunk donde Dataverse devuelve un fault de service protection en alguna operación
- WHEN se procesa el chunk
- THEN el sistema SHALL re-lanzar la excepción (no marcar esas filas como fallidas definitivas)
- AND Power Automate SHALL reintentar el chunk con backoff

#### Scenario: Error de dato no se reintenta

- GIVEN un chunk con una fila rechazada por dato inválido (no transitorio)
- WHEN se procesa el chunk
- THEN el sistema SHALL loguear esa fila (categoría `TargetData`) y continuar
- AND MUST NOT re-lanzar por ese error

#### Scenario: Error fatal marca el job Failed

- GIVEN un fallo no recuperable (p. ej. configuración inválida) que agota los reintentos
- WHEN el flujo no puede completar el chunk
- THEN el job SHALL quedar en estado Failed con el detalle del error
