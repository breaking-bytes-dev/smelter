// [E2E-n] Cobertura E2E de TODO campo ✅ que la MDA ofrece, probada contra Dev en cada corrida de verify (auditoría de campos,
// "✅ sin E2E"). Un paso por caso, cada uno con su propio template/CSV chicos (la lógica pura: Smelter.Deploy/E2eSpec*.cs, validada
// por tests de contrato) y con salida [E2E-<id>] que dice QUÉ falló y DÓNDE (fila, columna, contador).
//
//   dotnet run --project tools/Smelter.Verify -- --only 4a,5c     # solo esos pasos (1 = grupo; "e2e" = todos)
//
// Reglas del runner:
//   - Solo tablas estándar: contact (su alternate key emailaddress1 es la única garantizada en Dev).
//   - Todo lo que crea (contacts, jobs, templates) cuelga de un dominio/prefijo propio (E2eSpec) y se borra al terminar Y antes de
//     empezar (por si una corrida anterior murió a medias). Los contacts se borran hijos primero.
//   - Un paso que falla no frena a los demás: se informa cada uno y al final falla el verify con la lista.
//   - El caso 9 (contadores) no es un paso: AssertJob verifica status/total/processed/success/failed/skipped/starttime/endtime en todos.
//   - Caso 13 (ValidateQueueAccess negativo) NO va acá: necesita una identidad sin privilegios (se prueba aparte con sesiones de navegador).

using System.Diagnostics;
using System.Text;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using static Smelter.Verify.ApiCalls;

namespace Smelter.Verify;

/// <summary>Uniquenames de las Custom APIs que usan los pasos.</summary>
internal sealed record E2eApis(string ImportTemplate, string ExportTemplate, string Prepare, string ProcessChunk);

internal static partial class E2eRunner
{
    private const string JobTable = "sanic_tbl_smlt_importjob";
    private const string TemplateTable = "sanic_tbl_smlt_importtemplate";
    private const string TableMappingTable = "sanic_tbl_smlt_tablemapping";
    private const string ColumnMappingTable = "sanic_tbl_smlt_columnmapping";
    private const string RelationshipTable = "sanic_tbl_smlt_relationshipmapping";
    private const string LogTable = "sanic_tbl_smlt_importjoblog";

    private const string StatusAttr = "sanic_smlt_ij_status";
    private const string FileAttr = "sanic_smlt_ij_sourcefile";

    private sealed record Env(ServiceClient Client, E2eApis Apis)
    {
        /// <summary>Jobs de un paso que se encolaron antes, para correr en paralelo con el paso anterior (7b mientras 7a espera al flow).</summary>
        internal Dictionary<string, Guid> Pending { get; } = new();
    }

    internal static void Run(ServiceClient client, E2eApis apis, string? only)
    {
        var steps = E2eSteps.Select(only);
        var env = new Env(client, apis);
        var failures = new List<string>();
        var total = Stopwatch.StartNew();

        Console.WriteLine($"[E2E] {steps.Count} paso(s): {string.Join(", ", steps.Select(s => s.Id))}");
        DeleteData(client); // por si una corrida anterior murió a medias

        foreach (var step in steps)
        {
            Console.WriteLine($"[E2E-{step.Id}] {step.Title} ...");
            var sw = Stopwatch.StartNew();
            try
            {
                // El flow tarda decenas de segundos por job: si también se pidió 7b, su job se encola ya y 7a y 7b esperan en paralelo.
                if (step.Id == "7a" && steps.Any(s => s.Id == "7b")) PrequeueFlowFailure(env);
                Dispatch(env, step.Id);
                Console.WriteLine($"[E2E-{step.Id}] OK ({sw.Elapsed.TotalSeconds:F0}s)");
            }
            catch (Exception ex)
            {
                var text = Describe(ex);
                failures.Add($"[E2E-{step.Id}] {step.Title}\n    {text.Replace("\n", "\n    ")}");
                Console.Error.WriteLine($"[E2E-{step.Id}] FAIL ({sw.Elapsed.TotalSeconds:F0}s): {text}");
            }
        }

        Console.WriteLine($"[E2E] {steps.Count - failures.Count}/{steps.Count} paso(s) OK en {total.Elapsed.TotalSeconds:F0}s.");
        if (failures.Count > 0)
            throw new InvalidOperationException($"{failures.Count} paso(s) E2E fallaron:\n" + string.Join("\n", failures));
    }

    private static void Dispatch(Env env, string id)
    {
        switch (id)
        {
            case "1": CreateIfNotExistsFalse(env); break;
            case "2": DataTypes(env); break;
            case "2g": GuidType(env); break;
            case "3": RequiredAndDefault(env); break;
            case "3b": EmptyDedupKey(env); break;
            case "4a": Relationship(env, hmac: false); break;
            case "4b": Relationship(env, hmac: true); break;
            case "5a":
            case "5b":
            case "5c": RunContactCase(env, E2eSpec.Formats.ForStep(id), id); break;
            case "6": ArchivedRejected(env); break;
            case "7a": CloudFlowCompletes(env); break;
            case "7b": CloudFlowFails(env); break;
            case "7c": CloudFlowDoubleQueue(env); break;
            case "7d": RequeueWhileProcessingRejected(env); break;
            case "7e": RetryClearsPreviousLogs(env); break;
            case "8": DryRun(env); break;
            case "10": ExportImportRoundtrip(env); break;
            case "11": RunContactCase(env, E2eSpec.CaseInsensitive.Case(), id); break;
            case "12": Transforms(env); break;
            case "13":
                Console.WriteLine("[E2E-13] NOTA: el camino positivo del gate (el SP pone Queued y pasa) lo cubren 7a/7b; el NEGATIVO necesita una " +
                                  "identidad sin acceso al template y se prueba aparte con sesiones de navegador.");
                break;
            case "14": ManyToManyRejected(env); break;
            default: throw new InvalidOperationException($"Paso E2E sin implementación: {id}");
        }
    }

    // ─── helpers de datos ──────────────────────────────────────────────────────────────────────────────────

    private static Guid ImportTemplate(Env env, TemplateExportDto dto) =>
        ImportTemplateJson(env, new TemplateSerializer().Serialize(dto));

    private static Guid ImportTemplateJson(Env env, string json) =>
        (Guid)env.Client.Execute(new OrganizationRequest(env.Apis.ImportTemplate) { ["Json"] = json })["TemplateId"];

    /// <summary>Job con status vacío (como lo deja el form antes del botón Ejecutar) y el CSV subido, si hay.</summary>
    private static Guid CreateJob(Env env, Guid templateId, string key, string? csv, bool dryRun = false)
    {
        var job = new Entity(JobTable)
        {
            ["sanic_smlt_ij_name"] = E2eSpec.JobName(key),
            ["sanic_smlt_ij_importtemplateid"] = new EntityReference(TemplateTable, templateId),
        };
        if (dryRun) job["sanic_smlt_ij_isdryrun"] = true;
        var id = env.Client.Create(job);
        if (csv is not null)
            UploadBlocks(env.Client, new EntityReference(JobTable, id), FileAttr, key + ".csv", new UTF8Encoding(false).GetBytes(csv));
        return id;
    }

    /// <summary>Prepare + ProcessChunk hasta que HasMore=false (o falla). Devuelve lo que contestó cada chunk.</summary>
    private static List<(int ProcessedSoFar, bool HasMore)> RunJob(Env env, Guid jobId, int expectTotal)
    {
        var prep = ExecuteRetryingBlob(env.Client, new OrganizationRequest(env.Apis.Prepare) { ["JobId"] = jobId });
        Ensure((bool)prep["Claimed"], "PrepareImport debía reclamar el job (Claimed=true) y no lo hizo");
        var total = (int)prep["TotalRows"];
        Ensure(total == expectTotal, $"PrepareImport debía contar {expectTotal} filas, contó {total}");

        var chunks = new List<(int, bool)>();
        for (var i = 0; i < 30; i++)
        {
            var resp = ExecuteRetryingBlob(env.Client, new OrganizationRequest(env.Apis.ProcessChunk) { ["JobId"] = jobId });
            var hasMore = (bool)resp["HasMore"];
            chunks.Add(((int)resp["ProcessedSoFar"], hasMore));
            if (!hasMore) return chunks;
        }
        throw new InvalidOperationException("ProcessChunk no terminó en 30 iteraciones (HasMore nunca fue false).");
    }

    private sealed record JobExpect(ImportJobStatus Status, int Total, int Success, int Failed = 0, int Skipped = 0);

    /// <summary>Corre el job y verifica contadores y fechas; si algo falla, imprime los Import Job Logs antes de propagar.</summary>
    private static void RunAndAssert(Env env, Guid jobId, JobExpect expect)
    {
        try
        {
            RunJob(env, jobId, expect.Total);
            AssertJob(env, jobId, expect);
        }
        catch
        {
            DumpLogs(env, jobId);
            throw;
        }
    }

    /// <summary>Caso 9: status + totalrows + processedrows + success/failed/skipped + starttime/endtime coherentes.</summary>
    private static void AssertJob(Env env, Guid jobId, JobExpect e)
    {
        var job = env.Client.Retrieve(JobTable, jobId, new ColumnSet(StatusAttr, "sanic_smlt_ij_totalrows", "sanic_smlt_ij_processedrows",
            "sanic_smlt_ij_successcount", "sanic_smlt_ij_failedcount", "sanic_smlt_ij_skippedcount",
            "sanic_smlt_ij_starttime", "sanic_smlt_ij_endtime"));

        var status = job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value;
        Ensure(status == (int)e.Status, $"el job debía quedar {e.Status}({(int)e.Status}), quedó {StatusName(status)}");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_totalrows") == e.Total, $"totalrows debía ser {e.Total}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_totalrows")}");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_processedrows") == e.Total, $"processedrows debía ser {e.Total}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_processedrows")}");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_successcount") == e.Success, $"successcount debía ser {e.Success}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_successcount")}");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_failedcount") == e.Failed, $"failedcount debía ser {e.Failed}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_failedcount")}");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_skippedcount") == e.Skipped, $"skippedcount debía ser {e.Skipped}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_skippedcount")}");

        var start = job.GetAttributeValue<DateTime?>("sanic_smlt_ij_starttime");
        var end = job.GetAttributeValue<DateTime?>("sanic_smlt_ij_endtime");
        Ensure(start.HasValue, "starttime quedó vacío (Prepare debía escribirlo)");
        Ensure(end.HasValue, "endtime quedó vacío (el último ProcessChunk debía escribirlo)");
        Ensure(end!.Value >= start!.Value, $"endtime ({end:O}) es anterior a starttime ({start:O})");
        Ensure(DateTime.UtcNow - start.Value.ToUniversalTime() < TimeSpan.FromHours(6) && start.Value.ToUniversalTime() < DateTime.UtcNow.AddMinutes(10),
            $"starttime ({start:O}) no es de esta corrida");
    }

    private static string StatusName(int? status) =>
        status.HasValue && Enum.IsDefined(typeof(ImportJobStatus), status.Value) ? $"{(ImportJobStatus)status.Value}({status.Value})" : status?.ToString() ?? "(vacío)";

    private static List<Entity> LogsOf(Env env, Guid jobId) =>
        env.Client.RetrieveMultiple(new QueryExpression(LogTable)
        {
            ColumnSet = new ColumnSet("sanic_smlt_ijl_rownumber", "sanic_smlt_ijl_errorcategory", "sanic_smlt_ijl_errormessage"),
            Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ijl_importjobid", ConditionOperator.Equal, jobId) } },
            Orders = { new OrderExpression("sanic_smlt_ijl_rownumber", OrderType.Ascending) },
        }).Entities.ToList();

    /// <summary>Los mensajes del log no llevan datos de filas (PII-free por diseño): son lo que hay para diagnosticar.</summary>
    private static void DumpLogs(Env env, Guid jobId)
    {
        try
        {
            var logs = LogsOf(env, jobId);
            Console.Error.WriteLine($"      Import Job Log del job ({logs.Count} registro/s{(logs.Count > 5 ? ", primeros 5" : "")}):");
            foreach (var l in logs.Take(5))
            {
                var cat = l.GetAttributeValue<OptionSetValue>("sanic_smlt_ijl_errorcategory")?.Value;
                Console.Error.WriteLine($"      fila {l.GetAttributeValue<int?>("sanic_smlt_ijl_rownumber")} " +
                    $"[{(cat.HasValue && Enum.IsDefined(typeof(ErrorCategory), cat.Value) ? ((ErrorCategory)cat.Value).ToString() : cat?.ToString())}] " +
                    l.GetAttributeValue<string>("sanic_smlt_ijl_errormessage"));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"      (no se pudieron leer los logs: {ex.Message})");
        }
    }

    private static Entity? ContactByEmail(Env env, string email, params string[] columns) =>
        env.Client.RetrieveMultiple(new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(columns),
            TopCount = 1,
            Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.Equal, email) } },
        }).Entities.FirstOrDefault();

    private static Entity RequireContact(Env env, string email, params string[] columns) =>
        ContactByEmail(env, email, columns) ?? throw new InvalidOperationException($"ASSERT: debía existir el contact '{email}' y no existe");

    private static List<Entity> RetrieveAll(ServiceClient client, QueryExpression query)
    {
        query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
        var all = new List<Entity>();
        while (true)
        {
            var page = client.RetrieveMultiple(query);
            all.AddRange(page.Entities);
            if (!page.MoreRecords) return all;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    private static List<Entity> AllE2eContacts(Env env, params string[] columns) =>
        RetrieveAll(env.Client, new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(columns),
            Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, E2eSpec.Domain) } },
        });

    private static int CountContactsWithPrefix(Env env, string localPrefix) =>
        RetrieveAll(env.Client, new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(false),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("emailaddress1", ConditionOperator.BeginsWith, localPrefix),
                    new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, E2eSpec.Domain),
                },
            },
        }).Count;

    /// <summary>Verifica firstname/lastname de cada fila del caso (las filas existen exactamente como el CSV las describe).</summary>
    private static void AssertContactNames(Env env, IEnumerable<ContactRow> rows)
    {
        foreach (var r in rows)
        {
            var c = RequireContact(env, r.Email, "firstname", "lastname");
            Ensure(c.GetAttributeValue<string>("firstname") == r.First, $"{r.Email}: firstname debía ser '{r.First}', es '{c.GetAttributeValue<string>("firstname")}'");
            Ensure(c.GetAttributeValue<string>("lastname") == r.Last, $"{r.Email}: lastname debía ser '{r.Last}', es '{c.GetAttributeValue<string>("lastname")}'");
        }
    }

    /// <summary>Import + job + corrida + contadores + nombres de un caso "contact simple" (formatos, case-insensitive).</summary>
    private static void RunContactCase(Env env, ContactCase c, string stepId)
    {
        var templateId = ImportTemplate(env, c.Dto);
        var jobId = CreateJob(env, templateId, c.Key, c.Csv);
        RunAndAssert(env, jobId, new JobExpect(ImportJobStatus.Completed, c.Rows.Count, c.Rows.Count));
        AssertContactNames(env, c.Rows);
        Console.WriteLine($"[E2E-{stepId}]   {c.Rows.Count} filas importadas con los valores esperados (delimiter '{c.Dto.Template.Delimiter}', header={c.Dto.Template.HasHeaderRow}).");
    }

    // ─── fallas esperadas ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>La acción debe fallar con un fault cuya cadena (mensaje/trace) contenga TODAS las frases.</summary>
    private static void ExpectFault(string what, Action action, params string[] needles)
    {
        try
        {
            action();
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> fx)
        {
            foreach (var needle in needles)
                Ensure(ChainContains(fx.Detail, needle), $"{what}: el fault debía mencionar '{needle}', fue: {fx.Detail.Message}");
            return;
        }
        throw new InvalidOperationException($"ASSERT: {what} debía fallar y terminó bien");
    }

    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder(ex.Message);
        if (ex is System.ServiceModel.FaultException<OrganizationServiceFault> fx)
            for (var fault = fx.Detail; fault is not null; fault = fault.InnerFault)
                sb.Append($"\n  Fault 0x{fault.ErrorCode:X8}: {fault.Message}");
        return sb.ToString();
    }

    // ─── limpieza ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Borra TODO lo que crean los pasos E2E: jobs (los logs caen por cascade), contacts (los que tienen padre primero) y templates
    /// con sus mappings. Nunca lanza: cada borrado fallido se informa como WARN y sigue con el resto.
    /// </summary>
    internal static void DeleteData(ServiceClient client)
    {
        try
        {
            var jobs = RetrieveAll(client, new QueryExpression(JobTable)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ij_name", ConditionOperator.BeginsWith, E2eSpec.JobPrefix) } },
            });
            DeleteMany(client, JobTable, jobs.Select(j => j.Id).ToList());

            // Contacts: los que tienen padre (parentcustomerid) primero, para no chocar con una restricción de borrado.
            var contacts = RetrieveAll(client, new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("parentcustomerid"),
                Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, E2eSpec.Domain) } },
            });
            DeleteMany(client, "contact", contacts.Where(c => c.GetAttributeValue<EntityReference>("parentcustomerid") is not null).Select(c => c.Id).ToList());
            DeleteMany(client, "contact", contacts.Where(c => c.GetAttributeValue<EntityReference>("parentcustomerid") is null).Select(c => c.Id).ToList());

            foreach (var t in RetrieveAll(client, new QueryExpression(TemplateTable)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = { Conditions = { new ConditionExpression("sanic_smlt_it_name", ConditionOperator.BeginsWith, E2eSpec.TemplatePrefix) } },
            }))
                DeleteTemplate(client, t.Id);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Limpieza E2E incompleta: {ex.Message}");
        }
    }

    /// <summary>Borra en lotes (ExecuteMultiple, ContinueOnError). "Does Not Exist" (borrado en cascada) no es un problema; lo demás se informa.</summary>
    private static void DeleteMany(ServiceClient client, string table, IReadOnlyList<Guid> ids)
    {
        foreach (var batch in ids.Chunk(500))
        {
            var request = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true },
            };
            foreach (var id in batch)
                request.Requests.Add(new DeleteRequest { Target = new EntityReference(table, id) });

            try
            {
                var response = (ExecuteMultipleResponse)client.Execute(request);
                foreach (var item in response.Responses.Where(r => r.Fault is not null
                             && r.Fault.Message?.Contains("Does Not Exist", StringComparison.OrdinalIgnoreCase) != true))
                    Console.Error.WriteLine($"[WARN] No se pudo borrar {table}/{batch[item.RequestIndex]}: {item.Fault.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] No se pudo borrar un lote de {table}: {ex.Message}");
            }
        }
    }

    private static void DeleteTemplate(ServiceClient client, Guid templateId)
    {
        // Relaciones primero (referencian table mappings), luego column mappings, luego table mappings y por último el template.
        foreach (var r in ByFk(client, RelationshipTable, "sanic_smlt_rm_importtemplateid", templateId))
            DeleteQuietly(client, RelationshipTable, r.Id);
        foreach (var tm in ByFk(client, TableMappingTable, "sanic_smlt_tm_importtemplateid", templateId))
        {
            foreach (var cm in ByFk(client, ColumnMappingTable, "sanic_smlt_cm_tablemappingid", tm.Id))
                DeleteQuietly(client, ColumnMappingTable, cm.Id);
            DeleteQuietly(client, TableMappingTable, tm.Id);
        }
        DeleteQuietly(client, TemplateTable, templateId);
    }

    private static List<Entity> ByFk(ServiceClient client, string table, string fkColumn, Guid value) =>
        RetrieveAll(client, new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression(fkColumn, ConditionOperator.Equal, value) } },
        });

    private static void DeleteQuietly(ServiceClient client, string table, Guid id)
    {
        try
        {
            client.Delete(table, id);
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> fx)
        {
            // Ya no existe (p. ej. borrado en cascada): nada que hacer. Cualquier otra cosa se informa.
            if (fx.Detail.Message?.Contains("Does Not Exist", StringComparison.OrdinalIgnoreCase) == true) return;
            Console.Error.WriteLine($"[WARN] No se pudo borrar {table}/{id}: {fx.Detail.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] No se pudo borrar {table}/{id}: {ex.Message}");
        }
    }
}
