# Smelter Access Control Specification

## Purpose

Separar configuración/operación con dos roles vía SDK, gatear Custom APIs por privilegio, restringir la MDA
al Configurador, propagar acceso vía cascade, y validar en runtime el acceso al template. El motor sigue
SYSTEM.

## Requirements

### Requirement: Rol Smelter Configurador

MUST otorgar CRUD Org (Create/Read/Write/Delete/Append/AppendTo) sobre las 6 tablas `sanic_tbl_smlt_*`
(Template, Table/Column/Relationship Mapping, Import Job, Import Job Log). MUST crearse vía
`tools/Smelter.Deploy` (SDK) como solution component de `sanic`.

#### Scenario: Configurador con CRUD total

- GIVEN un usuario con rol Configurador
- WHEN opera cualquier registro de las 6 tablas, sin importar el owner
- THEN SHALL permitirse y el rol SHALL figurar en `sanic` como solution component

### Requirement: Rol Smelter Operador

MUST otorgar: Template → Read User + AppendTo; Table/Column/Relationship Mapping → Read Org sin
Create/Write/Delete; Import Job → Create Org + Read/Write User; Import Job Log → Read User. MUST crearse vía
SDK como solution component.

#### Scenario: Operador bloqueado en write, habilitado en lectura y jobs

- GIVEN un operador sin Configurador, con un template visible
- WHEN intenta editar ese Template/mapping, y luego lee un mapping, crea un Import Job y lee/edita ese job y su log
- THEN lo primero SHALL denegarse por privilegios y lo segundo SHALL permitirse

### Requirement: MDA restringida al Configurador

La app model-driven de Smelter MUST asociarse solo al rol Configurador (`AppModuleRoles`), vía SDK.

#### Scenario: Operador no ve la app, Configurador sí

- GIVEN un Operador y un Configurador
- WHEN cada uno lista sus apps disponibles
- THEN Smelter MUST NOT aparecer para el Operador y SHALL aparecer para el Configurador

### Requirement: Cascade share/reparent Template→ImportJob

La relación Import Job→Template (`sanic_smlt_ij_importtemplateid`) MUST tener `CascadeShare`=`Cascade` y
`CascadeReparent`=`Cascade` (`UpdateRelationshipRequest`) para que los jobs hereden el acceso del team del
template.

#### Scenario: Job hereda acceso del team; sin share no propaga

- GIVEN un template compartido con Team A (O1/O2 miembros) y otro sin compartir con ningún team
- WHEN O1 crea un Import Job bajo cada uno
- THEN O2 SHALL ver el primer job; el otro SHALL crearse sin error y sin herencia extra

### Requirement: Gating de Custom APIs por ExecutePrivilegeName

Las 4 Custom APIs MUST declarar `ExecutePrivilegeName` reutilizando privilegios existentes:
`exporttemplate`/`importtemplate` → `prvWritesanic_tbl_smlt_importtemplate`; `prepareimport`/`processchunk`
→ `prvCreatesanic_tbl_smlt_importjob`.

#### Scenario: Operador bloqueado en export/import, habilitado en prepare/process

- GIVEN un operador sin Write sobre Template pero con Create sobre Import Job
- WHEN invoca `exporttemplate`/`importtemplate` y luego `prepareimport`/`processchunk`
- THEN lo primero SHALL fallar por privilegio y lo segundo SHALL ejecutarse

### Requirement: Validación de acceso al template en PrepareImportPlugin

`PrepareImportPlugin` MUST validar (`RetrievePrincipalAccessRequest`) que `InitiatingUserId` tenga Read
sobre el Template del job antes de prepararlo. Sin acceso, MUST lanzar `InvalidPluginExecutionException`
con mensaje claro. ProcessChunk/downstream SHALL seguir como SYSTEM.

#### Scenario: Sin acceso es rechazado, con acceso es aceptado

- GIVEN job A: template NO compartido con ningún team del iniciador; job B: template sí compartido
- WHEN se ejecuta `PrepareImportPlugin` para cada uno
- THEN A SHALL lanzar `InvalidPluginExecutionException` (no se prepara) y B SHALL continuar normalmente

### Requirement: Provisioning idempotente

`tools/Smelter.Deploy` MUST ser idempotente: re-ejecutarlo MUST NOT duplicar roles ni romper la config
(roles, privilegios, app-rol, cascade). `tools/Smelter.Verify` MUST verificar ese estado post-deploy.

#### Scenario: Segunda corrida no duplica y Verify confirma

- GIVEN un ambiente ya provisionado
- WHEN corre `Smelter.Deploy` de nuevo y luego `Smelter.Verify`
- THEN NO SHALL haber rol duplicado y Verify SHALL confirmar roles/privilegios/app-rol/cascade OK

### Requirement: Ownership de templates por team

Un operador SHALL ver y disparar imports solo sobre templates de sus equipos; un configurador SHALL operar
todos.

#### Scenario: Operador acotado, Configurador sin límite

- GIVEN templates T1 (Team A) y T2 (Team B), y un operador solo de Team A
- WHEN intenta crear un job contra T1 y contra T2
- THEN SHALL poder contra T1, MUST NOT contra T2, y un configurador SHALL poder contra ambos
