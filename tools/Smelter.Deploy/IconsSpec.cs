// Especificación DECLARATIVA de los íconos de Smelter: SVG Fluent-style (24x24, un solo color) versionados en
// Assets/icons/ (embebidos en el assembly, ver AssetLoader) y provisionados como web resources (webresourcetype 11 = SVG)
// DENTRO de la solución smlt_core_smelter. La consume IconsProvisioner (deploy, paso 4m) e IconsAssertions (verify).
//
// Se usan en tres lugares:
//   - EntityMetadata.IconVectorName de cada tabla de Smelter (ícono en la grilla, el form y los lookups).
//   - VectorIcon="$webresource:<nombre>" de cada SubArea del sitemap (SiteMapSpec.Apply, reemplaza al Icon transparent_spacer).
//   - appmodule.webresourceid: el ícono de la app (el tile de la lista de apps).
//
// Las tablas Table/Column/Relationship Mapping no tienen entrada en el sitemap (se abren desde el template) pero sí ícono.
// El HMAC Key table solo se toca si existe (HmacKeyTableProvisioner la crea antes, paso 4e).

namespace Smelter.Deploy;

internal sealed record IconSpec(string Key, string FileName, string WebResourceName, string DisplayName);

internal sealed record TableIconSpec(string Table, IconSpec Icon);

internal sealed record SubAreaIconSpec(string SubAreaId, IconSpec Icon);

internal static class IconsCatalogo
{
    /// <summary>Tipo de web resource SVG (webresource.webresourcetype).</summary>
    internal const int SvgWebResourceType = 11;

    /// <summary>ComponentType de WebResource en solutioncomponent / AddSolutionComponentRequest.</summary>
    internal const int WebResourceComponentType = 61;

    /// <summary>Color único de todos los íconos (azul Smelter).</summary>
    internal const string Color = "#2F6FB3";

    private const string Prefix = "sanic_/smelter/icons/";

    private static IconSpec Icon(string key, string display) =>
        new(key, key + ".svg", Prefix + key + ".svg", "Icon | SMLT | " + display);

    internal static readonly IconSpec Template = Icon("template", "Import Template");
    internal static readonly IconSpec Job = Icon("job", "Import Job");
    internal static readonly IconSpec Log = Icon("joblog", "Import Job Log");
    internal static readonly IconSpec Dashboard = Icon("dashboard", "Dashboard");
    internal static readonly IconSpec HmacKey = Icon("hmackey", "HMAC Key");
    internal static readonly IconSpec App = Icon("app", "App");
    internal static readonly IconSpec TableMapping = Icon("tablemapping", "Table Mapping");
    internal static readonly IconSpec ColumnMapping = Icon("columnmapping", "Column Mapping");
    internal static readonly IconSpec RelationshipMapping = Icon("relationshipmapping", "Relationship Mapping");

    internal static readonly IReadOnlyList<IconSpec> All =
    [
        Template, Job, Log, Dashboard, HmacKey, App, TableMapping, ColumnMapping, RelationshipMapping,
    ];

    internal static readonly TableIconSpec[] TableIcons =
    [
        new(SecuritySpec.Tables.ImportTemplate, Template),
        new(SecuritySpec.Tables.ImportJob, Job),
        new(SecuritySpec.Tables.ImportJobLog, Log),
        new(SecuritySpec.Tables.TableMapping, TableMapping),
        new(SecuritySpec.Tables.ColumnMapping, ColumnMapping),
        new(SecuritySpec.Tables.RelationshipMapping, RelationshipMapping),
        new(HmacKeyTableSpec.Table, HmacKey),
    ];

    /// <summary>Ids de SiteMapCatalogo.Labels: cada SubArea con su ícono.</summary>
    internal static readonly SubAreaIconSpec[] SubAreaIcons =
    [
        new("subarea_a5465f8e", Template),
        new("subarea_6569ce1a", Job),
        new("subarea_smlt_dashboard", Dashboard),
        new("subarea_a2da1554", Log),
    ];

    /// <summary>Valor del atributo VectorIcon del sitemap para un web resource.</summary>
    internal static string VectorIconRef(string webResourceName) => "$webresource:" + webResourceName;

    /// <summary>Contenido del SVG embebido (Assets/icons/&lt;archivo&gt;).</summary>
    internal static string ReadSvg(IconSpec icon) => AssetLoader.ReadText("icons." + icon.FileName);

    /// <summary>webresource.content: el SVG en UTF-8 sin BOM, en base64.</summary>
    internal static string ToBase64(string svg) => Convert.ToBase64String(new System.Text.UTF8Encoding(false).GetBytes(svg));

    /// <summary>¿Hay que escribir el contenido? (distinto del actual, o actual vacío). Compara el base64.</summary>
    internal static bool NeedsContentUpdate(string? currentBase64, string desiredBase64) =>
        !string.Equals(currentBase64, desiredBase64, StringComparison.Ordinal);

    /// <summary>ParameterXml de PublishXml para publicar SOLO los web resources, tablas y la app que tocó el paso.</summary>
    internal static string PublishXml(IEnumerable<Guid> webResourceIds, IEnumerable<string> tables, string? appKey)
    {
        var xml = new System.Text.StringBuilder("<importexportxml>");
        var ids = webResourceIds.ToList();
        if (ids.Count > 0)
            xml.Append("<webresources>").Append(string.Concat(ids.Select(i => $"<webresource>{{{i:D}}}</webresource>"))).Append("</webresources>");
        var names = tables.ToList();
        if (names.Count > 0)
            xml.Append("<entities>").Append(string.Concat(names.Select(t => $"<entity>{t}</entity>"))).Append("</entities>");
        if (appKey is not null)
            xml.Append("<appmodules><appmodule>").Append(appKey).Append("</appmodule></appmodules>");
        return xml.Append("</importexportxml>").ToString();
    }
}
