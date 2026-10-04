// Asserts E2E de la tabla DEV-only de keys HMAC provisionada por HmacKeyTableProvisioner
// (HmacKeyTableSpec.cs): existe, Organization-owned, audit de tabla ON, columna del valor SIN audit
// y fuera de Advanced Find. NO lee ningún valor de key.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class HmacKeyAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine($"[HMAC] Verificando la tabla DEV-only {HmacKeyTableSpec.Table} (HmacKeyTableSpec.cs) ...");

        Assert(HmacKeyTableProvisioner.TableExists(client),
            $"la tabla {HmacKeyTableSpec.Table} no existe (¿corriste Smelter.Deploy?)");

        var table = HmacKeyTableProvisioner.RetrieveTable(client);
        Assert(table.OwnershipType == OwnershipTypes.OrganizationOwned,
            $"la tabla es {table.OwnershipType}, esperada OrganizationOwned");
        Assert(table.IsAuditEnabled?.Value == HmacKeyTableSpec.TableAudited,
            "el audit de la tabla no coincide con el spec (se auditan los cambios de keys, no sus valores)");

        var value = table.Attributes?.SingleOrDefault(a =>
            string.Equals(a.LogicalName, HmacKeyTableSpec.ValueColumn, StringComparison.OrdinalIgnoreCase));
        Assert(value is not null, $"falta la columna {HmacKeyTableSpec.ValueColumn}");
        Assert(value!.IsAuditEnabled?.Value == HmacKeyTableSpec.ValueColumnAudited,
            $"{HmacKeyTableSpec.ValueColumn} tiene audit de columna ON: el valor de la key quedaría en el audit log");
        Assert(value.IsValidForAdvancedFind?.Value == HmacKeyTableSpec.ValueColumnValidForAdvancedFind,
            $"{HmacKeyTableSpec.ValueColumn} es válida para Advanced Find");

        Console.WriteLine("[HMAC] OK: Organization-owned, audit de tabla ON, columna del valor sin audit y fuera de Advanced Find.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[HMAC]: " + message);
    }
}
