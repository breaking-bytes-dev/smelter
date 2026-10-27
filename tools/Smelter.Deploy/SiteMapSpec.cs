// Especificación DECLARATIVA de las etiquetas de la MDA (sitemap + nombre de la app) en español, sin prefijos
// técnicos (auditoría de campos tanda 2, #6). La consume SiteMapProvisioner (deploy, paso 4k) y SiteMapAssertions
// (verify). Apply() es PURA e idempotente sobre el sitemapxml.
//
// RESTRICCIÓN: NO se cambian display names de TABLAS ni de columnas (otras canvas apps los referencian en Power Fx).
// Un SubArea sin <Titles> muestra el nombre de la tabla ("TBL | SMLT | Import Templates"); la solución es ponerle un
// Title explícito al SubArea, que solo afecta a la navegación de esta app.
// Solo LCID 1033 (el idioma base de la org, el mismo que usa el resto de la provisión).

using System.Xml.Linq;

namespace Smelter.Deploy;

internal sealed record SiteMapLabel(string Id, string Title);

internal static class SiteMapCatalogo
{
    internal const string AppName = "Smelter — Configuración de importaciones";

    private const string Lcid = "1033";

    /// <summary>Ids tal como están en solutions/unpacked/AppModuleSiteMaps (un test los corrobora).</summary>
    internal static readonly SiteMapLabel[] Labels =
    [
        new("area_a3db6788", "Smelter"),
        new("group_f9abc8da", "Configuración"),
        new("subarea_a5465f8e", "Templates de importación"),     // Import Template
        new("group_d07a1763", "Ejecución y monitoreo"),
        new("subarea_6569ce1a", "Import Jobs"),                  // Import Job
        new("subarea_smlt_dashboard", "Panel de Smelter"),       // Smelter Dashboard
        new("group_308470d1", "Diagnóstico"),
        new("subarea_a2da1554", "Logs de importación"),          // Import Job Log
    ];

    private static readonly string[] TechnicalMarkers = ["TBL |", "SMLT |"];

    /// <summary>Escribe los títulos de la spec en el sitemapxml. Devuelve el XML y si cambió algo.</summary>
    internal static (string SiteMapXml, bool Changed) Apply(string siteMapXml)
    {
        var doc = XDocument.Parse(siteMapXml.TrimStart('﻿'));
        var changed = false;

        foreach (var label in Labels)
        {
            var element = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName is "Area" or "Group" or "SubArea" && (string?)e.Attribute("Id") == label.Id);
            if (element is null) continue;

            var titles = element.Element("Titles");
            if (titles is null)
            {
                // El XSD del sitemap exige <Titles> antes que cualquier otro hijo.
                titles = new XElement("Titles");
                element.AddFirst(titles);
                changed = true;
            }

            var title = titles.Elements("Title").FirstOrDefault(t => (string?)t.Attribute("LCID") == Lcid);
            if (title is null)
            {
                titles.Add(new XElement("Title", new XAttribute("LCID", Lcid), new XAttribute("Title", label.Title)));
                changed = true;
            }
            else if ((string?)title.Attribute("Title") != label.Title)
            {
                title.SetAttributeValue("Title", label.Title);
                changed = true;
            }
        }

        return changed ? (doc.ToString(), true) : (siteMapXml, false);
    }

    /// <summary>
    /// Problemas de etiquetado: SubArea sin Title explícito (cae al nombre técnico de la tabla), o cualquier título con
    /// prefijos técnicos (TBL | / SMLT |). Vacío = sitemap limpio.
    /// </summary>
    internal static IReadOnlyList<string> Violations(string siteMapXml)
    {
        var problems = new List<string>();
        var doc = XDocument.Parse(siteMapXml.TrimStart('﻿'));

        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "Area" or "Group" or "SubArea"))
        {
            var id = (string?)e.Attribute("Id") ?? "?";
            var titles = e.Element("Titles")?.Elements("Title").Select(t => (string?)t.Attribute("Title")).ToList() ?? [];
            if (e.Name.LocalName == "SubArea" && titles.Count == 0)
                problems.Add($"{id}: SubArea sin Title explícito (mostraría el nombre técnico de la tabla)");
            foreach (var t in titles.Where(t => t is not null && TechnicalMarkers.Any(m => t.Contains(m, StringComparison.OrdinalIgnoreCase))))
                problems.Add($"{id}: título técnico '{t}'");
        }
        return problems;
    }

    /// <summary>Motivo por el que el nombre de la app no sirve (typo / prefijo técnico / vacío); null si está bien.</summary>
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
