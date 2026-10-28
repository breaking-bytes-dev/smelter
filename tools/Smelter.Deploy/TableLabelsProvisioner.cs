// Provisión idempotente de los nombres en español (3082) de tablas, columnas y opciones locales de Smelter
// (TableLabelsSpec.cs). El 1033 NO se toca: se lee el label actual, se le agrega/actualiza solo el 3082 y se reenvía con
// MergeLabels = true (UpdateEntityRequest / UpdateAttributeRequest; opciones de picklist con UpdateOptionValueRequest;
// opciones Sí/No de un boolean, junto con la columna). Lo que ya vale lo declarado no se escribe. Publica cada tabla tocada.
// Una columna u opción que no existe en la tabla (p.ej. ya eliminada por SchemaCleanup) se saltea.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Smelter.Deploy;

internal static class TableLabelsProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4n] TableLabelsProvisioner: nombres 3082 de tablas, columnas y opciones (TableLabelsSpec.cs) ...");
        var total = 0;

        foreach (var spec in TableLabelsCatalogo.Tables)
        {
            if (TableLabelsCatalogo.IsOptional(spec.Table) && !HmacKeyTableProvisioner.TableExists(client))
            {
                Console.WriteLine($"[4n]   {spec.Table}: la tabla no existe todavía — se omite.");
                continue;
            }

            var touched = 0;

            var entity = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = spec.Table,
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true,
            })).EntityMetadata;
            if (TableLabelsCatalogo.ApplyToEntity(entity, spec))
            {
                client.Execute(new UpdateEntityRequest { Entity = entity, MergeLabels = true });
                Console.WriteLine($"[4n]   {spec.Table}: '{spec.DisplayEs}' / '{spec.CollectionEs}' (3082).");
                touched++;
            }

            foreach (var column in TableLabelsCatalogo.Columns.Where(c => c.Table == spec.Table))
                touched += ProvisionColumn(client, column);

            if (touched > 0)
            {
                client.Execute(new PublishXmlRequest
                {
                    ParameterXml = $"<importexportxml><entities><entity>{spec.Table}</entity></entities></importexportxml>",
                });
                Console.WriteLine($"[4n]     entidad '{spec.Table}' publicada ({touched} cambio(s)).");
            }
            total += touched;
        }

        Console.WriteLine(total == 0
            ? "[4n] TableLabels OK: todo el 3082 ya estaba — nada que hacer."
            : $"[4n] TableLabels OK: {total} cambio(s) de etiqueta 3082.");
    }

    private static int ProvisionColumn(ServiceClient client, ColumnLabelSpec column)
    {
        AttributeMetadata attribute;
        try
        {
            attribute = ((RetrieveAttributeResponse)client.Execute(new RetrieveAttributeRequest
            {
                EntityLogicalName = column.Table,
                LogicalName = column.Column,
                RetrieveAsIfPublished = true,
            })).AttributeMetadata;
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
        {
            Console.WriteLine($"[4n]   {column.Table}.{column.Column}: no existe en la tabla — se omite.");
            return 0;
        }

        var touched = 0;
        var attributeChanged = TableLabelsCatalogo.ApplyToAttribute(attribute, column.Es);
        var options = TableLabelsCatalogo.Options.Where(o => o.Table == column.Table && o.Column == column.Column).ToList();

        // Opciones Sí/No: viven en la columna, se escriben con ella.
        if (attribute is BooleanAttributeMetadata boolean && boolean.OptionSet is { } set)
        {
            foreach (var o in options)
            {
                var option = o.Value == 1 ? set.TrueOption : set.FalseOption;
                if (option is not null && TableLabelsCatalogo.ApplyToOption(option, o.Es))
                {
                    attributeChanged = true;
                    Console.WriteLine($"[4n]   {column.Table}.{column.Column}: opción {o.Value} = '{o.Es}' (3082).");
                }
            }
        }

        if (attributeChanged)
        {
            client.Execute(new UpdateAttributeRequest { EntityName = column.Table, Attribute = attribute, MergeLabels = true });
            Console.WriteLine($"[4n]   {column.Table}.{column.Column}: '{column.Es}' (3082).");
            touched++;
        }

        if (attribute is EnumAttributeMetadata picklist)
        {
            foreach (var o in options)
            {
                var option = picklist.OptionSet?.Options.FirstOrDefault(x => x.Value == o.Value);
                if (option is null)
                {
                    Console.WriteLine($"[4n]   {column.Table}.{column.Column}: opción {o.Value} no existe — se omite.");
                    continue;
                }
                if (!TableLabelsCatalogo.ApplyToOption(option, o.Es))
                    continue;

                // El label lleva el 1033 existente (sin cambios) + el 3082; MergeLabels conserva cualquier otro idioma.
                client.Execute(new UpdateOptionValueRequest
                {
                    EntityLogicalName = column.Table,
                    AttributeLogicalName = column.Column,
                    Value = o.Value,
                    Label = option.Label,
                    MergeLabels = true,
                });
                Console.WriteLine($"[4n]   {column.Table}.{column.Column}: opción {o.Value} = '{o.Es}' (3082).");
                touched++;
            }
        }

        return touched;
    }
}
