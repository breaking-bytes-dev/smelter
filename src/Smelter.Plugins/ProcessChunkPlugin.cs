using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;

namespace Smelter.Plugins
{
    /// <summary>
    /// Backing plugin for the Custom API <c>sanic_ProcessChunk</c>.
    /// Processes ONE chunk per invocation: downloads the CSV, parses only its slice (skip to offset),
    /// upserts in DAG order, logs only failed rows, and advances the offset.
    /// In: JobId (Guid), ChunkSize (int, optional override). Out: HasMore (bool), ProcessedSoFar (int).
    /// </summary>
    public class ProcessChunkPlugin : IPlugin
    {
        private const string BuildMarker = "Smelter ProcessChunk — BUILD chunked-v2 (tracing)";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null); // SYSTEM

            tracing.Trace("=== " + BuildMarker + " ===");

            var jobId = (Guid)context.InputParameters["JobId"];
            var chunkOverride = context.InputParameters.Contains("ChunkSize")
                ? (int)context.InputParameters["ChunkSize"]
                : 0;
            tracing.Trace($"ProcessChunk: JobId={jobId}, chunkOverride={chunkOverride}");

            try
            {
                var handler = new ProcessChunkHandler(
                    new ImportContextLoader(service),
                    new FileDownloader(service),
                    new CsvParserService(),
                    // DEV-only key source for the HMAC transform (see DataverseHmacKeyProvider).
                    new NormalizationEngine(service, tracing, new DataverseHmacKeyProvider(service)),
                    new UpsertWriter(service, tracing),
                    service,
                    tracing);

                var result = handler.Run(jobId, chunkOverride);
                tracing.Trace($"ProcessChunk: HasMore={result.HasMore}, ProcessedSoFar={result.ProcessedSoFar}");

                context.OutputParameters["HasMore"] = result.HasMore;
                context.OutputParameters["ProcessedSoFar"] = result.ProcessedSoFar;
            }
            catch (Exception ex)
            {
                tracing.Trace($"ProcessChunk FAILED: {ex.GetType().Name}: {ex.Message}\nSTACK:\n{ex.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>
    /// Return value from a single <see cref="ProcessChunkHandler.Run"/> call.
    /// Consumed by the Cloud Flow to decide whether to schedule the next chunk.
    /// </summary>
    public class ProcessChunkResult
    {
        /// <summary>
        /// <c>true</c> when the chunk was full (rows read equals chunkSize), meaning more rows
        /// likely remain in the file and the Cloud Flow should invoke ProcessChunk again.
        /// <c>false</c> when the last slice was partial or empty — processing is complete.
        /// </summary>
        public bool HasMore { get; set; }

        /// <summary>
        /// Cumulative count of source rows processed so far across all chunks (including this one).
        /// </summary>
        public int ProcessedSoFar { get; set; }
    }

    /// <summary>
    /// Testable core of ProcessChunk. No staging.
    /// Handles one chunk per invocation: reads the current offset from the job record,
    /// advances it by the number of rows consumed, and writes the new state back.
    /// </summary>
    public class ProcessChunkHandler
    {
        /// <summary>Logical name of the Import Job table.</summary>
        public const string ImportJobTable = "sanic_tbl_smlt_importjob";

        /// <summary>Logical name of the Import Job Log table (one record per failed row).</summary>
        public const string ImportJobLogTable = "sanic_tbl_smlt_importjoblog";

        /// <summary>
        /// Lookup del log hacia su Import Job. OJO: el FK es <c>sanic_smlt_ijl_importjobid</c> —
        /// <c>sanic_smlt_ijl_id</c> NO es un lookup, es el nombre primario autonumber
        /// (<c>IJL-{SEQNUM:4}</c>). Confundirlos rompía TODA la escritura de logs
        /// ("Incorrect type of attribute value EntityReference", bug corregido en 1.2.1.0).
        /// </summary>
        public const string LogJobLookupAttribute = "sanic_smlt_ijl_importjobid";

        /// <summary>Logical name of the file column on the Import Job that stores the source CSV.</summary>
        public const string SourceFileAttribute = "sanic_smlt_ij_sourcefile";

        /// <summary>Rows per chunk when neither the template nor the caller specifies one.</summary>
        public const int DefaultChunkSize = 1000;

        /// <summary>Floor for the chunk size — prevents excessively small batches that amplify API overhead.</summary>
        public const int MinChunkSize = 100;

        /// <summary>Ceiling for the chunk size — prevents plugin timeout due to ExecuteMultiple cost.</summary>
        public const int MaxChunkSize = 10000;

        private readonly IImportContextLoader _loader;
        private readonly IFileDownloader _downloader;
        private readonly ICsvReader _csv;
        private readonly INormalizationEngine _engine;
        private readonly IUpsertWriter _writer;
        private readonly IOrganizationService _service;
        private readonly ITracingService _tracing;

        /// <summary>
        /// Initializes a new instance of <see cref="ProcessChunkHandler"/>.
        /// </summary>
        /// <param name="loader">Loads the full Import Context (template + mappings + current progress) from Dataverse.</param>
        /// <param name="downloader">Downloads the source CSV bytes from the file column on the job record.</param>
        /// <param name="csv">Streaming CSV reader; parses only the chunk slice without loading the whole file.</param>
        /// <param name="engine">Builds and normalizes <see cref="TargetRecord"/>s in DAG order.</param>
        /// <param name="writer">Issues ExecuteMultiple upserts to Dataverse.</param>
        /// <param name="service">Organization service used to update the job record (SYSTEM context).</param>
        /// <param name="tracing">Optional tracing service; pass <c>null</c> in unit tests.</param>
        public ProcessChunkHandler(IImportContextLoader loader, IFileDownloader downloader, ICsvReader csv,
            INormalizationEngine engine, IUpsertWriter writer, IOrganizationService service, ITracingService tracing)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _csv = csv ?? throw new ArgumentNullException(nameof(csv));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _tracing = tracing;
        }

        /// <summary>
        /// Processes one chunk of the source CSV and persists updated counters/offset on the job record.
        /// </summary>
        /// <param name="importJobId">GUID of the <c>sanic_tbl_smlt_importjob</c> record.</param>
        /// <param name="chunkSizeOverride">
        /// Number of rows to process in this invocation. Pass <c>0</c> to use the value stored on
        /// the template; the result is always clamped to [<see cref="MinChunkSize"/>, <see cref="MaxChunkSize"/>].
        /// </param>
        /// <returns>
        /// A <see cref="ProcessChunkResult"/> indicating whether more rows remain and the cumulative
        /// processed count, so the Cloud Flow can schedule the next invocation.
        /// </returns>
        public ProcessChunkResult Run(Guid importJobId, int chunkSizeOverride)
        {
            var context = _loader.LoadContext(importJobId);
            var template = context.Template;
            var chunkSize = ResolveChunkSize(context, chunkSizeOverride);
            var offset = context.InitialProcessingBatch;
            _tracing?.Trace($"Run: offset={offset}, chunkSize={chunkSize}, tableMappings={context.TableMappings.Count}");

            var bytes = _downloader.Download(new EntityReference(ImportJobTable, importJobId), SourceFileAttribute);
            var rows = _csv.ReadRange(bytes, template.HasHeaderRow, template.Delimiter, offset, chunkSize);
            _tracing?.Trace($"Run: read {rows.Count} rows from offset {offset}.");

            if (rows.Count == 0)
            {
                var doneStatus = context.InitialFailedCount > 0
                    ? ImportJobStatus.CompletedWithErrors : ImportJobStatus.Completed;
                _tracing?.Trace($"Run: no rows. Completing as {doneStatus}.");
                UpdateJob(importJobId, doneStatus, offset, context.InitialProcessedRows,
                    context.InitialSuccessCount, context.InitialFailedCount, context.InitialSkippedCount, complete: true);
                return new ProcessChunkResult { HasMore = false, ProcessedSoFar = context.InitialProcessedRows };
            }

            var results = _engine.ProcessBatch(context, rows.ToList(), _writer);

            var successN = results.Count(r => r.IsSuccess);
            var failedN = results.Count(r => r.Status == ImportLogStatus.Failed);
            var skippedN = results.Count(r => r.Status == ImportLogStatus.Skipped);
            _tracing?.Trace($"Run: batch results — success={successN}, failed={failedN}, skipped={skippedN}.");

            LogFailures(importJobId, results);

            var success = context.InitialSuccessCount + successN;
            var failed = context.InitialFailedCount + failedN;
            var skipped = context.InitialSkippedCount + skippedN;
            var processed = context.InitialProcessedRows + rows.Count;

            // The offset stored on the job is the number of DATA rows already consumed — it equals
            // the next ReadRange skip value. It advances by exactly the number of rows we read this
            // invocation, NOT by chunkSize, to avoid skipping rows on a partial last chunk.
            var newOffset = offset + rows.Count;

            // If we read a full chunk there are likely more rows; a partial read means the file end
            // was reached. This avoids a redundant "empty chunk" invocation in almost all cases.
            var hasMore = rows.Count >= chunkSize;

            var status = hasMore
                ? ImportJobStatus.Processing
                : (failed > 0 ? ImportJobStatus.CompletedWithErrors : ImportJobStatus.Completed);

            UpdateJob(importJobId, status, newOffset, processed, success, failed, skipped, complete: !hasMore);
            return new ProcessChunkResult { HasMore = hasMore, ProcessedSoFar = processed };
        }

        /// <summary>
        /// Determines the effective chunk size for this invocation. Priority:
        /// <list type="number">
        ///   <item><description>Explicit override from the Custom API caller (Power Automate action).</description></item>
        ///   <item><description>Value stored on the Import Template (<c>ChunkSize</c> field).</description></item>
        ///   <item><description><see cref="DefaultChunkSize"/> when both are absent or zero.</description></item>
        /// </list>
        /// The result is always clamped to [<see cref="MinChunkSize"/>, <see cref="MaxChunkSize"/>].
        /// </summary>
        private int ResolveChunkSize(ImportContext context, int chunkSizeOverride)
        {
            var chunk = chunkSizeOverride > 0 ? chunkSizeOverride : context.Template.ChunkSize;
            if (chunk <= 0) chunk = DefaultChunkSize;
            return Math.Max(MinChunkSize, Math.Min(MaxChunkSize, chunk));
        }

        /// <summary>
        /// Persists a log record for every failed row using an ExecuteMultiple upsert keyed by
        /// (<see cref="LogJobLookupAttribute"/>, RowNumber, TargetTable) — la alternate key
        /// <c>sanic_smlt_ijl_key_logidempotente</c> del Import Job Log (recreada con estas columnas
        /// por KeyProvisioner en 1.2.1.0). Upsert ensures idempotency if the same chunk is retried.
        /// Successes are intentionally NOT logged here; only the aggregated counters on the job
        /// record track them.
        /// </summary>
        private void LogFailures(Guid jobId, IList<ProcessingResult> results)
        {
            var failures = results.Where(r => r.Status == ImportLogStatus.Failed).ToList();
            if (failures.Count == 0) return;

            // Always trace each failure so it's visible even if the log write fails.
            foreach (var failure in failures)
                _tracing?.Trace($"  FAILED row {failure.RowNumber} [{failure.Category}] " +
                    $"{failure.TargetTable}: {failure.ErrorMessage}");

            var jobRef = new EntityReference(ImportJobTable, jobId);
            var requests = new OrganizationRequestCollection();
            foreach (var failure in failures)
            {
                var log = new Entity(ImportJobLogTable);
                log.KeyAttributes[LogJobLookupAttribute] = jobRef;
                log.KeyAttributes["sanic_smlt_ijl_rownumber"] = failure.RowNumber;
                log.KeyAttributes["sanic_smlt_ijl_targettable"] = failure.TargetTable ?? string.Empty;
                // El lookup también va como atributo: es required, y así el create no depende de que
                // la plataforma copie los KeyAttributes al registro.
                log[LogJobLookupAttribute] = jobRef;
                log["sanic_smlt_ijl_status"] = new OptionSetValue((int)ImportLogStatus.Failed);
                log["sanic_smlt_ijl_errormessage"] = failure.ErrorMessage;
                if (failure.Category.HasValue)
                    log["sanic_smlt_ijl_errorcategory"] = new OptionSetValue((int)failure.Category.Value);

                requests.Add(new UpsertRequest { Target = log });
            }

            try
            {
                var response = (ExecuteMultipleResponse)_service.Execute(new ExecuteMultipleRequest
                {
                    Requests = requests,
                    Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true }
                });

                if (response.IsFaulted)
                    foreach (var item in response.Responses.Where(r => r.Fault != null))
                        _tracing?.Trace($"  LOG WRITE FAULT (idx {item.RequestIndex}): {item.Fault.Message} " +
                            "→ revisá la alternate key del Import Job Log y la columna errorcategory.");
            }
            catch (Exception ex)
            {
                _tracing?.Trace($"  LogFailures could NOT write to Import Job Log: {ex.Message}");
            }
        }

        /// <summary>
        /// Persists the new job state after processing a chunk: advances the offset (used as the
        /// skip value on the next invocation), updates all counters, and optionally stamps
        /// <c>EndTime</c> when the run is complete.
        /// </summary>
        private void UpdateJob(Guid jobId, ImportJobStatus status, int offset,
            int processed, int success, int failed, int skipped, bool complete)
        {
            var update = new Entity(ImportJobTable, jobId)
            {
                ["sanic_smlt_ij_status"] = new OptionSetValue((int)status),
                ["sanic_smlt_ij_processingbatch"] = offset,
                ["sanic_smlt_ij_processedrows"] = processed,
                ["sanic_smlt_ij_successcount"] = success,
                ["sanic_smlt_ij_failedcount"] = failed,
                ["sanic_smlt_ij_skippedcount"] = skipped
            };
            if (complete)
                update["sanic_smlt_ij_endtime"] = DateTime.UtcNow;

            _service.Update(update);
        }
    }
}
