// [CHUNK] Smoke multi-chunk: 250 filas por el cursor de bytes con ChunkSize=100 (3 chunks, el último parcial),
// con campos entrecomillados con newline embebido y multibyte (ñ, é) cruzando los límites de chunk, y 2 filas
// malas (RowNumber 102 y 251). Llama a las Custom APIs directo, igual que el resto de Verify. La lógica pura
// (CSV, números esperados) vive en Smelter.Deploy/MultiChunkSmokeSpec.cs.
// ProcessChunk clampa el chunk a [100, 10000]: 5 filas por chunk no es posible, 100 es el mínimo real.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using static Smelter.Verify.ApiCalls;

namespace Smelter.Verify;

internal static class ChunkSmoke
{
    private const string JobTable = "sanic_tbl_smlt_importjob";
    private const string TemplateTable = "sanic_tbl_smlt_importtemplate";
    private const string LogTable = "sanic_tbl_smlt_importjoblog";

    internal static void Run(ServiceClient client, string apiImportTemplate, string apiPrepare, string apiProcessChunk)
    {
        DeleteData(client); // por si una corrida anterior murió a medias
        var csv = MultiChunkSmokeSpec.BuildCsv();
        var csvBytes = System.Text.Encoding.UTF8.GetBytes(csv);

        var dto = new TemplateExportDto
        {
            FormatVersion = "1.0",
            Template = new TemplateInfoDto { Name = MultiChunkSmokeSpec.TemplateName, SourceType = 0, HasHeaderRow = true, Delimiter = ",", ChunkSize = 0 },
            TableMappings =
            [
                new TableMappingDto
                {
                    Ref = "tm-contact-chunk", Name = "Contact (smoke multi-chunk)", TargetTable = "contact",
                    ProcessingOrder = 1, DedupColumns = "emailaddress1", CreateIfNotExists = true, UpdateIfExists = true,
                    ColumnMappings =
                    [
                        new ColumnMappingDto { Name = "Email", SourceColumn = "Email", TargetColumn = "emailaddress1", DataType = (int)ColumnDataType.String },
                        new ColumnMappingDto { Name = "Notes", SourceColumn = "Notes", TargetColumn = "description", DataType = (int)ColumnDataType.String },
                        new ColumnMappingDto { Name = "Children", SourceColumn = "Children", TargetColumn = "numberofchildren", DataType = (int)ColumnDataType.Integer },
                    ]
                }
            ]
        };
        var templateId = (Guid)client.Execute(new OrganizationRequest(apiImportTemplate) { ["Json"] = new TemplateSerializer().Serialize(dto) })["TemplateId"];
        client.Update(new Entity(TemplateTable, templateId) { ["sanic_smlt_it_status"] = new OptionSetValue((int)ImportTemplateStatus.Active) });

        var jobId = client.Create(new Entity(JobTable)
        {
            ["sanic_smlt_ij_name"] = MultiChunkSmokeSpec.JobName,
            ["sanic_smlt_ij_importtemplateid"] = new EntityReference(TemplateTable, templateId),
        });
        UploadBlocks(client, new EntityReference(JobTable, jobId), "sanic_smlt_ij_sourcefile", "smoke-chunks.csv", csvBytes);
        Console.WriteLine($"[CHUNK] Job {jobId}: CSV de {MultiChunkSmokeSpec.Rows} filas, {csvBytes.Length} bytes ({csv.Length} chars), subido.");

        // 1ª corrida y 2ª (idempotencia): mismos asserts, mismo resultado, sin duplicar contacts ni logs.
        for (var run = 1; run <= 2; run++)
        {
            RunAndAssert(client, apiPrepare, apiProcessChunk, jobId, csvBytes.Length);
            AssertContacts(client);
            AssertLogs(client, jobId);
            Console.WriteLine($"[CHUNK] Corrida {run}/2 OK.");
        }
        Console.WriteLine("[CHUNK] OK: 3 chunks (100/100/50), cursor de bytes en el fin del archivo, 248 contacts exactamente una vez, logs en filas 102 y 251.");
    }

    private static void RunAndAssert(ServiceClient client, string apiPrepare, string apiProcessChunk, Guid jobId, int byteLength)
    {
        // PrepareImport solo reclama jobs Queued o sin status (1.0.12): la 2ª corrida sobre el MISMO job lo devuelve a
        // "sin status" (NO a Queued, que dispararía el Cloud Flow).
        client.Update(new Entity("sanic_tbl_smlt_importjob", jobId) { ["sanic_smlt_ij_status"] = null });
        var prep = ExecuteRetryingBlob(client, new OrganizationRequest(apiPrepare) { ["JobId"] = jobId });
        Ensure((bool)prep["Claimed"], "PrepareImport debía reclamar el job (Claimed=true) y no lo hizo");
        var total = (int)prep["TotalRows"];
        Ensure(total == MultiChunkSmokeSpec.Rows, $"PrepareImport debía contar {MultiChunkSmokeSpec.Rows} filas (el newline embebido NO suma filas), contó {total}");

        var expected = MultiChunkSmokeSpec.ExpectedChunks();
        var lastCursor = ReadCursor(client, jobId);
        for (var i = 0; i < expected.Count; i++)
        {
            var resp = ExecuteRetryingBlob(client, new OrganizationRequest(apiProcessChunk)
            {
                ["JobId"] = jobId,
                ["ChunkSize"] = MultiChunkSmokeSpec.ChunkSize,
            });
            var hasMore = (bool)resp["HasMore"];
            var processed = (int)resp["ProcessedSoFar"];
            Ensure(processed == expected[i].ProcessedSoFar, $"chunk {i + 1}: ProcessedSoFar debía ser {expected[i].ProcessedSoFar}, fue {processed}");
            Ensure(hasMore == expected[i].HasMore, $"chunk {i + 1}: HasMore debía ser {expected[i].HasMore}, fue {hasMore}");

            var cursor = ReadCursor(client, jobId);
            Ensure(cursor > lastCursor, $"chunk {i + 1}: el cursor de bytes debía avanzar ({lastCursor} -> {cursor})");
            lastCursor = cursor;
        }

        Ensure(lastCursor == byteLength, $"el cursor de bytes debía terminar en {byteLength} (fin del archivo, multibyte contado en bytes), es {lastCursor}");

        var job = client.Retrieve(JobTable, jobId, new ColumnSet("sanic_smlt_ij_status", "sanic_smlt_ij_processedrows",
            "sanic_smlt_ij_successcount", "sanic_smlt_ij_failedcount"));
        Ensure(job.GetAttributeValue<OptionSetValue>("sanic_smlt_ij_status")?.Value == (int)ImportJobStatus.CompletedWithErrors,
            "el job debía quedar CompletedWithErrors");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_processedrows") == MultiChunkSmokeSpec.Rows,
            $"processedrows debía ser {MultiChunkSmokeSpec.Rows}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_processedrows")}");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_successcount") == MultiChunkSmokeSpec.ExpectedSuccess,
            $"successcount debía ser {MultiChunkSmokeSpec.ExpectedSuccess}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_successcount")}");
        Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_failedcount") == MultiChunkSmokeSpec.BadRecords.Count,
            $"failedcount debía ser {MultiChunkSmokeSpec.BadRecords.Count}, es {job.GetAttributeValue<int?>("sanic_smlt_ij_failedcount")}");
    }

    private static decimal? ReadCursor(ServiceClient client, Guid jobId) =>
        client.Retrieve(JobTable, jobId, new ColumnSet("sanic_smlt_ij_bytecursor")).GetAttributeValue<decimal?>("sanic_smlt_ij_bytecursor");

    /// <summary>Cada fila buena existe EXACTAMENTE una vez; las malas no; el texto con newline/multibyte llega intacto.</summary>
    private static void AssertContacts(ServiceClient client)
    {
        var contacts = Contacts(client);
        var byEmail = contacts.GroupBy(c => c.GetAttributeValue<string>("emailaddress1"), StringComparer.OrdinalIgnoreCase).ToList();
        Ensure(byEmail.All(g => g.Count() == 1), "hay emails duplicados entre los contacts del smoke multi-chunk");
        Ensure(contacts.Count == MultiChunkSmokeSpec.ExpectedSuccess, $"debían existir {MultiChunkSmokeSpec.ExpectedSuccess} contacts, hay {contacts.Count}");

        var emails = byEmail.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = MultiChunkSmokeSpec.GoodRecords.Where(i => !emails.Contains(MultiChunkSmokeSpec.Email(i))).Take(5).ToList();
        Ensure(missing.Count == 0, $"faltan contacts de los registros {string.Join(",", missing)} ...");
        Ensure(MultiChunkSmokeSpec.BadRecords.All(i => !emails.Contains(MultiChunkSmokeSpec.Email(i))), "una fila con Children no numérico NO debe crear contact");

        foreach (var c in contacts)
        {
            var email = c.GetAttributeValue<string>("emailaddress1");
            var record = int.Parse(email[5..8]); // "chunkNNN@..."
            var notes = MultiChunkSmokeSpec.NormalizeNewlines(c.GetAttributeValue<string>("description") ?? "");
            Ensure(notes == MultiChunkSmokeSpec.ExpectedNotes(record),
                $"registro {record}: el texto (newline embebido / ñ é) no llegó intacto — esperado '{MultiChunkSmokeSpec.ExpectedNotes(record).Replace("\n", "\\n")}', es '{notes.Replace("\n", "\\n")}'");
        }
    }

    private static void AssertLogs(ServiceClient client, Guid jobId)
    {
        var logs = client.RetrieveMultiple(new QueryExpression(LogTable)
        {
            ColumnSet = new ColumnSet("sanic_smlt_ijl_rownumber", "sanic_smlt_ijl_status", "sanic_smlt_ijl_errorcategory"),
            Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ijl_importjobid", ConditionOperator.Equal, jobId) } },
        }).Entities;
        var rowNumbers = logs.Select(l => l.GetAttributeValue<int?>("sanic_smlt_ijl_rownumber") ?? -1).OrderBy(n => n).ToList();
        Ensure(rowNumbers.SequenceEqual(MultiChunkSmokeSpec.ExpectedFailedRowNumbers),
            $"los logs debían apuntar a las filas [{string.Join(",", MultiChunkSmokeSpec.ExpectedFailedRowNumbers)}], apuntan a [{string.Join(",", rowNumbers)}]");
        Ensure(logs.All(l => l.GetAttributeValue<OptionSetValue>("sanic_smlt_ijl_status")?.Value == (int)ImportLogStatus.Failed), "los logs debían estar Failed");
        Ensure(logs.All(l => l.GetAttributeValue<OptionSetValue>("sanic_smlt_ijl_errorcategory")?.Value == (int)ErrorCategory.TypeError), "los logs debían ser TypeError");
    }

    private static List<Entity> Contacts(ServiceClient client) =>
        client.RetrieveMultiple(new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("emailaddress1", "description"),
            Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, MultiChunkSmokeSpec.Domain) } },
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
        }).Entities.ToList();

    /// <summary>Borra contacts y jobs del smoke multi-chunk (los logs caen por cascade con el job).</summary>
    internal static void DeleteData(ServiceClient client)
    {
        foreach (var c in client.RetrieveMultiple(new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, MultiChunkSmokeSpec.Domain) } },
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
        }).Entities)
            client.Delete("contact", c.Id);

        foreach (var j in client.RetrieveMultiple(new QueryExpression(JobTable)
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ij_name", ConditionOperator.Equal, MultiChunkSmokeSpec.JobName) } },
        }).Entities)
            client.Delete(JobTable, j.Id);
    }
}
