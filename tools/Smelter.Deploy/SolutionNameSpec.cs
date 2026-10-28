// Especificación DECLARATIVA del nombre visible (friendly name) de la solución smlt_core_smelter. La consume
// SolutionNameProvisioner (deploy, paso 4l) y SolutionNameAssertions (verify).
//
// Convención de nombres del estudio (proyecto hermano "CM | Core | Cobranza Movil"): "<PREFIJO> | <capa> | <producto>".
// Solo cambia solution.friendlyname; el unique name (smlt_core_smelter) NO se toca, así que los pipelines y los
// exports/imports siguen resolviendo la misma solución. El friendly name de una solución NO es localizable (no hay
// SetLocLabels para solution): el mismo texto sirve en 1033 y 3082.

namespace Smelter.Deploy;

internal static class SolutionNameCatalogo
{
    internal const string UniqueName = Catalogo.SolutionName;

    internal const string FriendlyName = "SMLT | Core | Smelter";

    /// <summary>¿Hay que escribir el friendly name? (distinto del actual, o actual vacío).</summary>
    internal static bool NeedsUpdate(string? current) =>
        !string.Equals(current, FriendlyName, StringComparison.Ordinal);
}
