// Provisión idempotente de las vistas dedicadas de lookup (FormPolishCatalogo.LookupViews): crea la vista pública (id fijo, dentro de
// la solución) o la actualiza si fetch/layout difieren, le pone nombre 1033 + 3082 y publica la entidad. Corre ANTES del pin de la
// MDA ([4g], que la referencia) y del FormPolish ([4j], que hace que el lookup del form la use). Re-correr es un no-op.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class LookupViewsProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4f2] LookupViewsProvisioner: vistas dedicadas de lookup (FormPolishSpec.cs) ...");

        foreach (var spec in FormPolishCatalogo.LookupViews)
        {
            var objectTypeCode = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = spec.TargetTable,
                EntityFilters = EntityFilters.Entity,
            })).EntityMetadata.ObjectTypeCode
                ?? throw new InvalidOperationException($"La entidad {spec.TargetTable} no expone ObjectTypeCode en metadata.");
            var layoutXml = spec.LayoutXml.Replace(SecuritySpec.ObjectTypeCodePlaceholder, objectTypeCode.ToString());

            var existing = client.RetrieveMultiple(new QueryExpression("savedquery")
            {
                ColumnSet = new ColumnSet("fetchxml", "layoutxml"),
                Criteria = { Conditions = { new ConditionExpression("savedqueryid", ConditionOperator.Equal, spec.ViewId) } },
            }).Entities.SingleOrDefault();

            var touched = false;
            if (existing is null)
            {
                var req = new CreateRequest
                {
                    Target = new Entity("savedquery", spec.ViewId)
                    {
                        ["name"] = spec.Name.En,
                        ["returnedtypecode"] = spec.TargetTable,
                        ["fetchxml"] = spec.FetchXml,
                        ["layoutxml"] = layoutXml,
                        ["querytype"] = 0,
                        ["isquickfindquery"] = false,
                        ["isdefault"] = false,
                    },
                };
                req.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
                client.Execute(req);
                Console.WriteLine($"[4f2]   vista '{spec.Name.En}' creada: {spec.ViewId}.");
                touched = true;
            }
            else if (!DashboardProvisioner.XmlEquivalent(existing.GetAttributeValue<string>("fetchxml"), spec.FetchXml)
                     || !DashboardProvisioner.XmlEquivalent(existing.GetAttributeValue<string>("layoutxml"), layoutXml))
            {
                client.Update(new Entity("savedquery", spec.ViewId) { ["fetchxml"] = spec.FetchXml, ["layoutxml"] = layoutXml });
                Console.WriteLine($"[4f2]   vista '{spec.Name.En}': fetch/layout actualizados.");
                touched = true;
            }

            if (LocLabels.Ensure(client, "savedquery", spec.ViewId, "name", spec.Name))
            {
                Console.WriteLine($"[4f2]   vista '{spec.Name.En}': nombre 1033 + 3082 '{spec.Name.Es}'.");
                touched = true;
            }

            if (touched)
                client.Execute(new PublishXmlRequest
                {
                    ParameterXml = $"<importexportxml><entities><entity>{spec.TargetTable}</entity></entities></importexportxml>",
                });
            else
                Console.WriteLine($"[4f2]   vista '{spec.Name.En}' ya al día — nada que hacer.");
        }

        Console.WriteLine("[4f2] LookupViews OK.");
    }
}
