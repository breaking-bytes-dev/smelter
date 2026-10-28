// Especificación DECLARATIVA de los botones custom del command bar de Smelter y de sus RibbonDiffXml versionados en Assets/:
//   - Import Job (form):         Ejecutar / Reintentar        -> sanic_tbl_smlt_importjob.RibbonDiffXml.xml
//   - Import Template (grilla):  Export / Import template     -> sanic_tbl_smlt_importtemplate.RibbonDiffXml.xml
// SecurityProvisioner.UpsertRibbon ([6]) importa ambos en UNA mini-solución (SolutionZipBuilder); IconsAssertions los verifica.
//
// Por qué LocLabels bilingües + ModernImage: en un entorno cuyo idioma BASE es español (3082), un LocLabel que solo trae
// languagecode="1033" no tiene traducción para el idioma del usuario y la UCI muestra el texto literal "LabelText"; y un Button
// sin ModernImage/Image16by16 muestra el ícono genérico (puzzle). Cada LocLabel lleva EXACTAMENTE un título 1033 y uno 3082.

using System.Xml.Linq;

namespace Smelter.Deploy;

internal sealed record RibbonButtonSpec(string ButtonId, IconSpec Icon);

internal sealed record RibbonAssetSpec(string Table, string FileName, IReadOnlyList<RibbonButtonSpec> Buttons);

internal static class RibbonCatalogo
{
    /// <summary>Idiomas que TODO LocLabel debe traer: inglés (1033) y español (3082).</summary>
    internal static readonly IReadOnlyList<int> RequiredLanguages = [1033, 3082];

    private const string LocLabelsPrefix = "$LocLabels:";

    internal static readonly IReadOnlyList<RibbonAssetSpec> Assets =
    [
        new(SecuritySpec.Tables.ImportJob, "sanic_tbl_smlt_importjob.RibbonDiffXml.xml",
        [
            new("sanic.smlt.EjecutarImportJob.Button", IconsCatalogo.Run),
            new("sanic.smlt.ReintentarImportJob.Button", IconsCatalogo.Retry),
        ]),
        new(SecuritySpec.Tables.ImportTemplate, "sanic_tbl_smlt_importtemplate.RibbonDiffXml.xml",
        [
            new("sanic.smlt.ExportTemplate.Button", IconsCatalogo.Export),
            new("sanic.smlt.ImportTemplate.Button", IconsCatalogo.Import),
        ]),
    ];

    /// <summary>Tabla -> RibbonDiffXml completo, listo para SolutionZipBuilder.BuildRibbonSolution.</summary>
    internal static Dictionary<string, string> XmlByEntity() =>
        Assets.ToDictionary(a => a.Table, a => AssetLoader.ReadText(a.FileName));

    /// <summary>
    /// Problemas de los LocLabel: cada uno debe tener exactamente un título por idioma requerido, no vacío.
    /// <paramref name="onlyIds"/> limita el chequeo a esos ids (el ribbon completo de la tabla trae LocLabels de Microsoft).
    /// </summary>
    internal static IReadOnlyList<string> LocLabelProblems(XDocument ribbon, IEnumerable<string>? onlyIds = null)
    {
        var only = onlyIds?.ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var label in ribbon.Descendants("LocLabel"))
        {
            var id = (string?)label.Attribute("Id") ?? "(sin Id)";
            if (only is not null && !only.Contains(id)) continue;
            var titles = label.Descendants("Title").ToList();
            foreach (var lang in RequiredLanguages)
            {
                var forLang = titles.Where(t => (string?)t.Attribute("languagecode") == lang.ToString()).ToList();
                if (forLang.Count != 1)
                    problems.Add($"LocLabel '{id}': {forLang.Count} título(s) {lang}, esperado 1");
                else if (string.IsNullOrWhiteSpace((string?)forLang[0].Attribute("description")))
                    problems.Add($"LocLabel '{id}': el título {lang} está vacío");
            }
        }
        return problems;
    }

    /// <summary>Ids de LocLabel que referencian los botones del asset (LabelText/ToolTipTitle/ToolTipDescription).</summary>
    internal static IReadOnlyList<string> LabelIdsOf(XDocument ribbon, RibbonAssetSpec asset) =>
        asset.Buttons.Select(b => FindButton(ribbon, b.ButtonId)).Where(b => b is not null)
            .SelectMany(b => new[] { "LabelText", "ToolTipTitle", "ToolTipDescription" }.Select(a => (string?)b!.Attribute(a)))
            .Where(v => v is not null && v.StartsWith(LocLabelsPrefix, StringComparison.Ordinal))
            .Select(v => v![LocLabelsPrefix.Length..]).ToList();

    /// <summary>Problemas de los botones: deben existir y llevar ModernImage = $webresource:&lt;su ícono&gt;.</summary>
    internal static IReadOnlyList<string> ButtonProblems(XDocument ribbon, RibbonAssetSpec asset)
    {
        var problems = new List<string>();
        foreach (var spec in asset.Buttons)
        {
            var button = FindButton(ribbon, spec.ButtonId);
            if (button is null) { problems.Add($"no existe el botón '{spec.ButtonId}'"); continue; }
            var expected = IconsCatalogo.VectorIconRef(spec.Icon.WebResourceName);
            var actual = (string?)button.Attribute("ModernImage");
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                problems.Add($"botón '{spec.ButtonId}': ModernImage '{actual}', esperado '{expected}'");
        }
        return problems;
    }

    private static XElement? FindButton(XDocument ribbon, string id) =>
        ribbon.Descendants("Button").FirstOrDefault(b => (string?)b.Attribute("Id") == id);
}
