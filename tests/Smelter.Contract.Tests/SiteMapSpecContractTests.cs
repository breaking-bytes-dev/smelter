using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="SiteMapCatalogo"/>: etiquetas en español sin prefijos técnicos (TBL | / SMLT |) en el sitemap
/// de la MDA y nombre de la app sin typos. Se aplica en memoria sobre el sitemap REAL de solutions/unpacked.
/// Los display names de TABLAS no se tocan (canvas apps): un SubArea sin Title muestra el de la tabla, por eso cada
/// SubArea lleva Title explícito.
/// </summary>
public class SiteMapSpecContractTests
{
    private static string RealSiteMapXml()
    {
        var dir = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "AppModuleSiteMaps");
        var file = Directory.GetFiles(dir, "AppModuleSiteMap.xml", SearchOption.AllDirectories).Single();
        var root = XDocument.Load(file).Descendants("SiteMap").Single();
        return root.ToString();
    }

    private static string? TitleOf(XElement e) =>
        (string?)e.Element("Titles")?.Elements("Title").FirstOrDefault(t => (string?)t.Attribute("LCID") == "1033")?.Attribute("Title");

    [Fact]
    public void RealSiteMap_BeforeApply_HasTechnicalOrMissingTitles()
    {
        SiteMapCatalogo.Violations(RealSiteMapXml()).Should().NotBeEmpty("precondición: el sitemap versionado tiene Area1 y subareas sin título");
    }

    [Fact]
    public void Apply_SetsTheSpanishLabels_AndLeavesNoViolations()
    {
        var (xml, changed) = SiteMapCatalogo.Apply(RealSiteMapXml());

        changed.Should().BeTrue();
        SiteMapCatalogo.Violations(xml).Should().BeEmpty();
        var doc = XDocument.Parse(xml);
        TitleOf(doc.Descendants("Area").Single()).Should().Be("Smelter");
        var titles = doc.Descendants("SubArea").ToDictionary(s => (string)s.Attribute("Id")!, TitleOf);
        titles["subarea_a5465f8e"].Should().Be("Templates de importación");
        titles["subarea_6569ce1a"].Should().Be("Import Jobs");
        titles["subarea_smlt_dashboard"].Should().Be("Panel de Smelter");
        titles["subarea_a2da1554"].Should().Be("Logs de importación");
        doc.Descendants("Group").Select(TitleOf).Should().Equal("Configuración", "Ejecución y monitoreo", "Diagnóstico");
    }

    [Fact]
    public void Apply_PutsTitlesBeforeAnyOtherChild_AsTheSitemapXsdRequires()
    {
        var (xml, _) = SiteMapCatalogo.Apply(RealSiteMapXml());

        foreach (var sub in XDocument.Parse(xml).Descendants("SubArea"))
            sub.Elements().First().Name.LocalName.Should().Be("Titles");
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var (first, _) = SiteMapCatalogo.Apply(RealSiteMapXml());

        var (second, changed) = SiteMapCatalogo.Apply(first);

        changed.Should().BeFalse();
        second.Should().Be(first);
    }

    [Fact]
    public void Apply_KeepsEverySubAreaAttribute_OnlyTitlesChange()
    {
        var before = XDocument.Parse(RealSiteMapXml());
        var (xml, _) = SiteMapCatalogo.Apply(RealSiteMapXml());
        var after = XDocument.Parse(xml);

        string Attrs(XDocument d) => string.Join("|", d.Descendants().Where(e => e.Name.LocalName is "Area" or "Group" or "SubArea")
            .Select(e => e.Name.LocalName + ":" + string.Join(",", e.Attributes().OrderBy(a => a.Name.LocalName).Select(a => a.Name.LocalName + "=" + a.Value))));
        Attrs(after).Should().Be(Attrs(before));
    }

    [Fact]
    public void Violations_FlagsTechnicalPrefixesAndMissingTitles()
    {
        const string xml = "<SiteMap><Area Id='a'><Titles><Title LCID='1033' Title='X' /></Titles><Group Id='g'><Titles><Title LCID='1033' Title='G' /></Titles>" +
            "<SubArea Id='s1' Entity='e'><Titles><Title LCID='1033' Title='TBL | SMLT | Import Templates' /></Titles></SubArea>" +
            "<SubArea Id='s2' Entity='e2' /></Group></Area></SiteMap>";

        SiteMapCatalogo.Violations(xml).Should().HaveCount(2);
    }

    [Fact]
    public void AppName_IsCleanSpanish_WithoutTyposOrTechnicalPrefixes()
    {
        SiteMapCatalogo.AppName.Should().Be("Smelter — Configuración de importaciones");
        SiteMapCatalogo.AppNameViolation(SiteMapCatalogo.AppName).Should().BeNull();
        SiteMapCatalogo.AppNameViolation("MDA - SMLT - Import Configurationd and Monitoring").Should().NotBeNull();
        SiteMapCatalogo.AppNameViolation(null).Should().NotBeNull();
    }

    [Fact]
    public void Labels_TargetOnlyIdsThatExistInTheRealSiteMap()
    {
        var ids = XDocument.Parse(RealSiteMapXml()).Descendants().Select(e => (string?)e.Attribute("Id")).ToHashSet();

        SiteMapCatalogo.Labels.Select(l => l.Id).Should().OnlyContain(id => ids.Contains(id));
    }
}
