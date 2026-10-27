// Provisión idempotente de las columnas nuevas (SchemaAdditionsSpec.cs) por SDK: CreateAttributeRequest dentro
// de smlt_core_smelter y, si la spec lo pide, inserción del campo en los forms de la tabla (FormXmlEditor +
// systemform.formxml, como el scrub de SchemaCleanupProvisioner pero en sentido inverso). Una columna que ya
// existe no se reconfigura; un form que ya tiene el control no se toca. No es destructivo.

using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class SchemaAdditionsProvisioner
{
    private const int Lcid = 1033;

    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4f] SchemaAdditionsProvisioner: columnas nuevas (SchemaAdditionsSpec.cs) ...");

        foreach (var spec in SchemaAdditionsCatalogo.Columns)
        {
            if (AttributeExists(client, spec))
            {
                Console.WriteLine($"[4f]   {spec.EntityLogicalName}.{spec.SchemaName} ya existe — no se reconfigura.");
            }
            else
            {
                client.Execute(new CreateAttributeRequest
                {
                    EntityName = spec.EntityLogicalName,
                    Attribute = BuildAttribute(spec),
                    SolutionUniqueName = SchemaAdditionsCatalogo.SolutionUniqueName,
                });
                Console.WriteLine($"[4f]   {spec.EntityLogicalName}.{spec.SchemaName} CREADA ({spec.Kind}).");
            }
        }

        // El form referencia la columna: se publica la entidad antes de tocarlo y de nuevo al final.
        foreach (var porEntidad in SchemaAdditionsCatalogo.Columns.GroupBy(c => c.EntityLogicalName))
        {
            Publish(client, porEntidad.Key);
            foreach (var spec in porEntidad.Where(c => c.Form is not null))
                EnsureOnForms(client, spec);
            Publish(client, porEntidad.Key);
        }

        Console.WriteLine($"[4f] SchemaAdditions OK ({SchemaAdditionsCatalogo.Columns.Length} specs procesadas).");
    }

    private static AttributeMetadata BuildAttribute(SchemaColumnSpec spec)
    {
        AttributeMetadata attribute = spec.Kind switch
        {
            SchemaColumnKind.Decimal => new DecimalAttributeMetadata
            {
                Precision = SchemaAdditionsCatalogo.ByteCursorPrecision,
                MinValue = SchemaAdditionsCatalogo.ByteCursorMin,
                MaxValue = SchemaAdditionsCatalogo.ByteCursorMax,
                ImeMode = ImeMode.Disabled,
            },
            SchemaColumnKind.Boolean => new BooleanAttributeMetadata
            {
                DefaultValue = SchemaAdditionsCatalogo.InsertOnlyDefault,
                OptionSet = new BooleanOptionSetMetadata(
                    new OptionMetadata(new Label("Yes", Lcid), 1),
                    new OptionMetadata(new Label("No", Lcid), 0)),
            },
            _ => throw new InvalidOperationException($"Kind no soportado: {spec.Kind}"),
        };

        attribute.SchemaName = spec.SchemaName;
        attribute.DisplayName = new Label(spec.DisplayName, Lcid);
        attribute.Description = new Label(spec.Description, Lcid);
        attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None);
        return attribute;
    }

    private static bool AttributeExists(ServiceClient client, SchemaColumnSpec spec)
    {
        var metadata = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = spec.EntityLogicalName,
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = true,
        })).EntityMetadata;

        return metadata.Attributes?.Any(a =>
            string.Equals(a.LogicalName, spec.SchemaName, StringComparison.OrdinalIgnoreCase)) == true;
    }

    // Todos los forms de la tabla que contienen el ancla y todavía no el campo nuevo (mismo filtro por
    // objecttypecode lógico que SchemaCleanupProvisioner.ScrubForms).
    private static void EnsureOnForms(ServiceClient client, SchemaColumnSpec spec)
    {
        var placement = spec.Form!;
        var forms = client.RetrieveMultiple(new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid", "formxml", "name"),
            Criteria = { Conditions = { new ConditionExpression("objecttypecode", ConditionOperator.Equal, spec.EntityLogicalName) } },
        }).Entities;

        var touched = 0;
        foreach (var form in forms)
        {
            var formXml = form.GetAttributeValue<string>("formxml");
            if (string.IsNullOrWhiteSpace(formXml) ||
                !formXml.Contains(placement.AfterField, StringComparison.OrdinalIgnoreCase))
                continue;

            var doc = XDocument.Parse(formXml);
            if (!FormXmlEditor.TryAddCellAfter(doc, placement.AfterField, spec.SchemaName, placement.Label))
                continue;

            client.Update(new Entity("systemform", form.Id) { ["formxml"] = doc.ToString(SaveOptions.DisableFormatting) });
            Console.WriteLine($"[4f]     form '{form.GetAttributeValue<string>("name")}': {spec.SchemaName} agregado tras {placement.AfterField}.");
            touched++;
        }

        if (touched == 0)
            Console.WriteLine($"[4f]   {spec.SchemaName}: ningún form para modificar (ya está, o no hay form con {placement.AfterField}).");
    }

    private static void Publish(ServiceClient client, string entityLogicalName)
    {
        client.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{entityLogicalName}</entity></entities></importexportxml>",
        });
        Console.WriteLine($"[4f]     entidad '{entityLogicalName}' publicada.");
    }
}
