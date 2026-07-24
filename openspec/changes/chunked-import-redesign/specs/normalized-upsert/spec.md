# Normalized Upsert Specification

## Purpose

Delegar deduplicación y resolución de lookups a la plataforma vía Alternate Keys + Upsert, y
garantizar que la normalización padre-hijo efectivamente ocurra (corrige el bug donde las relaciones
nunca se cargaban).

## Requirements

### Requirement: Carga de relationship mappings

El sistema MUST cargar los Relationship Mappings del template antes de procesar, de modo que el orden
de dependencias y los lookups padre-hijo se resuelvan. (Corrige #1: hoy nunca se consultan.)

#### Scenario: Relaciones cargadas y orden topológico aplicado

- GIVEN un template con tablas Country → State → City y sus relationship mappings
- WHEN se procesa el import
- THEN el sistema SHALL procesar las tablas en orden de dependencia (padres antes que hijos)
- AND el orden SHALL derivarse de los relationship mappings cargados

### Requirement: Upsert idempotente por Alternate Key

El sistema MUST crear o actualizar registros mediante upsert por una clave alternativa configurada,
de forma que reprocesar el mismo archivo no genere duplicados.

#### Scenario: Reprocesamiento no duplica

- GIVEN una tabla destino con alternate key y un archivo ya importado
- WHEN se reprocesa el mismo archivo
- THEN el sistema MUST NOT crear registros duplicados
- AND SHALL actualizar los existentes si sus valores cambiaron

#### Scenario: Sin alternate key disponible

- GIVEN una tabla destino sin alternate key configurada
- WHEN se procesa un lote
- THEN el sistema SHALL usar el fallback (CreateMultiple/UpdateMultiple separados por existencia)
- AND SHALL registrar la degradación en el log del job

### Requirement: Resolución de lookups padre sin GUID

El sistema MUST resolver los lookups hacia registros padre por valor de negocio usando
`EntityReference(tabla, alternateKey, valor)`, sin requerir el GUID del padre.

#### Scenario: Hijo referencia a padre por clave de negocio

- GIVEN una fila hija cuyo padre se identifica por un valor de clave alternativa (p.ej. country code)
- WHEN se construye el registro hijo
- THEN el lookup SHALL setearse como `EntityReference(tablaPadre, claveAlt, valor)`
- AND Dataverse SHALL resolver el GUID del padre en el upsert

### Requirement: Operaciones en bloque

El sistema SHALL enviar las operaciones en lotes de hasta 1000 registros por mensaje
(`CreateMultiple`/`UpdateMultiple`/`UpsertMultiple`) para minimizar el conteo de API requests.

#### Scenario: Lote enviado en una sola operación múltiple

- GIVEN un lote de 1000 registros válidos
- WHEN se envían a Dataverse
- THEN el sistema SHALL usar una operación múltiple (no 1000 requests individuales)
