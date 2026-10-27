using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;

namespace Smelter.Plugins
{
    /// <summary>
    /// Step plugin (sync, Pre-operation) sobre <c>sanic_tbl_smlt_importjob</c>: Update filtrado por
    /// <c>sanic_smlt_ij_status</c> y Create. Cierra el hueco de seguridad de "Ejecutar/Reintentar solo
    /// encolan": el Cloud Flow corre con la identidad de su conexión, así que el chequeo por usuario de
    /// PrepareImport (InitiatingUserId) valida al flow, no a la persona. Acá se valida a la PERSONA
    /// (<c>InitiatingUserId</c>) en el momento de poner el job en Queued, sin importar por qué camino
    /// llegue (botón, API, canvas app). Registro: StepSpec/StepProvisioner de Smelter.Deploy.
    /// </summary>
    public class ValidateQueueAccessPlugin : IPlugin
    {
        // Bump this marker every deploy. If it doesn't show in the Plugin Trace Log, the assembly didn't update.
        private const string BuildMarker = "Smelter ValidateQueueAccess — BUILD queue-gate-v1";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            // SYSTEM: leer el job y consultar RetrievePrincipalAccess no debe depender de los
            // privilegios del usuario evaluado. El sujeto evaluado es context.InitiatingUserId.
            var service = factory.CreateOrganizationService(null);

            tracing.Trace("=== " + BuildMarker + " ===");

            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
            {
                tracing.Trace("ValidateQueueAccess: no Entity Target in InputParameters — nothing to do.");
                return;
            }

            try
            {
                new ValidateQueueAccessHandler(service, new PrincipalAccessChecker(service), tracing)
                    .Run(context.MessageName, target, context.InitiatingUserId);
            }
            catch (InvalidPluginExecutionException)
            {
                throw; // mensaje de usuario, sin PII; se propaga tal cual
            }
            catch (Exception ex)
            {
                tracing.Trace($"ValidateQueueAccess FAILED: {ex.GetType().Name}: {ex.Message}\nSTACK:\n{ex.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>
    /// Testable core de ValidateQueueAccess: si el Target deja el job en Queued, el usuario iniciador
    /// debe poder leer el Import Template del job (misma semántica que PrepareImport:
    /// <see cref="IAccessChecker"/> → RetrievePrincipalAccess, que incluye acceso vía teams).
    /// </summary>
    public class ValidateQueueAccessHandler
    {
        /// <summary>Columna de estado del Import Job.</summary>
        public const string StatusAttribute = "sanic_smlt_ij_status";

        private readonly IOrganizationService _service;
        private readonly IAccessChecker _accessChecker;
        private readonly ITracingService _tracing;

        /// <summary>Initializes a new instance of <see cref="ValidateQueueAccessHandler"/>.</summary>
        /// <param name="service">Servicio SYSTEM, usado solo para leer el template del job en Update.</param>
        /// <param name="accessChecker">Chequeo de Read sobre el template para un principal.</param>
        /// <param name="tracing">Optional tracing service; pass <c>null</c> in unit tests.</param>
        public ValidateQueueAccessHandler(IOrganizationService service, IAccessChecker accessChecker,
            ITracingService tracing = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _accessChecker = accessChecker ?? throw new ArgumentNullException(nameof(accessChecker));
            _tracing = tracing;
        }

        /// <param name="messageName">"Create" o "Update".</param>
        /// <param name="target">Target del message.</param>
        /// <param name="initiatingUserId">Persona real que originó la operación (no el SYSTEM del plugin ni la conexión del flow).</param>
        /// <exception cref="InvalidPluginExecutionException">El usuario no puede leer el template.</exception>
        public void Run(string messageName, Entity target, Guid initiatingUserId)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var status = target.GetAttributeValue<OptionSetValue>(StatusAttribute);
            if (status == null || status.Value != (int)ImportJobStatus.Queued)
            {
                _tracing?.Trace("Run: Target does not set status Queued — nothing to validate.");
                return;
            }

            var templateRef = target.GetAttributeValue<EntityReference>(AssignJobOwnerHandler.TemplateLookupAttribute);
            if (templateRef == null && string.Equals(messageName, "Update", StringComparison.OrdinalIgnoreCase)
                && target.Id != Guid.Empty)
            {
                // El Target de un Update solo trae lo que cambió: se lee el lookup del job persistido.
                // (Alternativa descartada: PreImage — un step image más para registrar y mantener.)
                var job = _service.Retrieve(AssignJobOwnerHandler.ImportJobTable, target.Id,
                    new ColumnSet(AssignJobOwnerHandler.TemplateLookupAttribute));
                templateRef = job.GetAttributeValue<EntityReference>(AssignJobOwnerHandler.TemplateLookupAttribute);
            }

            if (templateRef == null)
            {
                _tracing?.Trace("Run: job has no template — nothing to validate (PrepareImport rejects it later).");
                return;
            }

            if (_accessChecker.HasReadAccess(initiatingUserId, templateRef))
            {
                _tracing?.Trace("Run: initiating user can read the template — queue allowed.");
                return;
            }

            _tracing?.Trace($"Run: initiating user {initiatingUserId} cannot read template {templateRef.Id} — queue blocked.");
            throw new InvalidPluginExecutionException(
                "No tenés acceso al Import Template de este job: no se puede poner en cola.");
        }
    }
}
