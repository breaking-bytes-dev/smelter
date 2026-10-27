using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;

namespace Smelter.Plugins
{
    /// <summary>
    /// Backing plugin for the Custom API <c>sanic_PrepareImport</c>.
    /// Lightweight: counts data rows, validates headers, and atomically CLAIMS the job (Queued → Processing, resetting its
    /// state). NO staging.
    /// In: JobId (Guid). Out: TotalRows (int), Claimed (bool — false: another flow run owns the job, do NOT process).
    /// </summary>
    public class PrepareImportPlugin : IPlugin
    {
        // Bump this marker every deploy. If it doesn't show in the Plugin Trace Log, the assembly didn't update.
        private const string BuildMarker = "Smelter PrepareImport — BUILD chunked-v2 (no-probe, with-tracing)";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null); // SYSTEM

            tracing.Trace("=== " + BuildMarker + " ===");

            if (!context.InputParameters.Contains("JobId"))
            {
                tracing.Trace("PrepareImport: InputParameters does NOT contain 'JobId'. Keys: "
                    + string.Join(", ", context.InputParameters.Keys));
                throw new InvalidPluginExecutionException("PrepareImport requires a 'JobId' input parameter.");
            }

            var jobId = (Guid)context.InputParameters["JobId"];
            tracing.Trace($"PrepareImport: JobId = {jobId}");

            try
            {
                var handler = new PrepareImportHandler(
                    new ImportContextLoader(service),
                    new FileDownloader(service),
                    new CsvParserService(),
                    service,
                    new PrincipalAccessChecker(service),
                    tracing);

                var result = handler.Prepare(jobId, context.InitiatingUserId);
                tracing.Trace($"PrepareImport: COMPLETED. Claimed = {result.Claimed}, TotalRows = {result.TotalRows}");
                context.OutputParameters["TotalRows"] = result.TotalRows;
                context.OutputParameters["Claimed"] = result.Claimed;
            }
            catch (Exception ex)
            {
                tracing.Trace($"PrepareImport FAILED: {ex.GetType().Name}: {ex.Message}\nSTACK:\n{ex.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>Resultado de <see cref="PrepareImportHandler.Prepare"/>.</summary>
    public sealed class PrepareResult
    {
        /// <summary>true: esta llamada reclamó el job (lo dejó en Processing) y el caller debe procesarlo.</summary>
        public bool Claimed { get; }

        /// <summary>Filas de datos del archivo (0 si no se reclamó).</summary>
        public int TotalRows { get; }

        public PrepareResult(bool claimed, int totalRows)
        {
            Claimed = claimed;
            TotalRows = totalRows;
        }
    }

    /// <summary>
    /// Testable core of PrepareImport: load config → download CSV → validate headers → count rows → reset job.
    /// Extracted from <see cref="PrepareImportPlugin"/> so it can be unit-tested without an
    /// <see cref="IServiceProvider"/> context.
    /// </summary>
    public class PrepareImportHandler
    {
        /// <summary>Logical name of the Import Job table.</summary>
        public const string ImportJobTable = "sanic_tbl_smlt_importjob";

        /// <summary>Logical name of the file column on the Import Job that stores the source CSV.</summary>
        public const string SourceFileAttribute = "sanic_smlt_ij_sourcefile";

        /// <summary>
        /// Import Job column (Decimal, precision 0) with the BYTE offset of the next unread record. Prepare points it
        /// to the first data byte (after BOM + header); ProcessChunk advances it so each chunk downloads only its range.
        /// </summary>
        public const string ByteCursorAttribute = "sanic_smlt_ij_bytecursor";

        /// <summary>Dataverse ErrorCode 0x80060882 ConcurrencyVersionMismatch (UpdateRequest con IfRowVersionMatches).</summary>
        public const int ConcurrencyVersionMismatch = unchecked((int)0x80060882);

        private const string StatusAttribute = "sanic_smlt_ij_status";

        private readonly IImportContextLoader _loader;
        private readonly IFileDownloader _downloader;
        private readonly ICsvReader _csv;
        private readonly IOrganizationService _service;
        private readonly IAccessChecker _accessChecker;
        private readonly ITracingService _tracing;

        /// <summary>
        /// Initializes a new instance of <see cref="PrepareImportHandler"/>.
        /// </summary>
        /// <param name="loader">Loads the Import Template configuration from Dataverse.</param>
        /// <param name="downloader">Downloads the source file from a Dataverse file column.</param>
        /// <param name="csv">Reads and counts rows from the CSV bytes without staging.</param>
        /// <param name="service">Organization service used to reset the job record (SYSTEM context).</param>
        /// <param name="accessChecker">
        /// Validates that the Custom API caller has Read access to the Import Template referenced by
        /// the job (smelter-access-control, Requirement "Validación de acceso al template en
        /// PrepareImportPlugin"). The engine itself keeps running as SYSTEM — only this check is
        /// scoped to the initiating user.
        /// </param>
        /// <param name="tracing">Optional tracing service; pass <c>null</c> in unit tests.</param>
        public PrepareImportHandler(IImportContextLoader loader, IFileDownloader downloader,
            ICsvReader csv, IOrganizationService service, IAccessChecker accessChecker, ITracingService tracing = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _csv = csv ?? throw new ArgumentNullException(nameof(csv));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _accessChecker = accessChecker ?? throw new ArgumentNullException(nameof(accessChecker));
            _tracing = tracing;
        }

        /// <summary>
        /// Executes the prepare-import pipeline: loads context, downloads the CSV, validates
        /// headers against the column mappings, counts data rows, and resets the job to
        /// <see cref="ImportJobStatus.Processing"/> with all counters at zero.
        /// </summary>
        /// <param name="importJobId">GUID of the <c>sanic_tbl_smlt_importjob</c> record.</param>
        /// <param name="initiatingUserId">
        /// <c>InitiatingUserId</c> of the Custom API call — the real caller, even though the engine
        /// runs as SYSTEM. Must have Read access to the job's Import Template.
        /// </param>
        /// <returns>Total number of data rows in the source file (header row excluded).</returns>
        /// <exception cref="InvalidPluginExecutionException">
        /// Thrown when <paramref name="initiatingUserId"/> lacks Read access to the Import Template, or
        /// when one or more source columns referenced by the template are absent from the CSV header.
        /// </exception>
        public int Run(Guid importJobId, Guid initiatingUserId) => Prepare(importJobId, initiatingUserId).TotalRows;

        /// <summary>
        /// Igual que <see cref="Run"/> pero informa si se reclamó el job. CLAIM ATÓMICO (1.0.12): un job encolado puede
        /// disparar varias corridas del Cloud Flow orquestador (triggers de Dataverse at-least-once, re-disparos por
        /// updates de la fila); si cada una reseteara y procesara, los upserts competirían (DuplicateRecordEntityKey).
        /// Después de TODAS las validaciones (que siguen lanzando → el flow marca Failed) se lee el job:
        /// <list type="bullet">
        /// <item>status Queued, o null/vacío (llamadas directas a la API sobre jobs sin status, p.ej. Smelter.Verify) →
        /// reclamable; el reset (que pasa el job a Processing) va con <c>IfRowVersionMatches</c> y el RowVersion leído:
        /// si otra corrida lo tocó en el medio (0x80060882) → no reclamado, sin lanzar.</item>
        /// <item>cualquier otro status (Processing, Completed, ...) → no reclamado: no se resetea ni se procesa nada.</item>
        /// </list>
        /// </summary>
        /// <returns><see cref="PrepareResult"/> con <c>Claimed=false</c> y <c>TotalRows=0</c> si otra corrida es dueña del job.</returns>
        public PrepareResult Prepare(Guid importJobId, Guid initiatingUserId)
        {
            _tracing?.Trace($"Run: loading context for job {importJobId}...");
            var context = _loader.LoadContext(importJobId);
            var template = context.Template;
            _tracing?.Trace($"Run: context loaded. TemplateId={template?.Id}, " +
                $"hasHeader={template?.HasHeaderRow}, delimiter='{template?.Delimiter}', " +
                $"tableMappings={context.TableMappings.Count}");

            ValidateTemplateStatus(template);
            _tracing?.Trace("Run: template status is Active OK.");

            ValidateTemplateAccess(initiatingUserId, template);
            _tracing?.Trace("Run: template access validated OK.");

            ValidateDedupGate(context.TableMappings);
            _tracing?.Trace("Run: idempotency gate (dedup columns / insert-only) OK.");

            ValidateConfiguration(context);
            _tracing?.Trace("Run: template configuration is coherent OK.");

            // Claim: se lee el job ANTES del trabajo pesado (descarga + conteo) para no gastarlo si otra corrida ya lo tomó.
            var job = _service.Retrieve(ImportJobTable, importJobId, new ColumnSet(StatusAttribute));
            var currentStatus = job?.GetAttributeValue<OptionSetValue>(StatusAttribute)?.Value;
            if (currentStatus.HasValue && currentStatus.Value != (int)ImportJobStatus.Queued)
            {
                _tracing?.Trace($"Run: job NOT claimed (status={currentStatus.Value} is not Queued): another run owns it.");
                return new PrepareResult(false, 0);
            }
            var rowVersion = job?.RowVersion;

            var bytes = _downloader.Download(new EntityReference(ImportJobTable, importJobId), SourceFileAttribute);
            _tracing?.Trace($"Run: downloaded file, {bytes?.Length ?? 0} bytes.");

            ValidateHeaders(context, bytes, template);
            _tracing?.Trace("Run: headers validated OK.");

            var totalRows = _csv.CountRows(bytes, template.HasHeaderRow, template.Delimiter);
            _tracing?.Trace($"Run: counted {totalRows} data rows. Resetting job state...");

            // Cursor de bytes: null cuando el archivo no se puede recorrer por bytes (UTF-16/32, UTF-8 inválido) —
            // ProcessChunk cae entonces al camino legacy (descarga completa + skip de filas).
            var headerInfo = _csv.ReadHeaderInfo(bytes, template.HasHeaderRow, template.Delimiter);
            long? byteCursor = headerInfo != null && headerInfo.Supported ? (long?)headerInfo.DataStartOffset : null;
            _tracing?.Trace(byteCursor.HasValue
                ? $"Run: byte cursor starts at {byteCursor.Value}."
                : "Run: file not walkable by bytes; ProcessChunk will use the legacy full-download path.");

            if (!TryClaimAndReset(importJobId, rowVersion, totalRows, byteCursor))
            {
                _tracing?.Trace("Run: job NOT claimed (RowVersion mismatch): another run claimed it first.");
                return new PrepareResult(false, 0);
            }
            return new PrepareResult(true, totalRows);
        }

        /// <summary>
        /// Gate de estado (1.3.0.0): solo un template <see cref="ImportTemplateStatus.Active"/> es
        /// ejecutable. Antes, un template en Draft (a medio configurar) o Archived corría igual —
        /// el campo status existía pero el motor lo ignoraba. Público: ProcessChunk reusa el mismo gate.
        /// </summary>
        public static void ValidateTemplateStatus(ImportTemplateConfig template)
        {
            if (template.Status == (int)ImportTemplateStatus.Active) return;

            throw new InvalidPluginExecutionException(
                $"Import Template '{template.Name}' ({template.Id}) is not Active " +
                $"(status={(ImportTemplateStatus)template.Status}). Activate the template before running imports.");
        }

        /// <summary>
        /// Gate de idempotencia: todo Table Mapping necesita dedup columns (la alternate key que hace que reprocesar
        /// nunca duplique), salvo que tenga el opt-in explícito "Solo inserción (permite duplicados)". El mensaje
        /// nombra el mapping y la tabla destino — nunca valores de filas. Público: ProcessChunk reusa el mismo gate.
        /// </summary>
        public static void ValidateDedupGate(IEnumerable<TableMappingConfig> tableMappings)
        {
            foreach (var tm in tableMappings ?? Enumerable.Empty<TableMappingConfig>())
            {
                if (tm.InsertOnly || tm.GetDedupColumnNames().Any(c => !string.IsNullOrWhiteSpace(c))) continue;

                throw new InvalidPluginExecutionException(
                    $"Table Mapping '{tm.Name}' ({tm.TargetTable}) has no dedup columns: Smelter requires an alternate key " +
                    "so reprocessing never duplicates. Set dedup columns or enable 'Insert only (allows duplicates)'.");
            }
        }

        /// <summary>
        /// Coherencia entre registros del template (<see cref="TemplateValidator"/>): dedup columns que existen como
        /// Target Column, Lookup con Lookup Table, relaciones 1:N completas y con la misma cantidad de columnas de key
        /// que el padre. Antes cada una de estas configuraciones fallaba fila por fila (o en silencio) en ProcessChunk.
        /// Público: lo reusan los tests.
        /// </summary>
        public static void ValidateConfiguration(ImportContext context)
        {
            var errors = TemplateValidator.Validate(context);
            if (errors.Count == 0) return;

            throw new InvalidPluginExecutionException(
                $"Import Template '{context.Template?.Name}' has configuration errors: {string.Join(" ", errors)}");
        }

        /// <summary>
        /// Validates that <paramref name="initiatingUserId"/> has Read access to the Import Template
        /// before any file is downloaded or the job is reset. Prevents an Operador from preparing an
        /// import against a template that was never shared with their team (smelter-access-control).
        /// </summary>
        private void ValidateTemplateAccess(Guid initiatingUserId, ImportTemplateConfig template)
        {
            var templateRef = new EntityReference(ImportContextLoader.ImportTemplateTable, template.Id);
            if (_accessChecker.HasReadAccess(initiatingUserId, templateRef))
                return;

            throw new InvalidPluginExecutionException(
                $"User {initiatingUserId} does not have Read access to the Import Template ({template.Id}) " +
                "referenced by this Import Job. Prepare aborted.");
        }

        /// <summary>
        /// Verifies that every non-transform source column referenced by the template exists in the
        /// CSV header row. Transform-only columns are excluded because they derive their value from
        /// a DSL expression and don't read a raw source column.
        /// </summary>
        private void ValidateHeaders(ImportContext context, byte[] bytes, ImportTemplateConfig template)
        {
            if (!template.HasHeaderRow) return; // no header → mapping is by Column index, nothing to validate

            var headers = new HashSet<string>(
                _csv.ReadHeaders(bytes, true, template.Delimiter), StringComparer.OrdinalIgnoreCase);

            var missing = context.TableMappings
                .SelectMany(t => t.ColumnMappings)
                .Where(c => string.IsNullOrWhiteSpace(c.TransformExpression) && !string.IsNullOrEmpty(c.SourceColumn))
                .Select(c => c.SourceColumn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(sc => !headers.Contains(sc))
                .ToList();

            if (missing.Count > 0)
                throw new InvalidPluginExecutionException(
                    $"CSV is missing mapped source column(s): {string.Join(", ", missing)}.");
        }

        /// <summary>
        /// Claims the job and writes its initial state back to Dataverse: sets status to
        /// <see cref="ImportJobStatus.Processing"/>, stores the row count, and zeros all counters
        /// so the Cloud Flow can loop <see cref="ProcessChunkPlugin"/> from offset 0 (and from the first data byte).
        /// </summary>
        private bool TryClaimAndReset(Guid jobId, string rowVersion, int totalRows, long? byteCursor)
        {
            var target = new Entity(ImportJobTable, jobId)
            {
                // decimal (Decimal precision 0); null limpia un cursor de una corrida anterior.
                [ByteCursorAttribute] = byteCursor.HasValue ? (object)(decimal)byteCursor.Value : null,
                ["sanic_smlt_ij_status"] = new OptionSetValue((int)ImportJobStatus.Processing),
                ["sanic_smlt_ij_totalrows"] = totalRows,
                ["sanic_smlt_ij_processingbatch"] = 0,
                ["sanic_smlt_ij_processedrows"] = 0,
                ["sanic_smlt_ij_successcount"] = 0,
                ["sanic_smlt_ij_failedcount"] = 0,
                ["sanic_smlt_ij_skippedcount"] = 0,
                ["sanic_smlt_ij_starttime"] = DateTime.UtcNow,
                // Un Reintentar no debe arrastrar el fin de la corrida anterior.
                ["sanic_smlt_ij_endtime"] = null
            };
            target.RowVersion = rowVersion;

            try
            {
                _service.Execute(new UpdateRequest
                {
                    Target = target,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
                });
                return true;
            }
            catch (FaultException<OrganizationServiceFault> ex) when (ex.Detail != null && ex.Detail.ErrorCode == ConcurrencyVersionMismatch)
            {
                return false;
            }
        }
    }
}
