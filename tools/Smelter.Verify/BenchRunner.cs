// [BENCH] (--bench, NO forma parte del verify por defecto) Benchmark de 100.000 filas en DRY RUN:
// CSV en memoria (Email, FirstName, LastName) -> job con IsDryRun=true contra un template mínimo a `contact`
// (dedup por emailaddress1) -> PrepareImport + ProcessChunk en loop directo (chunk 1000), cronometrando cada
// llamada del lado cliente. Imprime total / primer, medio y último chunk / p50 / p95 / max y FALLA si el chunk
// más lento tarda >= 60 s (el límite de un plugin síncrono es 2 min). En dry run NO se escribe nada en contact:
// se verifica que no exista ningún contact del dominio del bench. La lógica pura vive en Smelter.Deploy/BenchSpec.cs.

using System.Diagnostics;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using static Smelter.Verify.ApiCalls;

namespace Smelter.Verify;

internal static class BenchRunner
{
    private const string JobTable = "sanic_tbl_smlt_importjob";
    private const string TemplateTable = "sanic_tbl_smlt_importtemplate";

    internal static void Run(ServiceClient client, string apiImportTemplate, string apiPrepare, string apiProcessChunk)
    {
        try
        {
            DeleteData(client);

            var dto = new TemplateExportDto
            {
                FormatVersion = "1.0",
                Template = new TemplateInfoDto { Name = BenchSpec.TemplateName, SourceType = 0, HasHeaderRow = true, Delimiter = ",", ChunkSize = 0 },
                TableMappings =
                [
                    new TableMappingDto
                    {
                        Ref = "tm-contact-bench", Name = "Contact (bench)", TargetTable = "contact",
                        ProcessingOrder = 1, DedupColumns = "emailaddress1", CreateIfNotExists = true, UpdateIfExists = true,
                        ColumnMappings =
                        [
                            new ColumnMappingDto { Name = "Email", SourceColumn = "Email", TargetColumn = "emailaddress1", DataType = (int)ColumnDataType.String },
                            new ColumnMappingDto { Name = "FirstName", SourceColumn = "FirstName", TargetColumn = "firstname", DataType = (int)ColumnDataType.String },
                            new ColumnMappingDto { Name = "LastName", SourceColumn = "LastName", TargetColumn = "lastname", DataType = (int)ColumnDataType.String },
                        ]
                    }
                ]
            };
            var templateId = (Guid)client.Execute(new OrganizationRequest(apiImportTemplate) { ["Json"] = new TemplateSerializer().Serialize(dto) })["TemplateId"];
            client.Update(new Entity(TemplateTable, templateId) { ["sanic_smlt_it_status"] = new OptionSetValue((int)ImportTemplateStatus.Active) });

            var jobId = client.Create(new Entity(JobTable)
            {
                ["sanic_smlt_ij_name"] = BenchSpec.JobName,
                ["sanic_smlt_ij_importtemplateid"] = new EntityReference(TemplateTable, templateId),
                ["sanic_smlt_ij_isdryrun"] = true,
            });

            var csv = BenchSpec.BuildCsv(BenchSpec.Rows);
            var sw = Stopwatch.StartNew();
            UploadBlocks(client, new EntityReference(JobTable, jobId), "sanic_smlt_ij_sourcefile", "bench.csv", csv);
            Console.WriteLine($"[BENCH] CSV de {BenchSpec.Rows:N0} filas ({csv.Length / 1024.0 / 1024.0:0.0} MB) subido en {sw.Elapsed.TotalSeconds:0.0} s. Job dry-run {jobId}.");

            sw.Restart();
            var total = (int)ExecuteRetryingBlob(client, new OrganizationRequest(apiPrepare) { ["JobId"] = jobId })["TotalRows"];
            Console.WriteLine($"[BENCH] PrepareImport: {sw.Elapsed.TotalSeconds:0.0} s, contó {total:N0} filas.");
            Ensure(total == BenchSpec.Rows, $"PrepareImport debía contar {BenchSpec.Rows} filas, contó {total}");

            // Cada llamada cronometrada. La llamada final suele leer 0 filas (el chunk anterior vino lleno y por eso
            // HasMore=true): se descarta de las estadísticas porque solo cierra el job, no procesa filas.
            var chunkMs = new List<double>();
            var previous = 0;
            var maxCalls = BenchSpec.Rows / BenchSpec.ChunkSize + 5;
            for (var call = 1; call <= maxCalls; call++)
            {
                var t = Stopwatch.StartNew();
                OrganizationResponse resp;
                try
                {
                    resp = client.Execute(new OrganizationRequest(apiProcessChunk) { ["JobId"] = jobId, ["ChunkSize"] = BenchSpec.ChunkSize });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"BENCH: ProcessChunk #{call} falló tras {t.Elapsed.TotalSeconds:0.0} s: {ex.Message}", ex);
                }
                t.Stop();

                var processed = (int)resp["ProcessedSoFar"];
                if (processed > previous) chunkMs.Add(t.Elapsed.TotalMilliseconds);
                if (call % 10 == 0 || !(bool)resp["HasMore"])
                    Console.WriteLine($"[BENCH]   chunk #{call}: {t.Elapsed.TotalMilliseconds:0} ms, procesadas {processed:N0}");
                previous = processed;
                if (!(bool)resp["HasMore"]) break;
                Ensure(call < maxCalls, $"ProcessChunk no terminó en {maxCalls} llamadas (HasMore nunca fue false)");
            }

            Ensure(previous == BenchSpec.Rows, $"ProcessedSoFar final debía ser {BenchSpec.Rows}, fue {previous}");
            var stats = BenchStats.Compute(chunkMs);
            Console.WriteLine("[BENCH] " + BenchStats.Format(stats));

            var job = client.Retrieve(JobTable, jobId, new ColumnSet("sanic_smlt_ij_status", "sanic_smlt_ij_processedrows"));
            Ensure(job.GetAttributeValue<OptionSetValue>("sanic_smlt_ij_status")?.Value == (int)ImportJobStatus.Completed, "el job de bench debía quedar Completed");
            Ensure(job.GetAttributeValue<int?>("sanic_smlt_ij_processedrows") == BenchSpec.Rows, "processedrows del job de bench debía ser el total");

            var written = ContactsWritten(client);
            Ensure(written == 0, $"BENCH: el dry run NO debe escribir contacts, pero hay {written}+ con el dominio {BenchSpec.Domain}");

            var gate = BenchStats.Gate(stats, BenchSpec.MaxChunkSeconds);
            if (gate is not null) throw new InvalidOperationException(gate);
            Console.WriteLine($"[BENCH] OK: max {stats.MaxMs:0} ms < {BenchSpec.MaxChunkSeconds} s; 0 contacts escritos (dry run).");
        }
        finally
        {
            try { DeleteData(client); Console.WriteLine("[BENCH] Cleanup OK (job de bench eliminado)."); }
            catch (Exception ex) { Console.Error.WriteLine($"[WARN] Cleanup del bench incompleto: {ex.Message}"); }
        }
    }

    private static int ContactsWritten(ServiceClient client) =>
        client.RetrieveMultiple(new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, BenchSpec.Domain) } },
            TopCount = 1000,
        }).Entities.Count;

    /// <summary>Borra el job de bench (y, por si el dry run falló a medias, cualquier contact del dominio del bench).</summary>
    internal static void DeleteData(ServiceClient client)
    {
        foreach (var j in client.RetrieveMultiple(new QueryExpression(JobTable)
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ij_name", ConditionOperator.Equal, BenchSpec.JobName) } },
        }).Entities)
            client.Delete(JobTable, j.Id);

        foreach (var c in client.RetrieveMultiple(new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, BenchSpec.Domain) } },
            TopCount = 1000,
        }).Entities)
            client.Delete("contact", c.Id);
    }
}
