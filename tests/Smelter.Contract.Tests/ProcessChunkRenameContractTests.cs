using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO del rename de processchunk — estado Release N+1 (Fase 9, smelter-mda-ux Requirement
/// "Rename de processchunk sin ventana rota"): la entrada legacy <c>anic_</c> ya NO existe en
/// <see cref="Catalogo.Apis"/> (el deploy la borra de Dev con un DeleteRequest explícito) y la
/// entrada correcta <c>sanic_</c> queda como única processchunk, con la forma y el privilegio que
/// el Cloud Flow ya migrado espera. El estado transicional de Release N (ambas entradas conviviendo)
/// quedó documentado en el historial de este archivo y se cerró tras backend-verify verde
/// (run 30364284364).
/// </summary>
public class ProcessChunkRenameContractTests
{
    private const string LegacyUniqueName = "anic_capi_smelter_processchunk";
    private const string NewUniqueName = "sanic_capi_smelter_processchunk";

    [Fact]
    public void LegacyProcessChunkApi_IsGoneFromCatalog()
    {
        Catalogo.Apis.Should().NotContain(a => a.UniqueName == LegacyUniqueName,
            "Fase 9 (Release N+1): el Cloud Flow ya migró al uniquename nuevo con verify verde — " +
            "la entrada legacy no debe re-upsertearse nunca más");
    }

    [Fact]
    public void NewProcessChunkApi_IsTheOnlyProcessChunkEntry()
    {
        var processChunkApis = Catalogo.Apis
            .Where(a => a.PluginTypeName == "Smelter.Plugins.ProcessChunkPlugin")
            .ToArray();

        processChunkApis.Should().ContainSingle()
            .Which.UniqueName.Should().Be(NewUniqueName);
    }

    [Fact]
    public void NewProcessChunkApi_KeepsShapeAndPrivilegeTheFlowDependsOn()
    {
        var api = Catalogo.Apis.Single(a => a.UniqueName == NewUniqueName);

        api.RequestParams.Select(p => p.Name).Should().BeEquivalentTo("JobId", "ChunkSize");
        api.ResponseProps.Select(p => p.Name).Should().BeEquivalentTo("HasMore", "ProcessedSoFar");
        api.ExecutePrivilegeName.Should().Be(
            SecuritySpec.PrivilegeName(PrivilegeType.Create, SecuritySpec.Tables.ImportJob));
    }
}
