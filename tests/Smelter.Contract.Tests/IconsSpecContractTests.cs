using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="IconsCatalogo"/>: cada ícono es un SVG real (parsea como XML, viewBox 0 0 24 24, color único) que
/// existe en disco Y embebido en el assembly, con nombre de web resource válido; cada tabla de Smelter y cada SubArea
/// del sitemap tiene su ícono.
/// </summary>
public class IconsSpecContractTests
{
    private static string IconsDir => Path.Combine(SolutionMetadata.RepoRoot, "tools", "Smelter.Deploy", "Assets", "icons");

    [Fact]
    public void EveryIcon_HasAnSvgFileOnDisk_ThatIsAlsoEmbeddedInTheAssembly()
    {
        foreach (var icon in IconsCatalogo.All)
        {
            var path = Path.Combine(IconsDir, icon.FileName);
            File.Exists(path).Should().BeTrue($"falta {path}");
            IconsCatalogo.ReadSvg(icon).Should().Be(File.ReadAllText(path), $"{icon.FileName}: el recurso embebido coincide con el archivo");
        }
    }

    [Fact]
    public void NoSvgFileIsOrphan_EveryFileOfTheFolderIsInTheSpec()
    {
        Directory.GetFiles(IconsDir, "*.svg").Select(f => Path.GetFileName(f))
            .Should().BeEquivalentTo(IconsCatalogo.All.Select(i => i.FileName));
    }

    [Fact]
    public void EverySvg_ParsesAsXml_With24x24ViewBox_AndASingleColour()
    {
        foreach (var icon in IconsCatalogo.All)
        {
            var root = XDocument.Parse(IconsCatalogo.ReadSvg(icon)).Root!;

            root.Name.LocalName.Should().Be("svg", icon.FileName);
            root.Name.NamespaceName.Should().Be("http://www.w3.org/2000/svg", icon.FileName);
            ((string?)root.Attribute("viewBox")).Should().Be("0 0 24 24", icon.FileName);
            ((string?)root.Attribute("width")).Should().Be("24");
            ((string?)root.Attribute("height")).Should().Be("24");
            root.Descendants().Should().NotBeEmpty(icon.FileName);

            var colours = root.DescendantsAndSelf().SelectMany(e => e.Attributes()).Where(a => a.Name.LocalName is "fill" or "stroke")
                .Select(a => a.Value.ToUpperInvariant()).Where(v => v != "NONE").Distinct().ToList();
            colours.Should().Contain(IconsCatalogo.Color.ToUpperInvariant(), icon.FileName);
            colours.Should().OnlyContain(c => c == IconsCatalogo.Color.ToUpperInvariant() || c == "#FFFFFF",
                $"{icon.FileName}: un solo color (más el blanco del glifo sobre el tile de la app)");
        }
    }

    [Fact]
    public void SvgFiles_HaveNoByteOrderMark_AndNoScripts()
    {
        foreach (var icon in IconsCatalogo.All)
        {
            var bytes = File.ReadAllBytes(Path.Combine(IconsDir, icon.FileName));
            bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, icon.FileName);
            IconsCatalogo.ReadSvg(icon).ToLowerInvariant().Should().NotContain("<script").And.NotContain("onload");
        }
    }

    [Fact]
    public void WebResourceNames_AreUnique_PrefixedAndEndInSvg()
    {
        IconsCatalogo.All.Select(i => i.WebResourceName).Should().OnlyHaveUniqueItems();
        IconsCatalogo.All.Should().OnlyContain(i => i.WebResourceName.StartsWith("sanic_/smelter/icons/") && i.WebResourceName.EndsWith(".svg"));
        // Reglas de nombre de web resource: solo letras, números, '_', '.', '/', '-'; empieza con el prefijo del publisher.
        IconsCatalogo.All.Should().OnlyContain(i => System.Text.RegularExpressions.Regex.IsMatch(i.WebResourceName, @"^[a-z0-9_]+_/[A-Za-z0-9_./-]+$"));
        IconsCatalogo.All.Select(i => i.FileName).Should().OnlyHaveUniqueItems();
        IconsCatalogo.SvgWebResourceType.Should().Be(11);
        IconsCatalogo.WebResourceComponentType.Should().Be(61);
    }

    [Fact]
    public void EverySmelterTable_HasItsIcon_AndTheHmacKeyTableToo()
    {
        var tables = IconsCatalogo.TableIcons.Select(t => t.Table).ToList();

        tables.Should().Contain(SecuritySpec.Tables.All);
        tables.Should().Contain(HmacKeyTableSpec.Table);
        tables.Should().OnlyHaveUniqueItems();
        IconsCatalogo.TableIcons.Single(t => t.Table == SecuritySpec.Tables.ImportTemplate).Icon.Should().BeSameAs(IconsCatalogo.Template);
        IconsCatalogo.TableIcons.Single(t => t.Table == SecuritySpec.Tables.ImportJob).Icon.Should().BeSameAs(IconsCatalogo.Job);
        IconsCatalogo.TableIcons.Single(t => t.Table == SecuritySpec.Tables.ImportJobLog).Icon.Should().BeSameAs(IconsCatalogo.Log);
        IconsCatalogo.TableIcons.Single(t => t.Table == HmacKeyTableSpec.Table).Icon.Should().BeSameAs(IconsCatalogo.HmacKey);
    }

    [Fact]
    public void SubAreaIcons_TargetRealSubAreas_OfTheSiteMapSpec()
    {
        IconsCatalogo.SubAreaIcons.Select(i => i.SubAreaId).Should().OnlyContain(id => SiteMapCatalogo.Labels.Any(l => l.Id == id));
        IconsCatalogo.SubAreaIcons.Select(i => i.SubAreaId).Should().BeEquivalentTo(
            "subarea_a5465f8e", "subarea_6569ce1a", "subarea_smlt_dashboard", "subarea_a2da1554");
        IconsCatalogo.SubAreaIcons.Single(i => i.SubAreaId == "subarea_smlt_dashboard").Icon.Should().BeSameAs(IconsCatalogo.Dashboard);
    }

    [Fact]
    public void RibbonButtonIcons_ExportImportRunRetry_AreInTheSpec()
    {
        IconsCatalogo.All.Select(i => i.FileName).Should().Contain(["export.svg", "import.svg", "run.svg", "retry.svg"]);
        IconsCatalogo.All.Should().HaveCount(13);
        IconsCatalogo.Export.WebResourceName.Should().Be("sanic_/smelter/icons/export.svg");
        IconsCatalogo.Retry.WebResourceName.Should().Be("sanic_/smelter/icons/retry.svg");
    }

    [Fact]
    public void VectorIconRef_UsesTheWebResourceSyntax()
    {
        IconsCatalogo.VectorIconRef(IconsCatalogo.Template.WebResourceName).Should().Be("$webresource:sanic_/smelter/icons/template.svg");
    }

    [Fact]
    public void ToBase64_RoundTripsTheSvg_AndNeedsContentUpdateComparesIt()
    {
        var svg = IconsCatalogo.ReadSvg(IconsCatalogo.Job);
        var b64 = IconsCatalogo.ToBase64(svg);

        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Should().Be(svg);
        IconsCatalogo.NeedsContentUpdate(b64, b64).Should().BeFalse();
        IconsCatalogo.NeedsContentUpdate(null, b64).Should().BeTrue();
        IconsCatalogo.NeedsContentUpdate("", b64).Should().BeTrue();
        IconsCatalogo.NeedsContentUpdate(IconsCatalogo.ToBase64(svg + " "), b64).Should().BeTrue();
    }

    [Fact]
    public void PublishXml_ListsOnlyWhatWasTouched()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        IconsCatalogo.PublishXml([id], ["t1", "t2"], "app-id").Should().Be(
            "<importexportxml><webresources><webresource>{11111111-2222-3333-4444-555555555555}</webresource></webresources>" +
            "<entities><entity>t1</entity><entity>t2</entity></entities><appmodules><appmodule>app-id</appmodule></appmodules></importexportxml>");
        IconsCatalogo.PublishXml([], [], null).Should().Be("<importexportxml></importexportxml>");
    }

    [Fact]
    public void Sitemap_AppliedWithIcons_ReferencesOnlyWebResourcesOfTheSpec()
    {
        var dir = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "AppModuleSiteMaps");
        var file = Directory.GetFiles(dir, "AppModuleSiteMap.xml", SearchOption.AllDirectories).Single();

        var (xml, _) = SiteMapCatalogo.Apply(XDocument.Load(file).Descendants("SiteMap").Single().ToString());

        var refs = XDocument.Parse(xml).Descendants("SubArea").Select(s => (string?)s.Attribute("VectorIcon")).Where(v => v is not null);
        refs.Should().OnlyContain(r => IconsCatalogo.All.Any(i => IconsCatalogo.VectorIconRef(i.WebResourceName) == r));
    }
}
