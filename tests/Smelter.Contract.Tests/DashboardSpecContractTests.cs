using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Dashboard "Smelter Dashboard" como dashboard CLÁSICO (systemform type 0): el interaction-centric (type 10) que traía la
/// solución se renderiza en la MDA como el overview genérico "Dynamics 365 Social" y no aparece en el selector.
/// La spec se corrobora contra la metadata real de solutions/unpacked (charts, vista, sitemap, componentes de la app).
/// </summary>
public class DashboardSpecContractTests
{
    private static readonly string Unpacked = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked");

    private static XElement[] Controls() =>
        XElement.Parse(DashboardCatalogo.BuildFormXml()).Descendants("control").ToArray();

    [Fact]
    public void FormId_IsNew_NotTheInteractionCentricOne()
    {
        DashboardCatalogo.FormId.Should().NotBe(DashboardCatalogo.LegacyInteractionCentricId);
        DashboardCatalogo.LegacyInteractionCentricId.Should().Be(Guid.Parse("590df35c-1b64-f111-ab0c-0022480a5be8"));
    }

    [Fact]
    public void FormXml_IsAClassicDashboard_TwoCharts_AndOneGrid_WithUniqueIds()
    {
        var xml = XElement.Parse(DashboardCatalogo.BuildFormXml());
        xml.Name.LocalName.Should().Be("form");
        xml.Descendants("dashboard").Should().BeEmpty("Dev rechazó un hijo 'dashboard' dentro de form (run 30360739680)");
        xml.Descendants("forms").Should().BeEmpty("el formxml de un systemform es <form>, sin el envoltorio <forms> del export");

        var controls = Controls();
        controls.Should().HaveCount(3);
        controls.Select(c => (string)c.Attribute("classid")!).Distinct().Should().ContainSingle()
            .Which.Should().Be("{E7A81278-8635-4d9e-8D4D-59480B391C5B}");

        var charts = controls.Where(c => (string)c.Descendants("ChartGridMode").Single() == "Chart").ToArray();
        charts.Should().HaveCount(2);
        charts.Select(c => Guid.Parse(c.Descendants("VisualizationId").Single().Value))
            .Should().BeEquivalentTo(DashboardCatalogo.ChartIds);
        controls.Single(c => (string)c.Descendants("ChartGridMode").Single() == "Grid")
            .Descendants("ViewId").Single().Value.Should().BeEquivalentTo($"{{{DashboardCatalogo.ViewId:D}}}");

        var ids = xml.Descendants().Select(e => (string?)e.Attribute("id")).Where(i => i is not null).ToList();
        ids.Should().OnlyHaveUniqueItems();
        DashboardCatalogo.Violations(DashboardCatalogo.BuildFormXml()).Should().BeEmpty();
    }

    [Fact]
    public void Violations_FlagsAnInteractionCentricOrEmptyFormXml()
    {
        DashboardCatalogo.Violations("<form />").Should().NotBeEmpty();
        DashboardCatalogo.Violations("<forms type=\"interactioncentricdashboard\"><form /></forms>").Should().NotBeEmpty();
    }

    [Fact]
    public void ChartsAndView_ExistInTheSolution()
    {
        foreach (var chart in DashboardCatalogo.ChartIds)
            File.Exists(Path.Combine(Unpacked, "Entities", SecuritySpec.Tables.ImportJob, "Visualizations", $"{{{chart.ToString("D").ToUpperInvariant()}}}.xml"))
                .Should().BeTrue($"chart {chart}");
        File.ReadAllText(Path.Combine(Unpacked, "Entities", SecuritySpec.Tables.ImportJob, "SavedQueries", $"{{{DashboardCatalogo.ViewId:D}}}.xml"))
            .Should().Contain("savedqueryid");
    }

    [Fact]
    public void Name_Is_TheBilingualOne()
    {
        DashboardCatalogo.Name.En.Should().Be("Smelter Dashboard");
        DashboardCatalogo.Name.Es.Should().Be("Panel de Smelter");
    }

    [Fact]
    public void Url_RoundTripsThroughMdaXml()
    {
        MdaXml.DashboardId(DashboardCatalogo.Url).Should().Be(DashboardCatalogo.FormId);
    }

    [Fact]
    public void Unpacked_SolutionCarriesTheClassicDashboard_AndNotTheOldOne()
    {
        var id = DashboardCatalogo.FormId;
        File.Exists(Path.Combine(Unpacked, "Dashboards", $"{{{id:D}}}.xml")).Should().BeTrue();
        Directory.Exists(Path.Combine(Unpacked, "InteractionCentricDashboards")).Should().BeFalse();

        File.ReadAllText(Path.Combine(Unpacked, "Other", "Solution.xml")).ToLowerInvariant()
            .Should().Contain($"type=\"60\" id=\"{{{id:D}}}\"").And.NotContain("590df35c");

        var appModule = Directory.GetDirectories(Path.Combine(Unpacked, "AppModules")).Single();
        File.ReadAllText(Path.Combine(appModule, "AppModule.xml")).ToLowerInvariant()
            .Should().Contain($"<appmodulecomponent type=\"60\" id=\"{{{id:D}}}\" />");

        var siteMap = Directory.GetDirectories(Path.Combine(Unpacked, "AppModuleSiteMaps")).Single();
        File.ReadAllText(Path.Combine(siteMap, "AppModuleSiteMap.xml")).ToLowerInvariant()
            .Should().Contain(id.ToString("D")).And.NotContain("590df35c");
    }

    [Fact]
    public void SiteMapEntry_PointsAtTheDashboard_AndIsIdempotent()
    {
        const string xml = "<SiteMap><Area Id=\"a\"><Group Id=\"g\"><SubArea Id=\"subarea_smlt_dashboard\" Url=\"/main.aspx?pagetype=dashboard&amp;id=%7b590df35c-1b64-f111-ab0c-0022480a5be8%7d\" /></Group></Area></SiteMap>";
        var (updated, changed) = DashboardCatalogo.PointSiteMapAtDashboard(xml);
        changed.Should().BeTrue();
        updated.Should().Contain(DashboardCatalogo.FormId.ToString("D")).And.NotContain("590df35c");
        DashboardCatalogo.PointSiteMapAtDashboard(updated).Changed.Should().BeFalse();
    }
}
