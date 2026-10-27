// Provisión idempotente de la tabla de keys HMAC (HmacKeyTableSpec.cs) por SDK:
// CreateEntityRequest (Organization-owned, audit de tabla ON) + CreateAttributeRequest para la
// columna del valor (sin audit, no válida para Advanced Find). Si la tabla/columna ya existen, no
// se toca nada (no se reconfigura una tabla viva), salvo la membresía en smlt_core_smelter: si la tabla
// existe pero no es componente de la solución (caso Dev: se creó en Default), se agrega con
// AddSolutionComponentRequest. NO escribe ni lee valores de keys: las keys las
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
        Console.WriteLine($"[4e] HmacKeyTableProvisioner: tabla {HmacKeyTableSpec.Table} (HmacKeyTableSpec.cs) ...");

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
                // La tabla nace dentro de smlt_core_smelter: su schema viaja a Test/Prod (ver el spec).
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

        EnsureInSolution(client);

        client.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{HmacKeyTableSpec.Table}</entity></entities></importexportxml>",
        });
        Console.WriteLine("[4e] HmacKeyTable OK (recordá: la key de cada entorno la carga a mano un System Administrator; ningún rol de Smelter la lee).");
    }

    // Pura: ¿hay que agregar la entidad a la solución? Sí si su MetadataId no está entre los objectid
    // de los solutioncomponent de tipo Entity de esa solución.
    internal static bool NeedsSolutionAdd(Guid entityId, IEnumerable<Guid> solutionEntityComponentIds) =>
        !solutionEntityComponentIds.Contains(entityId);

    // Reconciliación idempotente: la tabla de Dev se creó en Default solution antes de esta decisión.
    private static void EnsureInSolution(ServiceClient client)
    {
        var solutionId = client.RetrieveMultiple(new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, HmacKeyTableSpec.SolutionUniqueName) } },
        }).Entities.SingleOrDefault()?.Id;

        if (solutionId is null)
            throw new InvalidOperationException(
                $"Solución '{HmacKeyTableSpec.SolutionUniqueName}' no encontrada: no se puede agregar {HmacKeyTableSpec.Table}.");

        var entityId = RetrieveTable(client).MetadataId
            ?? throw new InvalidOperationException($"{HmacKeyTableSpec.Table} sin MetadataId.");

        var componentIds = client.RetrieveMultiple(new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId.Value),
                    new ConditionExpression("componenttype", ConditionOperator.Equal, HmacKeyTableSpec.EntityComponentType),
                    new ConditionExpression("objectid", ConditionOperator.Equal, entityId),
                },
            },
        }).Entities.Select(e => e.GetAttributeValue<Guid>("objectid")).ToList();

        if (!NeedsSolutionAdd(entityId, componentIds))
        {
            Console.WriteLine($"[4e]   {HmacKeyTableSpec.Table} ya es componente de '{HmacKeyTableSpec.SolutionUniqueName}'.");
            return;
        }

        client.Execute(new AddSolutionComponentRequest
        {
            ComponentId = entityId,
            ComponentType = HmacKeyTableSpec.EntityComponentType,
            SolutionUniqueName = HmacKeyTableSpec.SolutionUniqueName,
            AddRequiredComponents = false,
            DoNotIncludeSubcomponents = false, // las columnas viajan con la tabla
        });
        Console.WriteLine($"[4e]   {HmacKeyTableSpec.Table} AGREGADA a '{HmacKeyTableSpec.SolutionUniqueName}' (con sus columnas).");
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
