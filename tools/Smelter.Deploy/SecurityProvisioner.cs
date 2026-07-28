// Provisioning idempotente de seguridad + UX de la MDA (Fases 3 y 4, ver
// openspec/changes/smelter-security/design.md): roles/privilegios, cascade Template→ImportJob,
// ExecutePrivilegeName de las Custom APIs, restricción de la MDA a Configurador, vistas del
// Import Job, botones Ejecutar/Reintentar (RibbonDiffXml) y dashboard+sitemap. Corre después del
// upsert de Custom APIs en Program.cs. Mismo patrón que Program.cs: consultar por clave natural,
// crear con SolutionUniqueName si no existe, actualizar si existe — re-ejecutable sin duplicar.
//
// RIESGO CONOCIDO (no validable en este sandbox — sin acceso a Dataverse, ver openspec/changes/
// smelter-security/tasks.md Fase 1): el nombre de relación N:N "appmoduleroles_association" usado
// en AssociateAppModuleRole y el schema exacto del formxml del dashboard en UpsertDashboardSubArea
// son best-effort a partir de documentación pública — son datos de metadata de ORGANIZACIÓN
// (nombres de relación/forms), no tipos compilados, así que no hay forma de corroborarlos sin un
// Dev real. Si backend-deploy falla en cualquiera de los dos, son los primeros sospechosos.
// El resto (roles/privilegios/cascade/executeprivilegename/vistas) usa mensajes y tipos SDK
// FUERTEMENTE TIPADOS: se verificaron por reflection contra Microsoft.CrmSdk.CoreAssemblies
// 9.0.2.56 real (RolePrivilege.Depth, no .PrivilegeDepth; CascadeConfiguration.Share/Reparent;
// RelatedEntityCollection.TryGetValue; etc.) — si compila, la forma del mensaje es correcta.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class SecurityProvisioner
{
    // Relación N:N appmodule↔role usada por "App Settings > Security roles" en el App Designer.
    // Nombre tomado de la navigation property documentada del Web API para el mismo escenario
    // (ver comentario de riesgo arriba) — confirmar contra Dev en el primer backend-deploy.
    private const string AppModuleRoleRelationship = "appmoduleroles_association";

    // Solution Component Type de `role` — valor documentado públicamente por Microsoft (tabla
    // "solution component (Type) choices/values" del Web API, componenttype=20 para Role). No es un
    // tipo SDK compilado — es un option set de plataforma, mismo estándar de evidencia que ParamType
    // en ApiSpec.cs.
    private const int RoleComponentType = 20;

    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[6] SecurityProvisioner: roles, cascade, ExecutePrivilegeName, MDA, vistas, ribbon, dashboard ...");

        var roleIds = UpsertRoles(client);
        SetCascade(client);
        SetExecutePrivileges(client);
        AssociateAppModuleRole(client, roleIds[SecuritySpec.ConfiguradorRoleName]);
        UpsertViews(client);
        UpsertRibbon(client);
        UpsertDashboardSubArea(client);

        Console.WriteLine("[6] Publicando customizations (roles/vistas/ribbon/dashboard) ...");
        client.Execute(new PublishAllXmlRequest());
        Console.WriteLine("[6]   PublishAllXml OK.");
    }

    // ── 3.2 Roles + privilegios ─────────────────────────────────────────────────────────────────
    private static Dictionary<string, Guid> UpsertRoles(ServiceClient client)
    {
        var businessUnitId = ((WhoAmIResponse)client.Execute(new WhoAmIRequest())).BusinessUnitId;
        var roleIds = new Dictionary<string, Guid>();

        foreach (var role in SecuritySpec.Roles)
        {
            var roleId = UpsertRole(client, role.Name, businessUnitId);
            roleIds[role.Name] = roleId;

            var privilegeIds = ResolvePrivilegeIds(client, role.Privileges);
            var rolePrivileges = role.Privileges
                .Select(p => new RolePrivilege
                {
                    PrivilegeId = privilegeIds[p.PrivilegeName],
                    Depth = p.Depth,
                    BusinessUnitId = businessUnitId,
                })
                .ToArray();

            // Idempotente por diseño (design.md): AddPrivilegesRoleRequest re-aplicado con el mismo
            // privilegio+profundidad no duplica — no hace falta diff previo.
            client.Execute(new AddPrivilegesRoleRequest { RoleId = roleId, Privileges = rolePrivileges });
            Console.WriteLine($"[6]   role '{role.Name}': {rolePrivileges.Length} privilegios aplicados.");
        }

        return roleIds;
    }

    private static Guid UpsertRole(ServiceClient client, string name, Guid businessUnitId)
    {
        var existentes = client.RetrieveMultiple(new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, name),
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, businessUnitId),
                },
            },
        }).Entities;

        if (existentes.Count > 0)
        {
            var roleId = existentes[0].Id;
            Console.WriteLine($"[6]   role '{name}' existente ({roleId}).");
            EnsureRoleInSolution(client, roleId, name);
            return roleId;
        }

        var creado = CrearEnSolucion(client, new Entity("role")
        {
            ["name"] = name,
            ["businessunitid"] = new EntityReference("businessunit", businessUnitId),
        });
        Console.WriteLine($"[6]   role '{name}' creado: {creado}.");
        return creado;
    }

    // Reconciliación de huérfanos: un rol pudo haber sido creado a mano (portal) ANTES de que
    // Smelter.Deploy existiera, o en una solución distinta — en ese caso el query por nombre lo
    // encuentra y UpsertRole lo reusa, pero eso NO lo hace parte de `smlt_core_smelter`: el ALM
    // (export/import de la solución) no se lo llevaría. Esto lo agrega como solution component si
    // todavía no lo es, sin duplicar nada si ya estaba.
    private static void EnsureRoleInSolution(ServiceClient client, Guid roleId, string name)
    {
        var solutionId = client.RetrieveMultiple(new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, Catalogo.SolutionName) } },
        }).Entities.SingleOrDefault()?.Id;

        if (solutionId is null)
        {
            Console.Error.WriteLine($"[6]   WARN: solución '{Catalogo.SolutionName}' no encontrada — no se puede " +
                $"verificar si el rol '{name}' es su solution component.");
            return;
        }

        var yaEsComponente = client.RetrieveMultiple(new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("solutioncomponentid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId.Value),
                    new ConditionExpression("objectid", ConditionOperator.Equal, roleId),
                    new ConditionExpression("componenttype", ConditionOperator.Equal, RoleComponentType),
                },
            },
        }).Entities.Count > 0;

        if (yaEsComponente)
            return;

        client.Execute(new AddSolutionComponentRequest
        {
            ComponentId = roleId,
            ComponentType = RoleComponentType,
            SolutionUniqueName = Catalogo.SolutionName,
            AddRequiredComponents = false,
        });
        Console.WriteLine($"[6]   role '{name}' ({roleId}) NO era solution component de '{Catalogo.SolutionName}' " +
            "— agregado (reconciliación de huérfanos).");
    }

    private static Dictionary<string, Guid> ResolvePrivilegeIds(ServiceClient client, PrivilegeSpec[] privileges)
    {
        var nombres = privileges.Select(p => p.PrivilegeName).Distinct().Cast<object>().ToArray();
        var filas = client.RetrieveMultiple(new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet("privilegeid", "name"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.In, nombres) } },
        }).Entities;

        var mapa = filas.ToDictionary(f => f.GetAttributeValue<string>("name"), f => f.Id);

        var faltantes = privileges.Select(p => p.PrivilegeName).Distinct().Where(n => !mapa.ContainsKey(n)).ToArray();
        if (faltantes.Length > 0)
            throw new InvalidOperationException(
                $"Privilegios no encontrados en Dataverse (¿nombre de tabla o tipo incorrecto?): {string.Join(", ", faltantes)}");

        return mapa;
    }

    // ── 3.3 Cascade Template → ImportJob ───────────────────────────────────────────────────────
    private static void SetCascade(ServiceClient client)
    {
        var response = (RetrieveRelationshipResponse)client.Execute(new RetrieveRelationshipRequest
        {
            Name = SecuritySpec.TemplateToJobRelationshipSchemaName,
        });

        if (response.RelationshipMetadata is not OneToManyRelationshipMetadata oneToMany)
            throw new InvalidOperationException(
                $"La relación '{SecuritySpec.TemplateToJobRelationshipSchemaName}' no es 1:N — no se puede setear cascade.");

        oneToMany.CascadeConfiguration.Share = CascadeType.Cascade;
        oneToMany.CascadeConfiguration.Reparent = CascadeType.Cascade;

        client.Execute(new UpdateRelationshipRequest { Relationship = oneToMany });
        Console.WriteLine("[6]   cascade Template→ImportJob: Share=Cascade, Reparent=Cascade.");
    }

    // ── 3.4 ExecutePrivilegeName de las Custom APIs ────────────────────────────────────────────
    private static void SetExecutePrivileges(ServiceClient client)
    {
        foreach (var api in Catalogo.Apis)
        {
            var existentes = client.RetrieveMultiple(new QueryExpression("customapi")
            {
                ColumnSet = new ColumnSet("customapiid"),
                Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, api.UniqueName) } },
            }).Entities;

            if (existentes.Count == 0)
            {
                // El upsert de Custom APIs (Program.cs, paso [4]) corre antes que SecurityProvisioner
                // — si no aparece acá, algo falló antes. No se aborta el resto del provisioning.
                Console.Error.WriteLine($"[6]   WARN: customapi '{api.UniqueName}' no encontrada — se omite ExecutePrivilegeName.");
                continue;
            }

            client.Update(new Entity("customapi", existentes[0].Id) { ["executeprivilegename"] = api.ExecutePrivilegeName });
            Console.WriteLine($"[6]   {api.UniqueName}.executeprivilegename = {api.ExecutePrivilegeName}");
        }
    }

    // ── 3.5 MDA restringida al Configurador ────────────────────────────────────────────────────
    private static void AssociateAppModuleRole(ServiceClient client, Guid configuradorRoleId)
    {
        var appModule = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, SecuritySpec.AppModuleUniqueName) } },
        }).Entities.SingleOrDefault();

        if (appModule is null)
        {
            Console.Error.WriteLine($"[6]   WARN: appmodule '{SecuritySpec.AppModuleUniqueName}' no encontrado — se omite restricción de roles.");
            return;
        }

        var relationship = new Relationship(AppModuleRoleRelationship);
        var retrieve = (RetrieveResponse)client.Execute(new RetrieveRequest
        {
            Target = new EntityReference("appmodule", appModule.Id),
            ColumnSet = new ColumnSet(false),
            RelatedEntitiesQuery = new RelationshipQueryCollection
            {
                { relationship, new QueryExpression("role") { ColumnSet = new ColumnSet("roleid") } },
            },
        });

        var rolesActuales = retrieve.Entity.RelatedEntities.TryGetValue(relationship, out var related)
            ? related.Entities.Select(e => e.Id).ToArray()
            : [];

        foreach (var rolRoto in rolesActuales.Where(id => id != configuradorRoleId))
        {
            try
            {
                client.Execute(new DisassociateRequest
                {
                    Target = new EntityReference("appmodule", appModule.Id),
                    Relationship = relationship,
                    RelatedEntities = new EntityReferenceCollection { new EntityReference("role", rolRoto) },
                });
                Console.WriteLine($"[6]   AppModuleRole roto desasociado: {rolRoto}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[6]   WARN: no se pudo desasociar el AppModuleRole {rolRoto}: {ex.Message}");
            }
        }

        if (!rolesActuales.Contains(configuradorRoleId))
        {
            client.Execute(new AssociateRequest
            {
                Target = new EntityReference("appmodule", appModule.Id),
                Relationship = relationship,
                RelatedEntities = new EntityReferenceCollection { new EntityReference("role", configuradorRoleId) },
            });
            Console.WriteLine("[6]   MDA asociada al rol Configurador.");
        }
        else
        {
            Console.WriteLine("[6]   MDA ya asociada al rol Configurador.");
        }
    }

    // ── 4.1 Vistas custom de Import Job ────────────────────────────────────────────────────────
    private static void UpsertViews(ServiceClient client)
    {
        var metadata = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = SecuritySpec.Tables.ImportJob,
            EntityFilters = EntityFilters.Entity,
        });
        var objectTypeCode = metadata.EntityMetadata.ObjectTypeCode
            ?? throw new InvalidOperationException(
                $"La entidad {SecuritySpec.Tables.ImportJob} no expone ObjectTypeCode en metadata.");

        foreach (var view in SecuritySpec.Views)
        {
            var layoutXml = view.LayoutXml.Replace(
                SecuritySpec.ObjectTypeCodePlaceholder, objectTypeCode.ToString());
            var existentes = client.RetrieveMultiple(new QueryExpression("savedquery")
            {
                ColumnSet = new ColumnSet("savedqueryid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("name", ConditionOperator.Equal, view.Name),
                        new ConditionExpression("returnedtypecode", ConditionOperator.Equal, SecuritySpec.Tables.ImportJob),
                    },
                },
            }).Entities;

            if (existentes.Count > 0)
            {
                client.Update(new Entity("savedquery", existentes[0].Id)
                {
                    ["description"] = view.Description,
                    ["fetchxml"] = view.FetchXml,
                    ["layoutxml"] = layoutXml,
                });
                Console.WriteLine($"[6]   vista '{view.Name}' existente — actualizada.");
                continue;
            }

            var creada = CrearEnSolucion(client, new Entity("savedquery")
            {
                ["name"] = view.Name,
                ["description"] = view.Description,
                ["returnedtypecode"] = SecuritySpec.Tables.ImportJob,
                ["fetchxml"] = view.FetchXml,
                ["layoutxml"] = layoutXml,
                ["querytype"] = 0,
                ["isquickfindquery"] = false,
                ["isdefault"] = false,
            });
            Console.WriteLine($"[6]   vista '{view.Name}' creada: {creada}.");
        }
    }

    // ── 4.2/4.3 Botones Ejecutar/Reintentar (webresource + RibbonDiffXml vía mini-solución) ─────
    private static void UpsertRibbon(ServiceClient client)
    {
        var js = AssetLoader.ReadText("sanic_smlt_importjob_actions.js");
        UpsertWebResource(client, "sanic_smlt_importjob_actions.js", "WebResource | SMLT | Import Job Actions", js);

        var ribbonDiffXml = AssetLoader.ReadText("sanic_tbl_smlt_importjob.RibbonDiffXml.xml");
        var solutionZip = SolutionZipBuilder.BuildRibbonSolution(
            solutionUniqueName: Catalogo.SolutionName,
            publisherUniqueName: "Sistemas_Abiertos_Nicaragua",
            publisherPrefix: "sanic",
            version: "1.0.0.0",
            ribbonDiffXmlByEntity: new Dictionary<string, string> { [SecuritySpec.Tables.ImportJob] = ribbonDiffXml });

        client.Execute(new ImportSolutionRequest
        {
            CustomizationFile = solutionZip,
            PublishWorkflows = true,
            OverwriteUnmanagedCustomizations = true,
        });
        Console.WriteLine("[6]   RibbonDiffXml de Import Job (Ejecutar/Reintentar) importado.");
    }

    private static void UpsertWebResource(ServiceClient client, string name, string displayName, string content)
    {
        var contentB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

        var existentes = client.RetrieveMultiple(new QueryExpression("webresource")
        {
            ColumnSet = new ColumnSet("webresourceid"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, name) } },
        }).Entities;

        if (existentes.Count > 0)
        {
            client.Update(new Entity("webresource", existentes[0].Id) { ["content"] = contentB64 });
            Console.WriteLine($"[6]   webresource '{name}' existente — content actualizado.");
            return;
        }

        var creado = CrearEnSolucion(client, new Entity("webresource")
        {
            ["name"] = name,
            ["displayname"] = displayName,
            ["webresourcetype"] = new OptionSetValue(3), // Script (JScript)
            ["content"] = contentB64,
        });
        Console.WriteLine($"[6]   webresource '{name}' creado: {creado}.");
    }

    // ── 4.5 Dashboard + entrada de sitemap ─────────────────────────────────────────────────────
    private static void UpsertDashboardSubArea(ServiceClient client)
    {
        var jobsActivosViewId = RetrieveViewId(client, SecuritySpec.ActiveJobsView.Name);
        var completadosConErroresViewId = RetrieveViewId(client, SecuritySpec.CompletedWithErrorsView.Name);

        var dashboardId = UpsertDashboard(client, jobsActivosViewId, completadosConErroresViewId);
        UpsertSiteMapEntry(client, dashboardId);
    }

    private static Guid RetrieveViewId(ServiceClient client, string viewName) =>
        client.RetrieveMultiple(new QueryExpression("savedquery")
        {
            ColumnSet = new ColumnSet("savedqueryid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, viewName),
                    new ConditionExpression("returnedtypecode", ConditionOperator.Equal, SecuritySpec.Tables.ImportJob),
                },
            },
        }).Entities.Single().Id;

    private static Guid UpsertDashboard(ServiceClient client, Guid jobsActivosViewId, Guid completadosConErroresViewId)
    {
        var formXml = BuildDashboardFormXml(jobsActivosViewId, completadosConErroresViewId);

        var existentes = client.RetrieveMultiple(new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, SecuritySpec.DashboardName),
                    new ConditionExpression("type", ConditionOperator.Equal, 0), // Dashboard
                },
            },
        }).Entities;

        if (existentes.Count > 0)
        {
            client.Update(new Entity("systemform", existentes[0].Id) { ["formxml"] = formXml });
            Console.WriteLine($"[6]   dashboard '{SecuritySpec.DashboardName}' existente — actualizado.");
            return existentes[0].Id;
        }

        var creado = CrearEnSolucion(client, new Entity("systemform")
        {
            ["name"] = SecuritySpec.DashboardName,
            ["description"] = SecuritySpec.DashboardDescription,
            ["type"] = new OptionSetValue(0), // Dashboard
            ["formxml"] = formXml,
        });
        Console.WriteLine($"[6]   dashboard '{SecuritySpec.DashboardName}' creado: {creado}.");
        return creado;
    }

    // Schema de "single-stream dashboard" (subgrid por control) documentado por Microsoft para
    // crear dashboards por código — best-effort, ver comentario de riesgo al inicio del archivo.
    private static string BuildDashboardFormXml(Guid jobsActivosViewId, Guid completadosConErroresViewId) =>
        "<form><dashboard>" +
        "<rows>" +
        "<row>" +
        BuildDashboardCell("id_smlt_dashboard_activos", "Jobs Activos", SecuritySpec.Tables.ImportJob,
            jobsActivosViewId, SecuritySpec.DashboardActiveJobsControlUniqueId) +
        BuildDashboardCell("id_smlt_dashboard_errores", "Completados con Errores", SecuritySpec.Tables.ImportJob,
            completadosConErroresViewId, SecuritySpec.DashboardCompletedWithErrorsControlUniqueId) +
        "</row>" +
        "</rows>" +
        "</dashboard></form>";

    // `uniqueId` viene de SecuritySpec (GUID FIJO, no Guid.NewGuid() por corrida) — de lo contrario
    // el formxml cambiaría en TODO deploy aunque nada lógico haya cambiado (churn/ruido de diffs).
    private static string BuildDashboardCell(string controlId, string title, string entityLogicalName, Guid viewId, string uniqueId) =>
        "<cell showlabel=\"true\" showborder=\"true\" labelwidth=\"auto\">" +
        $"<labelwidth>auto</labelwidth><label description=\"{title}\" languagecode=\"1033\" />" +
        $"<control id=\"{controlId}\" uniqueid=\"{uniqueId}\" type=\"subgrid\">" +
        "<parameters>" +
        $"<ViewId>{{{viewId}}}</ViewId>" +
        $"<EntityLogicalName>{entityLogicalName}</EntityLogicalName>" +
        "</parameters>" +
        "</control>" +
        "</cell>";

    private static void UpsertSiteMapEntry(ServiceClient client, Guid dashboardId)
    {
        var sitemap = client.RetrieveMultiple(new QueryExpression("sitemap")
        {
            ColumnSet = new ColumnSet("sitemapid", "sitemapxml"),
            Criteria = { Conditions = { new ConditionExpression("sitemapnameunique", ConditionOperator.Equal, SecuritySpec.AppModuleUniqueName) } },
        }).Entities.SingleOrDefault();

        if (sitemap is null)
        {
            Console.Error.WriteLine($"[6]   WARN: sitemap de '{SecuritySpec.AppModuleUniqueName}' no encontrado — se omite entrada del dashboard.");
            return;
        }

        var sitemapXml = sitemap.GetAttributeValue<string>("sitemapxml") ?? "";
        var subAreaId = "subarea_smlt_dashboard";
        if (sitemapXml.Contains(subAreaId))
        {
            Console.WriteLine($"[6]   sitemap: entrada del '{SecuritySpec.DashboardName}' ya existe.");
            return;
        }

        var doc = System.Xml.Linq.XDocument.Parse(sitemapXml);
        var firstGroup = doc.Descendants("Group").FirstOrDefault();
        if (firstGroup is null)
        {
            Console.Error.WriteLine("[6]   WARN: sitemap sin ningún <Group> — se omite entrada del dashboard (estructura inesperada).");
            return;
        }

        var subArea = new System.Xml.Linq.XElement("SubArea",
            new System.Xml.Linq.XAttribute("Id", subAreaId),
            new System.Xml.Linq.XAttribute("Url", $"/main.aspx?pagetype=dashboard&id=%7b{dashboardId:D}%7d"),
            new System.Xml.Linq.XAttribute("AvailableOffline", "false"),
            new System.Xml.Linq.XElement("Titles",
                new System.Xml.Linq.XElement("Title",
                    new System.Xml.Linq.XAttribute("LCID", "1033"),
                    new System.Xml.Linq.XAttribute("Title", SecuritySpec.DashboardName))));
        firstGroup.AddFirst(subArea);

        client.Update(new Entity("sitemap", sitemap.Id) { ["sitemapxml"] = doc.ToString() });
        Console.WriteLine($"[6]   sitemap: entrada '{SecuritySpec.DashboardName}' agregada.");
    }

    // Crea un registro DENTRO de la solución `smlt_core_smelter` — mismo helper que Program.cs
    // (local function, no accesible entre archivos top-level statements), duplicado acá a propósito.
    private static Guid CrearEnSolucion(ServiceClient client, Entity e)
    {
        var req = new CreateRequest { Target = e };
        req.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
        return ((CreateResponse)client.Execute(req)).id;
    }
}
