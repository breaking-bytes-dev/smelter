// Provisioning idempotente de alternate keys (KeySpec.cs). Para cada spec:
//   - si la key existe con la composición correcta → solo verifica que el índice esté Active;
//   - si existe con composición INCORRECTA (el caso del bug 1.2.1.0 del Import Job Log) →
//     DeleteEntityKeyRequest + CreateEntityKeyRequest con las columnas correctas;
//   - si no existe → se crea.
// La creación de una key dispara la creación ASÍNCRONA del índice en SQL: se espera hasta que
// EntityKeyIndexStatus sea Active (Failed aborta el deploy — típico si hay datos duplicados
// preexistentes que violan la unicidad de la key nueva).

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Smelter.Deploy;

internal static class KeyProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4c] KeyProvisioner: alternate keys (KeySpec.cs) ...");

        foreach (var spec in KeyCatalogo.Keys)
        {
            var existente = RetrieveKey(client, spec);

            if (existente is not null && MatchesSpec(existente, spec))
            {
                Console.WriteLine($"[4c]   key '{spec.SchemaName}' ya tiene la composición correcta " +
                    $"({string.Join(", ", spec.KeyAttributes)}).");
                EsperarIndiceActivo(client, spec);
                continue;
            }

            if (existente is not null)
            {
                Console.WriteLine($"[4c]   key '{spec.SchemaName}' tiene composición INCORRECTA " +
                    $"({string.Join(", ", existente.KeyAttributes ?? [])}) — se elimina y recrea.");
                client.Execute(new DeleteEntityKeyRequest
                {
                    EntityLogicalName = spec.EntityLogicalName,
                    Name = existente.LogicalName,
                });
            }

            var create = new CreateEntityKeyRequest
            {
                EntityName = spec.EntityLogicalName,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = spec.SchemaName,
                    LogicalName = spec.SchemaName.ToLowerInvariant(),
                    DisplayName = new Label(spec.DisplayName, 1033),
                    KeyAttributes = spec.KeyAttributes,
                },
            };
            create.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
            client.Execute(create);
            Console.WriteLine($"[4c]   key '{spec.SchemaName}' creada con ({string.Join(", ", spec.KeyAttributes)}). " +
                "Esperando el índice ...");

            EsperarIndiceActivo(client, spec);
        }
    }

    internal static EntityKeyMetadata? RetrieveKey(ServiceClient client, EntityKeySpec spec)
    {
        // EntityFilters.All para que EntityMetadata.Keys venga poblado (los filtros parciales no
        // siempre incluyen las keys). Es una sola llamada por spec — costo aceptable en deploy.
        var metadata = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = spec.EntityLogicalName,
            EntityFilters = EntityFilters.All,
        })).EntityMetadata;

        return metadata.Keys?.FirstOrDefault(k =>
            string.Equals(k.LogicalName, spec.SchemaName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(k.SchemaName, spec.SchemaName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool MatchesSpec(EntityKeyMetadata key, EntityKeySpec spec) =>
        key.KeyAttributes is not null
        && key.KeyAttributes.Length == spec.KeyAttributes.Length
        && spec.KeyAttributes.All(a => key.KeyAttributes.Contains(a, StringComparer.OrdinalIgnoreCase));

    private static void EsperarIndiceActivo(ServiceClient client, EntityKeySpec spec)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (true)
        {
            var key = RetrieveKey(client, spec)
                ?? throw new InvalidOperationException($"key '{spec.SchemaName}' desapareció durante la espera del índice.");

            switch (key.EntityKeyIndexStatus)
            {
                case EntityKeyIndexStatus.Active:
                    Console.WriteLine($"[4c]   key '{spec.SchemaName}': índice Active.");
                    return;
                case EntityKeyIndexStatus.Failed:
                    throw new InvalidOperationException(
                        $"key '{spec.SchemaName}': el índice quedó en Failed — casi seguro hay filas duplicadas " +
                        $"preexistentes en {spec.EntityLogicalName} que violan ({string.Join(", ", spec.KeyAttributes)}). " +
                        "Limpiar los duplicados y reactivar la key (o re-correr el deploy).");
                default:
                    if (DateTime.UtcNow > deadline)
                        throw new InvalidOperationException(
                            $"key '{spec.SchemaName}': el índice sigue en {key.EntityKeyIndexStatus} tras 180 s.");
                    Console.WriteLine($"[4c]   ... índice {key.EntityKeyIndexStatus}, reintento en 10 s");
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
                    break;
            }
        }
    }
}
