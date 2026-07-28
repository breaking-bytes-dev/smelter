[← Volver al índice de documentación](../README.md)

# Guía — Seguridad de Smelter (roles, cascade, ExecutePrivilegeName, MDA, UX)

> Implementación real del feature **smelter-security** (`openspec/changes/smelter-security/`). Todo lo
> de esta guía — roles, privilegios, cascade sharing, gating de Custom APIs, restricción de la MDA,
> vistas/ribbon/dashboard — se provisiona **por SDK** (`tools/Smelter.Deploy`), NO a mano en el portal.
> Lo único que sigue siendo manual es **operación de teams** (Paso 4 de esta guía): crear el team,
> agregarle operadores, y compartirle templates — eso no lo automatiza `Smelter.Deploy` porque depende
> de cómo cada organización agrupa a sus operadores (no hay una convención única que provisionar).
>
> **Separación de roles:** **Configurador** (crea/edita templates y mappings, ve todo) vs. **Operador**
> (solo carga datos y ve logs, viendo solo los templates de sus teams).

---

## Cómo funciona (el modelo, en 4 piezas)

1. **`tools/Smelter.Deploy`** provisiona TODO lo declarativo — roles, privilegios, cascade, gating de
   Custom APIs, restricción de la MDA, vistas, botones y dashboard — de forma **idempotente**: correrlo
   de nuevo no duplica nada (Requirement "Provisioning idempotente", smelter-access-control).
2. **Templates compartidos con un Team** → los operadores miembros ven solo esos templates.
3. **Cascade sharing** en la relación Template→Import Job → los jobs creados bajo un template
   **heredan** el acceso del template. Así un operador ve los jobs de sus templates (de cualquier
   operador del team), sin necesitar Business Unit ni compartir cada job a mano.
4. **`PrepareImportPlugin` valida en runtime** que quien dispara el import (`InitiatingUserId`) tenga
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
| Cascade Template→Job | Share=Cascade, Reparent=Cascade en la relación 1:N | `UpdateRelationshipRequest` |
| `ExecutePrivilegeName` | Gatea cada Custom API por el privilegio real que reutiliza (tabla de abajo) | update `customapi.executeprivilegename` |
| MDA → solo Configurador | Desasocia cualquier rol roto, asocia Configurador | `Associate`/`DisassociateRequest` |
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
  *crear* un job; una vez creado, solo lo ve/edita quien tiene acceso (el team, vía cascade).
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

## Cascade Template → Import Job

`tools/Smelter.Deploy` setea `Share=Cascade`, `Reparent=Cascade` en la relación
`sanic_tbl_smlt_importjob_smlt_ij_importtemplateid_sanic_tbl_smlt_importtemplate` vía
`UpdateRelationshipRequest` — sin plugin, nativo de la plataforma (POA). `Reparent=Cascade` es la clave:
hace que los Import Jobs **nuevos** (creados después del share) también hereden el acceso, no solo los
que ya existían al momento de compartir.

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

`tools/Smelter.Verify` (workflow de CI `backend-verify`) corre `SecurityAssertions.RunAll` ANTES del
smoke test funcional — si la seguridad regresionó, no tiene sentido seguir con el import de prueba.
Confirma contra el ambiente real:

- Los roles Configurador/Operador existen con EXACTAMENTE los privilegios de `SecuritySpec` (incluido
  que el Operador NO tenga Create/Write/Delete en Template — proxy estático de "no puede configurar",
  ya que `Smelter.Verify` corre con un único Service Principal y no loguea un segundo usuario real).
- Cascade Share=Cascade, Reparent=Cascade en la relación Template→Job.
- `executeprivilegename` seteado en las 5 Custom APIs registradas (Release N: incluye `anic_`/`sanic_`
  processchunk en transición).
- La MDA está asociada solo al rol Configurador.
- Al menos un Cloud Flow que referencia `processchunk` apunta a `sanic_capi_smelter_processchunk` (si
  no lo encuentra, lo reporta como WARN en vez de fallar en silencio — puede que el flow real tenga otra
  forma de JSON; revisar `CloudFlowUpdater.cs`).

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

### 3. Compartir (asignar) un template al team

Esto es lo que "asignar un template a un operador" significa en la práctica — se comparte con el
**team**, no con cada usuario, y gracias al cascade del paso anterior los Import Jobs futuros heredan
el acceso automáticamente.

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

Repetí el paso 3 por cada template, para cada team de operadores que deba usarlo. Asigná el rol
**Smelter Configurador** a los usuarios que configuran templates de la misma forma (paso 1, con
`ConfiguradorRoleId` en vez de `operadorRoleId` — no hace falta un team, se puede asignar el rol
directo al usuario vía `systemuserroles_association`).

---

## Verificación manual (checklist)

Probá con un usuario **Operador** de prueba (solo el rol Smelter Operador, miembro de UN team):

- [ ] **No puede configurar:** abrir un Import Template para editar/crear está bloqueado.
- [ ] **Ve solo lo asignado:** en la vista Import Templates ve SOLO los templates compartidos con su
      team. Un template de OTRO team no debe aparecerle.
- [ ] **No ve la MDA de Smelter** en su selector de apps (pero SÍ puede operar un Import Job por URL
      directa — ver caveat de "MDA restringida" arriba).
- [ ] **Carga datos:** crea un Import Job contra un template asignado, sube el CSV, ejecuta (botón
      Ejecutar) → procesa OK y ve los logs.
- [ ] **Cascade:** un SEGUNDO operador del MISMO team ve el job que creó el primero, incluidos jobs
      creados DESPUÉS del share (valida `Reparent=Cascade`).
- [ ] **Sin acceso al template → rechazado:** intentar `prepareimport` sobre un job cuyo template NO
      está compartido con ningún team del operador debe fallar (Fase 5).

---

## Troubleshooting

| Síntoma | Causa probable | Solución |
|---------|----------------|----------|
| Operador no ve NINGÚN template | El template no está compartido con su team / no es miembro | Repetí el Paso 3 (GrantAccess) y confirmá membresía (Paso 2) |
| Operador no ve los jobs de otro operador | Cascade no configurado o `Reparent` ≠ Cascade | Corré `Smelter.Verify` — `AssertCascade` lo confirma; si falla, re-corré `Smelter.Deploy` |
| `prepareimport` falla con "does not have Read access" | El template del job no está compartido con ningún team del `InitiatingUserId` | Compartí el template con el team del operador (Paso 3) |
| Botón Ejecutar/Reintentar no aparece | RibbonDiffXml no se importó, o el estado del job no matchea la visibilidad del botón | Confirmá que `backend-deploy` corrió `UpsertRibbon`; revisá el estado (`sanic_smlt_ij_status`) del job |
| Cloud Flow falla con "acción no encontrada" tras un deploy | El Cloud Flow quedó apuntando a `anic_...` o `sanic_...` incorrectamente | Corré `Smelter.Verify` — `AssertProcessChunkCloudFlowReferencesNewApi` lo detecta; revisar `CloudFlowUpdater.cs` |
| `executeprivilegename` no seteado en una Custom API | El upsert de Custom APIs (paso `[4]`) falló antes de `SecurityProvisioner` | Revisá logs de `backend-deploy`; `Smelter.Verify` (`AssertExecutePrivileges`) lo confirma |
| `Smelter.Deploy` corrido 2 veces duplica algo | No debería — reportar como bug (rompe Requirement "Provisioning idempotente") | Abrí un issue con el log completo |

---

## Resumen
```
1. tools/Smelter.Deploy (SecurityProvisioner) provisiona TODO lo declarativo:
   roles + privilegios (SecuritySpec), cascade Template→Job, ExecutePrivilegeName
   por Custom API, MDA restringida a Configurador, vistas + ribbon + dashboard.
2. PrepareImportPlugin valida en runtime (Fase 5) que InitiatingUserId tenga
   Read sobre el Template del job — defensa en profundidad, además de la matriz.
3. tools/Smelter.Verify (SecurityAssertions) confirma todo lo anterior post-deploy
   contra el ambiente real, antes del smoke test funcional.
4. Lo único manual: crear el team, agregarle operadores, compartir templates
   con el team (GrantAccess) — vía Web API/pwsh o SDK, ejemplos arriba.
```

> **Caveat de escala:** el cascade sharing y el `GrantAccess` a teams escriben en la tabla
> **PrincipalObjectAccess (POA)**. A volúmenes normales de Smelter (jobs por import) es irrelevante. Si
> el volumen de jobs crece muchísimo, revisá el tamaño de POA (admin center).
