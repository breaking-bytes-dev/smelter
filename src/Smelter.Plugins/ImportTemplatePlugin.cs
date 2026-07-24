using System;
using Microsoft.Xrm.Sdk;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;

namespace Smelter.Plugins
{
    /// <summary>
    /// Backing plugin for the Custom API <c>sanic_ImportTemplate</c>.
    /// Validates the JSON and recreates the Import Template tree in this environment (upsert by name,
    /// GUID remapping). In: Json (string). Out: TemplateId (Guid), Warnings (string).
    /// </summary>
    public class ImportTemplatePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null); // SYSTEM

            tracing.Trace("=== Smelter ImportTemplate (chunked-v2) ===");
            var json = (string)context.InputParameters["Json"];

            try
            {
                var handler = new ImportTemplateHandler(new TemplateSerializer(), new TemplateImporter(service));
                var result = handler.Run(json);

                context.OutputParameters["TemplateId"] = result.TemplateId;
                context.OutputParameters["Warnings"] =
                    (result.Warnings != null && result.Warnings.Count > 0)
                        ? string.Join("\n", result.Warnings)
                        : string.Empty;

                tracing.Trace($"ImportTemplate OK. TemplateId={result.TemplateId}, warnings={result.Warnings?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                tracing.Trace($"ImportTemplate FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>
    /// Testable core of ImportTemplate: deserializes and validates the JSON, then recreates
    /// the full mapping tree in the current Dataverse environment. Extracted from
    /// <see cref="ImportTemplatePlugin"/> so it can be unit-tested without an
    /// <see cref="IServiceProvider"/> context.
    /// </summary>
    public class ImportTemplateHandler
    {
        private readonly ITemplateSerializer _serializer;
        private readonly ITemplateImporter _importer;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportTemplateHandler"/>.
        /// </summary>
        /// <param name="serializer">Deserializes and structurally validates the JSON payload.</param>
        /// <param name="importer">Recreates the template tree in Dataverse with GUID remapping.</param>
        public ImportTemplateHandler(ITemplateSerializer serializer, ITemplateImporter importer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        }

        /// <summary>
        /// Deserializes the JSON exported by <see cref="ExportTemplatePlugin"/>, validates it,
        /// and recreates (or replaces) the template tree in this environment.
        /// </summary>
        /// <param name="json">Portable template JSON produced by <c>sanic_ExportTemplate</c>.</param>
        /// <returns>
        /// An <see cref="ImportResult"/> containing the GUID of the upserted template and any
        /// non-fatal warnings (e.g., target tables that don't exist in this environment).
        /// </returns>
        /// <exception cref="InvalidPluginExecutionException">
        /// Thrown when the JSON is empty, malformed, unsupported version, or references unknown table mapping refs.
        /// </exception>
        public ImportResult Run(string json)
        {
            var dto = _serializer.Deserialize(json); // validates; throws on invalid
            return _importer.Import(dto);
        }
    }
}
