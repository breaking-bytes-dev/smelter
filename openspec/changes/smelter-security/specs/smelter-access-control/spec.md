# Smelter Access Control Specification

## Purpose

Separar configuración de operación mediante dos roles de seguridad, controlar la visibilidad de templates
por ownership de equipo, y compartir la visibilidad de Import Jobs/logs entre operadores del mismo template
mediante auto-asignación al Owner Team. El procesamiento corre como SYSTEM y no se ve afectado.

## Requirements

### Requirement: Rol Configurador

El rol **Smelter Configurador** MUST otorgar CRUD a nivel Organización sobre Import Template, Table Mapping,
Column Mapping y Relationship Mapping, y lectura de todos los Import Jobs y logs.

#### Scenario: Configurador crea y ve todo

- GIVEN un usuario con rol Smelter Configurador
- WHEN abre Smelter
- THEN SHALL poder crear/editar/eliminar templates y mappings
- AND SHALL ver TODOS los templates, jobs y logs (sin importar el owner)

### Requirement: Rol Operador no configura

El rol **Smelter Operador** MUST NOT otorgar create/write/delete sobre Import Template ni sobre los mappings.
SHALL permitir leer mappings (config no sensible) pero no modificarlos.

#### Scenario: Operador no puede configurar

- GIVEN un usuario con rol Smelter Operador (sin Configurador)
- WHEN intenta crear o editar un template o un mapping
- THEN la operación SHALL ser denegada (botón/acción bloqueada o error de privilegios)

### Requirement: Operador opera y ve logs

El rol Operador MUST permitir crear Import Jobs (incluyendo subir el archivo y vincular el template) y leer
los Import Job Logs de los jobs visibles.

#### Scenario: Operador carga datos

- GIVEN un operador con un template asignado
- WHEN crea un Import Job, sube el CSV y lo ejecuta
- THEN el job SHALL procesarse
- AND el operador SHALL poder ver los logs de ese job

### Requirement: Visibilidad de templates por team ownership

Un operador SHALL ver únicamente los templates propiedad de los equipos a los que pertenece. Un configurador
SHALL ver todos los templates.

#### Scenario: Operador ve solo lo asignado

- GIVEN templates T1 (Team A) y T2 (Team B), y un operador miembro solo de Team A
- WHEN el operador lista templates
- THEN SHALL ver T1
- AND MUST NOT ver T2

#### Scenario: Configurador ve todos

- GIVEN los mismos T1 y T2
- WHEN un configurador lista templates
- THEN SHALL ver T1 y T2

### Requirement: Jobs heredan el acceso del template (cascade sharing)

Los Import Jobs creados bajo un template SHALL ser visibles para los miembros del team con el que el template
está compartido, aunque el job lo haya creado otro operador. Esto se logra con **cascade sharing** en la
relación Template→Import Job (los hijos heredan el acceso del padre). No requiere código.

#### Scenario: Job visible para el equipo

- GIVEN un template compartido con Team A (relación con Share/Reparent = Cascade All), y operadores O1 y O2 miembros de Team A
- WHEN O1 crea un Import Job contra ese template
- THEN O2 SHALL poder ver ese job y sus logs (acceso heredado del template)

#### Scenario: Job de template no asignado no es visible

- GIVEN un template compartido solo con Team B, y un operador miembro únicamente de Team A
- WHEN se crea un job bajo ese template
- THEN ese operador MUST NOT ver el job (visibilidad acotada a los templates asignados)

#### Scenario: Template sin compartir con team

- GIVEN un job creado bajo un template no compartido con ningún team
- WHEN se crea
- THEN no hay herencia de acceso (solo el creador/owner lo ve) y la creación SHALL completarse sin error
