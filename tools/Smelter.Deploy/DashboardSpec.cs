// Especificación DECLARATIVA del dashboard de Smelter como dashboard CLÁSICO (systemform type 0). La consumen
// DashboardProvisioner (deploy, paso 6b) y las aserciones [MDA]/[SITEMAP] de Smelter.Verify.
//
// POR QUÉ: la solución traía el "Smelter Dashboard" como dashboard interaction-centric (type 10, 590df35c-...). En la MDA
// (Unified Interface) ese tipo no aparece en el selector de dashboards y el SubArea del sitemap termina renderizando el
// overview genérico "Microsoft Dynamics 365 Social". Un dashboard clásico (type 0) sí se ve, con los mismos contenidos:
// los 2 gráficos del Import Job + el listado (vista "Active TBL | SMLT | Import Jobs").
//
// FORMXML: un systemform guarda <form><tabs>…</tabs></form> (sin el envoltorio <forms type="…"> del export de la solución).
// El intento anterior de crear uno por código (run 30360739680) falló por meter un hijo <dashboard> dentro de <form>; la
// estructura de acá es la de un dashboard clásico real del mismo ambiente (tab → column → section → rows/cells con
// controles de subgrid ClassId E7A81278-…, ChartGridMode Chart/Grid).
// Todos los ids de tab/section/cell son FIJOS, así el formxml es determinístico y el provisioner idempotente.
//
// IDs: los gráficos y la vista son los de solutions/unpacked (DashboardSpecContractTests los corrobora).
//
// Nombre: 1033 "Smelter Dashboard" (SecuritySpec.DashboardName, no cambia) + 3082 "Panel de Smelter" (ViewLabelsSpec).

using System.Xml.Linq;

namespace Smelter.Deploy;

internal static class DashboardCatalogo
{
    /// <summary>formid del dashboard clásico (nuevo; el de la solución original es <see cref="LegacyInteractionCentricId"/>).</summary>
    internal static readonly Guid FormId = Guid.Parse("0aaf3a0c-c3a8-4f67-a7b8-a1cfd5a5dfbd");

    /// <summary>El dashboard interaction-centric (type 10) que se reemplaza: se saca de la app y se borra.</summary>
    internal static readonly Guid LegacyInteractionCentricId = Guid.Parse("590df35c-1b64-f111-ab0c-0022480a5be8");

    internal static readonly Bilingual Name = ViewLabelsCatalogo.Dashboard;

    /// <summary>Gráficos del Import Job: "Status by Status" y "Import Template by Import Template".</summary>
    internal static readonly Guid[] ChartIds =
    [
        Guid.Parse("dc8cc371-1a64-f111-ab0c-0022480a5be8"),
        Guid.Parse("0c1107f3-1a64-f111-ab0c-0022480a5be8"),
    ];

    /// <summary>Vista del listado: "Active TBL | SMLT | Import Jobs" (la misma que usaba el stream del dashboard anterior).</summary>
    internal static readonly Guid ViewId = Guid.Parse("27b8d129-9055-4ca5-a051-961ff5d054ca");

    internal const string SubAreaId = "subarea_smlt_dashboard";

    /// <summary>Url del SubArea del sitemap (Unified Interface: pagetype=dashboard).</summary>
    internal static string Url => $"/main.aspx?pagetype=dashboard&id=%7b{FormId:D}%7d";

    private const string SubgridClassId = "{E7A81278-8635-4d9e-8D4D-59480B391C5B}";
    private const string TabId = "{bf43daa9-dcce-4f92-9582-6f7c590baaca}";
    private const string SectionId = "{1c3e4bf4-3a78-49ed-a812-9c44e697b459}";
    private static readonly string[] CellIds =
        ["{d5b384c8-f3ce-4394-80eb-dfbd8911f4d5}", "{b6b42157-9b53-4f0e-b3ff-cff2b5f0af75}", "{93750f76-b381-4404-8a08-c4216a57d3a3}"];

    private static XElement Label(string text) =>
        new("labels", new XElement("label", new XAttribute("description", text), new XAttribute("languagecode", Lcids.English)));

    private static XElement Cell(string cellId, string controlId, int colspan, string mode, Guid? chart, string label) =>
        new("cell",
            new XAttribute("colspan", colspan), new XAttribute("rowspan", 12), new XAttribute("showlabel", "false"),
            new XAttribute("id", cellId), new XAttribute("auto", "false"),
            Label(label),
            new XElement("control",
                new XAttribute("id", controlId), new XAttribute("classid", SubgridClassId), new XAttribute("indicationOfSubgrid", "true"),
                new XElement("parameters",
                    new XElement("TargetEntityType", SecuritySpec.Tables.ImportJob),
                    new XElement("ChartGridMode", mode),
                    new XElement("EnableQuickFind", "false"),
                    new XElement("EnableViewPicker", "false"),
                    new XElement("EnableJumpBar", "false"),
                    new XElement("RecordsPerPage", 10),
                    new XElement("HeaderColorCode"),
                    new XElement("ViewId", $"{{{ViewId:D}}}"),
                    new XElement("IsUserView", "false"),
                    new XElement("ViewIds"),
                    new XElement("AutoExpand", "Fixed"),
                    new XElement("VisualizationId", chart is { } c ? $"{{{c:D}}}" : ""),
                    new XElement("IsUserChart", "false"),
                    new XElement("EnableChartPicker", "false"),
                    new XElement("RelationshipName"))));

    /// <summary>Fila 1: los 2 gráficos lado a lado (2 columnas c/u de 4). Fila 2: el listado a todo el ancho.</summary>
    internal static string BuildFormXml()
    {
        var filler = Enumerable.Range(0, 11).Select(_ => new XElement("row"));
        var rows = new XElement("rows",
            new XElement("row",
                Cell(CellIds[0], "ComponentSmltStatusChart", 2, "Chart", ChartIds[0], "Import Jobs by status"),
                Cell(CellIds[1], "ComponentSmltTemplateChart", 2, "Chart", ChartIds[1], "Import Jobs by template")),
            filler.Select(r => new XElement(r)),
            new XElement("row", Cell(CellIds[2], "ComponentSmltJobsGrid", 4, "Grid", null, "Import Jobs")),
            filler.Select(r => new XElement(r)));

        var form = new XElement("form",
            new XElement("tabs",
                new XElement("tab",
                    new XAttribute("showlabel", "false"), new XAttribute("verticallayout", "true"), new XAttribute("id", TabId),
                    Label("Tab"),
                    new XElement("columns",
                        new XElement("column", new XAttribute("width", "100%"),
                            new XElement("sections",
                                new XElement("section",
                                    new XAttribute("showlabel", "false"), new XAttribute("showbar", "false"),
                                    new XAttribute("columns", "1111"), new XAttribute("id", SectionId),
                                    Label("Section"),
                                    rows))))))));
        return form.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Problemas del formxml de un dashboard clásico: raíz distinta de &lt;form&gt;, envoltorio &lt;forms&gt; del export,
    /// hijo &lt;dashboard&gt;, o faltan los 2 gráficos / el listado de la spec. Vacío = el formxml sirve.
    /// </summary>
    internal static IReadOnlyList<string> Violations(string? formXml)
    {
        var problems = new List<string>();
        XElement root;
        try { root = XElement.Parse(formXml ?? ""); }
        catch (System.Xml.XmlException ex) { return [$"formxml ilegible: {ex.Message}"]; }

        if (root.Name.LocalName != "form") problems.Add($"la raíz es <{root.Name.LocalName}>, debe ser <form> (¿formxml de un interaction-centric?)");
        if (root.Descendants("dashboard").Any()) problems.Add("tiene un hijo <dashboard> (rechazado por Dataverse)");

        var controls = root.Descendants("control").ToList();
        var charts = controls.Select(c => c.Descendants("VisualizationId").FirstOrDefault()?.Value?.Trim('{', '}')).Where(v => !string.IsNullOrEmpty(v)).ToList();
        foreach (var chart in ChartIds)
            if (!charts.Any(c => string.Equals(c, chart.ToString("D"), StringComparison.OrdinalIgnoreCase)))
                problems.Add($"falta el gráfico {chart:D}");
        var views = controls.Where(c => c.Descendants("ChartGridMode").FirstOrDefault()?.Value == "Grid")
            .Select(c => c.Descendants("ViewId").FirstOrDefault()?.Value?.Trim('{', '}')).ToList();
        if (!views.Any(v => string.Equals(v, ViewId.ToString("D"), StringComparison.OrdinalIgnoreCase)))
            problems.Add($"falta el listado con la vista {ViewId:D}");
        return problems;
    }

    /// <summary>Pura e idempotente: apunta el Url del SubArea del dashboard al <see cref="FormId"/>. Si el SubArea no existe no toca nada.</summary>
    internal static (string SiteMapXml, bool Changed) PointSiteMapAtDashboard(string siteMapXml)
    {
        var doc = XDocument.Parse(siteMapXml.TrimStart('﻿'));
        var sub = doc.Descendants("SubArea").FirstOrDefault(e => (string?)e.Attribute("Id") == SubAreaId);
        if (sub is null || (string?)sub.Attribute("Url") == Url) return (siteMapXml, false);
        sub.SetAttributeValue("Url", Url);
        return (doc.ToString(), true);
    }

    /// <summary>ParameterXml de PublishXml para el dashboard.</summary>
    internal static string PublishXml() => $"<importexportxml><dashboards><dashboard>{{{FormId:D}}}</dashboard></dashboards></importexportxml>";
}
