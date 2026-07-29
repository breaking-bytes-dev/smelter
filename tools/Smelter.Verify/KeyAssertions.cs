// Asserts E2E de las alternate keys provisionadas por KeyProvisioner (KeySpec.cs): para cada spec,
// la key debe existir en Dev con EXACTAMENTE esa composición de columnas y el índice Active. Corre
// antes del smoke funcional: si la key del Import Job Log está mal (el bug de 1.2.1.0), el assert
// del log de filas fallidas daría un falso negativo confuso.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class KeyAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[KEY] Verificando alternate keys (KeySpec.cs) ...");

        foreach (var spec in KeyCatalogo.Keys)
        {
            var key = KeyProvisioner.RetrieveKey(client, spec);
            Assert(key is not null,
                $"la key '{spec.SchemaName}' no existe en {spec.EntityLogicalName} (¿corriste Smelter.Deploy?)");
            Assert(KeyProvisioner.MatchesSpec(key!, spec),
                $"la key '{spec.SchemaName}' tiene composición ({string.Join(", ", key!.KeyAttributes ?? [])}), " +
                $"esperada ({string.Join(", ", spec.KeyAttributes)})");
            Assert(key.EntityKeyIndexStatus == EntityKeyIndexStatus.Active,
                $"la key '{spec.SchemaName}' tiene el índice en {key.EntityKeyIndexStatus}, esperado Active " +
                "— con el índice inactivo los upserts por esa key fallan");

            Console.WriteLine($"[KEY]   '{spec.SchemaName}' ({string.Join(", ", spec.KeyAttributes)}): índice Active. OK.");
        }

        Console.WriteLine($"[KEY] OK: {KeyCatalogo.Keys.Length} alternate key(s) según KeySpec.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[KEY]: " + message);
    }
}
