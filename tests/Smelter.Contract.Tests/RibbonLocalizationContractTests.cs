using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="RibbonCatalogo"/>: los dos RibbonDiffXml versionados (Import Job e Import Template) tienen cada
/// LocLabel con UN título 1033 y UN título 3082 no vacíos (en un entorno de idioma base español, un LocLabel sin 3082
/// se renderiza como "LabelText"), y cada botón custom lleva ModernImage apuntando a un ícono declarado en IconsSpec.
/// Los CommandDefinitions/reglas/JS no se tocan.
/// </summary>
public class RibbonLocalizationContractTests
{
    private static XDocument Load(RibbonAssetSpec asset) => XDocument.Parse(AssetLoader.ReadText(asset.FileName));

    [Fact]
    public void Catalogo_CoversImportJobAndImportTemplate_WithFourButtons()
    {
        RibbonCatalogo.Assets.Select(a => a.Table).Should().BeEquivalentTo(SecuritySpec.Tables.ImportJob, SecuritySpec.Tables.ImportTemplate);
        RibbonCatalogo.Assets.SelectMany(a => a.Buttons).Select(b => b.ButtonId).Should().BeEquivalentTo(
            "sanic.smlt.EjecutarImportJob.Button", "sanic.smlt.ReintentarImportJob.Button",
            "sanic.smlt.ExportTemplate.Button", "sanic.smlt.ImportTemplate.Button");
        RibbonCatalogo.RequiredLanguages.Should().BeEquivalentTo(new[] { 1033, 3082 });
        RibbonCatalogo.XmlByEntity().Keys.Should().BeEquivalentTo(SecuritySpec.Tables.ImportJob, SecuritySpec.Tables.ImportTemplate);
    }

    [Fact]
    public void EveryLocLabel_HasExactlyOne1033AndOne3082Title_NonEmpty()
    {
        foreach (var asset in RibbonCatalogo.Assets)
        {
            var doc = Load(asset);
            doc.Descendants("LocLabel").Should().NotBeEmpty(asset.FileName);
            foreach (var label in doc.Descendants("LocLabel"))
            {
                var titles = label.Descendants("Title").ToList();
                foreach (var lang in new[] { 1033, 3082 })
                {
                    var forLang = titles.Where(t => (string?)t.Attribute("languagecode") == lang.ToString()).ToList();
                    forLang.Should().HaveCount(1, $"{asset.FileName}/{(string?)label.Attribute("Id")} debe tener un único título {lang}");
                    ((string?)forLang[0].Attribute("description")).Should().NotBeNullOrWhiteSpace($"{(string?)label.Attribute("Id")}/{lang}");
                }
            }
            RibbonCatalogo.LocLabelProblems(doc).Should().BeEmpty(asset.FileName);
        }
    }

    [Fact]
    public void EveryCustomButton_HasModernImage_PointingAtAnIconOfTheSpec()
    {
        foreach (var asset in RibbonCatalogo.Assets)
        {
            var doc = Load(asset);
            var buttons = doc.Descendants("CustomAction").Descendants("Button").ToList();
            buttons.Select(b => (string?)b.Attribute("Id")).Should().BeEquivalentTo(asset.Buttons.Select(b => b.ButtonId), asset.FileName);
            foreach (var button in buttons)
            {
                var image = (string?)button.Attribute("ModernImage");
                image.Should().NotBeNullOrWhiteSpace($"{(string?)button.Attribute("Id")} sin ModernImage");
                IconsCatalogo.All.Select(i => IconsCatalogo.VectorIconRef(i.WebResourceName)).Should().Contain(image!);
            }
            RibbonCatalogo.ButtonProblems(doc, asset).Should().BeEmpty(asset.FileName);
        }
    }

    [Fact]
    public void EveryButtonLabelReference_ResolvesToALocLabel()
    {
        foreach (var asset in RibbonCatalogo.Assets)
        {
            var doc = Load(asset);
            var ids = doc.Descendants("LocLabel").Select(l => (string)l.Attribute("Id")!).ToHashSet();
            foreach (var button in doc.Descendants("CustomAction").Descendants("Button"))
                foreach (var attr in new[] { "LabelText", "ToolTipTitle", "ToolTipDescription" })
                {
                    var v = (string?)button.Attribute(attr);
                    v.Should().StartWith("$LocLabels:", $"{(string?)button.Attribute("Id")}/{attr}");
                    ids.Should().Contain(v!["$LocLabels:".Length..]);
                }
        }
    }

    [Fact]
    public void ButtonIcons_AreTheFourNewSvgs()
    {
        RibbonCatalogo.Assets.SelectMany(a => a.Buttons).Select(b => b.Icon).Should().BeEquivalentTo(
            new[] { IconsCatalogo.Run, IconsCatalogo.Retry, IconsCatalogo.Export, IconsCatalogo.Import });
        IconsCatalogo.All.Should().Contain([IconsCatalogo.Run, IconsCatalogo.Retry, IconsCatalogo.Export, IconsCatalogo.Import]);
    }

    [Fact]
    public void ExpectedTexts_AreBilingual()
    {
        var all = RibbonCatalogo.Assets.SelectMany(a => Load(a).Descendants("LocLabel"))
            .ToDictionary(l => (string)l.Attribute("Id")!, l => l.Descendants("Title").ToDictionary(t => (string)t.Attribute("languagecode")!, t => (string)t.Attribute("description")!));

        all["sanic.smlt.ExportTemplate.Button.LabelText"].Should().Contain(new KeyValuePair<string, string>("1033", "Export template")).And.Contain(new KeyValuePair<string, string>("3082", "Exportar template"));
        all["sanic.smlt.ImportTemplate.Button.LabelText"].Should().Contain(new KeyValuePair<string, string>("1033", "Import template")).And.Contain(new KeyValuePair<string, string>("3082", "Importar template"));
        all["sanic.smlt.EjecutarImportJob.Button.LabelText"].Should().Contain(new KeyValuePair<string, string>("1033", "Run")).And.Contain(new KeyValuePair<string, string>("3082", "Ejecutar"));
        all["sanic.smlt.ReintentarImportJob.Button.LabelText"].Should().Contain(new KeyValuePair<string, string>("1033", "Retry")).And.Contain(new KeyValuePair<string, string>("3082", "Reintentar"));
        all["sanic.smlt.ExportTemplate.Button.ToolTipDescription"]["3082"].Should().Be("Descarga el template seleccionado como JSON.");
    }

    [Fact]
    public void ImportTemplateRibbon_KeepsItsCommandsRulesAndJs()
    {
        var doc = XDocument.Parse(AssetLoader.ReadText("sanic_tbl_smlt_importtemplate.RibbonDiffXml.xml"));
        var fns = doc.Descendants("JavaScriptFunction").ToDictionary(f => (string)f.Attribute("FunctionName")!, f => (string)f.Attribute("Library")!);

        fns["Sanic.Smelter.exportTemplate"].Should().Be("$webresource:sanic_smlt_exporttemplate.js");
        fns["Sanic.Smelter.importTemplate"].Should().Be("$webresource:sanic_smlt_importtemplate.js");
        doc.Descendants("SelectionCountRule").Single().Attribute("Minimum")!.Value.Should().Be("1");
        doc.Descendants("CustomAction").Select(c => (string)c.Attribute("Location")!).Should().OnlyContain(
            l => l == "Mscrm.HomepageGrid.sanic_tbl_smlt_importtemplate.MainTab.Management.Controls._children");
    }

    [Fact]
    public void Validator_DetectsMissingSpanishTitleDuplicatesAndMissingImage()
    {
        var bad = XDocument.Parse("""
            <RibbonDiffXml><CustomActions><CustomAction Id="a" Location="l"><CommandUIDefinition>
              <Button Id="b1" Command="c" LabelText="$LocLabels:x" />
            </CommandUIDefinition></CustomAction></CustomActions>
            <LocLabels>
              <LocLabel Id="x"><Titles><Title description="Only EN" languagecode="1033" /></Titles></LocLabel>
              <LocLabel Id="y"><Titles><Title description="a" languagecode="1033" /><Title description="b" languagecode="1033" /><Title description="" languagecode="3082" /></Titles></LocLabel>
            </LocLabels></RibbonDiffXml>
            """);

        RibbonCatalogo.LocLabelProblems(bad).Should().HaveCount(3);
        RibbonCatalogo.LocLabelProblems(bad, ["x"]).Should().ContainSingle().Which.Should().Contain("3082");
        RibbonCatalogo.ButtonProblems(bad, new RibbonAssetSpec("t", "f", [new RibbonButtonSpec("b1", IconsCatalogo.Run)]))
            .Should().ContainSingle().Which.Should().Contain("ModernImage");
        RibbonCatalogo.ButtonProblems(bad, new RibbonAssetSpec("t", "f", [new RibbonButtonSpec("nope", IconsCatalogo.Run)]))
            .Should().ContainSingle().Which.Should().Contain("nope");
    }
}
