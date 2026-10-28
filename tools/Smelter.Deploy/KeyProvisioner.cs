// Provisioning idempotente de alternate keys (KeySpec.cs). Para cada spec:
//   - si la key existe con la composición correcta → verifica el índice (y lo repara si hace falta);
//   - si existe con composición INCORRECTA (el caso del bug 1.2.1.0 del Import Job Log) →
//     DeleteEntityKeyRequest + CreateEntityKeyRequest con las columnas correctas;
//   - si no existe → se crea.
// La creación dispara la construcción ASÍNCRONA del índice: se espera EntityKeyIndexStatus.Active.
//
// Si el índice queda Failed, la causa típica son filas DUPLICADAS preexistentes que violan la
// unicidad de la key (en el Import Job Log: basura de la era del bug — la key vieja con autonumber
// no deduplicaba nada). El provisioner se auto-repara con el flujo documentado de la plataforma:
// deduplicar los grupos exactos (conserva la fila MÁS RECIENTE de cada grupo, borra el resto — son
// filas de diagnóstico idénticas en la key, no data de negocio) + ReactivateEntityKeyRequest, y
// re-espera. Si aún así queda Failed, aborta con detalle.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

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
                AsegurarIndiceActivo(client, spec);
                continue;
            }

            if (existente is not null && !DeployOptions.AllowDestructive)
            {
                Console.WriteLine($"[4c]   key '{spec.SchemaName}' tiene composición INCORRECTA — recrearla es destructivo: " +
                    "OMITIDO (usar --allow-destructive o SMELTER_ALLOW_DESTRUCTIVE=true).");
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

            CrearKey(client, spec);
            AsegurarIndiceActivo(client, spec);
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

    private static void CrearKey(ServiceClient client, EntityKeySpec spec)
    {
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
    }

    // Espera el índice; si queda Failed, dedupea las filas en conflicto + reactiva la key (hasta 2
    // intentos — el segundo cubre duplicados que aparezcan entre el scan y la reactivación).
    private static void AsegurarIndiceActivo(ServiceClient client, EntityKeySpec spec)
    {
        if (EsperarIndice(client, spec, failedGrace: TimeSpan.Zero) == EntityKeyIndexStatus.Active)
            return;

        if (!DeployOptions.AllowDestructive)
            throw new InvalidOperationException(
                $"key '{spec.SchemaName}': índice Failed y deduplicar borra filas de {spec.EntityLogicalName} " +
                "(destructivo). Re-correr con --allow-destructive o SMELTER_ALLOW_DESTRUCTIVE=true solo en DEV.");

        for (var intento = 1; intento <= 2; intento++)
        {
            Console.WriteLine($"[4c]   key '{spec.SchemaName}': índice Failed — hay filas que violan la unicidad. " +
                $"Deduplicando (se conserva la más reciente de cada grupo), intento {intento}/2 ...");
            var eliminadas = DedupeRows(client, spec);
            Console.WriteLine($"[4c]   {eliminadas} fila(s) duplicada(s) eliminadas de {spec.EntityLogicalName}. " +
                "Reactivando la key ...");

            client.Execute(new ReactivateEntityKeyRequest
            {
                EntityLogicalName = spec.EntityLogicalName,
                EntityKeyLogicalName = spec.SchemaName.ToLowerInvariant(),
            });

            // La reactivación resetea el estado de forma ASÍNCRONA: el primer read puede seguir
            // devolviendo el Failed viejo (visto en run 30451570950: read 1 s después de reactivar
            // → Failed stale → abort prematuro). Se tolera Failed durante la ventana de gracia.
            if (EsperarIndice(client, spec, failedGrace: TimeSpan.FromSeconds(90)) == EntityKeyIndexStatus.Active)
                return;
        }

        throw new InvalidOperationException(
            $"key '{spec.SchemaName}': el índice sigue Failed tras deduplicar y reactivar 2 veces. " +
            $"Revisar manualmente los datos de {spec.EntityLogicalName}.");
    }

    // failedGrace: tiempo durante el cual un estado Failed se trata como "posiblemente stale"
    // (post-reactivación) y se sigue esperando en vez de devolverlo como definitivo.
    private static EntityKeyIndexStatus EsperarIndice(ServiceClient client, EntityKeySpec spec, TimeSpan failedGrace)
    {
        var inicio = DateTime.UtcNow;
        var deadline = inicio.AddSeconds(300);
        while (true)
        {
            var key = RetrieveKey(client, spec)
                ?? throw new InvalidOperationException($"key '{spec.SchemaName}' desapareció durante la espera del índice.");

            switch (key.EntityKeyIndexStatus)
            {
                case EntityKeyIndexStatus.Active:
                    Console.WriteLine($"[4c]   key '{spec.SchemaName}': índice Active.");
                    return EntityKeyIndexStatus.Active;
                case EntityKeyIndexStatus.Failed:
                    if (DateTime.UtcNow - inicio < failedGrace)
                    {
                        Console.WriteLine("[4c]   ... índice Failed (posible estado stale pre-reactivación), reintento en 10 s");
                        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
                        continue;
                    }
                    return EntityKeyIndexStatus.Failed;
                default:
                    if (DateTime.UtcNow > deadline)
                        throw new InvalidOperationException(
                            $"key '{spec.SchemaName}': el índice sigue en {key.EntityKeyIndexStatus} tras 300 s.");
                    Console.WriteLine($"[4c]   ... índice {key.EntityKeyIndexStatus}, reintento en 10 s");
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
                    break;
            }
        }
    }

    // Borra las filas cuyo valor de key está EXACTAMENTE duplicado, conservando la más reciente de
    // cada grupo. Solo toca duplicados — nunca vacía la tabla.
    private static int DedupeRows(ServiceClient client, EntityKeySpec spec)
    {
        var query = new QueryExpression(spec.EntityLogicalName)
        {
            ColumnSet = new ColumnSet(spec.KeyAttributes.Concat(["createdon"]).ToArray()),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
        };

        var todas = new List<Entity>();
        while (true)
        {
            var page = client.RetrieveMultiple(query);
            todas.AddRange(page.Entities);
            if (!page.MoreRecords) break;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }

        var aBorrar = new List<Guid>();
        var gruposLogueados = 0;
        foreach (var grupo in todas.GroupBy(e => string.Join("|", spec.KeyAttributes.Select(a => ValorNormalizado(e, a)))))
        {
            var duplicadas = grupo
                .OrderByDescending(e => e.GetAttributeValue<DateTime?>("createdon") ?? DateTime.MinValue)
                .Skip(1)
                .ToList();
            if (duplicadas.Count == 0) continue;

            // Con miles de grupos (run 30451570950: 15100 duplicadas), loguear cada uno inunda el log.
            if (++gruposLogueados <= 20)
                Console.WriteLine($"[4c]     grupo ({grupo.Key}): {duplicadas.Count + 1} filas — se conserva 1, se borran {duplicadas.Count}.");
            else if (gruposLogueados == 21)
                Console.WriteLine("[4c]     ... (más grupos con duplicados; se omite el detalle)");

            aBorrar.AddRange(duplicadas.Select(d => d.Id));
        }

        // Deletes en lotes por ExecuteMultiple: de a uno, 15k filas tomaron ~80 min (run 30451570950).
        foreach (var lote in aBorrar.Chunk(500))
        {
            var requests = new OrganizationRequestCollection();
            foreach (var id in lote)
                requests.Add(new DeleteRequest { Target = new EntityReference(spec.EntityLogicalName, id) });

            var response = (ExecuteMultipleResponse)client.Execute(new ExecuteMultipleRequest
            {
                Requests = requests,
                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true },
            });

            if (response.IsFaulted)
                foreach (var item in response.Responses.Where(r => r.Fault is not null))
                    Console.Error.WriteLine($"[4c]     WARN: delete de {lote[item.RequestIndex]} falló: {item.Fault.Message}");
        }

        return aBorrar.Count;
    }

    // Normaliza el valor de una columna de la key para agrupar como agruparía el índice:
    // strings case-insensitive (collation de Dataverse), lookups por GUID.
    private static string ValorNormalizado(Entity e, string attribute)
    {
        var v = e.Contains(attribute) ? e[attribute] : null;
        return v switch
        {
            null => "<null>",
            EntityReference er => er.Id.ToString(),
            OptionSetValue os => os.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => s.Trim().ToLowerInvariant(),
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>",
        };
    }
}
