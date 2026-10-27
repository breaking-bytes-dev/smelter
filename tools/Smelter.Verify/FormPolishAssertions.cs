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
            var mains = client.RetrieveMultiple(new QueryExpression("systemform")
            {
                ColumnSet = new ColumnSet("name", "formxml"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("objecttypecode", ConditionOperator.Equal, entity),
                        new ConditionExpression("type", ConditionOperator.Equal, 2),
                        new ConditionExpression("formactivationstate", ConditionOperator.Equal, 1),
                    },
                },
            }).Entities;
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

        Console.WriteLine("[POLISH] OK: forms y vistas según spec.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[POLISH]: " + message);
    }
}
