// Asserts E2E de ViewLabelsSpec.cs: las 3 vistas custom del Import Job tienen nombre y descripción en inglés (1033) y
// español (3082) y el dashboard tiene su nombre 3082; no queda ninguna vista con el nombre legacy en español bajo 1033. Solo lee.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class ViewLabelsAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[VIEWS] Verificando nombres 1033 + 3082 de vistas y dashboard (ViewLabelsSpec.cs) ...");

        foreach (var spec in ViewLabelsCatalogo.Views)
        {
            var view = ViewLabelsProvisioner.FindView(client, spec);
            Assert(view is not null, $"no existe la vista '{spec.View.Name}' (1033) del Import Job");

            var name = LocLabels.Get(client, "savedquery", view!.Id, "name");
            Assert(LocLabels.Matches(name, spec.Name),
                $"vista '{spec.View.Name}': nombre 1033/3082 = '{name.GetValueOrDefault(Lcids.English)}' / '{name.GetValueOrDefault(Lcids.Spanish)}', esperado '{spec.Name.En}' / '{spec.Name.Es}'");
            var description = LocLabels.Get(client, "savedquery", view.Id, "description");
            Assert(LocLabels.Matches(description, spec.Description), $"vista '{spec.View.Name}': descripción 1033/3082 distinta de la spec");

            var legacy = client.RetrieveMultiple(new QueryExpression("savedquery")
            {
                ColumnSet = new ColumnSet("savedqueryid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("name", ConditionOperator.Equal, spec.LegacyName),
                        new ConditionExpression("returnedtypecode", ConditionOperator.Equal, SecuritySpec.Tables.ImportJob),
                    },
                },
            }).Entities;
            Assert(legacy.Count == 0 || spec.LegacyName == spec.View.Name, $"quedó una vista con el nombre legacy '{spec.LegacyName}' (duplicado)");
        }

        var dashboard = client.RetrieveMultiple(new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, ViewLabelsCatalogo.Dashboard.En),
                    new ConditionExpression("type", ConditionOperator.Equal, 0),
                },
            },
        }).Entities.FirstOrDefault();
        Assert(dashboard is not null, $"no existe el dashboard '{ViewLabelsCatalogo.Dashboard.En}'");
        var dash = LocLabels.Get(client, "systemform", dashboard!.Id, "name");
        Assert(LocLabels.Matches(dash, ViewLabelsCatalogo.Dashboard),
            $"dashboard: nombre 1033/3082 = '{dash.GetValueOrDefault(Lcids.English)}' / '{dash.GetValueOrDefault(Lcids.Spanish)}'");

        Console.WriteLine($"[VIEWS] OK: {ViewLabelsCatalogo.Views.Length} vistas y el dashboard con nombre 1033 + 3082.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[VIEWS]: " + message);
    }
}
