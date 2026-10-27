// Provisión idempotente de las Description de columnas (ColumnDescriptionsSpec.cs) por SDK:
// RetrieveAttributeRequest + UpdateAttributeRequest(MergeLabels = true) tocando SOLO la Description (los display
// names, tipos y demás propiedades se reenvían tal cual). Una Description que ya coincide se saltea. No destructivo.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Smelter.Deploy;

internal static class ColumnDescriptionsProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4i] ColumnDescriptionsProvisioner: Description de columnas (ColumnDescriptionsSpec.cs) ...");

        var updated = 0;
        foreach (var porEntidad in ColumnDescriptionsCatalogo.Descriptions.GroupBy(d => d.Entity))
        {
            var touched = 0;
            foreach (var spec in porEntidad)
            {
                var attribute = ((RetrieveAttributeResponse)client.Execute(new RetrieveAttributeRequest
                {
                    EntityLogicalName = spec.Entity,
                    LogicalName = spec.Column,
                    RetrieveAsIfPublished = true,
                })).AttributeMetadata;

                if (!ColumnDescriptionsCatalogo.NeedsUpdate(CurrentDescription(attribute), spec.Text))
                    continue;

                attribute.Description = new Label(spec.Text, ColumnDescriptionsCatalogo.LanguageCode);
                client.Execute(new UpdateAttributeRequest { EntityName = spec.Entity, Attribute = attribute, MergeLabels = true });
                Console.WriteLine($"[4i]   {spec.Entity}.{spec.Column}: Description actualizada.");
                touched++;
            }

            if (touched > 0)
            {
                client.Execute(new PublishXmlRequest
                {
                    ParameterXml = $"<importexportxml><entities><entity>{porEntidad.Key}</entity></entities></importexportxml>",
                });
                Console.WriteLine($"[4i]     entidad '{porEntidad.Key}' publicada ({touched} columna(s)).");
            }
            updated += touched;
        }

        Console.WriteLine(updated == 0
            ? $"[4i] Descriptions OK: las {ColumnDescriptionsCatalogo.Descriptions.Length} ya estaban — nada que hacer."
            : $"[4i] Descriptions OK: {updated} de {ColumnDescriptionsCatalogo.Descriptions.Length} actualizadas.");
    }

    internal static string? CurrentDescription(AttributeMetadata attribute) =>
        attribute.Description?.LocalizedLabels
            .FirstOrDefault(l => l.LanguageCode == ColumnDescriptionsCatalogo.LanguageCode)?.Label;
}
