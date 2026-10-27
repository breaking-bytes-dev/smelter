// Parsers PUROS (sin Dataverse) del [MDA] de Smelter.Verify: extraen de formxml / fetchxml /
// layoutxml / sitemap lo que hay que contrastar contra la metadata real (atributos, web resources,
// subareas). Testeados con XML real de solutions/unpacked (MdaXmlTests).

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Smelter.Deploy;

internal sealed record SiteMapSubArea(string? Id, string? Entity, string? Url);

internal static class MdaXml
{
    /// <summary>Atributos del form: control@datafieldname y event@attribute (onchange).</summary>
    internal static HashSet<string> FormAttributes(string formXml)
    {
        var doc = XDocument.Parse(formXml);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in doc.Descendants("control"))
            Add(set, (string?)c.Attribute("datafieldname"));
        foreach (var e in doc.Descendants("event"))
            Add(set, (string?)e.Attribute("attribute"));
        return set;
    }

    /// <summary>Web resources de las form libraries (Library@name), sin prefijo $webresource:.</summary>
    internal static HashSet<string> FormWebResources(string formXml)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in XDocument.Parse(formXml).Descendants("Library"))
        {
            var name = (string?)l.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            set.Add(name.StartsWith(WebResourcePrefix, StringComparison.OrdinalIgnoreCase) ? name[WebResourcePrefix.Length..] : name);
        }
        return set;
    }

    private const string WebResourcePrefix = "$webresource:";
    private static readonly Regex WebResourceRef = new(@"\$webresource:([A-Za-z0-9_./\-]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Referencias "$webresource:nombre" en cualquier texto (RibbonDiffXml, ribbon completo).</summary>
    internal static HashSet<string> WebResourceRefs(string text) =>
        WebResourceRef.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Entidad raíz y atributos del fetch: attribute@name, order@attribute y condition@attribute de la entidad
    /// raíz. Ignora todo lo que cuelga de link-entity y las conditions con @entityname (apuntan a un alias).
    /// </summary>
    internal static (string Entity, HashSet<string> Attributes) FetchAttributes(string fetchXml)
    {
        var entity = XDocument.Parse(fetchXml).Descendants("entity").FirstOrDefault()
            ?? throw new FormatException("fetchxml sin <entity>.");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in entity.Descendants().Where(d => !d.Ancestors("link-entity").Any() && d.Name != "link-entity"))
        {
            switch (n.Name.LocalName)
            {
                case "attribute" when n.Parent == entity: Add(set, (string?)n.Attribute("name")); break;
                case "order": Add(set, (string?)n.Attribute("attribute")); break;
                case "condition" when n.Attribute("entityname") is null: Add(set, (string?)n.Attribute("attribute")); break;
            }
        }
        return ((string?)entity.Attribute("name") ?? "", set);
    }

    /// <summary>Columnas del layoutxml (cell@name); omite las de entidades enlazadas (alias.atributo).</summary>
    internal static HashSet<string> LayoutAttributes(string layoutXml)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in XDocument.Parse(layoutXml).Descendants("cell"))
        {
            var name = (string?)c.Attribute("name");
            if (!string.IsNullOrWhiteSpace(name) && !name.Contains('.')) set.Add(name);
        }
        return set;
    }

    internal static List<SiteMapSubArea> SiteMapSubAreas(string siteMapXml) =>
        XDocument.Parse(siteMapXml.TrimStart('﻿')).Descendants("SubArea")
            .Select(s => new SiteMapSubArea((string?)s.Attribute("Id"), (string?)s.Attribute("Entity"), (string?)s.Attribute("Url")))
            .ToList();

    private static readonly Regex DashboardUrl = new(
        @"pagetype=dashboard.*?[?&]id=(?:%7[bB]|\{)?([0-9a-fA-F\-]{36})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Id del dashboard si la URL del subarea es de tipo dashboard; null si no.</summary>
    internal static Guid? DashboardId(string url)
    {
        var m = DashboardUrl.Match(url);
        return m.Success && Guid.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    private static void Add(HashSet<string> set, string? v)
    {
        if (!string.IsNullOrWhiteSpace(v)) set.Add(v);
    }
}

/// <summary>Identidad de la model-driven app de Smelter (unique name = carpeta en solutions/unpacked/AppModules).</summary>
internal static class MdaSpec
{
    internal const string AppUniqueName = "sanic_MDASMLTImportConfigurationdandMoni";

    /// <summary>ComponentType de appmodulecomponent: entidad, vista (savedquery), form (systemform), sitemap.</summary>
    internal const int ComponentEntity = 1, ComponentView = 26, ComponentForm = 60, ComponentSiteMap = 62;
}
