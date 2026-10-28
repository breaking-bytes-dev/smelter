// Asserts E2E de FormPolishSpec.cs: cada main form activo de las entidades de la spec ya está al día
// (FormPolishCatalogo.Apply sobre una copia no cambia nada y los controles pedidos existen) y la vista default del
// log tiene la columna pedida. Solo lee.

using System.Xml.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class FormPolishAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[POLISH] Verificando forms y vistas (FormPolishSpec.cs) ...");

        foreach (var entity in FormPolishCatalogo.FormEntities)
        {
            // Dataverse devuelve el formxml solo en el idioma de quien lee: se fusionan 1033 + 3082 (FormLabelsReader).
            var mains = FormLabelsReader.RetrieveMainForms(client, entity, onlyActive: true);
            Assert(mains.Count > 0, $"sin main form activo @ {entity}");

            foreach (var form in mains)
            {
                var name = form.GetAttributeValue<string>("name");
                var xml = form.GetAttributeValue<string>("formxml");
                var pending = FormPolishCatalogo.Apply(XDocument.Parse(xml), entity);
                Assert(pending.Count == 0, $"el form '{name}' de {entity} no está al día: {string.Join("; ", pending)}");

                var doc = XDocument.Parse(xml);
                var controls = doc.Descendants("control").Select(c => (string?)c.Attribute("id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var add in FormPolishCatalogo.AddedFields.Where(a => a.Entity == entity))
                    Assert(MdaXml.FormAttributes(xml).Contains(add.Field), $"el form '{name}' no tiene {add.Field}");
                foreach (var sub in FormPolishCatalogo.Subgrids.Where(s => s.Entity == entity))
                    Assert(controls.Contains(sub.SubgridId), $"el form '{name}' no tiene el subgrid {sub.SubgridId}");
            }
            Console.WriteLine($"[POLISH]   {entity}: main form al día.");
        }

        foreach (var spec in FormPolishCatalogo.ViewColumns)
        {
            var view = client.Retrieve("savedquery", spec.ViewId, new ColumnSet("layoutxml", "fetchxml", "name"));
            var (_, _, changed) = ViewXmlEditor.TryAddColumn(
                view.GetAttributeValue<string>("layoutxml"), view.GetAttributeValue<string>("fetchxml"),
                spec.AfterColumn, spec.Column, spec.Width);
            Assert(!changed, $"la vista '{view.GetAttributeValue<string>("name")}' no tiene {spec.Column}");
            Console.WriteLine($"[POLISH]   vista '{view.GetAttributeValue<string>("name")}': {spec.Column} OK.");
        }

        foreach (var spec in FormPolishCatalogo.LookupViews)
        {
            var view = client.RetrieveMultiple(new QueryExpression("savedquery")
            {
                ColumnSet = new ColumnSet("fetchxml", "querytype", "name"),
                Criteria = { Conditions = { new ConditionExpression("savedqueryid", ConditionOperator.Equal, spec.ViewId) } },
            }).Entities.SingleOrDefault();
            Assert(view is not null, $"no existe la vista de lookup '{spec.Name.En}' ({spec.ViewId})");
            Assert(DashboardProvisioner.XmlEquivalent(view!.GetAttributeValue<string>("fetchxml"), spec.FetchXml),
                $"la vista '{spec.Name.En}' no filtra como la spec (solo templates Active)");
            var names = LocLabels.Get(client, "savedquery", spec.ViewId, "name");
            Assert(LocLabels.Matches(names, spec.Name), $"la vista '{spec.Name.En}' no tiene nombre 1033 + 3082 ('{names.GetValueOrDefault(Lcids.Spanish)}')");
            Console.WriteLine($"[POLISH]   vista de lookup '{spec.Name.En}' OK (el control {spec.Field} la usa: form al día).");
        }

        foreach (var spec in FormPolishCatalogo.ViewOrders)
        {
            var view = client.Retrieve("savedquery", spec.ViewId, new ColumnSet("fetchxml", "name"));
            var (_, changed) = ViewXmlEditor.TrySetOrder(view.GetAttributeValue<string>("fetchxml"), spec.Attribute, spec.Descending);
            Assert(!changed, $"la vista '{view.GetAttributeValue<string>("name")}' no ordena por {spec.Attribute} {(spec.Descending ? "descendente" : "ascendente")} (más nuevo primero)");
            Console.WriteLine($"[POLISH]   vista '{view.GetAttributeValue<string>("name")}': orden {spec.Attribute} {(spec.Descending ? "desc" : "asc")} OK.");
        }

        Console.WriteLine("[POLISH] OK: forms y vistas según spec.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[POLISH]: " + message);
    }
}
