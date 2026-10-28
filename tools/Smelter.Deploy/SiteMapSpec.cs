// Especificación DECLARATIVA de las etiquetas de la MDA (sitemap + nombre de la app), BILINGÜES: inglés bajo LCID 1033
// (idioma base de la org) y español bajo LCID 3082, sin prefijos técnicos (auditoría de campos tanda 2, #6). La consume
// SiteMapProvisioner (deploy, paso 4k) y SiteMapAssertions (verify). Apply() es PURA e idempotente sobre el sitemapxml.
//
// RESTRICCIÓN: NO se cambian display names de TABLAS ni de columnas (otras canvas apps los referencian en Power Fx).
// Un SubArea sin <Titles> muestra el nombre de la tabla ("TBL | SMLT | Import Templates"); la solución es ponerle un
// Title explícito al SubArea, que solo afecta a la navegación de esta app. Cada usuario ve el idioma de su interfaz
// (3082 si lo eligió; si no, el 1033).
//
// ÍCONOS: cada SubArea lleva VectorIcon="$webresource:sanic_/smelter/icons/x.svg" (IconsSpec.cs) en lugar del Icon
// transparent_spacer.gif con el que nació. Apply() quita ese Icon legacy.

using System.Xml.Linq;

namespace Smelter.Deploy;

internal sealed record SiteMapLabel(string Id, Bilingual Title);

internal static class SiteMapCatalogo
{
    /// <summary>
    /// Nombre de la app. appmodule.name es el valor base (1033, inglés); el 3082 va con SetLocLabelsRequest (appmodule.name
    /// es IsLocalizable = True según la referencia de la tabla AppModule en Microsoft Learn).
    /// </summary>
    internal static readonly Bilingual AppName = new("Smelter — Import configuration", "Smelter — Configuración de importaciones");

    /// <summary>Ids tal como están en solutions/unpacked/AppModuleSiteMaps (un test los corrobora).</summary>
    internal static readonly SiteMapLabel[] Labels =
    [
        new("area_a3db6788", new("Smelter", "Smelter")),
        new("group_f9abc8da", new("Configuration", "Configuración")),
        new("subarea_a5465f8e", new("Import templates", "Templates de importación")),     // Import Template
        new("group_d07a1763", new("Execution & monitoring", "Ejecución y monitoreo")),
        new("subarea_6569ce1a", new("Import jobs", "Import Jobs")),                        // Import Job
        new("subarea_smlt_dashboard", new("Smelter dashboard", "Panel de Smelter")),       // Smelter Dashboard
        new("group_308470d1", new("Diagnostics", "Diagnóstico")),
        new("subarea_a2da1554", new("Import logs", "Logs de importación")),                // Import Job Log
    ];

    /// <summary>Icon con el que nacieron los SubArea (gif transparente): se reemplaza por el VectorIcon.</summary>
    private const string LegacySpacerIcon = "/_imgs/imagestrips/transparent_spacer.gif";

    private static readonly string[] TechnicalMarkers = ["TBL |", "SMLT |"];

    private static readonly int[] Languages = [Lcids.English, Lcids.Spanish];

    private static bool IsNode(XElement e) => e.Name.LocalName is "Area" or "Group" or "SubArea";

    /// <summary>Escribe los títulos (1033 + 3082) y los íconos de la spec en el sitemapxml. Devuelve el XML y si cambió algo.</summary>
    internal static (string SiteMapXml, bool Changed) Apply(string siteMapXml)
    {
        var doc = XDocument.Parse(siteMapXml.TrimStart('\uFEFF'));
        var changed = false;

        foreach (var label in Labels)
        {
            var element = doc.Descendants().FirstOrDefault(e => IsNode(e) && (string?)e.Attribute("Id") == label.Id);
            if (element is null) continue;

            var titles = element.Element("Titles");
            if (titles is null)
            {
                // El XSD del sitemap exige <Titles> antes que cualquier otro hijo.
                titles = new XElement("Titles");
                element.AddFirst(titles);
                changed = true;
            }

            foreach (var lcid in Languages)
            {
                var text = label.Title.For(lcid);
                var title = titles.Elements("Title").FirstOrDefault(t => (string?)t.Attribute("LCID") == lcid.ToString());
                if (title is null)
                {
                    titles.Add(new XElement("Title", new XAttribute("LCID", lcid), new XAttribute("Title", text)));
                    changed = true;
                }
                else if ((string?)title.Attribute("Title") != text)
                {
                    title.SetAttributeValue("Title", text);
                    changed = true;
                }
            }
        }

        foreach (var icon in IconsCatalogo.SubAreaIcons)
        {
            var sub = doc.Descendants("SubArea").FirstOrDefault(e => (string?)e.Attribute("Id") == icon.SubAreaId);
            if (sub is null) continue;

            var wanted = IconsCatalogo.VectorIconRef(icon.Icon.WebResourceName);
            if ((string?)sub.Attribute("VectorIcon") != wanted)
            {
                sub.SetAttributeValue("VectorIcon", wanted);
                changed = true;
            }
            if ((string?)sub.Attribute("Icon") == LegacySpacerIcon)
            {
                sub.Attribute("Icon")!.Remove();
                changed = true;
            }
        }

        return changed ? (doc.ToString(), true) : (siteMapXml, false);
    }

    /// <summary>
    /// Problemas de etiquetado: SubArea sin Title explícito (cae al nombre técnico de la tabla), cualquier Area/Group/SubArea
    /// al que le falte el título en 1033 o en 3082, o cualquier título con prefijos técnicos (TBL | / SMLT |).
    /// Vacío = sitemap limpio.
    /// </summary>
    internal static IReadOnlyList<string> Violations(string siteMapXml)
    {
        var problems = new List<string>();
        var doc = XDocument.Parse(siteMapXml.TrimStart('\uFEFF'));

        foreach (var e in doc.Descendants().Where(IsNode))
        {
            var id = (string?)e.Attribute("Id") ?? "?";
            var all = e.Element("Titles")?.Elements("Title").ToList() ?? [];
            var titles = all.Select(t => (string?)t.Attribute("Title")).ToList();
            if (e.Name.LocalName == "SubArea" && titles.Count == 0)
            {
                problems.Add($"{id}: SubArea sin Title explícito (mostraría el nombre técnico de la tabla)");
                continue;
            }
            foreach (var lcid in Languages)
                if (!all.Any(t => (string?)t.Attribute("LCID") == lcid.ToString() && !string.IsNullOrWhiteSpace((string?)t.Attribute("Title"))))
                    problems.Add($"{id}: falta el título LCID {lcid}");
            foreach (var t in titles.Where(t => t is not null && TechnicalMarkers.Any(m => t.Contains(m, StringComparison.OrdinalIgnoreCase))))
                problems.Add($"{id}: título técnico '{t}'");
        }
        return problems;
    }

    /// <summary>SubArea de la spec que no tiene el VectorIcon pedido (o conserva el Icon legacy). Vacío = íconos al día.</summary>
    internal static IReadOnlyList<string> IconViolations(string siteMapXml)
    {
        var problems = new List<string>();
        var doc = XDocument.Parse(siteMapXml.TrimStart('\uFEFF'));

        foreach (var icon in IconsCatalogo.SubAreaIcons)
        {
            var sub = doc.Descendants("SubArea").FirstOrDefault(e => (string?)e.Attribute("Id") == icon.SubAreaId);
            if (sub is null) { problems.Add($"{icon.SubAreaId}: SubArea inexistente"); continue; }

            var wanted = IconsCatalogo.VectorIconRef(icon.Icon.WebResourceName);
            if ((string?)sub.Attribute("VectorIcon") != wanted)
                problems.Add($"{icon.SubAreaId}: VectorIcon '{(string?)sub.Attribute("VectorIcon")}', esperado '{wanted}'");
            if ((string?)sub.Attribute("Icon") == LegacySpacerIcon)
                problems.Add($"{icon.SubAreaId}: conserva el Icon transparent_spacer");
        }
        return problems;
    }

    /// <summary>
    /// Motivo por el que el nombre de la app (valor base, 1033) no sirve (typo / prefijo técnico / vacío); null si está bien.
    /// </summary>
    internal static string? AppNameViolation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "nombre vacío";
        if (name.Contains("Configurationd", StringComparison.OrdinalIgnoreCase)) return "typo 'Configurationd'";
        if (TechnicalMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
            name.Contains("SMLT", StringComparison.Ordinal) || name.StartsWith("MDA", StringComparison.OrdinalIgnoreCase))
            return "prefijo técnico (MDA / SMLT / TBL)";
        return null;
    }
}
