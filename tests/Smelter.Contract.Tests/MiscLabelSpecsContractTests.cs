using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>CONTRATO de <see cref="SolutionNameCatalogo"/> y <see cref="ViewLabelsCatalogo"/>.</summary>
public class MiscLabelSpecsContractTests
{
    [Fact]
    public void SolutionFriendlyName_FollowsTheStudioConvention_AndTheUniqueNameIsUntouched()
    {
        SolutionNameCatalogo.FriendlyName.Should().Be("SMLT | Core | Smelter");
        SolutionNameCatalogo.UniqueName.Should().Be("smlt_core_smelter").And.Be(Catalogo.SolutionName);
    }

    [Fact]
    public void SolutionNameNeedsUpdate_OnlyWhenDifferent()
    {
        SolutionNameCatalogo.NeedsUpdate("smlt_core_smelter").Should().BeTrue();
        SolutionNameCatalogo.NeedsUpdate(null).Should().BeTrue();
        SolutionNameCatalogo.NeedsUpdate("SMLT | Core | Smelter").Should().BeFalse();
        SolutionNameCatalogo.NeedsUpdate("smlt | core | smelter").Should().BeTrue("la comparación es exacta");
    }

    [Fact]
    public void CustomViews_AreEnglishUnder1033_WithSpanishAndLegacyNames()
    {
        ViewLabelsCatalogo.Views.Should().HaveCount(3);
        ViewLabelsCatalogo.Views.Select(v => v.View).Should().BeEquivalentTo(SecuritySpec.Views);
        ViewLabelsCatalogo.Views.Select(v => v.Name.En).Should().Equal("Active jobs", "Completed with errors", "All jobs");
        ViewLabelsCatalogo.Views.Select(v => v.Name.Es).Should().Equal("Jobs activos", "Completados con errores", "Todos los jobs");
        ViewLabelsCatalogo.Views.Select(v => v.LegacyName).Should().Equal("Jobs Activos", "Completados con Errores", "Todos los Jobs");
        ViewLabelsCatalogo.Views.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.DescriptionEs) && v.Description.En != v.Description.Es);
    }

    [Fact]
    public void ViewNames_ThatUpsertWillLookUp_IncludeBothTheCurrentAndTheLegacyName()
    {
        ViewLabelsCatalogo.KnownNames(SecuritySpec.ActiveJobsView).Should().BeEquivalentTo("Active jobs", "Jobs Activos");
        ViewLabelsCatalogo.KnownNames(SecuritySpec.AllJobsView).Should().BeEquivalentTo("All jobs", "Todos los Jobs");
    }

    [Fact]
    public void CustomViews_AreStillThePinnedOnesOfTheMda()
    {
        // Los GUID de las vistas custom viven en MdaComponentsSpec; sus 3 nombres (descriptivos) no cambian de identidad.
        MdaComponentsCatalogo.All.Count(c => c.Kind == MdaComponentKind.CustomView).Should().Be(ViewLabelsCatalogo.Views.Length);
    }

    [Fact]
    public void Dashboard_Keeps1033Name_AndGainsSpanish()
    {
        ViewLabelsCatalogo.Dashboard.En.Should().Be(SecuritySpec.DashboardName).And.Be("Smelter Dashboard");
        ViewLabelsCatalogo.Dashboard.Es.Should().Be("Panel de Smelter");
    }

    [Fact]
    public void ViewPublishXml_ListsTheEntity_AndTheDashboardOnlyWhenTouched()
    {
        ViewLabelsCatalogo.PublishXml(null).Should().Be(
            "<importexportxml><entities><entity>sanic_tbl_smlt_importjob</entity></entities></importexportxml>");
        ViewLabelsCatalogo.PublishXml(Guid.Parse("11111111-2222-3333-4444-555555555555")).Should()
            .Contain("<dashboards><dashboard>{11111111-2222-3333-4444-555555555555}</dashboard></dashboards>");
    }
}
