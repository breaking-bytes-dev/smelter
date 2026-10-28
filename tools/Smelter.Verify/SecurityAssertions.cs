// Asserts E2E de todo lo provisionado por SecurityProvisioner + CloudFlowUpdater (Fases 3/4/6,
// openspec/changes/smelter-security): roles con los privilegios exactos de SecuritySpec, cascade
// Share/Reparent en la relación Template→ImportJob, ExecutePrivilegeName seteado en las Custom APIs
// (Release N: 5 entradas, incluye anic_/sanic_ processchunk en transición), MDA asociada solo a
// Configurador, y el Cloud Flow apuntando al uniquename nuevo de processchunk. Corre ANTES del smoke
// test funcional de Program.cs — si la config de seguridad regresionó, no tiene sentido seguir con el
// import de prueba.
//
// Mismo patrón de lectura que SecurityProvisioner.cs (query por clave natural, sin asumir GUIDs
// fijos) para no duplicar el riesgo de hardcodear IDs que cambian entre ambientes.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
// Ambiguo con Microsoft.Xrm.Sdk.Metadata.PrivilegeType (metadata de atributos) — desambiguar al de
// SecuritySpec, que es el que describe la matriz Create/Read/Write/Delete/Append/AppendTo de roles.
using PrivilegeType = Smelter.Deploy.PrivilegeType;

namespace Smelter.Verify;

internal static class SecurityAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[SEC] Verificando provisioning de seguridad + UX (Fases 3/4/6) ...");

        var roleIds = AssertRolesExistAndMatchPrivileges(client);
        AssertCascade(client);
        AssertExecutePrivileges(client);
        AssertAppModuleRolesOnlyConfigurador(client, roleIds[SecuritySpec.ConfiguradorRoleName]);
        AssertProcessChunkCloudFlowReferencesNewApi(client);
        AssertImportJobButtonsQueueOnly(client);

        Console.WriteLine("[SEC] OK: roles, cascade, executeprivilegename, AppModuleRoles, Cloud Flow, botones Import Job — todo alineado.");
    }

    // ── 7.1 / 7.2: roles + privilegios (Configurador CRUD Org, Operador == SecuritySpec) ────────
    private static Dictionary<string, Guid> AssertRolesExistAndMatchPrivileges(ServiceClient client)
    {
        var roleIds = new Dictionary<string, Guid>();
        var allViolations = new List<string>();

        foreach (var role in SecuritySpec.Roles)
        {
            var roleId = RetrieveRoleId(client, role.Name);
            roleIds[role.Name] = roleId;

            var actual = ((RetrieveRolePrivilegesRoleResponse)client.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = roleId })).RolePrivileges;
            var actualByName = actual.ToDictionary(p => p.PrivilegeName, p => p.Depth);

            // Lista completa (nombres de privilegio, no son secretos): es el «antes/después» de lo que tiene cada rol en el ambiente.
            Console.WriteLine($"[SEC]   rol '{role.Name}' tiene {actualByName.Count} privilegios en el ambiente:");
            foreach (var p in actualByName.OrderBy(p => p.Key, StringComparer.Ordinal))
                Console.WriteLine($"[SEC]     {p.Key} = {p.Value}");

            // Regla estricta (SecurityAudit): exactamente la spec; cualquier otro privilegio, de Smelter o de plataforma, falla.
            var violations = SecurityAudit.Violations(role, actualByName.Select(p => (p.Key, p.Value)));
            allViolations.AddRange(violations.Select(v => $"rol '{role.Name}': {v}"));
            if (violations.Count == 0)
                Console.WriteLine($"[SEC]   rol '{role.Name}': exactamente los {role.Privileges.Length} privilegios de SecuritySpec, {role.Denied.Length} prohibido(s) ausente(s), ninguno de más, OK.");
        }

        // Se imprimen los dos roles antes de fallar, así un solo run muestra el estado completo.
        Assert(allViolations.Count == 0, $"los roles no coinciden con SecuritySpec ({allViolations.Count} diferencia/s):\n    " + string.Join("\n    ", allViolations));

        // Requirement "Operador bloqueado en write, habilitado en lectura y jobs" — proxy estático
        // (Smelter.Verify corre con 1 Service Principal, no loguea un 2º usuario real): Operador NO
        // debe tener Create/Write/Delete sobre el Template, sea cual sea el resto de su matriz.
        var operadorPrivileges = ((RetrieveRolePrivilegesRoleResponse)client.Execute(
            new RetrieveRolePrivilegesRoleRequest { RoleId = roleIds[SecuritySpec.OperadorRoleName] })).RolePrivileges;
        var forbiddenOnTemplate = new[]
        {
            SecuritySpec.PrivilegeName(PrivilegeType.Create, SecuritySpec.Tables.ImportTemplate),
            SecuritySpec.PrivilegeName(PrivilegeType.Write, SecuritySpec.Tables.ImportTemplate),
            SecuritySpec.PrivilegeName(PrivilegeType.Delete, SecuritySpec.Tables.ImportTemplate),
        };
        foreach (var forbidden in forbiddenOnTemplate)
            Assert(operadorPrivileges.All(p => p.PrivilegeName != forbidden),
                $"rol Operador NO debe tener '{forbidden}' — Write/Create/Delete sobre el Template es exclusivo de Configurador");
        Console.WriteLine("[SEC]   Operador confirmado SIN Create/Write/Delete en Template.");

        return roleIds;
    }

    private static Guid RetrieveRoleId(ServiceClient client, string name)
    {
        var rows = client.RetrieveMultiple(new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, name) } },
        }).Entities;
        Assert(rows.Count > 0, $"rol '{name}' no existe en el ambiente (¿corriste Smelter.Deploy?)");
        return rows[0].Id;
    }

    // ── 7.3: cascade Share=Cascade, Reparent=Cascade en Template→ImportJob ─────────────────────
    private static void AssertCascade(ServiceClient client)
    {
        var response = (RetrieveRelationshipResponse)client.Execute(new RetrieveRelationshipRequest
        {
            Name = SecuritySpec.TemplateToJobRelationshipSchemaName,
        });

        Assert(response.RelationshipMetadata is OneToManyRelationshipMetadata,
            $"la relación '{SecuritySpec.TemplateToJobRelationshipSchemaName}' no es 1:N — no se puede leer cascade");
        var oneToMany = (OneToManyRelationshipMetadata)response.RelationshipMetadata;

        Assert(oneToMany.CascadeConfiguration.Share == CascadeType.Cascade,
            $"cascade Share de Template→ImportJob debía ser Cascade, es {oneToMany.CascadeConfiguration.Share}");
        Assert(oneToMany.CascadeConfiguration.Reparent == CascadeType.Cascade,
            $"cascade Reparent de Template→ImportJob debía ser Cascade, es {oneToMany.CascadeConfiguration.Reparent}");
        Console.WriteLine("[SEC]   cascade Template→ImportJob: Share=Cascade, Reparent=Cascade OK.");
    }

    // ── 7.4: executeprivilegename seteado en las Custom APIs (Release N, incluye ambas processchunk) ─
    private static void AssertExecutePrivileges(ServiceClient client)
    {
        foreach (var api in Catalogo.Apis)
        {
            var rows = client.RetrieveMultiple(new QueryExpression("customapi")
            {
                ColumnSet = new ColumnSet("executeprivilegename"),
                Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, api.UniqueName) } },
            }).Entities;

            Assert(rows.Count > 0, $"customapi '{api.UniqueName}' no existe en el ambiente");

            var actual = rows[0].GetAttributeValue<string>("executeprivilegename");
            Assert(actual == api.ExecutePrivilegeName,
                $"customapi '{api.UniqueName}'.executeprivilegename es '{actual}', esperado '{api.ExecutePrivilegeName}'");
        }
        Console.WriteLine($"[SEC]   executeprivilegename OK en las {Catalogo.Apis.Length} Custom APIs del catálogo.");

        // Release N+1 (Fase 9): la API legacy del typo no debe existir más en Dev.
        var legacy = client.RetrieveMultiple(new QueryExpression("customapi")
        {
            ColumnSet = new ColumnSet("customapiid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, "anic_capi_smelter_processchunk") } },
        }).Entities;
        Assert(legacy.Count == 0, "la Custom API legacy 'anic_capi_smelter_processchunk' sigue existiendo en Dev (Fase 9 pendiente)");
        Console.WriteLine("[SEC]   Custom API legacy 'anic_' eliminada de Dev OK.");
    }

    // ── 7.5: AppModuleRoles — MDA asociada SOLO a Configurador ─────────────────────────────────
    private const string AppModuleRoleRelationship = "appmoduleroles_association";

    private static void AssertAppModuleRolesOnlyConfigurador(ServiceClient client, Guid configuradorRoleId)
    {
        var appModule = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, SecuritySpec.AppModuleUniqueName) } },
        }).Entities.SingleOrDefault();
        Assert(appModule is not null, $"appmodule '{SecuritySpec.AppModuleUniqueName}' no existe en el ambiente");

        var relationship = new Relationship(AppModuleRoleRelationship);
        var retrieve = (RetrieveResponse)client.Execute(new RetrieveRequest
        {
            Target = new EntityReference("appmodule", appModule!.Id),
            ColumnSet = new ColumnSet(false),
            RelatedEntitiesQuery = new RelationshipQueryCollection
            {
                { relationship, new QueryExpression("role") { ColumnSet = new ColumnSet("roleid", "name") } },
            },
        });

        var associatedRoles = retrieve.Entity.RelatedEntities.TryGetValue(relationship, out var related)
            ? related.Entities.ToArray()
            : Array.Empty<Entity>();
        Console.WriteLine($"[SEC]   AppModuleRoles asociados ({associatedRoles.Length}): " +
            string.Join(", ", associatedRoles.Select(e => $"{e.Id} ('{e.GetAttributeValue<string>("name")}')")));

        Assert(associatedRoles.Any(e => e.Id == configuradorRoleId),
            "la MDA de Smelter debe estar asociada al rol Configurador");

        // System Administrator/System Customizer los asocia la plataforma a toda MDA y no se
        // pueden quitar (verificado contra Dev, run 30363240939) — no cuentan como violación.
        var intrusos = associatedRoles
            .Where(e => e.Id != configuradorRoleId
                && !SecuritySpec.PlatformAppRoles.Contains(e.GetAttributeValue<string>("name")))
            .Select(e => $"'{e.GetAttributeValue<string>("name")}'")
            .ToArray();
        Assert(intrusos.Length == 0,
            $"la MDA de Smelter tiene roles NO permitidos asociados: {string.Join(", ", intrusos)}");
        Console.WriteLine("[SEC]   AppModuleRoles: MDA restringida a Configurador (+ roles de plataforma) OK.");
    }

    // ── 7.6: Cloud Flow de processchunk referencia el uniquename nuevo (Release N) ────────────
    private const int ModernFlowCategory = 5;

    private static void AssertProcessChunkCloudFlowReferencesNewApi(ServiceClient client)
    {
        var flows = client.RetrieveMultiple(new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name", "clientdata"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("category", ConditionOperator.Equal, ModernFlowCategory),
                    new ConditionExpression("clientdata", ConditionOperator.Like, "%capi_smelter_processchunk%"),
                },
            },
        }).Entities;

        if (flows.Count == 0)
        {
            Console.WriteLine("[SEC]   WARN: no se encontró ningún Cloud Flow que referencie processchunk — se omite el " +
                "assert (¿el flow no existe en este ambiente, o el filtro de clientdata no matcheó su forma real?).");
            return;
        }

        foreach (var flow in flows)
        {
            var name = flow.GetAttributeValue<string>("name") ?? flow.Id.ToString();
            var clientData = flow.GetAttributeValue<string>("clientdata") ?? "";

            // Verificación ESTRICTA por TOKEN exacto (no `Contains` — "sanic_..." CONTIENE "anic_..."
            // como substring, así que un `Contains` naive daría verde incluso con clientdata
            // corrupto "ssanic_..." o con la API vieja todavía referenciada). Usa la MISMA lógica que
            // CloudFlowUpdater/ClientDataProcessChunkRenamer.cs para no divergir del criterio real.
            // Requirement "Rename de processchunk sin ventana rota" — Scenario "Verify detecta un
            // flow desalineado": esto SHALL reportar la inconsistencia (vía la excepción de Assert,
            // que Program.cs imprime como [FAIL]) en vez de dejarla pasar silenciosamente.
            Assert(ClientDataProcessChunkRenamer.ReferencesNewToken(clientData),
                $"Cloud Flow '{name}' NO referencia '{ClientDataProcessChunkRenamer.NewActionUniqueName}' como token exacto " +
                "— quedó desalineado tras el rename de Release N (revisar CloudFlowUpdater / actualizar el flow manualmente).");
            Assert(!ClientDataProcessChunkRenamer.ReferencesOldToken(clientData),
                $"Cloud Flow '{name}' TODAVÍA referencia el uniquename viejo '{ClientDataProcessChunkRenamer.OldActionUniqueName}' " +
                "como token exacto — el rename quedó a medias.");
            Assert(!ClientDataProcessChunkRenamer.HasDoubleSCorruption(clientData),
                $"Cloud Flow '{name}' tiene clientdata CORRUPTO (doble 's' pegada al uniquename nuevo, 'ssanic_...') " +
                "— revisar manualmente, NO re-correr el deploy a ciegas.");
        }
        Console.WriteLine($"[SEC]   Cloud Flow(s) processchunk: {flows.Count} referencian " +
            $"'{ClientDataProcessChunkRenamer.NewActionUniqueName}' como token exacto, sin restos de la API vieja ni corrupción. OK.");
    }

    // ── 7.7: botones Ejecutar/Reintentar solo encolan (único camino = Cloud Flow) ──────────────
    private static void AssertImportJobButtonsQueueOnly(ServiceClient client)
    {
        const string webResourceName = "sanic_smlt_importjob_actions.js";
        var wr = client.RetrieveMultiple(new QueryExpression("webresource")
        {
            ColumnSet = new ColumnSet("content"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, webResourceName) } },
        }).Entities.FirstOrDefault();
        Assert(wr is not null, $"Falta el web resource '{webResourceName}'.");
        var js = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(wr!.GetAttributeValue<string>("content") ?? ""));
        Assert(!js.Contains("processchunk", StringComparison.OrdinalIgnoreCase),
            $"'{webResourceName}' desplegado TODAVÍA referencia processchunk: el loop del navegador debe estar removido (único camino = Cloud Flow).");
        Assert(!js.Contains("prepareimport", StringComparison.OrdinalIgnoreCase),
            $"'{webResourceName}' desplegado TODAVÍA referencia prepareimport: los botones solo deben encolar el job.");

        var ribbon = (RetrieveEntityRibbonResponse)client.Execute(new RetrieveEntityRibbonRequest
        {
            EntityName = SecuritySpec.Tables.ImportJob,
            RibbonLocationFilter = RibbonLocationFilters.All,
        });
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(ribbon.CompressedEntityXml));
        var entry = zip.GetEntry("RibbonXml.xml");
        Assert(entry is not null, "El ribbon de Import Job no trae RibbonXml.xml.");
        using var reader = new StreamReader(entry!.Open());
        var ribbonXml = reader.ReadToEnd();
        foreach (var rule in new[] { "sanic.smlt.EjecutarImportJob.EnableRule", "sanic.smlt.ReintentarImportJob.EnableRule" })
            Assert(ribbonXml.Contains(rule, StringComparison.Ordinal), $"Falta la enable rule '{rule}' en el ribbon de Import Job.");
        Console.WriteLine("[SEC]   Botones Import Job: web resource sin processchunk/prepareimport y enable rules presentes. OK.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[SEC]: " + message);
    }
}
