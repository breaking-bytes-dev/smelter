// Texto bilingüe de la UI de Smelter: inglés bajo LCID 1033 (idioma BASE de la org, el valor que leen las demás canvas
// apps y el resto de la provisión) y español bajo LCID 3082 (es-ES, idioma provisionado). Cada usuario elige su idioma de
// interfaz en sus opciones personales: ve el 3082 si lo tiene y, si no, cae al 1033.
//
// Las specs (SiteMapSpec, FormPolishSpec, ColumnDescriptionsSpec, ViewLabelsSpec ...) declaran sus textos como Bilingual y
// los provisioners escriben AMBOS idiomas. Los mensajes que guardan los plugins/flow en registros (motivos de falla,
// errores de validación, logs) NO pasan por acá: siguen en inglés.

using Microsoft.Xrm.Sdk;

namespace Smelter.Deploy;

internal static class Lcids
{
    internal const int English = 1033;
    internal const int Spanish = 3082;
}

internal sealed record Bilingual(string En, string Es)
{
    internal string For(int lcid) => lcid switch
    {
        Lcids.English => En,
        Lcids.Spanish => Es,
        _ => throw new ArgumentOutOfRangeException(nameof(lcid), lcid, "solo 1033 (en) y 3082 (es)"),
    };

    /// <summary>Label de metadata con ambos idiomas (para UpdateAttribute/UpdateEntity con MergeLabels = true).</summary>
    internal Label ToLabel()
    {
        var label = new Label(En, Lcids.English);
        label.LocalizedLabels.Add(new LocalizedLabel(Es, Lcids.Spanish));
        return label;
    }

    /// <summary>Los dos LocalizedLabel, en el orden que espera SetLocLabelsRequest.Labels.</summary>
    internal LocalizedLabel[] ToLocalizedLabels() =>
        [new LocalizedLabel(En, Lcids.English), new LocalizedLabel(Es, Lcids.Spanish)];
}

/// <summary>Helpers PUROS sobre los Label de metadata (testeables sin Dataverse).</summary>
internal static class LabelEditor
{
    internal static string? TextFor(Label? label, int lcid) =>
        label?.LocalizedLabels.FirstOrDefault(l => l.LanguageCode == lcid)?.Label;

    /// <summary>
    /// Deja el <paramref name="lcid"/> del label con <paramref name="text"/> SIN tocar los demás idiomas (el 1033 existente se
    /// conserva tal cual). Devuelve <c>false</c> si ya valía ese texto. Si el label es null devuelve el nuevo en
    /// <paramref name="result"/>.
    /// </summary>
    internal static bool Set(Label? label, int lcid, string text, out Label result)
    {
        result = label ?? new Label();
        var existing = result.LocalizedLabels.FirstOrDefault(l => l.LanguageCode == lcid);
        if (existing is null)
        {
            result.LocalizedLabels.Add(new LocalizedLabel(text, lcid));
            return true;
        }
        if (string.Equals(existing.Label, text, StringComparison.Ordinal))
            return false;
        existing.Label = text;
        return true;
    }
}
