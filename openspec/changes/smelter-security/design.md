# Design: Smelter Security + MDA UX (v2)

## Technical Approach

Todo cambio de plataforma vía SDK desde `tools/Smelter.Deploy`, extendiendo el patrón existente
(`Catalogo` declarativo + upsert idempotente + `SolutionUniqueName` en `CreateRequest` +
`PublishAllXmlRequest` final — ver `Program.cs`/`ApiSpec.cs`). Se agrega `SecurityProvisioner`,
que corre roles/privilegios/cascade/app-rol/vistas/ribbon/sitemap tras el upsert de Custom APIs.
Único cambio de código en el motor: `PrepareImportHandler` valida acceso del `InitiatingUserId`
sobre el Template (TDD). Implementa `smelter-access-control` + `smelter-mda-ux`.

## Architecture Decisions

| Decisión | Elegido | Alternativa | Rationale |
|---|---|---|---|
| Matriz de privilegios | C# `record`/`static class` en `SecuritySpec.cs`, patrón `Catalogo` de `ApiSpec.cs` | JSON embebido | Idiom ya existe en el repo; JSON exige parser/validador nuevo sin beneficio para 2 roles × 6 tablas. |
| Alta de roles/privilegios | `role` (Create + `SolutionUniqueName`, como `CrearEnSolucion`) + `AddPrivilegesRoleRequest` (privilegios resueltos por nombre `prv{Tipo}{tabla}` vía query a `privilege`) | Editar a mano en maker portal | Nativo SDK, idempotente, reproducible en CI limpio. |
| Idempotencia de roles | Query por `name`; si existe, re-aplica `AddPrivilegesRoleRequest` igual | Diff antes de escribir | Ya es upsert por privilegio — comparar es complejidad sin ganancia. |
| Cascade Template→Job | `UpdateRelationshipRequest` sobre `sanic_tbl_smlt_importjob_smlt_ij_importtemplateid_sanic_tbl_smlt_importtemplate` (confirmado en `Relationships/sanic_tbl_smlt_importtemplate.xml`): Share=Cascade, Reparent=Cascade | Plugin que reasigna el job | Nativo (POA), cero código; descartado en v1 por sobre-ingeniería. |
| Vistas + Dashboard | CRUD directo (`Create`/`UpdateRequest` sobre `savedquery` y `sitemap.sitemapxml`) | Import de solución .zip | CRUD-ables por SDK, mismo idiom que `customapi`, sin paquete extra. |
| Botones Ejecutar/Reintentar | RibbonDiff clásico + JS (coherente con Export/Import) | Modern commands (command bar JSON) | Migraría también los botones actuales (fuera de scope). Trade-off: RibbonDiffXml no es CRUD suelto — requiere solution.zip mínimo + `ImportSolutionRequest`, igual 100% SDK. |
| `solutions/unpacked/` | Solo lectura (`solution-sync` la escribe DESDE Dev tras un Release) | Fuente para importar | Confirmado en `SolutionMetadata.cs`/`solutions/README.md`: espejo de contrato, no origen de import. |
| Verificación Operador sin export/import | Assert estático: privilegios del rol Operador NO incluyen Write sobre Template (`RetrieveRolePrivilegesRoleRequest`) | Login E2E con 2º usuario | CI solo tiene 1 Service Principal; provisionar un 2º usuario es alcance nuevo. Cubre el mismo riesgo sin secretos extra. |

## Data Flow (deploy)

```
Program.cs (existente 1-5: package + 4 Custom APIs + PublishAllXml)
        │
[6] SecurityProvisioner.Run(client)
    ├─ UpsertRoles(SecuritySpec.Roles)         → role + AddPrivilegesRoleRequest (+SolutionUniqueName)
    ├─ SetCascade(Template→Job)                → UpdateRelationshipRequest
    ├─ SetExecutePrivileges(Catalogo.Apis)      → update customapi.executeprivilegename
    ├─ AssociateAppModuleRole(MDA, Configurador)→ limpia los 2 GUIDs rotos de AppModuleRoleMaps
    ├─ UpsertViews(SavedQuery specs)            → CreateRequest/UpdateRequest("savedquery")
    ├─ UpsertRibbon(mini-solution zip)          → ImportSolutionRequest (Ejecutar/Reintentar)
    └─ UpsertDashboardSubArea                   → update sitemap.sitemapxml
        │
[7] PublishAllXmlRequest (ya existe)
```

## File Changes

| Archivo | Acción | Descripción |
|---|---|---|
| `tools/Smelter.Deploy/SecuritySpec.cs` | Create | Matriz rol×tabla×privilegio×depth + specs de vistas/ribbon/sitemap |
| `tools/Smelter.Deploy/SecurityProvisioner.cs` | Create | Lógica idempotente descripta arriba |
| `tools/Smelter.Deploy/ApiSpec.cs` | Modify | `ExecutePrivilegeName` en `CustomApiSpec`; entrada nueva `sanic_capi_smelter_processchunk` (mismo `PluginTypeName`), `anic_...` intacta hasta confirmar el flow |
| `tools/Smelter.Deploy/Program.cs` | Modify | Llama `SecurityProvisioner.Run` tras [5] |
| `src/Smelter.Plugins/PrepareImportPlugin.cs` | Modify | `Run` recibe `initiatingUserId`; `IAccessChecker` (RetrievePrincipalAccessRequest) por constructor |
| `src/Smelter.Plugins/Services/PrincipalAccessChecker.cs` | Create | Impl real de `IAccessChecker` |
| `tests/.../PrepareImportHandlerTests.cs` | Modify | Acceso OK pasa; sin `ReadAccess` sobre Template → excepción, sin `Update` |
| `tools/Smelter.Verify/Program.cs` | Modify | Asserts de roles/cascade/executeprivilegename |
| Cloud Flow (`workflow`, no en `unpacked/`) | Modify (SDK) | `UpdateRequest` sobre `clientdata`: `anic_...`→`sanic_...`; deactivate/update/activate |

## Interfaces

```csharp
public interface IAccessChecker {
    bool HasReadAccess(Guid principalId, EntityReference target);
}
// Run(jobId, initiatingUserId): tras LoadContext, si !HasReadAccess(initiatingUserId, templateRef)
//   → throw InvalidPluginExecutionException
```

## Testing Strategy

| Capa | Qué | Cómo |
|---|---|---|
| Unit (TDD, NSubstitute) | Sin acceso → excepción, sin `Update`; con acceso → flujo actual | Mock `IAccessChecker`, `Received(0)`/retorna true |
| Contract | `executeprivilegename` de `SecuritySpec` con formato real (`prv{Tipo}{tabla}`) | Test de string, sin llamar Dataverse |
| E2E (`Smelter.Verify`) | Roles con privilegios de `SecuritySpec`; Cascade Share/Reparent=Cascade; `executeprivilegename` seteado en las 5 Custom APIs; Operador sin Write en Template (proxy de "no exporta") | `RetrieveRolePrivilegesRoleRequest` / `RetrieveRelationshipRequest` / `RetrieveMultiple("customapi")` |

## Migration / Rollout

1. Deploy N: package+APIs (incl. `sanic_capi_smelter_processchunk` nueva, `anic_...` intacta) →
   `SecurityProvisioner` → update del Cloud Flow al nuevo uniquename → `Smelter.Verify` en verde.
2. Confirmado el flow en verde: Deploy N+1 quita `anic_...` de `Catalogo` + `DeleteRequest`
   explícito (cleanup). Sin ventana rota: el flow ya apunta al nuevo antes de borrar el viejo.
3. Rollback: `git revert` + redeploy — roles/cascade/vistas/ribbon son idempotentes y se
   reconcilian solos; el rename del Cloud Flow se revierte junto con el paso 1 si no se llegó al 2.

## Open Questions

- [ ] `PrivilegeDepth` del Operador sobre `ImportJob` (Create/Read/Write): ¿User o BU? — definir en
      `SecuritySpec` según si operadores comparten Business Unit.
- [ ] Mini-solution.zip del RibbonDiff: ¿generarlo in-memory en `SecurityProvisioner` o versionarlo
      como asset en `tools/Smelter.Deploy/Assets/`? Definir en `sdd-tasks`.
