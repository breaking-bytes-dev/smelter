using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>Reglas puras de qué valores del JSON importado se escriben (separadas del I/O de TemplateImporter).</summary>
    public static class TemplateImportPolicy
    {
        /// <summary>
        /// Status a escribir en el template importado; <c>null</c> = no tocar la columna.
        /// Create: el del JSON si viene; si no (export 1.0), Active (el gate de PrepareImport exige Active).
        /// Update: el del JSON si viene; si no, se respeta el que tiene el template en este ambiente.
        /// </summary>
        public static int? ResolveStatus(int? jsonStatus, bool isCreate)
        {
            if (jsonStatus.HasValue) return jsonStatus;
            return isCreate ? (int?)ImportTemplateStatus.Active : null;
        }
    }
}
