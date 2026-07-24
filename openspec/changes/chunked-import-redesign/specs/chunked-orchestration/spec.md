# Chunked Orchestration Specification

## Purpose

Procesar un import CSV en invocaciones cortas e independientes orquestadas por Power Automate, sin tabla
de staging: cada invocación parsea el archivo en streaming, salta al offset y procesa una tajada, de modo
que ninguna invocación supere los 2 minutos del sandbox ni acumule plugin depth.

## Requirements

### Requirement: Preparación liviana sin staging

El sistema MUST preparar el import sin materializar las filas en una tabla intermedia. `PrepareImport`
SHALL contar las filas de datos, validar los headers y reiniciar el estado del job.

#### Scenario: Preparación exitosa

- GIVEN un Import Job Queued con un archivo CSV de N filas
- WHEN se invoca `PrepareImport`
- THEN el sistema SHALL registrar `TotalRows = N`, poner offset=0 y contadores en 0, y status Processing
- AND MUST NOT escribir filas en ninguna tabla de staging

#### Scenario: Header inválido falla temprano

- GIVEN un CSV al que le falta una columna mapeada como `SourceColumn`
- WHEN se invoca `PrepareImport`
- THEN el sistema SHALL fallar con un mensaje claro indicando la columna faltante
- AND MUST NOT iniciar el procesamiento

### Requirement: Procesamiento por chunk con parse en streaming + skip

El sistema MUST procesar las filas en lotes; cada `ProcessChunk` SHALL descargar el CSV, parsearlo en
streaming saltando `offset` filas de datos, y procesar hasta `chunkSize` filas. El estado (offset,
contadores) SHALL persistirse en el Import Job — nunca en memoria entre invocaciones.

#### Scenario: Una invocación procesa una tajada y avanza el offset

- GIVEN un Import Job con offset = 2000 y 100k filas en el archivo
- WHEN se invoca `ProcessChunk` con chunkSize 1000
- THEN el sistema SHALL saltar 2000 filas, procesar las siguientes 1000 (orden DAG, upsert)
- AND SHALL actualizar offset = 3000 y los contadores DESPUÉS de procesar
- AND SHALL devolver `HasMore = true`

#### Scenario: Última invocación marca completo

- GIVEN un Import Job cuyo offset alcanza el total de filas
- WHEN se invoca `ProcessChunk`
- THEN el sistema SHALL marcar el job Completed (o CompletedWithErrors si hubo fallos)
- AND SHALL devolver `HasMore = false`

### Requirement: chunkSize configurable por template

El sistema SHALL leer el `chunkSize` de la configuración del Import Template (no del flow), acotado a un
rango válido, con un default si no está definido.

#### Scenario: Chunk tomado del template

- GIVEN un Import Template con `chunkSize = 4000`
- WHEN se procesa un import de ese template
- THEN cada `ProcessChunk` SHALL procesar hasta 4000 filas por invocación

#### Scenario: Valor fuera de rango o ausente

- GIVEN un template sin `chunkSize` o con un valor fuera del rango permitido
- WHEN se procesa el import
- THEN el sistema SHALL usar el default (acotado al rango válido)

### Requirement: Aislamiento de depth/timeout y resume

Cada `ProcessChunk` MUST ejecutarse como una operación independiente (depth reiniciado, 2 min frescos).
Como el offset se persiste **después** de procesar y el upsert es idempotente, reanudar tras una caída
MUST retomar desde el último chunk completado sin duplicar ni perder filas.

#### Scenario: Gran volumen sin tocar el techo de depth

- GIVEN un import de ~100k filas CSV
- WHEN el Cloud Flow itera `ProcessChunk` hasta `HasMore = false`
- THEN el sistema MUST completar sin abortar por depth ni timeout

#### Scenario: Reanudación tras caída

- GIVEN un import interrumpido con offset persistido en 5000
- WHEN se re-dispara el procesamiento
- THEN el sistema SHALL continuar desde la fila 5001
- AND el chunk que estaba en curso al caer SHALL re-procesarse sin duplicar (upsert idempotente)

### Requirement: Orquestación por Cloud Flow

El sistema SHALL disparar el procesamiento mediante un Cloud Flow ante la creación de un Import Job
(status Queued), que invoque `PrepareImport` una vez y luego `ProcessChunk` en bucle hasta completar.

#### Scenario: Disparo desde Canvas App

- GIVEN un usuario que crea un Import Job con archivo desde la Canvas App
- WHEN el Import Job se crea con status Queued
- THEN el Cloud Flow SHALL ejecutar `PrepareImport` y luego el bucle de `ProcessChunk`
- AND el progreso (procesadas / TotalRows) SHALL ser observable en el Import Job mientras corre
