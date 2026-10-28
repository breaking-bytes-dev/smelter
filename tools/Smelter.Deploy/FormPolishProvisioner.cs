// Provisión idempotente de FormPolishSpec.cs por SDK: edita el formxml de los MAIN forms (systemform type = 2) con
// FormPolishCatalogo.Apply y el layoutxml/fetchxml de la vista default del Import Job Log con ViewXmlEditor. Un form o
// vista que ya está como pide la spec no se escribe. Publica cada entidad tocada. No destructivo.

using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class FormPolishProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4j] FormPolishProvisioner: forms y vistas (FormPolishSpec.cs) ...");

        var entities = FormPolishCatalogo.FormEntities
            .Concat(FormPolishCatalogo.ViewColumns.Select(v => v.Entity))
            .Concat(FormPolishCatalogo.ViewOrders.Select(v => v.Entity))
            .Distinct()
            .ToList();

        foreach (var entity in entities)
        {
            var touched = PolishForms(client, entity) + PolishViews(client, entity);
            if (touched > 0)
                Publish(client, entity);
        }

        Console.WriteLine("[4j] FormPolish OK.");
    }

    private static int PolishForms(ServiceClient client, string entity)
    {
        if (!FormPolishCatalogo.FormEntities.Contains(entity))
            return 0;

        // formxml con etiquetas 1033 + 3082 fusionadas: leído solo en 1033 no se ve el 3082 y se reescribiría siempre.
        var forms = FormLabelsReader.RetrieveMainForms(client, entity);

        var touched = 0;
        foreach (var form in forms)
        {
            var formXml = form.GetAttributeValue<string>("formxml");
            if (string.IsNullOrWhiteSpace(formXml))
                continue;

            var doc = XDocument.Parse(formXml);
            var applied = FormPolishCatalogo.Apply(doc, entity);
            if (applied.Count == 0)
                continue;

            client.Update(new Entity("systemform", form.Id) { ["formxml"] = doc.ToString(SaveOptions.DisableFormatting) });
            Console.WriteLine($"[4j]   {entity} form '{form.GetAttributeValue<string>("name")}': {applied.Count} cambio(s).");
            foreach (var line in applied)
                Console.WriteLine($"[4j]     + {line}");
            touched++;
        }

        if (touched == 0)
            Console.WriteLine($"[4j]   {entity}: main form ya al día — nada que hacer.");
        return touched;
    }

    private static int PolishViews(ServiceClient client, string entity)
    {
        var touched = 0;
        foreach (var spec in FormPolishCatalogo.ViewColumns.Where(v => v.Entity == entity))
        {
            var view = client.Retrieve("savedquery", spec.ViewId, new ColumnSet("layoutxml", "fetchxml", "name"));
            var (layout, fetch, changed) = ViewXmlEditor.TryAddColumn(
                view.GetAttributeValue<string>("layoutxml"), view.GetAttributeValue<string>("fetchxml"),
                spec.AfterColumn, spec.Column, spec.Width);
            if (!changed)
            {
                Console.WriteLine($"[4j]   vista '{view.GetAttributeValue<string>("name")}': {spec.Column} ya está — nada que hacer.");
                continue;
            }

            client.Update(new Entity("savedquery", spec.ViewId) { ["layoutxml"] = layout, ["fetchxml"] = fetch });
            Console.WriteLine($"[4j]   vista '{view.GetAttributeValue<string>("name")}': {spec.Column} agregada tras {spec.AfterColumn}.");
            touched++;
        }

        foreach (var spec in FormPolishCatalogo.ViewOrders.Where(v => v.Entity == entity))
        {
            var view = client.Retrieve("savedquery", spec.ViewId, new ColumnSet("fetchxml", "name"));
            var (fetch, changed) = ViewXmlEditor.TrySetOrder(view.GetAttributeValue<string>("fetchxml"), spec.Attribute, spec.Descending);
            if (!changed)
            {
                Console.WriteLine($"[4j]   vista '{view.GetAttributeValue<string>("name")}': ya ordena por {spec.Attribute} — nada que hacer.");
                continue;
            }

            client.Update(new Entity("savedquery", spec.ViewId) { ["fetchxml"] = fetch });
            Console.WriteLine($"[4j]   vista '{view.GetAttributeValue<string>("name")}': orden = {spec.Attribute} {(spec.Descending ? "desc" : "asc")}.");
            touched++;
        }
        return touched;
    }

    private static void Publish(ServiceClient client, string entityLogicalName)
    {
        client.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{entityLogicalName}</entity></entities></importexportxml>",
        });
        Console.WriteLine($"[4j]     entidad '{entityLogicalName}' publicada.");
    }
}
