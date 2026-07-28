// Lógica PURA (sin Dataverse) de renombre de la referencia a processchunk dentro del `clientdata`
// de un Cloud Flow — extraída de CloudFlowUpdater.cs para poder testearla sin mockear ServiceClient
// (mismo principio del batch: separar lógica de I/O). Fix de un bug CRÍTICO encontrado en review:
//
//   "sanic_capi_smelter_processchunk" CONTIENE "anic_capi_smelter_processchunk" como substring
//   (a partir del índice 1: "s" + "anic_..."). Un string.Replace ingenuo del uniquename viejo, corrido
//   una SEGUNDA vez sobre clientdata YA migrado (CloudFlowUpdater corre en TODO backend-deploy, no
//   una sola vez), encuentra ese substring adentro del nombre correcto y le antepone otra "s":
//   "ssanic_capi_smelter_processchunk" — corrompe el Cloud Flow en el segundo deploy en adelante.
//
// Fix: reemplazo por TOKEN exacto (regex con lookbehind/lookahead negativos de carácter de
// identificador), no por substring — "anic_..." dentro de "sanic_..." nunca matchea porque está
// precedido por 's' (identificador). Efecto secundario deseado: correr sobre clientdata ya correcto
// es un no-op real (Changed=false), así que CloudFlowUpdater puede saltear el Update/SetState por
// completo en los deploys de "nada que hacer" — menos escritura innecesaria en Dataverse.

using System.Text.RegularExpressions;

namespace Smelter.Deploy;

internal static class ClientDataProcessChunkRenamer
{
    internal const string OldActionUniqueName = "anic_capi_smelter_processchunk";
    internal const string NewActionUniqueName = "sanic_capi_smelter_processchunk";

    private static readonly Regex OldTokenPattern = BuildTokenPattern(OldActionUniqueName);
    private static readonly Regex NewTokenPattern = BuildTokenPattern(NewActionUniqueName);

    // Patrón de corrupción específico que el bug original podía producir: un "s" pegado adelante de
    // un uniquename YA correcto ("ssanic_..."). Se repara colapsando esa "s" extra de vuelta.
    private static readonly Regex DoubleSCorruptionPattern = new(
        $@"(?<![A-Za-z0-9_])s({Regex.Escape(NewActionUniqueName)})(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    private static Regex BuildTokenPattern(string token) =>
        new($@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])", RegexOptions.Compiled);

    /// <summary>
    /// Repara <c>"ssanic_capi_smelter_processchunk"</c> → <c>"sanic_capi_smelter_processchunk"</c> (si
    /// existe) y renombra toda ocurrencia del uniquename viejo, como TOKEN exacto, al nuevo.
    /// Idempotente: correrlo sobre <paramref name="clientData"/> ya migrado devuelve el mismo texto
    /// sin tocarlo (<c>Changed=false</c>) — es justamente el caso que el bug original rompía.
    /// </summary>
    /// <param name="clientData">JSON del Cloud Flow, o <c>null</c>/vacío (no-op).</param>
    /// <returns>El texto resultante y si hubo algún cambio.</returns>
    internal static (string ClientData, bool Changed) Rename(string? clientData)
    {
        var text = clientData ?? "";

        var repaired = DoubleSCorruptionPattern.Replace(text, "$1");
        var updated = OldTokenPattern.Replace(repaired, NewActionUniqueName);

        return (updated, updated != text);
    }

    /// <summary>true si <paramref name="clientData"/> referencia el uniquename VIEJO como token exacto
    /// (nunca como substring de "sanic_...").</summary>
    internal static bool ReferencesOldToken(string? clientData) => OldTokenPattern.IsMatch(clientData ?? "");

    /// <summary>true si <paramref name="clientData"/> referencia el uniquename NUEVO como token exacto.</summary>
    internal static bool ReferencesNewToken(string? clientData) => NewTokenPattern.IsMatch(clientData ?? "");

    /// <summary>true si <paramref name="clientData"/> tiene el patrón de corrupción específico
    /// ("ssanic_capi_smelter_processchunk") que el bug original podía producir.</summary>
    internal static bool HasDoubleSCorruption(string? clientData) => DoubleSCorruptionPattern.IsMatch(clientData ?? "");
}
