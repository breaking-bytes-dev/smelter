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
    public void ReadText_ImportJobActionsJs_IsEmbeddedAndReferencesBothApis()
    {
        var js = AssetLoader.ReadText("sanic_smlt_importjob_actions.js");

        js.Should().Contain("sanic_capi_smelter_prepareimport");
        js.Should().Contain("sanic_capi_smelter_processchunk");
        js.Should().Contain("Sanic.Smelter.executeImportJob");
        js.Should().Contain("Sanic.Smelter.retryImportJob");
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
