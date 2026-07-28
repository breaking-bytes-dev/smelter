using System.IO.Compression;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Fase 1 (spike ImportSolutionRequest): valida que <see cref="SolutionZipBuilder"/> arma un
/// solution.zip mínimo bien formado (solution.xml + customizations.xml, sin llamar Dataverse).
/// La confirmación de que Dataverse lo IMPORTA sin error queda para el primer backend-deploy en
/// CI (ver comentario de gotcha en SolutionZipBuilder.cs) — esto solo prueba la forma del paquete.
/// </summary>
public class SolutionZipBuilderTests
{
    [Fact]
    public void BuildRibbonSolution_ProducesZipWithExactlySolutionAndCustomizationsEntries()
    {
        var zip = SolutionZipBuilder.BuildRibbonSolution(
            "smlt_core_smelter", "Sistemas_Abiertos_Nicaragua", "sanic", "1.0.0.1",
            new Dictionary<string, string> { ["sanic_tbl_smlt_importjob"] = "<CustomActions />" });

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        // Dev exige los tres en la raíz: "The compressed file must contain the following
        // files at its root: solution.xml, customizations.xml, and [Content_Types].xml"
        archive.Entries.Select(e => e.FullName)
            .Should().BeEquivalentTo("solution.xml", "customizations.xml", "[Content_Types].xml");

        using var reader = new StreamReader(
            archive.GetEntry("[Content_Types].xml")!.Open());
        reader.ReadToEnd().Should().Contain("Extension=\"xml\"")
            .And.Contain("schemas.openxmlformats.org/package/2006/content-types");
    }

    [Fact]
    public void BuildRibbonSolution_SolutionXml_ContainsUniqueNameVersionAndRootComponentPerEntity()
    {
        var zip = SolutionZipBuilder.BuildRibbonSolution(
            "smlt_core_smelter", "Sistemas_Abiertos_Nicaragua", "sanic", "1.0.0.7",
            new Dictionary<string, string>
            {
                ["sanic_tbl_smlt_importjob"] = "<CustomActions />",
                ["sanic_tbl_smlt_importtemplate"] = "<CustomActions />",
            });

        var solutionXml = ReadEntry(zip, "solution.xml");

        solutionXml.Should().Contain("<UniqueName>smlt_core_smelter</UniqueName>");
        solutionXml.Should().Contain("<Version>1.0.0.7</Version>");
        solutionXml.Should().Contain("<RootComponent type=\"1\" schemaName=\"sanic_tbl_smlt_importjob\" behavior=\"0\" />");
        solutionXml.Should().Contain("<RootComponent type=\"1\" schemaName=\"sanic_tbl_smlt_importtemplate\" behavior=\"0\" />");
    }

    [Fact]
    public void BuildRibbonSolution_CustomizationsXml_EmbedsRibbonDiffXmlUnderMatchingEntity()
    {
        const string ribbon = "<RibbonDiffXml><CustomActions><CustomAction Id=\"x\" /></CustomActions></RibbonDiffXml>";
        var zip = SolutionZipBuilder.BuildRibbonSolution(
            "smlt_core_smelter", "Sistemas_Abiertos_Nicaragua", "sanic", "1.0.0.1",
            new Dictionary<string, string> { ["sanic_tbl_smlt_importjob"] = ribbon });

        var customizationsXml = ReadEntry(zip, "customizations.xml");

        customizationsXml.Should().Contain("<Name>sanic_tbl_smlt_importjob</Name>");
        customizationsXml.Should().Contain(ribbon);
    }

    [Fact]
    public void BuildRibbonSolution_StripsXmlDeclarationFromEmbeddedRibbonDiff()
    {
        // El asset versionado arranca con <?xml ...?>; embebido crudo dentro de
        // customizations.xml, Dev rechaza el import: "Unexpected XML declaration"
        // (verificado en backend-deploy run 30360173757).
        var zip = SolutionZipBuilder.BuildRibbonSolution(
            "smlt_core_smelter", "Sistemas_Abiertos_Nicaragua", "sanic", "1.0.0.1",
            new Dictionary<string, string>
            {
                ["sanic_tbl_smlt_importjob"] =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- comentario -->\n<RibbonDiffXml><CustomActions /></RibbonDiffXml>",
            });

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("customizations.xml")!.Open());
        var customizations = reader.ReadToEnd();

        customizations.LastIndexOf("<?xml", StringComparison.Ordinal).Should().Be(0,
            because: "la única declaración XML permitida es la del propio customizations.xml, como primer nodo");
        customizations.Should().Contain("<RibbonDiffXml>");
    }

    [Fact]
    public void BuildRibbonSolution_WithNoEntities_Throws()
    {
        var act = () => SolutionZipBuilder.BuildRibbonSolution(
            "smlt_core_smelter", "Sistemas_Abiertos_Nicaragua", "sanic", "1.0.0.1",
            new Dictionary<string, string>());

        act.Should().Throw<ArgumentException>();
    }

    private static string ReadEntry(byte[] zipBytes, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return reader.ReadToEnd();
    }
}
