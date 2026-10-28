// Provisión idempotente de las traducciones 3082 (español) de las vistas custom del Import Job y del dashboard
// (ViewLabelsSpec.cs) con SetLocLabelsRequest. Corre DESPUÉS del [6] (SecurityProvisioner), que es quien crea/renombra las
// vistas y enlaza el dashboard con el nombre base en inglés. No destructivo; publica lo que toca.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class ViewLabelsProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[6a] ViewLabelsProvisioner: nombres 1033 + 3082 de vistas y dashboard (ViewLabelsSpec.cs) ...");
        var touched = 0;
        Guid? dashboardId = null;

        foreach (var spec in ViewLabelsCatalogo.Views)
        {
            var view = FindView(client, spec);
            if (view is null)
            {
                Console.Error.WriteLine($"[6a]   WARN: vista '{spec.View.Name}' no encontrada — se omite.");
                continue;
            }

            var changed = LocLabels.Ensure(client, "savedquery", view.Id, "name", spec.Name);
            changed |= LocLabels.Ensure(client, "savedquery", view.Id, "description", spec.Description);
            if (changed)
            {
                Console.WriteLine($"[6a]   vista '{spec.View.Name}': nombre/descripción 3082 = '{spec.NameEs}'.");
                touched++;
            }
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

        if (dashboard is null)
        {
            Console.Error.WriteLine($"[6a]   WARN: dashboard '{ViewLabelsCatalogo.Dashboard.En}' no existe — se omite.");
        }
        else if (LocLabels.Ensure(client, "systemform", dashboard.Id, "name", ViewLabelsCatalogo.Dashboard))
        {
            Console.WriteLine($"[6a]   dashboard: nombre 3082 = '{ViewLabelsCatalogo.Dashboard.Es}'.");
            dashboardId = dashboard.Id;
            touched++;
        }

        if (touched > 0)
        {
            client.Execute(new PublishXmlRequest { ParameterXml = ViewLabelsCatalogo.PublishXml(dashboardId) });
            Console.WriteLine($"[6a]   entidad '{SecuritySpec.Tables.ImportJob}'{(dashboardId is null ? "" : " y dashboard")} publicados.");
        }

        Console.WriteLine(touched == 0
            ? "[6a] ViewLabels OK: ya al día — nada que hacer."
            : $"[6a] ViewLabels OK: {touched} elemento(s) actualizados.");
    }

    internal static Entity? FindView(IOrganizationService service, ViewLabelSpec spec) =>
        service.RetrieveMultiple(new QueryExpression("savedquery")
        {
            ColumnSet = new ColumnSet("savedqueryid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, spec.View.Name),
                    new ConditionExpression("returnedtypecode", ConditionOperator.Equal, SecuritySpec.Tables.ImportJob),
                },
            },
        }).Entities.FirstOrDefault();
}
