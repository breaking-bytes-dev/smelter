using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Fase 4.2/4.3: los assets de UX (RibbonDiffXml + JS de Ejecutar/Reintentar) están versionados en
/// tools/Smelter.Deploy/Assets/ y se embeben en el assembly — sin llamar Dataverse, esto solo
/// prueba que el csproj los embebió correctamente y que su contenido tiene la forma esperada.
/// </summary>
public class AssetLoaderTests
{
    [Fact]
    public void ReadText_ImportJobRibbonDiffXml_IsEmbeddedAndWellFormed()
    {
        var xml = AssetLoader.ReadText("sanic_tbl_smlt_importjob.RibbonDiffXml.xml");

        xml.Should().Contain("<RibbonDiffXml");
        xml.Should().Contain("sanic.smlt.EjecutarImportJob.Command");
        xml.Should().Contain("sanic.smlt.ReintentarImportJob.Command");
        xml.Should().Contain("sanic_smlt_importjob_actions.js");
    }

    [Fact]
    public void ReadText_ImportJobActionsJs_OnlyQueues_NoBrowserLoop()
    {
        var js = AssetLoader.ReadText("sanic_smlt_importjob_actions.js");

        js.Should().Contain("Sanic.Smelter.executeImportJob");
        js.Should().Contain("Sanic.Smelter.retryImportJob");
        js.Should().Contain("Sanic.Smelter.canExecuteImportJob");
        js.Should().Contain("Sanic.Smelter.canRetryImportJob");
        js.ToLowerInvariant().Should().NotContain("processchunk");
        js.ToLowerInvariant().Should().NotContain("prepareimport");
    }

    [Fact]
    public void ImportJobRibbon_EnableRules_GateButtonsByStatus()
    {
        var doc = XDocument.Parse(AssetLoader.ReadText("sanic_tbl_smlt_importjob.RibbonDiffXml.xml"));
        var rules = doc.Descendants("RuleDefinitions").Descendants("EnableRule")
            .ToDictionary(e => (string)e.Attribute("Id")!);

        rules["sanic.sanic_tbl_smlt_importjob.FormStateEnableRule.EnableRule"].Element("FormStateRule")!
            .Attribute("State")!.Value.Should().Be("Create");
        rules["sanic.sanic_tbl_smlt_importjob.FormStateEnableRule.EnableRule"].Element("FormStateRule")!
            .Attribute("InvertResult")!.Value.Should().Be("true");
        rules["sanic.smlt.EjecutarImportJob.EnableRule"].Element("CustomRule")!
            .Attribute("FunctionName")!.Value.Should().Be("Sanic.Smelter.canExecuteImportJob");
        rules["sanic.smlt.ReintentarImportJob.EnableRule"].Element("CustomRule")!
            .Attribute("FunctionName")!.Value.Should().Be("Sanic.Smelter.canRetryImportJob");

        // Cada comando referencia el gate de form state + su regla de estado.
        foreach (var (command, rule) in new[]
        {
            ("sanic.smlt.EjecutarImportJob.Command", "sanic.smlt.EjecutarImportJob.EnableRule"),
            ("sanic.smlt.ReintentarImportJob.Command", "sanic.smlt.ReintentarImportJob.EnableRule"),
        })
        {
            var ids = doc.Descendants("CommandDefinition").Single(c => (string)c.Attribute("Id")! == command)
                .Descendants("EnableRule").Select(e => (string)e.Attribute("Id")!).ToList();
            ids.Should().Contain(new[] { "sanic.sanic_tbl_smlt_importjob.FormStateEnableRule.EnableRule", rule });
        }
    }

    [Fact]
    public void ImportJobActionsJs_CanExecute_And_CanRetry_MatchStatusSemantics()
    {
        var js = AssetLoader.ReadText("sanic_smlt_importjob_actions.js");

        // Ejecutar: status vacío (null). Reintentar: Completed/CompletedWithErrors/Failed.
        js.Should().Contain("=== null");
        js.Should().Contain("s === St.Completed || s === St.CompletedWithErrors || s === St.Failed");
    }

    [Fact]
    public void ImportJobRibbonDiffXml_IsWellFormedXml()
    {
        var xml = AssetLoader.ReadText("sanic_tbl_smlt_importjob.RibbonDiffXml.xml");

        var doc = XDocument.Parse(xml); // no debe tirar XmlException
        doc.Root!.Name.LocalName.Should().Be("RibbonDiffXml");
    }

    [Fact]
    public void ReadText_UnknownAsset_ThrowsWithActionableMessage()
    {
        var act = () => AssetLoader.ReadText("does-not-exist.xml");

        act.Should().Throw<InvalidOperationException>().WithMessage("*does-not-exist.xml*");
    }
}
