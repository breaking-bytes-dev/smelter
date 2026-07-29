using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Plugins
{
    /// <summary>
    /// Step plugin (sync, Pre-operation, message Create de <c>sanic_tbl_smlt_importjob</c> — el
    /// único message step del assembly; el registro vive en StepSpec/StepProvisioner de
    /// Smelter.Deploy). Si el Import Template referenciado por el job nuevo es owned por un team,
    /// el job nace owned por ese MISMO team: con Read/Write Basic sobre Import Job (rol Operador),
    /// profundidad Basic cubre lo owned por el usuario Y por sus teams, así que todo el equipo ve
    /// y opera los jobs de cualquier miembro sin tocar la matriz de privilegios
    /// (smelter-access-control, Requirement "Ownership del Import Job heredado del team del template").
    /// </summary>
    public class AssignJobOwnerPlugin : IPlugin
    {
        // Bump this marker every deploy. If it doesn't show in the Plugin Trace Log, the assembly didn't update.
        private const string BuildMarker = "Smelter AssignJobOwner — BUILD team-ownership-v1";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            // SYSTEM: leer el ownerid del template no debe depender de los privilegios del creador
            // del job. El derecho a USAR el template lo valida PrepareImportHandler con el
            // InitiatingUserId real (ValidateTemplateAccess) — acá solo se resuelve el owner.
            var service = factory.CreateOrganizationService(null); // SYSTEM

            tracing.Trace("=== " + BuildMarker + " ===");

            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
            {
                tracing.Trace("AssignJobOwner: no Entity Target in InputParameters — nothing to do.");
                return;
            }

            try
            {
                new AssignJobOwnerHandler(service, tracing).Run(target);
            }
            catch (Exception ex)
            {
                tracing.Trace($"AssignJobOwner FAILED: {ex.GetType().Name}: {ex.Message}\nSTACK:\n{ex.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>
    /// Testable core of AssignJobOwner: resuelve el owner del template del Target y, si es un team,
    /// lo escribe como <c>ownerid</c> del job ANTES de que la plataforma persista el Create
    /// (stage Pre-operation ⇒ mutar el Target basta, sin AssignRequest ni Update extra).
    /// Extracted from <see cref="AssignJobOwnerPlugin"/> so it can be unit-tested without an
    /// <see cref="IServiceProvider"/> context.
    /// </summary>
    public class AssignJobOwnerHandler
    {
        /// <summary>Logical name of the Import Job table.</summary>
        public const string ImportJobTable = "sanic_tbl_smlt_importjob";

        /// <summary>Logical name of the Import Template table.</summary>
        public const string ImportTemplateTable = "sanic_tbl_smlt_importtemplate";

        /// <summary>Lookup del job hacia su Import Template.</summary>
        public const string TemplateLookupAttribute = "sanic_smlt_ij_importtemplateid";

        /// <summary>Columna de ownership (user/team-owned) del Import Job y del Template.</summary>
        public const string OwnerAttribute = "ownerid";

        private const string TeamEntity = "team";

        private readonly IOrganizationService _service;
        private readonly ITracingService _tracing;

        /// <summary>
        /// Initializes a new instance of <see cref="AssignJobOwnerHandler"/>.
        /// </summary>
        /// <param name="service">Organization service used to read the template's owner (SYSTEM context).</param>
        /// <param name="tracing">Optional tracing service; pass <c>null</c> in unit tests.</param>
        public AssignJobOwnerHandler(IOrganizationService service, ITracingService tracing = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _tracing = tracing;
        }

        /// <summary>
        /// Aplica la regla de ownership sobre el Target del Create:
        /// template owned por team ⇒ <c>Target["ownerid"]</c> = ese team (gana incluso a un owner
        /// explícito en el payload — la regla es determinista para que la visibilidad del equipo no
        /// dependa de cómo se creó el job). Template owned por user, o job sin template ⇒ no toca
        /// nada y la plataforma aplica su default (el creador).
        /// </summary>
        /// <param name="target">Target entity del message Create (mutado in-place).</param>
        public void Run(Entity target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var templateRef = target.GetAttributeValue<EntityReference>(TemplateLookupAttribute);
            if (templateRef == null)
            {
                _tracing?.Trace("Run: Target has no template reference — owner untouched.");
                return;
            }

            var template = _service.Retrieve(ImportTemplateTable, templateRef.Id, new ColumnSet(OwnerAttribute));
            var templateOwner = template.GetAttributeValue<EntityReference>(OwnerAttribute);

            if (templateOwner == null || templateOwner.LogicalName != TeamEntity)
            {
                _tracing?.Trace($"Run: template {templateRef.Id} owner is '{templateOwner?.LogicalName ?? "null"}' — owner untouched.");
                return;
            }

            target[OwnerAttribute] = templateOwner;
            _tracing?.Trace($"Run: job owner set to team {templateOwner.Id} (owner of template {templateRef.Id}).");
        }
    }
}
