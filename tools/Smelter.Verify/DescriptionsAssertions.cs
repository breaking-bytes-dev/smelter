// Asserts E2E de ColumnDescriptionsSpec.cs: toda columna custom (sanic_smlt_*) de las 6 tablas de Smelter tiene
// Description, y las de la spec valen exactamente el texto declarado. Solo lee metadata.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class DescriptionsAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[DESC] Verificando Description de columnas (ColumnDescriptionsSpec.cs) ...");

        var expected = ColumnDescriptionsCatalogo.Descriptions.ToDictionary(d => (d.Entity, d.Column), d => d.Text);
        var checkedCount = 0;

        foreach (var table in SecuritySpec.Tables.All)
        {
            var metadata = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Attributes,
                RetrieveAsIfPublished = true,
            })).EntityMetadata;

            var custom = (metadata.Attributes ?? [])
                .Where(a => a.LogicalName.StartsWith("sanic_smlt_", StringComparison.Ordinal))
                // Columnas virtuales que Dataverse genera solo (p.ej. "<bool>name", "<lookup>name"): no aceptan
                // Description y no son configurables; la descripción va en la columna real (AttributeOf).
                .Where(a => string.IsNullOrEmpty(a.AttributeOf))
                .ToList();
            Assert(custom.Count > 0, $"{table}: sin columnas sanic_smlt_* (¿metadata incompleta?)");

            foreach (var attribute in custom)
            {
                var current = ColumnDescriptionsProvisioner.CurrentDescription(attribute);
                Assert(!string.IsNullOrWhiteSpace(current), $"{table}.{attribute.LogicalName} no tiene Description");
                if (expected.TryGetValue((table, attribute.LogicalName), out var text))
                    Assert(current == text, $"{table}.{attribute.LogicalName}: Description distinta de la spec");
                checkedCount++;
            }
        }

        Console.WriteLine($"[DESC] OK: {checkedCount} columnas custom con Description.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[DESC]: " + message);
    }
}
