// Asserts E2E de la tabla de keys HMAC provisionada por HmacKeyTableProvisioner
// (HmacKeyTableSpec.cs): existe, es componente de smlt_core_smelter, Organization-owned, audit de tabla ON, columna del valor SIN audit
// fuera de Advanced Find y asegurada (IsSecured). NO lee ningún valor de key.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class HmacKeyAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine($"[HMAC] Verificando la tabla {HmacKeyTableSpec.Table} (HmacKeyTableSpec.cs) ...");

        Assert(HmacKeyTableProvisioner.TableExists(client),
            $"la tabla {HmacKeyTableSpec.Table} no existe (¿corriste Smelter.Deploy?)");

        var table = HmacKeyTableProvisioner.RetrieveTable(client);
        Assert(table.OwnershipType == OwnershipTypes.OrganizationOwned,
            $"la tabla es {table.OwnershipType}, esperada OrganizationOwned");
        Assert(table.IsAuditEnabled?.Value == HmacKeyTableSpec.TableAudited,
            "el audit de la tabla no coincide con el spec (se auditan los cambios de keys, no sus valores)");

        var solutionId = client.RetrieveMultiple(new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, HmacKeyTableSpec.SolutionUniqueName) } },
        }).Entities.SingleOrDefault()?.Id;
        Assert(solutionId is not null, $"no existe la solución {HmacKeyTableSpec.SolutionUniqueName}");
        var inSolution = client.RetrieveMultiple(new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId!.Value),
                    new ConditionExpression("componenttype", ConditionOperator.Equal, HmacKeyTableSpec.EntityComponentType),
                    new ConditionExpression("objectid", ConditionOperator.Equal, table.MetadataId!.Value),
                },
            },
        }).Entities.Count > 0;
        Assert(inSolution, $"{HmacKeyTableSpec.Table} no es componente de {HmacKeyTableSpec.SolutionUniqueName}: su schema no viajaría a Test/Prod");

        var value = table.Attributes?.SingleOrDefault(a =>
            string.Equals(a.LogicalName, HmacKeyTableSpec.ValueColumn, StringComparison.OrdinalIgnoreCase));
        Assert(value is not null, $"falta la columna {HmacKeyTableSpec.ValueColumn}");
        Assert(value!.IsAuditEnabled?.Value == HmacKeyTableSpec.ValueColumnAudited,
            $"{HmacKeyTableSpec.ValueColumn} tiene audit de columna ON: el valor de la key quedaría en el audit log");
        Assert(value.IsValidForAdvancedFind?.Value == HmacKeyTableSpec.ValueColumnValidForAdvancedFind,
            $"{HmacKeyTableSpec.ValueColumn} es válida para Advanced Find");

        Assert(value.IsSecured == HmacKeyTableSpec.ValueColumnSecured,
            $"{HmacKeyTableSpec.ValueColumn} no tiene column security (IsSecured={value.IsSecured}): el form mostraría la key en claro");

        Console.WriteLine("[HMAC] OK: componente de la solución, Organization-owned, audit de tabla ON, columna del valor sin audit, fuera de Advanced Find y asegurada (column security).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[HMAC]: " + message);
    }
}
