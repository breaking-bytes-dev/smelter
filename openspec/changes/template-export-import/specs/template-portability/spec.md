# Template Portability Specification

## Purpose

Exportar un Import Template (con sus Table/Column/Relationship Mappings) a un JSON portable e importarlo en
otro ambiente, remapeando los GUIDs internos y haciendo upsert por nombre. Exporta CONFIG, no esquema.

## Requirements

### Requirement: Export del árbol completo a JSON

El sistema MUST serializar un Import Template y todo su árbol (Table Mappings → Column Mappings +
Relationship Mappings) a un único JSON, donde cada Table Mapping lleva un **`ref` estable** que reemplaza
a su GUID, y los Relationship Mappings referencian `parentRef`/`childRef` (no GUIDs). El JSON SHALL incluir
un `formatVersion`.

#### Scenario: Export de un template con relaciones

- GIVEN un template con 2 table mappings (padre, hijo), sus column mappings y 1 relationship mapping
- WHEN se invoca ExportTemplate con su Id
- THEN el JSON SHALL contener el template, ambos table mappings con sus `ref`, sus column mappings anidados
- AND el relationship mapping SHALL referenciar `parentRef`/`childRef` que coinciden con esos `ref`
- AND NO SHALL incluir GUIDs como vínculo entre table y relationship mappings

#### Scenario: Refs únicos

- GIVEN dos table mappings con el mismo nombre
- WHEN se exporta
- THEN cada uno SHALL recibir un `ref` único (sin colisión)

### Requirement: Validación del JSON en import

El sistema MUST validar el JSON antes de aplicar cambios: bien formado, `formatVersion` soportada, y `ref`
referenciados por los relationship mappings existentes entre los table mappings. Ante JSON inválido o
versión no soportada, MUST abortar sin modificar datos y devolver un mensaje claro.

#### Scenario: JSON mal formado

- GIVEN un string que no es JSON válido
- WHEN se invoca ImportTemplate
- THEN el sistema SHALL fallar con un mensaje claro
- AND MUST NOT crear ni modificar ningún registro

#### Scenario: formatVersion no soportada

- GIVEN un JSON con `formatVersion` mayor a la soportada
- WHEN se importa
- THEN el sistema SHALL rechazar el import indicando la versión esperada

#### Scenario: parentRef inexistente

- GIVEN un relationship mapping cuyo `parentRef` no coincide con ningún table mapping del JSON
- WHEN se valida
- THEN el sistema SHALL abortar (o reportar Warning) identificando el ref huérfano, sin dejar el template a medias

### Requirement: Remapeo de GUIDs en import

El sistema MUST recrear los Table Mappings (con GUIDs nuevos del ambiente destino), construir un mapa
`ref → GUID-nuevo`, y crear los Relationship Mappings resolviendo `parentRef`/`childRef` a esos GUIDs nuevos.

#### Scenario: Relaciones recableadas en destino

- GIVEN un JSON exportado de otro ambiente
- WHEN se importa en un ambiente limpio
- THEN los Relationship Mappings resultantes SHALL apuntar a los Table Mappings recién creados (GUIDs del destino)
- AND el orden padre-hijo SHALL quedar resoluble por el DAG

### Requirement: Upsert por nombre e idempotencia

El sistema MUST hacer match del template por **nombre**: si existe, SHALL borrar sus mappings previos y
recrearlos desde el JSON; si no existe, SHALL crearlo. Re-importar el mismo JSON SHALL dejar el destino
equivalente (sin duplicar).

#### Scenario: Import en ambiente limpio crea

- GIVEN un nombre de template que no existe en destino
- WHEN se importa
- THEN el sistema SHALL crear el template y todo su árbol

#### Scenario: Re-import actualiza sin duplicar

- GIVEN un template ya importado
- WHEN se re-importa el mismo JSON
- THEN el sistema SHALL reemplazar los mappings (no acumular duplicados)
- AND el resultado SHALL ser equivalente al primer import

### Requirement: No exporta esquema; tabla destino faltante es Warning

El JSON MUST contener solo CONFIG (no crea tablas ni alternate keys). Si un `targetTable` referenciado no
existe en el ambiente destino, el import SHALL completar el template pero devolver un **Warning** que liste
las tablas faltantes (no aborta todo por eso).

#### Scenario: Tabla destino ausente

- GIVEN un JSON cuyo table mapping referencia `sanic_x` que no existe en destino
- WHEN se importa
- THEN el sistema SHALL importar el template
- AND SHALL devolver un Warning indicando que `sanic_x` no existe (crear tabla + alt key antes de cargar datos)
