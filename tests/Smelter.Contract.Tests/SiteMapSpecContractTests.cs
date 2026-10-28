using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="SiteMapCatalogo"/>: etiquetas BILINGÜES (inglés 1033 + español 3082) sin prefijos técnicos
/// (TBL | / SMLT |) en el sitemap de la MDA, VectorIcon por SubArea y nombre de la app sin typos. Se aplica en memoria
/// sobre el sitemap REAL de solutions/unpacked.
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

    private static string? TitleOf(XElement e, string lcid = "1033") =>
        (string?)e.Element("Titles")?.Elements("Title").FirstOrDefault(t => (string?)t.Attribute("LCID") == lcid)?.Attribute("Title");

    private static string? EsTitleOf(XElement e) => TitleOf(e, "3082");

    [Fact]
    public void RealSiteMap_BeforeApply_HasTechnicalOrMissingTitles()
    {
        SiteMapCatalogo.Violations(RealSiteMapXml()).Should().NotBeEmpty("precondición: el sitemap versionado tiene Area1 y subareas sin título");
    }

    [Fact]
    public void Apply_SetsEnglishUnder1033AndSpanishUnder3082_AndLeavesNoViolations()
    {
        var (xml, changed) = SiteMapCatalogo.Apply(RealSiteMapXml());

        changed.Should().BeTrue();
        SiteMapCatalogo.Violations(xml).Should().BeEmpty();
        var doc = XDocument.Parse(xml);
        TitleOf(doc.Descendants("Area").Single()).Should().Be("Smelter");
        EsTitleOf(doc.Descendants("Area").Single()).Should().Be("Smelter");
        var en = doc.Descendants("SubArea").ToDictionary(s => (string)s.Attribute("Id")!, s => TitleOf(s));
        var es = doc.Descendants("SubArea").ToDictionary(s => (string)s.Attribute("Id")!, EsTitleOf);
        en["subarea_a5465f8e"].Should().Be("Import templates");
        en["subarea_6569ce1a"].Should().Be("Import jobs");
        en["subarea_smlt_dashboard"].Should().Be("Smelter dashboard");
        en["subarea_a2da1554"].Should().Be("Import logs");
        es["subarea_a5465f8e"].Should().Be("Templates de importación");
        es["subarea_6569ce1a"].Should().Be("Import Jobs");
        es["subarea_smlt_dashboard"].Should().Be("Panel de Smelter");
        es["subarea_a2da1554"].Should().Be("Logs de importación");
        doc.Descendants("Group").Select(g => TitleOf(g)).Should().Equal("Configuration", "Execution & monitoring", "Diagnostics");
        doc.Descendants("Group").Select(EsTitleOf).Should().Equal("Configuración", "Ejecución y monitoreo", "Diagnóstico");
    }

    [Fact]
    public void Apply_WritesExactlyOneTitlePerLanguage()
    {
        var (xml, _) = SiteMapCatalogo.Apply(RealSiteMapXml());

        foreach (var titles in XDocument.Parse(xml).Descendants("Titles"))
            titles.Elements("Title").Select(t => (string?)t.Attribute("LCID")).Should().BeEquivalentTo(["1033", "3082"]);
    }

    [Fact]
    public void Apply_GivesEverySubAreaItsVectorIcon_AndDropsTheTransparentSpacer()
    {
        var (xml, _) = SiteMapCatalogo.Apply(RealSiteMapXml());

        var subs = XDocument.Parse(xml).Descendants("SubArea").ToDictionary(s => (string)s.Attribute("Id")!);
        subs["subarea_a5465f8e"].Attribute("VectorIcon")!.Value.Should().Be("$webresource:sanic_/smelter/icons/template.svg");
        subs["subarea_6569ce1a"].Attribute("VectorIcon")!.Value.Should().Be("$webresource:sanic_/smelter/icons/job.svg");
        subs["subarea_smlt_dashboard"].Attribute("VectorIcon")!.Value.Should().Be("$webresource:sanic_/smelter/icons/dashboard.svg");
        subs["subarea_a2da1554"].Attribute("VectorIcon")!.Value.Should().Be("$webresource:sanic_/smelter/icons/joblog.svg");
        subs.Values.Should().OnlyContain(s => s.Attribute("Icon") == null || !s.Attribute("Icon")!.Value.Contains("transparent_spacer"));
        SiteMapCatalogo.IconViolations(xml).Should().BeEmpty();
    }

    [Fact]
    public void IconViolations_FlagsTheSubAreasThatStillLackTheirIcon()
    {
        var problems = SiteMapCatalogo.IconViolations(RealSiteMapXml());

        foreach (var icon in IconsCatalogo.SubAreaIcons)
            problems.Should().Contain(p => p.StartsWith(icon.SubAreaId + ":"), "el sitemap versionado no trae VectorIcon");
    }

    [Fact]
    public void Apply_RewritesAnExistingEnglishOnlyOrSpanishUnder1033Sitemap_ToTheBilingualSpec()
    {
        // Estado de Dev antes de este cambio: español bajo 1033, sin 3082.
        const string legacy = "<SiteMap><Area Id='area_a3db6788'><Titles><Title LCID='1033' Title='Smelter' /></Titles>" +
            "<Group Id='group_f9abc8da'><Titles><Title LCID='1033' Title='Configuración' /></Titles>" +
            "<SubArea Id='subarea_a5465f8e' Icon='/_imgs/imagestrips/transparent_spacer.gif' Entity='e'><Titles><Title LCID='1033' Title='Templates de importación' /></Titles></SubArea>" +
            "</Group></Area></SiteMap>";

        var (xml, changed) = SiteMapCatalogo.Apply(legacy);

        changed.Should().BeTrue();
        var doc = XDocument.Parse(xml);
        TitleOf(doc.Descendants("Group").Single()).Should().Be("Configuration");
        EsTitleOf(doc.Descendants("Group").Single()).Should().Be("Configuración");
        TitleOf(doc.Descendants("SubArea").Single()).Should().Be("Import templates");
        EsTitleOf(doc.Descendants("SubArea").Single()).Should().Be("Templates de importación");
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

        // s1: técnico + falta 3082; s2: sin título; Area y Group: falta 3082 cada uno.
        SiteMapCatalogo.Violations(xml).Should().HaveCount(5);
    }

    [Fact]
    public void Violations_FlagsASubAreaMissingEitherLanguage()
    {
        const string onlyEn = "<SiteMap><Area Id='a'><Titles><Title LCID='1033' Title='A' /><Title LCID='3082' Title='A' /></Titles>" +
            "<SubArea Id='s1' Entity='e'><Titles><Title LCID='1033' Title='X' /></Titles></SubArea>" +
            "<SubArea Id='s2' Entity='e'><Titles><Title LCID='3082' Title='Y' /></Titles></SubArea>" +
            "<SubArea Id='s3' Entity='e'><Titles><Title LCID='1033' Title='Z' /><Title LCID='3082' Title='Z' /></Titles></SubArea></Area></SiteMap>";

        var problems = SiteMapCatalogo.Violations(onlyEn);

        problems.Should().ContainSingle(p => p.StartsWith("s1:") && p.Contains("3082"));
        problems.Should().ContainSingle(p => p.StartsWith("s2:") && p.Contains("1033"));
        problems.Should().NotContain(p => p.StartsWith("s3:"));
    }

    [Fact]
    public void AppName_IsEnglishUnder1033AndSpanishUnder3082_WithoutTyposOrTechnicalPrefixes()
    {
        SiteMapCatalogo.AppName.En.Should().Be("Smelter — Import configuration");
        SiteMapCatalogo.AppName.Es.Should().Be("Smelter — Configuración de importaciones");
        SiteMapCatalogo.AppNameViolation(SiteMapCatalogo.AppName.En).Should().BeNull();
        SiteMapCatalogo.AppNameViolation(SiteMapCatalogo.AppName.Es).Should().BeNull();
        SiteMapCatalogo.AppNameViolation("MDA - SMLT - Import Configurationd and Monitoring").Should().NotBeNull();
        SiteMapCatalogo.AppNameViolation(null).Should().NotBeNull();
    }

    [Fact]
    public void Labels_TargetOnlyIdsThatExistInTheRealSiteMap()
    {
        var ids = XDocument.Parse(RealSiteMapXml()).Descendants().Select(e => (string?)e.Attribute("Id")).ToHashSet();

        SiteMapCatalogo.Labels.Select(l => l.Id).Should().OnlyContain(id => ids.Contains(id));
    }

    [Fact]
    public void Labels_HaveNonEmptyTextsInBothLanguages_AndTheEnglishDiffersFromTheLegacySpanishWhereTranslated()
    {
        foreach (var l in SiteMapCatalogo.Labels)
        {
            l.Title.En.Should().NotBeNullOrWhiteSpace(l.Id);
            l.Title.Es.Should().NotBeNullOrWhiteSpace(l.Id);
        }
        SiteMapCatalogo.Labels.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        SiteMapCatalogo.Labels.Single(l => l.Id == "group_f9abc8da").Title.En.Should().NotBe("Configuración");
    }
}
