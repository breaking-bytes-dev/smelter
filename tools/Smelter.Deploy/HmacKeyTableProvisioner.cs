// Provisión idempotente de la tabla DEV-only de keys HMAC (HmacKeyTableSpec.cs) por SDK:
// CreateEntityRequest (Organization-owned, audit de tabla ON) + CreateAttributeRequest para la
// columna del valor (sin audit, no válida para Advanced Find). Si la tabla/columna ya existen, no
// se toca nada (no se reconfigura una tabla viva). NO escribe ni lee valores de keys: las keys las
// carga a mano un System Administrator.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class HmacKeyTableProvisioner
{
    private const int Lcid = 1033;

    internal static void Run(ServiceClient client)
    {
        Console.WriteLine($"[4e] HmacKeyTableProvisioner: tabla DEV-only {HmacKeyTableSpec.Table} (HmacKeyTableSpec.cs) ...");

        if (!TableExists(client))
        {
            client.Execute(new CreateEntityRequest
            {
                Entity = new EntityMetadata
                {
                    SchemaName = HmacKeyTableSpec.Table,
                    DisplayName = new Label(HmacKeyTableSpec.TableDisplayName, Lcid),
                    DisplayCollectionName = new Label(HmacKeyTableSpec.TableDisplayCollectionName, Lcid),
                    Description = new Label(HmacKeyTableSpec.TableDescription, Lcid),
                    OwnershipType = OwnershipTypes.OrganizationOwned,
                    IsAuditEnabled = new BooleanManagedProperty(HmacKeyTableSpec.TableAudited),
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = HmacKeyTableSpec.NameColumn,
                    DisplayName = new Label(HmacKeyTableSpec.NameDisplayName, Lcid),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = HmacKeyTableSpec.NameMaxLength,
                    FormatName = StringFormatName.Text,
                },
                HasActivities = false,
                HasNotes = false,
                // null a propósito: Default solution, no viaja en smlt_core_smelter (ver el spec).
                SolutionUniqueName = HmacKeyTableSpec.SolutionUniqueName,
            });
            Console.WriteLine($"[4e]   tabla {HmacKeyTableSpec.Table} CREADA (Organization-owned, audit de tabla ON).");
        }
        else
        {
            Console.WriteLine($"[4e]   tabla {HmacKeyTableSpec.Table} ya existe — no se reconfigura.");
        }

        if (!ValueColumnExists(client))
        {
            client.Execute(new CreateAttributeRequest
            {
                EntityName = HmacKeyTableSpec.Table,
                Attribute = new StringAttributeMetadata
                {
                    SchemaName = HmacKeyTableSpec.ValueColumn,
                    DisplayName = new Label(HmacKeyTableSpec.ValueDisplayName, Lcid),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = HmacKeyTableSpec.ValueMaxLength,
                    FormatName = StringFormatName.Text,
                    IsAuditEnabled = new BooleanManagedProperty(HmacKeyTableSpec.ValueColumnAudited),
                    IsValidForAdvancedFind = new BooleanManagedProperty(HmacKeyTableSpec.ValueColumnValidForAdvancedFind),
                },
                SolutionUniqueName = HmacKeyTableSpec.SolutionUniqueName,
            });
            Console.WriteLine($"[4e]   columna {HmacKeyTableSpec.ValueColumn} CREADA (sin audit, fuera de Advanced Find).");
        }
        else
        {
            Console.WriteLine($"[4e]   columna {HmacKeyTableSpec.ValueColumn} ya existe — no se reconfigura.");
        }

        client.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{HmacKeyTableSpec.Table}</entity></entities></importexportxml>",
        });
        Console.WriteLine("[4e] HmacKeyTable OK (recordá: las keys las carga a mano un System Administrator; ningún rol de Smelter la lee).");
    }

    // RetrieveMetadataChanges devuelve colección vacía si la tabla no existe (RetrieveEntity lanzaría fault).
    internal static bool TableExists(ServiceClient client)
    {
        var response = (RetrieveMetadataChangesResponse)client.Execute(new RetrieveMetadataChangesRequest
        {
            Query = new EntityQueryExpression
            {
                Criteria = new MetadataFilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new MetadataConditionExpression("LogicalName", MetadataConditionOperator.Equals, HmacKeyTableSpec.Table),
                    },
                },
                Properties = new MetadataPropertiesExpression("LogicalName"),
            },
        });
        return response.EntityMetadata.Count > 0;
    }

    private static bool ValueColumnExists(ServiceClient client)
    {
        var metadata = RetrieveTable(client);
        return metadata.Attributes?.Any(a =>
            string.Equals(a.LogicalName, HmacKeyTableSpec.ValueColumn, StringComparison.OrdinalIgnoreCase)) == true;
    }

    internal static EntityMetadata RetrieveTable(ServiceClient client) =>
        ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = HmacKeyTableSpec.Table,
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
        })).EntityMetadata;
}
