using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO del rename aditivo de processchunk (Release N, smelter-mda-ux Requirement "Rename de
/// processchunk sin ventana rota"): durante la transición ambas entradas (anic_/sanic_) MUST existir
/// en <see cref="Catalogo.Apis"/> y apuntar al MISMO <c>PluginTypeName</c>, con idéntica forma de
/// request/response y el mismo <c>ExecutePrivilegeName</c> — si alguna diverge, el Cloud Flow legacy
/// o el nuevo quedarían invocando un plugin distinto o con params rotos. Fase 9 (gateada, tras
/// confirmar Smelter.Verify en verde) remueve la entrada <c>anic_</c>.
/// </summary>
public class ProcessChunkRenameContractTests
{
    private const string OldUniqueName = "anic_capi_smelter_processchunk";
    private const string NewUniqueName = "sanic_capi_smelter_processchunk";

    [Fact]
    public void BothOldAndNewProcessChunkApis_ExistDuringTransition()
    {
        Catalogo.Apis.Should().Contain(a => a.UniqueName == OldUniqueName,
            "el flow legacy todavía invoca el uniquename viejo hasta que el Cloud Flow se actualice");
        Catalogo.Apis.Should().Contain(a => a.UniqueName == NewUniqueName,
            "el rename es ADITIVO en Release N — la entrada nueva debe existir antes de tocar el flow");
    }

    [Fact]
    public void BothOldAndNewProcessChunkApis_PointToTheSamePluginType()
    {
        var oldApi = Catalogo.Apis.Single(a => a.UniqueName == OldUniqueName);
        var newApi = Catalogo.Apis.Single(a => a.UniqueName == NewUniqueName);

        newApi.PluginTypeName.Should().Be(oldApi.PluginTypeName,
            "ambas entradas deben ejecutar el mismo backing plugin durante la transición");
    }

    [Fact]
    public void BothOldAndNewProcessChunkApis_HaveIdenticalRequestResponseShapeAndPrivilege()
    {
        var oldApi = Catalogo.Apis.Single(a => a.UniqueName == OldUniqueName);
        var newApi = Catalogo.Apis.Single(a => a.UniqueName == NewUniqueName);

        newApi.RequestParams.Should().BeEquivalentTo(oldApi.RequestParams);
        newApi.ResponseProps.Should().BeEquivalentTo(oldApi.ResponseProps);
        newApi.ExecutePrivilegeName.Should().Be(oldApi.ExecutePrivilegeName);
    }
}
