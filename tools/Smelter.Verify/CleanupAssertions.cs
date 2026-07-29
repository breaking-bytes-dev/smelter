// Asserts E2E de la limpieza de schema (SchemaCleanupSpec.cs): cada atributo del catálogo debe
// haber DESAPARECIDO de la metadata del ambiente. Mismo patrón por clave natural que el resto de
// las assertions de Verify.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class CleanupAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[CLEAN] Verificando atributos eliminados (SchemaCleanupSpec.cs) ...");

        foreach (var porEntidad in SchemaCleanupCatalogo.Attributes.GroupBy(s => s.EntityLogicalName))
        {
            var metadata = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = porEntidad.Key,
                EntityFilters = EntityFilters.Attributes,
            })).EntityMetadata;

            foreach (var spec in porEntidad)
            {
                var sigueExistiendo = metadata.Attributes?.Any(a =>
                    string.Equals(a.LogicalName, spec.AttributeLogicalName, StringComparison.OrdinalIgnoreCase)) == true;
                Assert(!sigueExistiendo,
                    $"el atributo {spec.EntityLogicalName}.{spec.AttributeLogicalName} SIGUE existiendo " +
                    "(¿SchemaCleanupProvisioner no corrió o falló por dependencias?)");
                Console.WriteLine($"[CLEAN]   {spec.EntityLogicalName}.{spec.AttributeLogicalName}: eliminado OK.");
            }
        }

        Console.WriteLine($"[CLEAN] OK: {SchemaCleanupCatalogo.Attributes.Length} atributo(s) fuera del schema.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[CLEAN]: " + message);
    }
}
