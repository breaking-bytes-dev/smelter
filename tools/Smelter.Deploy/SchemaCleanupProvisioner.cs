// Eliminación idempotente de atributos muertos (SchemaCleanupSpec.cs). DeleteAttributeRequest
// falla si el atributo tiene dependencias, así que antes se depuran las referencias en forms
// (systemform.formxml: se remueve el <cell> que contiene el <control datafieldname=...>) y en
// vistas (savedquery: <attribute>/<order>/<condition> del fetchxml y <cell> del layoutxml), se
// publica la entidad y recién entonces se borra. Si aún así falla (p.ej. una vista PERSONAL de
// un usuario referencia la columna), el fault se propaga con contexto — no se borra a ciegas.

using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class SchemaCleanupProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4d] SchemaCleanupProvisioner: atributos muertos (SchemaCleanupSpec.cs) ...");

        foreach (var spec in SchemaCleanupCatalogo.Attributes)
        {
            if (!AttributeExists(client, spec))
            {
                Console.WriteLine($"[4d]   {spec.EntityLogicalName}.{spec.AttributeLogicalName} ya no existe — nada que borrar.");
                continue;
            }

            ScrubForms(client, spec);
            ScrubViews(client, spec);

            // Publicar SIEMPRE antes de cada delete: la dependencia se evalúa contra la capa
            // PUBLICADA — un scrub sin publicar deja la referencia viva y el delete falla con
            // "referenced by N other components" (visto en run 30460874368 con totalbatches:
            // el publish "una vez por entidad" dejó sin publicar el scrub del segundo atributo).
            PublishEntity(client, spec.EntityLogicalName);

            try
            {
                client.Execute(new DeleteAttributeRequest
                {
                    EntityLogicalName = spec.EntityLogicalName,
                    LogicalName = spec.AttributeLogicalName,
                });
                Console.WriteLine($"[4d]   {spec.EntityLogicalName}.{spec.AttributeLogicalName} ELIMINADO ({spec.Reason}).");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"No se pudo eliminar {spec.EntityLogicalName}.{spec.AttributeLogicalName}: {ex.Message}. " +
                    "Posible dependencia restante (¿vista personal de un usuario?) — revisar con " +
                    "RetrieveDependenciesForDeleteRequest en el ambiente.", ex);
            }
        }

        RemoveOptions(client);

        Console.WriteLine($"[4d] SchemaCleanup OK ({SchemaCleanupCatalogo.Attributes.Length} atributos + " +
            $"{OptionCleanupCatalogo.Options.Length} opciones procesadas).");
    }

    // ── Opciones muertas de choices locales (OptionCleanupCatalogo) ──────────────────────────────
    // Se niega a borrar una opción que alguna fila usa: dejaría datos con un valor inexistente. Sin filas, es
    // DeleteOptionValueRequest + publish. Idempotente: una opción que ya no está se saltea.
    internal static void RemoveOptions(ServiceClient client)
    {
        foreach (var spec in OptionCleanupCatalogo.Options)
        {
            var label = $"{spec.EntityLogicalName}.{spec.AttributeLogicalName} = {spec.Value} ({spec.Label})";
            if (!OptionExists(client, spec))
            {
                Console.WriteLine($"[4d]   opción {label} ya no existe — nada que borrar.");
                continue;
            }

            var inUse = client.RetrieveMultiple(new QueryExpression(spec.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = { Conditions = { new ConditionExpression(spec.AttributeLogicalName, ConditionOperator.Equal, spec.Value) } },
            }).Entities.Count > 0;
            if (inUse)
                throw new InvalidOperationException(
                    $"No se elimina la opción {label}: existen filas que la usan (se consulta sin filtrar por estado). " +
                    "Migrar o borrar esas filas y volver a correr el deploy.");

            client.Execute(new DeleteOptionValueRequest
            {
                EntityLogicalName = spec.EntityLogicalName,
                AttributeLogicalName = spec.AttributeLogicalName,
                Value = spec.Value,
            });
            PublishEntity(client, spec.EntityLogicalName);
            Console.WriteLine($"[4d]   opción {label} ELIMINADA ({spec.Reason}).");
        }
    }

    internal static bool OptionExists(ServiceClient client, OptionCleanupSpec spec)
    {
        var attribute = ((RetrieveAttributeResponse)client.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = spec.EntityLogicalName,
            LogicalName = spec.AttributeLogicalName,
            RetrieveAsIfPublished = true,
        })).AttributeMetadata;

        var options = (attribute as EnumAttributeMetadata)?.OptionSet?.Options;
        return options?.Any(o => o.Value == spec.Value) == true;
    }

    private static bool AttributeExists(ServiceClient client, AttributeCleanupSpec spec)
    {
        var metadata = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = spec.EntityLogicalName,
            EntityFilters = EntityFilters.Attributes,
        })).EntityMetadata;

        return metadata.Attributes?.Any(a =>
            string.Equals(a.LogicalName, spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase)) == true;
    }

    // systemform.objecttypecode es un atributo EntityName: se filtra por el nombre lógico (mismo
    // criterio que sdkmessagefilter.primaryobjecttypecode en StepProvisioner).
    private static void ScrubForms(ServiceClient client, AttributeCleanupSpec spec)
    {
        var forms = client.RetrieveMultiple(new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid", "formxml", "name"),
            Criteria = { Conditions = { new ConditionExpression("objecttypecode", ConditionOperator.Equal, spec.EntityLogicalName) } },
        }).Entities;

        foreach (var form in forms)
        {
            var formXml = form.GetAttributeValue<string>("formxml");
            if (string.IsNullOrWhiteSpace(formXml) ||
                !formXml.Contains(spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase))
                continue;

            var doc = XDocument.Parse(formXml);
            if (!FormXmlEditor.TryRemoveField(doc, spec.AttributeLogicalName))
                continue;

            client.Update(new Entity("systemform", form.Id) { ["formxml"] = doc.ToString(SaveOptions.DisableFormatting) });
            Console.WriteLine($"[4d]     form '{form.GetAttributeValue<string>("name")}': " +
                $"{spec.AttributeLogicalName} removido.");
        }
    }

    private static void ScrubViews(ServiceClient client, AttributeCleanupSpec spec)
    {
        var views = client.RetrieveMultiple(new QueryExpression("savedquery")
        {
            ColumnSet = new ColumnSet("savedqueryid", "fetchxml", "layoutxml", "name"),
            Criteria = { Conditions = { new ConditionExpression("returnedtypecode", ConditionOperator.Equal, spec.EntityLogicalName) } },
        }).Entities;

        foreach (var view in views)
        {
            var update = new Entity("savedquery", view.Id);
            var changed = false;

            var fetchXml = view.GetAttributeValue<string>("fetchxml");
            if (!string.IsNullOrWhiteSpace(fetchXml) &&
                fetchXml.Contains(spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase))
            {
                var fetch = XDocument.Parse(fetchXml);
                var nodos = fetch.Descendants()
                    .Where(e => (e.Name.LocalName == "attribute" &&
                                 string.Equals((string?)e.Attribute("name"), spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase))
                             || ((e.Name.LocalName == "order" || e.Name.LocalName == "condition") &&
                                 string.Equals((string?)e.Attribute("attribute"), spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (nodos.Count > 0)
                {
                    foreach (var nodo in nodos) nodo.Remove();
                    update["fetchxml"] = fetch.ToString(SaveOptions.DisableFormatting);
                    changed = true;
                }
            }

            var layoutXml = view.GetAttributeValue<string>("layoutxml");
            if (!string.IsNullOrWhiteSpace(layoutXml) &&
                layoutXml.Contains(spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase))
            {
                var layout = XDocument.Parse(layoutXml);
                var celdas = layout.Descendants("cell")
                    .Where(c => string.Equals((string?)c.Attribute("name"), spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (celdas.Count > 0)
                {
                    foreach (var celda in celdas) celda.Remove();
                    update["layoutxml"] = layout.ToString(SaveOptions.DisableFormatting);
                    changed = true;
                }
            }

            if (!changed)
                continue;

            client.Update(update);
            Console.WriteLine($"[4d]     vista '{view.GetAttributeValue<string>("name")}': " +
                $"referencias a {spec.AttributeLogicalName} removidas.");
        }
    }

    private static void PublishEntity(ServiceClient client, string entityLogicalName)
    {
        client.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{entityLogicalName}</entity></entities></importexportxml>",
        });
        Console.WriteLine($"[4d]     entidad '{entityLogicalName}' publicada (forms/vistas depurados).");
    }
}
