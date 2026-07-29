namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Maps to the OptionSet values on <c>sanic_smlt_it_status</c>.
    /// Desde 1.3.0.0 el estado GATEA la ejecución: solo un template <see cref="Active"/> puede
    /// correr imports (PrepareImportHandler.ValidateTemplateStatus) — Draft y Archived se rechazan.
    /// </summary>
    public enum ImportTemplateStatus
    {
        /// <summary>Template en configuración — no ejecutable.</summary>
        Draft = 0,

        /// <summary>Template listo — único estado ejecutable.</summary>
        Active = 1,

        /// <summary>Template retirado — no ejecutable.</summary>
        Archived = 2
    }
}
