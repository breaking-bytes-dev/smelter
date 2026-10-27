// Ajustes idempotentes sobre columnas y forms existentes (SchemaTuningSpec.cs) por SDK: UpdateAttributeRequest
// (required level / default) y edición del formxml (FormXmlEditor.TrySetVisible). Un ajuste ya aplicado se saltea.
// No es destructivo: no borra nada ni toca datos (el default solo rige para filas nuevas).

using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class SchemaTuningProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4h] SchemaTuningProvisioner: ajustes de columnas y forms (SchemaTuningSpec.cs) ...");

        foreach (var spec in SchemaTuningCatalogo.Attributes)
            TuneAttribute(client, spec);

        foreach (var porEntidad in SchemaTuningCatalogo.HiddenFields.GroupBy(h => h.Entity))
        {
            foreach (var hidden in porEntidad)
                SetVisibleOnForms(client, hidden);
            Publish(client, porEntidad.Key);
        }

        Console.WriteLine("[4h] SchemaTuning OK.");
    }

    private static void TuneAttribute(ServiceClient client, AttributeTuningSpec spec)
    {
        var attribute = ((RetrieveAttributeResponse)client.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = spec.Entity,
            LogicalName = spec.Attribute,
            RetrieveAsIfPublished = true,
        })).AttributeMetadata;

        var changed = false;

        if (spec.RequiredLevel is { } level && attribute.RequiredLevel?.Value != level)
        {
            attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(level);
            changed = true;
        }

        if (spec.PicklistDefault is { } pickDefault)
        {
            var picklist = attribute as PicklistAttributeMetadata
                ?? throw new InvalidOperationException($"{spec.Entity}.{spec.Attribute} debía ser Picklist, es {attribute.AttributeType}");
            if (picklist.DefaultFormValue != pickDefault)
            {
                picklist.DefaultFormValue = pickDefault;
                changed = true;
            }
        }

        if (spec.BooleanDefault is { } boolDefault)
        {
            var boolean = attribute as BooleanAttributeMetadata
                ?? throw new InvalidOperationException($"{spec.Entity}.{spec.Attribute} debía ser Boolean, es {attribute.AttributeType}");
            if (boolean.DefaultValue != boolDefault)
            {
                boolean.DefaultValue = boolDefault;
                changed = true;
            }
        }

        if (spec.IntMin.HasValue || spec.IntMax.HasValue)
        {
            var integer = attribute as IntegerAttributeMetadata
                ?? throw new InvalidOperationException($"{spec.Entity}.{spec.Attribute} debía ser Integer, es {attribute.AttributeType}");
            if (spec.IntMin is { } min && integer.MinValue != min)
            {
                integer.MinValue = min;
                changed = true;
            }
            if (spec.IntMax is { } max && integer.MaxValue != max)
            {
                integer.MaxValue = max;
                changed = true;
            }
        }

        if (!changed)
        {
            Console.WriteLine($"[4h]   {spec.Entity}.{spec.Attribute} ya está ajustada — nada que hacer.");
            return;
        }

        client.Execute(new UpdateAttributeRequest { EntityName = spec.Entity, Attribute = attribute, MergeLabels = true });
        Publish(client, spec.Entity);
        Console.WriteLine($"[4h]   {spec.Entity}.{spec.Attribute} AJUSTADA ({spec.Reason}).");
    }

    private static void SetVisibleOnForms(ServiceClient client, FormFieldVisibilitySpec spec)
    {
        var forms = client.RetrieveMultiple(new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid", "formxml", "name"),
            Criteria = { Conditions = { new ConditionExpression("objecttypecode", ConditionOperator.Equal, spec.Entity) } },
        }).Entities;

        var touched = 0;
        foreach (var form in forms)
        {
            var formXml = form.GetAttributeValue<string>("formxml");
            if (string.IsNullOrWhiteSpace(formXml) || !formXml.Contains(spec.Field, StringComparison.OrdinalIgnoreCase))
                continue;

            var doc = XDocument.Parse(formXml);
            if (!FormXmlEditor.TrySetVisible(doc, spec.Field, spec.Visible))
                continue;

            client.Update(new Entity("systemform", form.Id) { ["formxml"] = doc.ToString(SaveOptions.DisableFormatting) });
            Console.WriteLine($"[4h]     form '{form.GetAttributeValue<string>("name")}': {spec.Field} " +
                $"{(spec.Visible ? "visible" : "oculto")} ({spec.Reason}).");
            touched++;
        }

        if (touched == 0)
            Console.WriteLine($"[4h]   {spec.Field}: ningún form para modificar (ya está en ese estado, o no lo contiene).");
    }

    private static void Publish(ServiceClient client, string entityLogicalName)
    {
        client.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{entityLogicalName}</entity></entities></importexportxml>",
        });
        Console.WriteLine($"[4h]     entidad '{entityLogicalName}' publicada.");
    }
}
