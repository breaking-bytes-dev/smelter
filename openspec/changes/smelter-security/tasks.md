# Tasks: Smelter Security + MDA UX (v2)

Decisiones cerradas (no relitigar): Operador.ImportJob `PrivilegeDepth=User` (Basic) — visibilidad
por cascade Template→Job, no BU. Assets RibbonDiff: XML versionado en `tools/Smelter.Deploy/Assets/`,
solution.zip armado en memoria en deploy (sin binarios versionados).

## Phase 1: Spike — de-risk ImportSolutionRequest (RibbonDiff)

- [x] 1.1 Spike descartable: `solution.zip` mínimo en memoria (`customizations.xml` con
      `RibbonDiffXml` sobre Import Job + `solution.xml`) vía `ImportSolutionRequest` contra Dev.
      → Implementado como capacidad reusable: `tools/Smelter.Deploy/SolutionZipBuilder.cs`
      (`BuildRibbonSolution`), formato de `RootComponent`/`SolutionManifest` corroborado contra
      `solutions/unpacked/Other/Solution.xml` real. Cubierto por
      `tests/Smelter.Contract.Tests/SolutionZipBuilderTests.cs` (4 tests, forma del zip, sin
      llamar Dataverse).
- [ ] 1.2 **BLOQUEADA** — Confirmar: el botón aparece sin error de import y sin publish manual
      extra. No se puede correr en este sandbox (sin acceso a Dataverse, restricción explícita del
      entorno). Queda pendiente del primer `backend-deploy` en CI que ejecute
      `SecurityProvisioner.UpsertRibbon` (Fase 4.3) contra Dev — ver gotcha documentado en el
      encabezado de `SolutionZipBuilder.cs`.

## Phase 2: SecuritySpec.cs — matriz declarativa

- [x] 2.1 Crear `tools/Smelter.Deploy/SecuritySpec.cs`: records `RoleSpec`/`PrivilegeSpec`
      (rol, tabla, tipo, `PrivilegeDepth`).
- [x] 2.2 Rol Configurador: CRUD+Append+AppendTo Org en las 6 tablas `sanic_tbl_smlt_*`.
- [x] 2.3 Rol Operador: Template Read User+AppendTo; Mappings Read Org; ImportJob Create Org +
      Read/Write **User**; Log Read User.
- [x] 2.4 Mapa `ExecutePrivilegeName` por API: export/import-template →
      `prvWritesanic_tbl_smlt_importtemplate`; prepareimport/processchunk →
      `prvCreatesanic_tbl_smlt_importjob`. → Campo `ExecutePrivilegeName` agregado a
      `CustomApiSpec` (`ApiSpec.cs`), poblado con `SecuritySpec.PrivilegeName(...)` en las 4
      entradas de `Catalogo.Apis`.
- [x] 2.5 Specs de las 3 vistas (FetchXML): Jobs Activos, Completados con Errores, Todos los Jobs.
- [x] 2.6 Specs de dashboard "Smelter Dashboard" + entrada de sitemap.

## Phase 3: Roles + privilegios (TDD contrato + provisioner)

- [x] 3.1 RED: test en `Smelter.Contract.Tests` — `ExecutePrivilegeName` de cada API matchea
      `prv{Tipo}{tabla}` real (sin llamar Dataverse). → `SecuritySpecContractTests.cs` (11 tests:
      formato `ExecutePrivilegeName`, matriz Configurador/Operador completa como datos).
- [x] 3.2 GREEN: crear `tools/Smelter.Deploy/SecurityProvisioner.cs` con `UpsertRoles` (query
      `role` por name, `CrearEnSolucion` si no existe, `AddPrivilegesRoleRequest` resolviendo
      `privilege` por nombre) — hace pasar 3.1.
- [x] 3.3 `SetCascade`: `UpdateRelationshipRequest` sobre la relación Template→ImportJob,
      Share=Cascade, Reparent=Cascade.
- [x] 3.4 `SetExecutePrivileges`: update `customapi.executeprivilegename` iterando `Catalogo.Apis`.
- [x] 3.5 `AssociateAppModuleRole`: implementado de forma más idempotente que "limpiar 2 GUIDs
      hardcodeados" — lee los roles actualmente asociados vía `RetrieveRequest` +
      `RelationshipQueryCollection`, desasocia todo lo que NO sea Configurador, asocia Configurador
      si falta. **RIESGO NO VALIDABLE ACÁ**: el nombre de relación N:N usado,
      `appmoduleroles_association`, es best-effort (documentación pública de la navigation
      property del Web API para el mismo escenario) — no corroborable sin Dev real. Ver comentario
      de riesgo en `SecurityProvisioner.cs`; confirmar en el primer `backend-deploy` de CI.
- [x] 3.6 Wire: `Program.cs` llama `SecurityProvisioner.Run(client)` tras el upsert de Custom APIs
      (paso [6], después del `PublishAllXmlRequest` existente del paso [5]).

## Phase 4: Vistas + Ribbon + Dashboard (MDA UX)

- [x] 4.1 `UpsertViews`: Create/UpdateRequest sobre `savedquery` para las 3 vistas de 2.5.
- [x] 4.2 Autor de `RibbonDiffXml` (botones Ejecutar/Reintentar, visibilidad por estado) como texto
      versionado en `tools/Smelter.Deploy/Assets/` (`sanic_tbl_smlt_importjob.RibbonDiffXml.xml`,
      embebido como `EmbeddedResource`, leído vía `AssetLoader.cs`).
- [x] 4.3 `UpsertRibbon`: arma `solution.zip` en memoria embebiendo 4.2 + `ImportSolutionRequest`
      (aplica lo validado en Fase 1 — también upsertea el webresource JS de 4.4 antes de importar
      el ribbon, ya que el `Library="$webresource:..."` del ribbon lo referencia).
- [x] 4.4 JS de los botones: `Assets/sanic_smlt_importjob_actions.js` invoca
      `prepareimport`/`processchunk` (loop hasta `HasMore=false`); el gating de privilegios del rol
      Operador lo hacen las Custom APIs server-side (`ExecutePrivilegeName`), no este script.
- [x] 4.5 `UpsertDashboardSubArea`: crea/actualiza dashboard + entrada de sitemap; segundo
      `PublishAllXmlRequest` al final de `SecurityProvisioner.Run`. **RIESGO NO VALIDABLE ACÁ**: el
      schema exacto del `formxml` de dashboard ("single-stream", 2 controles `subgrid`) y el
      formato de `Url` del `SubArea` del sitemap hacia un dashboard puntual son best-effort — datos
      de metadata de organización, no tipos SDK compilados, no corroborables sin Dev real. Ver
      comentario de riesgo en `SecurityProvisioner.cs`; confirmar en el primer `backend-deploy`.

## Phase 5: PrepareImportPlugin — acceso al template (TDD)

- [x] 5.1 RED: test en `PrepareImportHandlerTests.cs` — sin `ReadAccess` sobre Template →
      `InvalidPluginExecutionException`, `Update` NO invocado (mock `IAccessChecker`). →
      `Run_InitiatingUserWithoutReadAccessOnTemplate_ThrowsAndDoesNotDownloadOrUpdate`.
- [x] 5.2 RED: test — con `ReadAccess` → `Run` completa igual que hoy (mock retorna true). →
      `Run_InitiatingUserWithReadAccessOnTemplate_CompletesNormally`.
- [x] 5.3 Confirmar ambos tests en rojo (compilación falla por `IAccessChecker` inexistente cuenta
      como rojo). → confirmado: `dotnet build` fallaba con `CS0246 IAccessChecker` antes de crear
      `PrincipalAccessChecker.cs`/actualizar el ctor de `PrepareImportHandler`.
- [x] 5.4 GREEN: `IAccessChecker.HasReadAccess(Guid, EntityReference)` en `Smelter.Plugins.Services`.
      → `src/Smelter.Plugins/Services/PrincipalAccessChecker.cs`.
- [x] 5.5 GREEN: `PrincipalAccessChecker` (impl real, `RetrievePrincipalAccessRequest`) en
      `src/Smelter.Plugins/Services/PrincipalAccessChecker.cs`. Forma del mensaje
      (`Principal`/`Target`, response `AccessRights` de tipo `Microsoft.Crm.Sdk.Messages.AccessRights`)
      corroborada por reflection contra `Microsoft.PowerPlatform.Dataverse.Client` 1.2.26 real (no
      solo por doc pública) — mismo estándar de evidencia que el resto de `SecurityProvisioner.cs`.
      Cubierto por `tests/Smelter.Plugins.Tests/Services/PrincipalAccessCheckerTests.cs` (3 tests:
      flag ReadAccess presente/ausente, ctor null-check), mockeando `IOrganizationService.Execute`
      — mismo patrón que `FileDownloaderTests.cs`, sin llamar Dataverse.
- [x] 5.6 GREEN: `PrepareImportHandler` recibe `IAccessChecker` por constructor; `Run` valida acceso
      tras `LoadContext`, antes de `Download` (método privado `ValidateTemplateAccess`).
- [x] 5.7 GREEN: `PrepareImportPlugin.Execute` pasa `context.InitiatingUserId` y construye
      `PrincipalAccessChecker(service)`.
- [x] 5.8 Verde: `dotnet test tests/Smelter.Plugins.Tests -c Release` (CI Windows). → **No ejecutable
      en este sandbox Linux** (falla al lanzar: `Could not find 'mono' host` — net462 test host no
      corre sin Mono/Windows). Validado localmente con `dotnet build Smelter.sln -c Release` limpio
      (0 errores, incluye `Smelter.Plugins.Tests`), que SÍ compila y linkea los 2 tests nuevos + los 2
      preexistentes (actualizados a la firma nueva de `Run(jobId, initiatingUserId)` como safety net).
      El verde de ejecución real lo da el primer `backend-deploy`/CI Windows tras este batch.

## Phase 6: Rename processchunk — Release N (aditivo)

- [x] 6.1 `ApiSpec.cs`: agregar `sanic_capi_smelter_processchunk` a `Catalogo.Apis` (mismo
      `PluginTypeName`); dejar `anic_capi_smelter_processchunk` intacta.
- [x] 6.2 Contract test: ambas entradas (`anic_`/`sanic_`) existen y apuntan al mismo plugin type
      durante la transición. → `tests/Smelter.Contract.Tests/ProcessChunkRenameContractTests.cs` (3
      tests: ambas existen, mismo `PluginTypeName`, misma forma de request/response/privilegio) —
      TDD: confirmado en rojo (`Contain`/`Single` fallando) antes de agregar la entrada a
      `Catalogo.Apis`, verde después.
      **[Fix post sdd-verify, WARNING CI-hygiene]** El riesgo antes documentado acá (las 3 tests
      parametrizados preexistentes de `CustomApiContractTests` quedando en rojo para
      `sanic_capi_smelter_processchunk` porque `solutions/unpacked/customapis/` todavía no tiene esa
      carpeta) ya NO se acepta como rojo esperado: `CustomApiContractTests.cs` ahora excluye
      explícitamente esa API vía `PendingSolutionSync` (comentario `TODO` apuntando a esta misma
      task), y el resto de las Custom APIs SIGUE cubierto normalmente. CI queda verde y confiable
      hasta el primer `backend-deploy` + `solution-sync` — momento en que hay que sacar la exclusión.
- [x] 6.3 Helper SDK para actualizar el Cloud Flow: `UpdateRequest` sobre `clientdata`
      (`anic_`→`sanic_`), deactivate/update/activate. → `tools/Smelter.Deploy/CloudFlowUpdater.cs`
      (`UpdateProcessChunkReference`), wireado como paso `[7]` en `Program.cs` tras
      `SecurityProvisioner.Run`. **RIESGO NO VALIDABLE ACÁ** (mismo motivo que
      `AssociateAppModuleRole`/`UpsertDashboardSubArea`): que el uniquename viva como texto plano
      dentro del JSON de `clientdata` es el comportamiento documentado de los conectores Custom API
      de Power Automate, pero no es un tipo SDK compilado — no corroborable sin un Cloud Flow real en
      Dev. Fallo suave (WARN, no aborta el deploy) si no encuentra match o el update falla.
      **[Fix post sdd-verify, CRITICAL]** El reemplazo original (`clientData.Replace(OldName, NewName)`,
      substring naive) corrompía el flow en el SEGUNDO deploy en adelante:
      `"sanic_capi_smelter_processchunk"` CONTIENE `"anic_capi_smelter_processchunk"` como substring
      (índice 1), así que correr el replace sobre clientdata YA migrado le anteponía otra "s"
      (`"ssanic_..."`). Extraída la lógica pura a `tools/Smelter.Deploy/ClientDataProcessChunkRenamer.cs`
      (regex de TOKEN exacto, lookbehind/lookahead negativos de `[A-Za-z0-9_]` — nunca matchea "anic_..."
      cuando es substring de "sanic_..."), con reparación del patrón "ssanic_..." si ya existiera. TDD
      completo en `tests/Smelter.Contract.Tests/ClientDataProcessChunkRenamerTests.cs` (9 tests, ver
      evidencia RED→GREEN en el reporte de este batch). `CloudFlowUpdater.UpdateOne` ahora usa
      `Rename(...)` y saltea el `Update`/Draft-Activate por completo cuando `Changed=false`
      (idempotencia real, no solo "no se rompe").
- [ ] 6.4 Deploy release N a Dev vía CI (`backend-deploy`). — **BLOQUEADA**: sin acceso a Dataverse
      desde este sandbox (restricción explícita del entorno, igual que 1.2/3.5/4.5). Pendiente del
      primer `backend-deploy` de CI que corra `Program.cs` completo (incl. `CloudFlowUpdater`) contra
      Dev.

## Phase 7: Smelter.Verify — assert por pieza

- [x] 7.1 Assert privilegios Configurador (CRUD Org, 6 tablas) vía
      `RetrieveRolePrivilegesRoleRequest`. → `tools/Smelter.Verify/SecurityAssertions.cs`
      (`AssertRolesExistAndMatchPrivileges`, matchea por `RolePrivilege.PrivilegeName`/`Depth` reales
      contra `SecuritySpec.Roles`, forma corroborada por reflection igual que 5.5).
- [x] 7.2 Assert privilegios Operador == `SecuritySpec` (incl. sin Write en Template). → mismo método,
      más el chequeo explícito de ausencia de Create/Write/Delete en Template.
- [x] 7.3 Assert cascade Share=Cascade, Reparent=Cascade en la relación Template→ImportJob. →
      `AssertCascade` (mismo `RetrieveRelationshipRequest` que `SecurityProvisioner.SetCascade`).
- [x] 7.4 Assert `executeprivilegename` seteado en las 5 Custom APIs registradas (transición N). →
      `AssertExecutePrivileges`, itera `Catalogo.Apis` (5 entradas tras 6.1).
- [x] 7.5 Assert `AppModuleRoles`: MDA asociada solo a Configurador. →
      `AssertAppModuleRolesOnlyConfigurador` (mismo `RelationshipQueryCollection` que
      `SecurityProvisioner.AssociateAppModuleRole`, ahora en modo lectura).
- [x] 7.6 Assert Cloud Flow `clientdata` referencia `sanic_capi_smelter_processchunk` (reporta si
      quedó desalineado, no falla silencioso). → `AssertProcessChunkCloudFlowReferencesNewApi`; sin
      ningún flow encontrado → WARN y sigue (no hay flow que verificar); flow encontrado sin el
      uniquename nuevo → `Assert` (excepción → `[FAIL]` en `Program.cs`, exit 1) — cumple "reportar en
      vez de pasar en silencio". También se corrigió `apiProcessChunk` en `Program.cs` (ahora resuelve
      explícitamente `sanic_capi_smelter_processchunk`; el `.Single()` genérico por `PluginTypeName`
      hubiese tirado "more than one match" tras 6.1).
      **[Fix post sdd-verify, WARNING]** El assert original usaba `clientData.Contains(NewName)`, que
      daría VERDE incluso con clientdata corrupto (`"ssanic_..."` CONTIENE `"sanic_..."` como
      substring) o con la API vieja todavía referenciada junto a la nueva. Ahora usa
      `ClientDataProcessChunkRenamer.ReferencesNewToken`/`ReferencesOldToken`/`HasDoubleSCorruption`
      (misma lógica de token exacto que el fix de 6.3, sin divergir el criterio entre quien escribe y
      quien verifica) — 3 asserts independientes: token nuevo presente, token viejo AUSENTE, sin
      corrupción "ssanic_...".
- [ ] 7.7 Correr `Smelter.Verify` contra Dev vía CI (`backend-verify`), confirmar verde. —
      **BLOQUEADA**: mismo motivo que 6.4, sin acceso a Dataverse desde este sandbox. `dotnet build`
      confirma que `SecurityAssertions.cs` compila limpio contra los tipos reales del SDK.

## Phase 8: Docs

- [x] 8.1 Reescribir `docs/04-admin-guide/security-and-roles.md`: modelo SDK (no manual), matriz de
      2.2/2.3, `ExecutePrivilegeName`, restricción MDA, vistas/ribbon/dashboard. → reescrito completo;
      incluye caveat explícito de que "MDA restringida a Configurador" NO es seguridad de registro
      (un Operador puede seguir abriendo un Import Job por URL directa).
- [x] 8.2 Agregar guía operativa: crear team, compartir template con team, agregar operadores como
      miembros (lo único que sigue siendo manual — no lo cubre `Smelter.Deploy`). → sección "Guía
      operativa — Teams" en el mismo doc, con ejemplos pwsh (Web API: `teamroles_association`,
      `teammembership_association`, acción `GrantAccess`) y SDK (C#, `AssociateRequest`/
      `GrantAccessRequest`) — formas de request/response corroboradas por reflection contra el SDK
      real (mismo estándar que 5.5/7.1).
- [x] 8.3 Actualizar `CHANGELOG.md` con rename de API, roles nuevos y UX de Import Job. → sección
      `[Unreleased]` con `Added`/`Changed`.

## Fixes post sdd-verify (judgment-day review, batch 2)

`sdd-verify` devolvió PASS WITH WARNINGS con 1 CRITICAL sobre el batch de Fases 5-8. Los ítems de
CloudFlowUpdater/SecurityAssertions/CustomApiContractTests ya quedaron anotados inline arriba (6.2,
6.3, 7.6). Los 2 restantes no correspondían a ninguna task existente — se documentan acá:

- [x] **WARNING** `SecurityProvisioner.BuildDashboardCell` usaba `Guid.NewGuid()` para el `uniqueid`
      del control en CADA corrida → el `formxml` del dashboard cambiaba en todo deploy aunque nada
      lógico hubiera cambiado (churn/ruido, rompe el espíritu idempotente del resto del provisioner).
      Fix: `SecuritySpec.DashboardActiveJobsControlUniqueId` /
      `DashboardCompletedWithErrorsControlUniqueId` — GUIDs FIJOS, junto a las demás constantes del
      dashboard. No es lógica testeable unitariamente (es un valor constante consumido por I/O), pero
      el build limpio confirma que el flujo sigue compilando con la firma nueva de `BuildDashboardCell`.
- [x] **SUGGESTION** Contract test que pinnee `SecuritySpec.TemplateToJobRelationshipSchemaName`
      contra el XML real de `solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml`.
      → `tests/Smelter.Contract.Tests/RelationshipSchemaContractTests.cs` (2 tests: la relación existe
      con ese `Name` exacto en la metadata real; es 1:N de ImportJob→ImportTemplate — lo que
      `SecurityProvisioner.SetCascade` asume al castear a `OneToManyRelationshipMetadata`).
- [x] **SUGGESTION** `SecurityProvisioner.UpsertRoles`: si el rol ya existe pero NO es solution
      component de `smlt_core_smelter` (creado a mano antes de que existiera `Smelter.Deploy`, o en
      otra solución), agregarlo vía `AddSolutionComponentRequest` — reconciliación de huérfanos. →
      `EnsureRoleInSolution` (nuevo, llamado desde `UpsertRole` en la rama "ya existe"). Mismo patrón
      de la spec (query por clave natural — `solutioncomponent` por `solutionid`+`objectid`+
      `componenttype`), sin testear unitariamente (I/O puro, mismo estándar que el resto de
      `SecurityProvisioner.cs` — cubierto por contrato de compilación, no por Smelter.Verify porque no
      hay forma de simular "rol huérfano" sin Dataverse real).

## Phase 9: Rename processchunk — Release N+1 (cleanup, gated)

- [x] 9.1 Gate: Fase 7 (`Smelter.Verify`) debe estar en verde en release N antes de continuar.
      → CUMPLIDO: backend-verify run 30364063750/30364284364 VERDE completo ([SEC] + smoke E2E).
- [x] 9.2 `ApiSpec.cs`: quitar `anic_capi_smelter_processchunk` de `Catalogo.Apis`.
- [x] 9.3 `DeleteRequest` explícito del registro `customapi` legacy en el deploy (cleanup, no
      reconciliación silenciosa). → Program.cs paso [8], idempotente (query + delete si existe).
- [x] 9.4 Actualizar el contract test de 6.2: solo `sanic_` processchunk, sin la entrada legacy.
      → ProcessChunkRenameContractTests reescrito al estado N+1 (TDD: rojo→verde);
      SecuritySpecContractTests sin la InlineData legacy; SecurityAssertions exige la API
      legacy AUSENTE en Dev.
- [ ] 9.5 Deploy release N+1; re-correr `Smelter.Verify` — confirmar 4 APIs, verde.
