[← Volver al índice de documentación](../README.md)

# Guía — Seguridad de Smelter (roles, cascade, ExecutePrivilegeName, MDA, UX)

> Implementación real del feature **smelter-security** (`openspec/changes/smelter-security/`). Todo lo
> de esta guía — roles, privilegios, cascade sharing, gating de Custom APIs, restricción de la MDA,
> vistas/ribbon/dashboard — se provisiona **por SDK** (`tools/Smelter.Deploy`), NO a mano en el portal.
> Lo único que sigue siendo manual es **operación de teams** (guía operativa de esta página): crear el
> team, agregarle operadores, y **asignarle los templates** (ownership) — eso no lo automatiza
> `Smelter.Deploy` porque depende de cómo cada organización agrupa a sus operadores (no hay una
> convención única que provisionar).
>
> **Separación de roles:** **Configurador** (crea/edita templates y mappings, ve todo) vs. **Operador**
> (solo carga datos y ve logs, viendo solo los templates de sus teams).

---

## Cómo funciona (el modelo, en 5 piezas)

1. **`tools/Smelter.Deploy`** provisiona TODO lo declarativo — roles, privilegios, message steps,
   cascade, gating de Custom APIs, restricción de la MDA, vistas, botones y dashboard — de forma
   **idempotente**: correrlo de nuevo no duplica nada (Requirement "Provisioning idempotente",
   smelter-access-control).
2. **Templates owned por un Team** → los operadores miembros los ven directamente (Read Basic cubre lo
   owned por los teams del usuario). Compartir con `GrantAccess` queda para dar acceso a teams
   *adicionales*.
3. **Ownership del Import Job heredado del team** (`AssignJobOwnerPlugin`, step sync pre-operation en
   el Create del job): si el template del job es owned por un team, el job **nace owned por ese mismo
   team** — todos los miembros ven y operan los jobs de cualquier otro miembro, con la matriz de
   privilegios tal como está (Read/Write Basic).
4. **Cascade sharing** en la relación Template→Import Job → los jobs bajo un template *compartido*
   (teams adicionales, o templates owned por un user) también **heredan** el acceso del template vía
   POA — complementa a la pieza 3 para los casos donde el ownership no aplica.
5. **`PrepareImportPlugin` valida en runtime** que quien dispara el import (`InitiatingUserId`) tenga
   Read sobre el Template — defensa en profundidad además de la matriz de privilegios (Fase 5).

> 🔑 **El motor de Smelter sigue corriendo como SYSTEM.** La seguridad no puede romper un import ya
> autorizado a arrancar — solo controla quién puede *empezar* uno (privilegios + el check de Fase 5) y
> qué ve/toca el usuario en la app.

---

## Qué provisiona `tools/Smelter.Deploy` (SecurityProvisioner)

Corre automáticamente como paso `[6]` de `tools/Smelter.Deploy` (después del upsert de las Custom
APIs), o vía el workflow de CI `backend-deploy`:

```bash
DATAVERSE_URL=... PP_CLIENT_ID=... PP_CLIENT_SECRET=... \
  dotnet run --project tools/Smelter.Deploy -c Release
```

Fuente única de verdad: `tools/Smelter.Deploy/SecuritySpec.cs` (matriz de roles/privilegios, vistas,
dashboard) + `tools/Smelter.Deploy/ApiSpec.cs` (`ExecutePrivilegeName` por Custom API). El código en
`SecurityProvisioner.cs` solo lee esa spec y hace upsert — no hay valores hardcodeados sueltos.

| Paso | Qué hace | Mensaje SDK |
|---|---|---|
| Roles + privilegios | Crea/actualiza `Smelter Configurador` y `Smelter Operador` con la matriz exacta de abajo | `role` + `AddPrivilegesRoleRequest` |
| Message step AssignJobOwner | Registra el step sync pre-operation en el Create de Import Job (ownership por team) — paso `[4b]` del deploy, spec en `StepSpec.cs`, upsert en `StepProvisioner.cs` | `sdkmessageprocessingstep` |
| Cascade Template→Job | Share=Cascade, Reparent=Cascade en la relación 1:N | `UpdateRelationshipRequest` |
| `ExecutePrivilegeName` | Gatea cada Custom API por el privilegio real que reutiliza (tabla de abajo) | update `customapi.executeprivilegename` |
| MDA → solo Configurador | Asocia Configurador; desasocia cualquier otro rol NO de plataforma. `System Administrator` y `System Customizer` los asocia Dataverse a toda MDA y no se pueden quitar (la plataforma los re-deriva al instante) — quedan siempre, pero los operadores no los tienen | `Associate`/`DisassociateRequest` |
| Vistas de Import Job | 3 vistas públicas (`savedquery`) | `Create`/`UpdateRequest` |
| Botones Ejecutar/Reintentar | RibbonDiffXml versionado (`Assets/`) + JS, importados como mini-solución | `ImportSolutionRequest` |
| Dashboard + sitemap | "Smelter Dashboard" + entrada en el sitemap de la app | `Create`/`UpdateRequest` |

---

## Matriz de privilegios (fuente: `SecuritySpec.cs`)

### Rol "Smelter Configurador" — CRUD completo a nivel Organización

| Tabla | Create | Read | Write | Delete | Append | Append To |
|-------|:--:|:--:|:--:|:--:|:--:|:--:|
| Import Template | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org |
| Table Mapping | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org |
| Column Mapping | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org |
| Relationship Mapping | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org |
| Import Job | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org |
| Import Job Log | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org | 🟢 Org |

### Rol "Smelter Operador" — sin configurar, opera sus templates

| Tabla | Create | Read | Write | Delete | Append | Append To |
|-------|:--:|:--:|:--:|:--:|:--:|:--:|
| Import Template | ⚪ | 🔵 User (Basic) | ⚪ | ⚪ | ⚪ | 🔵 User (Basic) |
| Table Mapping | ⚪ | 🟢 Org | ⚪ | ⚪ | ⚪ | ⚪ |
| Column Mapping | ⚪ | 🟢 Org | ⚪ | ⚪ | ⚪ | ⚪ |
| Relationship Mapping | ⚪ | 🟢 Org | ⚪ | ⚪ | ⚪ | ⚪ |
| Import Job | 🟢 Org | 🔵 User (Basic) | 🔵 User (Basic) | ⚪ | ⚪ | ⚪ |
| Import Job Log | ⚪ | 🔵 User (Basic) | ⚪ | ⚪ | ⚪ | ⚪ |

**Por qué cada uno** (decisión cerrada, no relitigar — ver header de `tasks.md`):
- **Import Template Read/AppendTo = User (Basic), no BU:** la visibilidad del Operador viene del
  **cascade** (Template→Job), no de compartir Business Unit. Basic + cascade es lo que hace que "ver
  templates de mi team" funcione sin depender de cómo esté organizada la BU.
- **Mappings Read = Org:** config no sensible; el Operador necesita leerlos para entender el template,
  pero sin Write no puede tocarlos.
- **Import Job Create = Org, Read/Write = Basic:** cualquier Operador con acceso al template puede
  *crear* un job; una vez creado, solo lo ve/edita quien tiene acceso. Con template owned por un team,
  el job **nace owned por ese team** (`AssignJobOwnerPlugin`) y Basic alcanza para que TODO el team lo
  vea/opere — sin depender del cascade ni de shares por registro.
- **Import Job Log Read = Basic, sin Create:** el log lo escribe el plugin como SYSTEM — el Operador
  solo necesita verlo.

---

## `ExecutePrivilegeName` — gating de las Custom APIs

Cada Custom API reutiliza un privilegio EXISTENTE de la matriz de arriba (no se crean privilegios
custom nuevos) — Requirement "Gating de Custom APIs por ExecutePrivilegeName":

| Custom API | `ExecutePrivilegeName` | Quién puede invocarla |
|---|---|---|
| `sanic_capi_smelter_exporttemplate` | `prvWritesanic_tbl_smlt_importtemplate` | Solo Configurador (Operador no tiene Write en Template) |
| `sanic_capi_smelter_importtemplate` | `prvWritesanic_tbl_smlt_importtemplate` | Solo Configurador |
| `sanic_capi_smelter_prepareimport` | `prvCreatesanic_tbl_smlt_importjob` | Configurador y Operador |
| `anic_capi_smelter_processchunk` (legacy) | `prvCreatesanic_tbl_smlt_importjob` | Configurador y Operador |
| `sanic_capi_smelter_processchunk` (Release N) | `prvCreatesanic_tbl_smlt_importjob` | Configurador y Operador |

> **Sobre las dos entradas de `processchunk`:** Release N corrige el typo histórico `anic_` → `sanic_`
> de forma **aditiva** (Fase 6, smelter-mda-ux): la API nueva se agrega, el Cloud Flow se actualiza al
> nombre correcto en el mismo deploy (`CloudFlowUpdater`), y la vieja se remueve recién en Release N+1
> (Fase 9, gateada a que `Smelter.Verify` confirme el flow migrado en verde). Nunca hay una ventana
> donde el flow apunte a una API inexistente. Ver `ProcessChunkRenameContractTests` (contrato) y
> `SecurityAssertions.AssertProcessChunkCloudFlowReferencesNewApi` (E2E).

---

## Validación runtime en `PrepareImportPlugin` (Fase 5)

> **Desde que Ejecutar/Reintentar solo encolan (Cloud Flow como único camino):** el flow corre con la
> identidad de su conexión, así que `InitiatingUserId` en `PrepareImport` es esa identidad y este check
> valida al dueño de la conexión, no a quien hizo click. El control por usuario se movió a antes de
> encolar: el botón corre con los privilegios del usuario, poner el job en Queued exige **Write** sobre
> el job (lo hace cumplir Dataverse) y, **del lado servidor**, el plugin `ValidateQueueAccessPlugin`
> (step sync Pre-operation sobre Create y sobre Update de `sanic_smlt_ij_status` del Import Job) verifica
> que `InitiatingUserId` (la persona, no la conexión del flow) pueda **leer** el Import Template del job
> cada vez que el status pasa a Queued, vía el mismo `PrincipalAccessChecker` (incluye acceso por teams).
> Sin acceso falla con "No tenés acceso al Import Template de este job: no se puede poner en cola." Cubre
> botón, API y canvas apps que crean el job directamente en Queued. No bloquea al flow: nunca setea
> Queued (solo Processing/Completed/Failed).

Además de la matriz de privilegios, `PrepareImportHandler.Run` valida — ANTES de descargar el archivo o
tocar el job — que `InitiatingUserId` (quien realmente invocó la Custom API, no el service account) tenga
**Read** access sobre el Template del job, vía `RetrievePrincipalAccessRequest`
(`src/Smelter.Plugins/Services/PrincipalAccessChecker.cs`, interfaz `IAccessChecker`). Sin acceso, lanza
`InvalidPluginExecutionException` y el import NO arranca (no se descarga el CSV, no se resetea el job).

Por qué además de la matriz: los privilegios de tabla controlan si el Operador puede *ver* el template
en la UI, pero cualquier caller autenticado con el privilegio `Create` sobre Import Job podría, en
teoría, invocar `prepareimport` pasando el `JobId` de un job ajeno. Este check cierra esa brecha
verificando el acceso real al Template referenciado por el job específico que se está preparando.

---

## Ownership del Import Job — heredado del team del template

`AssignJobOwnerPlugin` (message step **sync, Pre-operation**, Create de `sanic_tbl_smlt_importjob` —
spec en `tools/Smelter.Deploy/StepSpec.cs`, registro idempotente en `StepProvisioner.cs`) resuelve el
owner del template referenciado por el job nuevo:

- **Template owned por un team** → escribe ese team como `ownerid` del Target ANTES de que la
  plataforma persista el Create: el job **nace owned por el team**, sin `AssignRequest` posterior ni
  ventana en la que el creador sea el owner. Un `ownerid` explícito en el payload se pisa — la regla es
  determinista para que la visibilidad del equipo no dependa de cómo se creó el job.
- **Template owned por un user** (o job sin template) → no toca nada; el owner queda en el default de
  la plataforma (el creador).

Esto es lo que hace que "cualquier miembro del team ve los jobs de cualquier otro" funcione con la
matriz tal cual está: la profundidad **Basic** de Read/Write del Operador cubre los registros owned por
el usuario **y por sus teams**.

## Cascade Template → Import Job

`tools/Smelter.Deploy` setea `Share=Cascade`, `Reparent=Cascade` en la relación
`sanic_tbl_smlt_importjob_smlt_ij_importtemplateid_sanic_tbl_smlt_importtemplate` vía
`UpdateRelationshipRequest` — sin plugin, nativo de la plataforma (POA). `Reparent=Cascade` es la clave:
hace que los Import Jobs **nuevos** (creados después del share) también hereden el acceso, no solo los
que ya existían al momento de compartir. Con el ownership por team (sección anterior) cubriendo el caso
principal, el cascade queda como complemento: da visibilidad a teams **adicionales** con los que se
compartió el template, y a los jobs de templates owned por un user.

---

## MDA restringida al Configurador — qué controla y qué NO controla

`SecurityProvisioner.AssociateAppModuleRole` asocia la app model-driven de Smelter **solo** al rol
Configurador (`AppModuleRoles`). Esto controla **si la app aparece en el selector de apps** del
Operador — el Operador no la ve en su lista de apps.

> ⚠️ **Esto NO es control de acceso a registros.** `AppModuleRoles` es visibilidad de la app, no
> seguridad de tabla — un Operador sigue pudiendo abrir un Import Job por URL directa (bookmark,
> notificación, otra app/dashboard), y ahí rige la matriz de privilegios + el check de Fase 5, no la
> restricción de la MDA. Si tu organización necesita una superficie dedicada para operadores (una
> segunda app, o exponer las vistas/dashboard de Import Job en OTRA app ya visible para ellos), es
> trabajo de configuración fuera del alcance de `Smelter.Deploy` — la MDA que provisiona este repo es
> la de administración/configuración, pensada para el Configurador.

---

## Vistas, botones y dashboard (smelter-mda-ux)

Provisionados por el mismo `SecurityProvisioner.Run`:

- **3 vistas públicas de Import Job** (`savedquery`): **Jobs Activos** (Preparando/En Progreso),
  **Completados con Errores** (`failedcount` > 0), **Todos los Jobs** (sin filtro, más nuevos primero).
- **Botones de command bar Ejecutar/Reintentar**: RibbonDiffXml versionado en
  `tools/Smelter.Deploy/Assets/sanic_tbl_smlt_importjob.RibbonDiffXml.xml`, importado como mini-solución
  (`SolutionZipBuilder` + `ImportSolutionRequest`) junto con el JS de los botones
  (`Assets/sanic_smlt_importjob_actions.js`, invoca `prepareimport`/`processchunk` en loop hasta
  `HasMore=false`). El gating de quién puede ejecutar/reintentar lo hacen las Custom APIs vía
  `ExecutePrivilegeName` (server-side) — el JS no valida nada por su cuenta.
- **Dashboard "Smelter Dashboard"**: resumen de jobs por estado + jobs con errores recientes, con
  entrada propia en el sitemap de la app.

---

## Verificación post-deploy

`tools/Smelter.Verify` (workflow de CI `backend-verify`) corre `SecurityAssertions.RunAll` +
`StepAssertions.RunAll` ANTES del smoke test funcional — si la seguridad regresionó, no tiene sentido
seguir con el import de prueba. Confirma contra el ambiente real:

- Los roles Configurador/Operador existen con EXACTAMENTE los privilegios de `SecuritySpec` (incluido
  que el Operador NO tenga Create/Write/Delete en Template — proxy estático de "no puede configurar",
  ya que `Smelter.Verify` corre con un único Service Principal y no loguea un segundo usuario real).
- Cascade Share=Cascade, Reparent=Cascade en la relación Template→Job.
- `executeprivilegename` seteado en las 5 Custom APIs registradas (Release N: incluye `anic_`/`sanic_`
  processchunk en transición).
- La MDA está asociada al rol Configurador y a ningún otro fuera de los de plataforma
  (`System Administrator`/`System Customizer`, que Dataverse asocia a toda MDA y no permite quitar).
- Al menos un Cloud Flow que referencia `processchunk` apunta a `sanic_capi_smelter_processchunk` (si
  no lo encuentra, lo reporta como WARN en vez de fallar en silencio — puede que el flow real tenga otra
  forma de JSON; revisar `CloudFlowUpdater.cs`).
- El message step de `AssignJobOwnerPlugin` existe, está **habilitado**, y con el stage/mode/rank
  exactos de `StepSpec` (`StepAssertions`). Además, el smoke test verifica el comportamiento: el job de
  prueba debe nacer owned por el team del template si este es team-owned, o conservar el owner default
  si es user-owned (el caso del smoke, que corre con el Service Principal).

Corré verify manualmente:

```bash
DATAVERSE_URL=... PP_CLIENT_ID=... PP_CLIENT_SECRET=... \
  dotnet run --project tools/Smelter.Verify -c Release
```

---

## Guía operativa — Teams (lo único manual)

`Smelter.Deploy` NO crea teams, no agrega miembros ni comparte templates — depende de cómo cada
organización agrupa a sus operadores. Estos 3 pasos son manuales, por diseño.

### 1. Crear el team y asignarle el rol Operador

**PowerShell (Dataverse Web API)** — reemplazá `<org>`, `<token>` (bearer de tu Service Principal o
usuario admin) y los GUIDs:

```powershell
$base = "https://<org>.crm.dynamics.com/api/data/v9.2"
$headers = @{ Authorization = "Bearer <token>"; "OData-MaxVersion" = "4.0"; "OData-Version" = "4.0"; "Content-Type" = "application/json" }

# Crear el team (Owner team, dentro de tu Business Unit)
$team = Invoke-RestMethod -Method Post -Uri "$base/teams" -Headers $headers -Body (@{
    name = "Cobranza - Operadores"
    teamtype = 0  # Owner
} | ConvertTo-Json)
$teamId = $team.teamid

# Asociar el rol "Smelter Operador" al team (todos sus miembros heredan el rol)
$roleId = (Invoke-RestMethod -Method Get -Headers $headers `
    -Uri "$base/roles?`$select=roleid&`$filter=name eq 'Smelter Operador'").value[0].roleid

Invoke-RestMethod -Method Post -Headers $headers `
    -Uri "$base/teams($teamId)/teamroles_association/`$ref" `
    -Body (@{ "@odata.id" = "$base/roles($roleId)" } | ConvertTo-Json)
```

**Equivalente en SDK (C#, mismo patrón que `SecurityProvisioner.cs`):**

```csharp
var teamId = client.Create(new Entity("team")
{
    ["name"] = "Cobranza - Operadores",
    ["teamtype"] = new OptionSetValue(0), // Owner
});

client.Execute(new AssociateRequest
{
    Target = new EntityReference("team", teamId),
    Relationship = new Relationship("teamroles_association"),
    RelatedEntities = new EntityReferenceCollection { new EntityReference("role", operadorRoleId) },
});
```

### 2. Agregar operadores como miembros del team

```powershell
Invoke-RestMethod -Method Post -Headers $headers `
    -Uri "$base/teams($teamId)/teammembership_association/`$ref" `
    -Body (@{ "@odata.id" = "$base/systemusers($operadorUserId)" } | ConvertTo-Json)
```

```csharp
client.Execute(new AssociateRequest
{
    Target = new EntityReference("team", teamId),
    Relationship = new Relationship("teammembership_association"),
    RelatedEntities = new EntityReferenceCollection { new EntityReference("systemuser", operadorUserId) },
});
```

### 3. Asignar el template al team (ownership — el camino recomendado)

**Asignar** (cambiar el owner) es lo que activa TODO el modelo: los miembros ven el template
directamente (Read Basic cubre lo owned por sus teams), y cada Import Job creado contra él **nace owned
por el team** (`AssignJobOwnerPlugin`) — todos los miembros ven y operan los jobs de todos.

```powershell
# Asignar el template al team (ownership)
Invoke-RestMethod -Method Patch -Headers $headers `
    -Uri "$base/sanic_tbl_smlt_importtemplates($templateId)" `
    -Body (@{ "ownerid@odata.bind" = "$base/teams($teamId)" } | ConvertTo-Json)
```

```csharp
client.Update(new Entity("sanic_tbl_smlt_importtemplate", templateId)
{
    ["ownerid"] = new EntityReference("team", teamId),
});
```

> El team debe ser de tipo **Owner** (`teamtype = 0`, como en el Paso 1) y tener un rol de seguridad
> asociado — sin rol, Dataverse rechaza el assign.

### 3b. (Opcional) Compartir el template con teams ADICIONALES

Un registro tiene UN solo owner. Si un mismo template lo usan varios teams, asignalo al principal
(Paso 3) y compartilo con los demás — el cascade Share/Reparent propaga el acceso de esos teams a los
jobs del template (los jobs igual nacen owned por el team **owner**).

```powershell
Invoke-RestMethod -Method Post -Headers $headers -Uri "$base/GrantAccess" -Body (@{
    Target = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.sanic_tbl_smlt_importtemplate"
        sanic_tbl_smlt_importtemplateid = $templateId
    }
    PrincipalAccess = @{
        Principal = @{ "@odata.type" = "Microsoft.Dynamics.CRM.team"; teamid = $teamId }
        AccessMask = "ReadAccess, AppendToAccess"
    }
} | ConvertTo-Json -Depth 5)
```

```csharp
client.Execute(new GrantAccessRequest
{
    Target = new EntityReference("sanic_tbl_smlt_importtemplate", templateId),
    PrincipalAccess = new PrincipalAccess
    {
        Principal = new EntityReference("team", teamId),
        AccessMask = AccessRights.ReadAccess | AccessRights.AppendToAccess,
    },
});
```

> `teamroles_association` y `teammembership_association` son relaciones N:N **estándar** de la
> plataforma (no custom, a diferencia de `appmoduleroles_association` que sí es best-effort — ver
> comentario de riesgo en `SecurityProvisioner.cs`). Si tu versión de Dataverse expone otro nombre,
> confirmalo con `GET {org}/api/data/v9.2/$metadata` antes de automatizar esto en un script propio.

Repetí el paso 3 (o 3b) por cada template, para cada team de operadores que deba usarlo. Asigná el rol
**Smelter Configurador** a los usuarios que configuran templates de la misma forma (paso 1, con
`ConfiguradorRoleId` en vez de `operadorRoleId` — no hace falta un team, se puede asignar el rol
directo al usuario vía `systemuserroles_association`).

---

## Verificación manual (checklist)

Probá con un usuario **Operador** de prueba (solo el rol Smelter Operador, miembro de UN team):

- [ ] **No puede configurar:** abrir un Import Template para editar/crear está bloqueado.
- [ ] **Ve solo lo asignado:** en la vista Import Templates ve SOLO los templates owned por (o
      compartidos con) su team. Un template de OTRO team no debe aparecerle.
- [ ] **No ve la MDA de Smelter** en su selector de apps (pero SÍ puede operar un Import Job por URL
      directa — ver caveat de "MDA restringida" arriba).
- [ ] **Carga datos:** crea un Import Job contra un template asignado, sube el CSV, ejecuta (botón
      Ejecutar) → procesa OK y ve los logs.
- [ ] **Ownership por team:** un job creado contra un template **owned por el team** queda con
      Owner = el team (visible en el form del job), y un SEGUNDO operador del MISMO team lo ve y lo
      opera. Ojo: ese job NO aparece en la vista de plataforma "My Import Jobs" del creador (filtra
      por owner = el usuario) — usá "Jobs Activos" / "Todos los Jobs".
- [ ] **Cascade (teams adicionales):** con un template *compartido* (no owned) con el team, un segundo
      operador ve los jobs, incluidos los creados DESPUÉS del share (valida `Reparent=Cascade`).
- [ ] **Sin acceso al template → rechazado:** intentar `prepareimport` sobre un job cuyo template NO
      está compartido con ningún team del operador debe fallar (Fase 5).

---

## Troubleshooting

| Síntoma | Causa probable | Solución |
|---------|----------------|----------|
| Operador no ve NINGÚN template | El template no está owned por / compartido con su team, o no es miembro | Asigná el template al team (Paso 3) o compartilo (Paso 3b); confirmá membresía (Paso 2) |
| Operador no ve los jobs de otro operador | El template NO es owned por un team (los jobs nacen owned por cada creador), el step AssignJobOwner está deshabilitado, o — para teams adicionales — cascade sin configurar | Asigná el template al team (Paso 3); corré `Smelter.Verify` — `StepAssertions` confirma el step y `AssertCascade` el cascade; si falla, re-corré `Smelter.Deploy` |
| Un job de team no aparece en "My Import Jobs" | Comportamiento esperado: la vista de plataforma filtra por owner = el usuario, y el job es owned por el team | Usá las vistas "Jobs Activos" / "Todos los Jobs" (muestran todo lo que el usuario puede leer) |
| `prepareimport` falla con "does not have Read access" | El template del job no está compartido con ningún team del `InitiatingUserId` | Compartí el template con el team del operador (Paso 3) |
| Botón Ejecutar/Reintentar no aparece | RibbonDiffXml no se importó, o el estado del job no matchea la visibilidad del botón | Confirmá que `backend-deploy` corrió `UpsertRibbon`; revisá el estado (`sanic_smlt_ij_status`) del job |
| Cloud Flow falla con "acción no encontrada" tras un deploy | El Cloud Flow quedó apuntando a `anic_...` o `sanic_...` incorrectamente | Corré `Smelter.Verify` — `AssertProcessChunkCloudFlowReferencesNewApi` lo detecta; revisar `CloudFlowUpdater.cs` |
| `executeprivilegename` no seteado en una Custom API | El upsert de Custom APIs (paso `[4]`) falló antes de `SecurityProvisioner` | Revisá logs de `backend-deploy`; `Smelter.Verify` (`AssertExecutePrivileges`) lo confirma |
| `Smelter.Deploy` corrido 2 veces duplica algo | No debería — reportar como bug (rompe Requirement "Provisioning idempotente") | Abrí un issue con el log completo |

---

## Clave HMAC

La función `HMAC(valor, "nombreKey")` del Transform DSL (ver [transform-dsl](../03-developer-guide/transform-dsl.md#hmac))
necesita una key por nombre. Se guarda en la tabla **TBL | SMLT | HMAC Key** (`sanic_tbl_smlt_hmackey`),
que es **componente de la solución `smlt_core_smelter`**: el schema viaja a Test/Prod con el managed, pero
**el valor de la key se carga a mano en cada entorno** (nunca por CI, nunca en el repo, nunca en un GitHub secret).

### Riesgo aceptado

Se decidió conscientemente guardar la key en Dataverse. Riesgos y mitigaciones:

| Riesgo | Detalle | Mitigación actual |
|--------|---------|-------------------|
| Lectura por administradores | Cualquier System Administrator del entorno puede leer la key | La tabla está fuera de todo rol de Smelter (solo SYSTEM y System Administrator); limitar quién es System Administrator |
| Copias y backups | Copiar o restaurar el entorno arrastra la key | Tratar los backups/copias con la misma sensibilidad que prod; al copiar a un entorno inferior, reemplazar la key |
| Sin audit del valor | La columna del valor no se audita (para no dejar la key en el audit log) | Sí se audita la tabla: se ve quién creó, rotó o borró una key (no su valor) |
| Advanced Find | — | La columna del valor no es válida para Advanced Find |
| Lectura accidental por otros roles | Un rol al que se le otorgue acceso a la tabla por error leería el valor | **Column security**: la columna del valor está asegurada (`IsSecured`). El formulario muestra `****` y ningún rol puede leerla ni escribirla salvo System Administrator y SYSTEM (el plugin). No se crea ningún field security profile: **no hay que dar acceso a nadie** |

**Column security: capa extra, no un blindaje.** Es defensa en profundidad: reduce el daño de un error de permisos,
pero **no oculta el dato a los System Administrators** (Microsoft: la seguridad de columna no aplica a ese rol), así que
el riesgo de "lectura por administradores" sigue en pie. El deploy (`Smelter.Deploy`) la activa de forma idempotente
y `Smelter.Verify` comprueba en cada corrida que está activa y que el plugin (SYSTEM) sigue leyendo la key (smoke E2E
con una key temporal `smoke-verify` que se borra al terminar). Al cargar la key a mano (procedimiento de abajo) el
formulario mostrará `****` en vez del hex: es lo esperado.

**Endurecimiento futuro**: Azure Key Vault con managed identity detrás de `IHmacKeyProvider`. Con la misma
key los hashes son idénticos, así que migrar no rompe datos existentes.

### Procedimiento MANUAL para cargar la key en un entorno nuevo

1. Tras importar la solución, generá 32 bytes aleatorios en hex (64 caracteres):
   ```bash
   openssl rand -hex 32
   ```
2. Entrá a [make.powerapps.com](https://make.powerapps.com) en el entorno destino, **como System Administrator**:
   Tables → **TBL | SMLT | HMAC Key** → editar datos.
3. Creá **una** fila activa:
   - **Name** (`[HK] Name`): la etiqueta que usan los templates, p. ej. `cm-pan`.
   - **Value** (`[HK] Value (hex)`): el hex generado.
4. Guardá. Probá con un template que use `HMAC(columna, "cm-pan")`.

Reglas de custodia:
- **Nunca** pegues la key en chats, tickets, mails ni en el repo.
- Guardala en el gestor de contraseñas (vault) de la organización, con el nombre y el entorno.
- Si hay dos filas activas con el mismo nombre, el motor falla (`more than one active key`): dejá una sola.

### Rotación

Cambiar la key **cambia todos los hashes**. Si un template hace upsert por una columna HMAC, los registros
existentes ya no coinciden y se crearían **duplicados**. Rotar requiere una **migración de re-key
planificada** (recalcular los hashes existentes con la key nueva) antes de volver a importar. No reemplaces
el valor "en caliente".

### ¿La misma key en todos los entornos?

Por defecto, **una key distinta por entorno** (un compromiso en Dev no afecta a Prod). Usá la misma key solo
si los hashes deben coincidir entre entornos (p. ej. datos de Prod copiados a Test que se vuelven a
importar); en ese caso, aplicá a Test la custodia de Prod.

---

## Resumen
```
1. tools/Smelter.Deploy provisiona TODO lo declarativo: roles + privilegios
   (SecuritySpec), message step AssignJobOwner (StepSpec), cascade Template→Job,
   ExecutePrivilegeName por Custom API, MDA restringida a Configurador,
   vistas + ribbon + dashboard.
2. AssignJobOwnerPlugin (step pre-op del Create): template owned por team ⇒
   el Import Job nace owned por ese team ⇒ todo el team lo ve/opera (Basic).
3. PrepareImportPlugin valida en runtime (Fase 5) que InitiatingUserId tenga
   Read sobre el Template del job — defensa en profundidad, además de la matriz.
4. tools/Smelter.Verify (SecurityAssertions + StepAssertions) confirma todo lo
   anterior post-deploy contra el ambiente real, antes del smoke test funcional.
5. Lo único manual: crear el team, agregarle operadores, ASIGNARLE los templates
   (ownership; GrantAccess solo para teams adicionales) — ejemplos arriba.
```

> **Caveat de escala:** el cascade sharing y el `GrantAccess` a teams escriben en la tabla
> **PrincipalObjectAccess (POA)**. A volúmenes normales de Smelter (jobs por import) es irrelevante. Si
> el volumen de jobs crece muchísimo, revisá el tamaño de POA (admin center).
