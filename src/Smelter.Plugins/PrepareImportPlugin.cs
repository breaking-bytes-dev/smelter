using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;

namespace Smelter.Plugins
{
    /// <summary>
    /// Backing plugin for the Custom API <c>sanic_PrepareImport</c>.
    /// Lightweight: counts data rows, validates headers, and resets the job state. NO staging.
    /// In: JobId (Guid). Out: TotalRows (int).
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

                var total = handler.Run(jobId, context.InitiatingUserId);
                tracing.Trace($"PrepareImport: COMPLETED. TotalRows = {total}");
                context.OutputParameters["TotalRows"] = total;
            }
            catch (Exception ex)
            {
                tracing.Trace($"PrepareImport FAILED: {ex.GetType().Name}: {ex.Message}\nSTACK:\n{ex.StackTrace}");
                throw;
            }
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
        public int Run(Guid importJobId, Guid initiatingUserId)
        {
            _tracing?.Trace($"Run: loading context for job {importJobId}...");
            var context = _loader.LoadContext(importJobId);
            var template = context.Template;
            _tracing?.Trace($"Run: context loaded. TemplateId={template?.Id}, " +
                $"hasHeader={template?.HasHeaderRow}, delimiter='{template?.Delimiter}', " +
                $"tableMappings={context.TableMappings.Count}");

            ValidateTemplateAccess(initiatingUserId, template);
            _tracing?.Trace("Run: template access validated OK.");

            var bytes = _downloader.Download(new EntityReference(ImportJobTable, importJobId), SourceFileAttribute);
            _tracing?.Trace($"Run: downloaded file, {bytes?.Length ?? 0} bytes.");

            ValidateHeaders(context, bytes, template);
            _tracing?.Trace("Run: headers validated OK.");

            var totalRows = _csv.CountRows(bytes, template.HasHeaderRow, template.Delimiter);
            _tracing?.Trace($"Run: counted {totalRows} data rows. Resetting job state...");

            ResetJob(importJobId, totalRows);
            return totalRows;
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
                .Select(c => c.SourceColumn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(sc => !headers.Contains(sc))
                .ToList();

            if (missing.Count > 0)
                throw new InvalidPluginExecutionException(
                    $"CSV is missing mapped source column(s): {string.Join(", ", missing)}.");
        }

        /// <summary>
        /// Writes the initial job state back to Dataverse: sets status to
        /// <see cref="ImportJobStatus.Processing"/>, stores the row count, and zeros all counters
        /// so the Cloud Flow can loop <see cref="ProcessChunkPlugin"/> from offset 0.
        /// </summary>
        private void ResetJob(Guid jobId, int totalRows)
        {
            _service.Update(new Entity(ImportJobTable, jobId)
            {
                ["sanic_smlt_ij_status"] = new OptionSetValue((int)ImportJobStatus.Processing),
                ["sanic_smlt_ij_totalrows"] = totalRows,
                ["sanic_smlt_ij_processingbatch"] = 0,
                ["sanic_smlt_ij_processedrows"] = 0,
                ["sanic_smlt_ij_successcount"] = 0,
                ["sanic_smlt_ij_failedcount"] = 0,
                ["sanic_smlt_ij_skippedcount"] = 0,
                ["sanic_smlt_ij_starttime"] = DateTime.UtcNow
            });
        }
    }
}
