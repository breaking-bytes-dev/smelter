// Provisión idempotente del dashboard CLÁSICO de Smelter (spec: DashboardSpec.cs). Corre justo después del [6] y antes del [6a]:
//   1. crea (o actualiza el formxml de) el systemform type 0 dentro de la solución;
//   2. nombre 1033 + 3082 con SetLocLabels (igual que el resto de componentes) y publica el dashboard;
//   3. lo agrega como componente de la app (AddAppComponents, componenttype 60) si falta;
//   4. apunta el SubArea del sitemap al dashboard nuevo;
//   5. saca de la app y BORRA el interaction-centric anterior (destructivo: solo con --allow-destructive).
// Re-correr es un no-op. Solo DEV.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class DashboardProvisioner
{
    private const int DashboardType = 0;

    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[6b] DashboardProvisioner: dashboard clásico 'Smelter Dashboard' (DashboardSpec.cs) ...");
        var formXml = DashboardCatalogo.BuildFormXml();

        var existing = client.RetrieveMultiple(new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid", "type", "formxml"),
            Criteria = { Conditions = { new ConditionExpression("formid", ConditionOperator.Equal, DashboardCatalogo.FormId) } },
        }).Entities.SingleOrDefault();

        if (existing is null)
        {
            var req = new CreateRequest
            {
                Target = new Entity("systemform", DashboardCatalogo.FormId)
                {
                    ["name"] = DashboardCatalogo.Name.En,
                    ["type"] = new OptionSetValue(DashboardType),
                    ["objecttypecode"] = "none",
                    ["formxml"] = formXml,
                },
            };
            req.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
            client.Execute(req);
            Console.WriteLine($"[6b]   dashboard clásico creado: {DashboardCatalogo.FormId}.");
        }
        else if (!XmlEquivalent(existing.GetAttributeValue<string>("formxml"), formXml))
        {
            client.Update(new Entity("systemform", DashboardCatalogo.FormId) { ["formxml"] = formXml });
            Console.WriteLine("[6b]   dashboard: formxml actualizado.");
        }
        else
        {
            Console.WriteLine("[6b]   dashboard: formxml al día.");
        }

        if (LocLabels.Ensure(client, "systemform", DashboardCatalogo.FormId, "name", DashboardCatalogo.Name))
            Console.WriteLine($"[6b]   dashboard: nombre 1033 '{DashboardCatalogo.Name.En}' / 3082 '{DashboardCatalogo.Name.Es}'.");
        client.Execute(new PublishXmlRequest { ParameterXml = DashboardCatalogo.PublishXml() });

        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid", "appmoduleidunique"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();
        if (app is null)
        {
            Console.Error.WriteLine($"[6b]   WARN: app '{MdaSpec.AppUniqueName}' no encontrada — se omiten componente, sitemap y limpieza.");
            return;
        }

        var pinned = client.RetrieveMultiple(new QueryExpression("appmodulecomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            Criteria = { Conditions = { new ConditionExpression("appmoduleidunique", ConditionOperator.Equal, app.GetAttributeValue<Guid>("appmoduleidunique")) } },
        }).Entities.Select(e => e.GetAttributeValue<Guid>("objectid")).ToHashSet();

        if (!pinned.Contains(DashboardCatalogo.FormId))
        {
            client.Execute(new AddAppComponentsRequest
            {
                AppId = app.Id,
                Components = new EntityReferenceCollection { new EntityReference("systemform", DashboardCatalogo.FormId) },
            });
            Console.WriteLine("[6b]   dashboard agregado como componente de la app.");
        }

        // Sitemap: si el SubArea ya existe se le cambia el Url; si no, se crea (mismo helper que usaba el [6]).
        SecurityProvisioner.UpsertSiteMapEntry(client, DashboardCatalogo.FormId);

        // Limpieza del interaction-centric: sacarlo de la app y borrarlo. Si ya no existe, nada que hacer.
        var legacyId = DashboardCatalogo.LegacyInteractionCentricId;
        var legacy = client.RetrieveMultiple(new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid"),
            Criteria = { Conditions = { new ConditionExpression("formid", ConditionOperator.Equal, legacyId) } },
        }).Entities.SingleOrDefault();
        if (legacy is null)
        {
            Console.WriteLine("[6b]   dashboard interaction-centric anterior ya no existe — nada que borrar.");
        }
        else if (!DeployOptions.AllowDestructive)
        {
            Console.WriteLine("[6b]   borrado del dashboard interaction-centric OMITIDO (destructivo; usar --allow-destructive o SMELTER_ALLOW_DESTRUCTIVE=true).");
        }
        else
        {
            if (pinned.Contains(legacyId))
                client.Execute(new RemoveAppComponentsRequest
                {
                    AppId = app.Id,
                    Components = new EntityReferenceCollection { new EntityReference("systemform", legacyId) },
                });
            client.Delete("systemform", legacyId);
            Console.WriteLine($"[6b]   dashboard interaction-centric {legacyId} eliminado.");
        }

        try { client.Execute(new PublishXmlRequest { ParameterXml = MdaComponentsCatalogo.PublishAppXml(app.Id.ToString()) }); }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
        {
            client.Execute(new PublishXmlRequest { ParameterXml = MdaComponentsCatalogo.PublishAppXml(MdaSpec.AppUniqueName) });
        }
        Console.WriteLine("[6b] Dashboard OK.");
    }

    /// <summary>Compara dos formxml ignorando el formato (Dataverse puede devolverlo re-serializado).</summary>
    internal static bool XmlEquivalent(string? a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return false;
        try { return System.Xml.Linq.XNode.DeepEquals(System.Xml.Linq.XElement.Parse(a), System.Xml.Linq.XElement.Parse(b)); }
        catch (System.Xml.XmlException) { return false; }
    }
}
