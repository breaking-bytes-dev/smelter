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
        Console.WriteLine("[4i] ColumnDescriptionsProvisioner: Description de columnas, 1033 + 3082 (ColumnDescriptionsSpec.cs) ...");

        var updated = 0;
        foreach (var porEntidad in ColumnDescriptionsCatalogo.All.GroupBy(d => d.Entity))
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

                if (!ColumnDescriptionsCatalogo.NeedsUpdate(attribute.Description, spec.Text))
                    continue;

                // 1033 (inglés, base) + 3082 (español); MergeLabels conserva cualquier otro idioma.
                attribute.Description = spec.Text.ToLabel();
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
            ? $"[4i] Descriptions OK: las {ColumnDescriptionsCatalogo.All.Count()} ya estaban — nada que hacer."
            : $"[4i] Descriptions OK: {updated} de {ColumnDescriptionsCatalogo.All.Count()} actualizadas.");
    }

    internal static string? CurrentDescription(AttributeMetadata attribute) =>
        attribute.Description?.LocalizedLabels
            .FirstOrDefault(l => l.LanguageCode == ColumnDescriptionsCatalogo.LanguageCode)?.Label;
}
