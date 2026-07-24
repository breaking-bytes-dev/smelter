using System;
using Microsoft.Xrm.Sdk;
using Smelter.Plugins.Services;

namespace Smelter.Plugins
{
    /// <summary>
    /// Backing plugin for the Custom API <c>sanic_ExportTemplate</c>.
    /// Serializes an Import Template (with its mapping tree) to portable JSON.
    /// In: TemplateId (Guid). Out: Json (string).
    /// </summary>
    public class ExportTemplatePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null); // SYSTEM

            tracing.Trace("=== Smelter ExportTemplate (chunked-v2) ===");
            var templateId = (Guid)context.InputParameters["TemplateId"];

            try
            {
                var handler = new ExportTemplateHandler(new TemplateExporter(service), new TemplateSerializer());
                context.OutputParameters["Json"] = handler.Run(templateId);
            }
            catch (Exception ex)
            {
                tracing.Trace($"ExportTemplate FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>
    /// Testable core of ExportTemplate: reads the full mapping tree from Dataverse and serializes
    /// it to a portable JSON string. Extracted from <see cref="ExportTemplatePlugin"/> so it can
    /// be unit-tested without an <see cref="IServiceProvider"/> context.
    /// </summary>
    public class ExportTemplateHandler
    {
        private readonly ITemplateExporter _exporter;
        private readonly ITemplateSerializer _serializer;

        /// <summary>
        /// Initializes a new instance of <see cref="ExportTemplateHandler"/>.
        /// </summary>
        /// <param name="exporter">Reads the template tree from Dataverse and builds the DTO.</param>
        /// <param name="serializer">Serializes the DTO to JSON using <c>DataContractJsonSerializer</c>.</param>
        public ExportTemplateHandler(ITemplateExporter exporter, ITemplateSerializer serializer)
        {
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// Exports the Import Template identified by <paramref name="templateId"/> as a JSON string
        /// that can be transported to another environment and re-imported via
        /// <see cref="ImportTemplatePlugin"/>.
        /// </summary>
        /// <param name="templateId">GUID of the <c>sanic_tbl_smlt_importtemplate</c> record.</param>
        /// <returns>Portable JSON representation of the template tree.</returns>
        public string Run(Guid templateId) => _serializer.Serialize(_exporter.Export(templateId));
    }
}
