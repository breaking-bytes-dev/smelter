using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// TDD (bug fix, judgment-day CRITICAL): <c>ClientDataProcessChunkRenamer</c> — la lógica PURA que
/// <c>CloudFlowUpdater</c> usa para renombrar la referencia a processchunk dentro del <c>clientdata</c>
/// de un Cloud Flow. Extraída para poder testearla sin Dataverse (mismo principio que el resto del
/// batch: lógica separada de I/O).
///
/// El bug original: <c>"sanic_capi_smelter_processchunk"</c> CONTIENE
/// <c>"anic_capi_smelter_processchunk"</c> como substring (a partir del índice 1 — "s" + "anic_...").
/// Un <c>string.Replace</c> ingenuo del uniquename viejo, corrido una SEGUNDA vez sobre clientdata ya
/// migrado, encuentra ese substring adentro del nombre correcto y le antepone otra "s":
/// <c>"ssanic_capi_smelter_processchunk"</c> — corrompe el flow en el segundo deploy (no es
/// hipotético: <c>CloudFlowUpdater</c> corre en CADA `backend-deploy`, no solo una vez).
/// </summary>
public class ClientDataProcessChunkRenamerTests
{
    private const string OldName = "anic_capi_smelter_processchunk";
    private const string NewName = "sanic_capi_smelter_processchunk";

    [Fact]
    public void Rename_ClientDataWithOldApi_RenamesToNewAndReportsChanged()
    {
        var clientData = "{\"action\":{\"url\":\"https://.../customapi/" + OldName + "\"}}";

        var (result, changed) = ClientDataProcessChunkRenamer.Rename(clientData);

        changed.Should().BeTrue();
        result.Should().Contain(NewName);
        // OJO: NO se puede assertar `result.Should().NotContain(OldName)` acá — "sanic_..." CONTIENE
        // "anic_..." como substring, así que ese assert sería trivialmente falso incluso con el fix
        // correcto. El check real es por TOKEN exacto (el mismo mecanismo que arregla el bug).
        ClientDataProcessChunkRenamer.ReferencesOldToken(result).Should().BeFalse();
    }

    [Fact]
    public void Rename_ClientDataAlreadyWithNewApi_IsNoOp_RegressionForTheDoubleSBug()
    {
        // Este es EXACTAMENTE el caso que rompía: clientdata ya migrado en un deploy anterior,
        // CloudFlowUpdater corre de nuevo (todo deploy re-corre SecurityProvisioner + esto).
        var clientData = "{\"action\":{\"url\":\"https://.../customapi/" + NewName + "\"}}";

        var (result, changed) = ClientDataProcessChunkRenamer.Rename(clientData);

        changed.Should().BeFalse();
        result.Should().Be(clientData);
        result.Should().NotContain("ssanic_capi_smelter_processchunk");
    }

    [Fact]
    public void Rename_CorruptedDoubleSClientData_RepairsBackToSingleS()
    {
        var corrupted = "{\"action\":{\"url\":\"https://.../customapi/s" + NewName + "\"}}";

        var (result, changed) = ClientDataProcessChunkRenamer.Rename(corrupted);

        changed.Should().BeTrue();
        result.Should().Contain(NewName);
        result.Should().NotContain("ssanic_capi_smelter_processchunk");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"action\":{\"url\":\"https://.../customapi/some_other_action\"}}")]
    [InlineData(null)]
    public void Rename_NoReference_IsNoOpWithoutThrowing(string? clientData)
    {
        var act = () => ClientDataProcessChunkRenamer.Rename(clientData);

        act.Should().NotThrow();
        var (result, changed) = act();
        changed.Should().BeFalse();
        result.Should().Be(clientData ?? "");
    }

    [Fact]
    public void ReferencesOldToken_OnlyMatchesStandaloneOldName_NotAsSubstringOfNewName()
    {
        ClientDataProcessChunkRenamer.ReferencesOldToken($"/{OldName}").Should().BeTrue();
        ClientDataProcessChunkRenamer.ReferencesOldToken($"/{NewName}").Should().BeFalse();
        ClientDataProcessChunkRenamer.ReferencesOldToken("no reference here").Should().BeFalse();
    }

    [Fact]
    public void ReferencesNewToken_MatchesOnlyTheCorrectName()
    {
        ClientDataProcessChunkRenamer.ReferencesNewToken($"/{NewName}").Should().BeTrue();
        ClientDataProcessChunkRenamer.ReferencesNewToken($"/{OldName}").Should().BeFalse();
    }

    [Fact]
    public void HasDoubleSCorruption_DetectsOnlyTheSpecificCorruptionPattern()
    {
        ClientDataProcessChunkRenamer.HasDoubleSCorruption($"/s{NewName}").Should().BeTrue();
        ClientDataProcessChunkRenamer.HasDoubleSCorruption($"/{NewName}").Should().BeFalse();
        ClientDataProcessChunkRenamer.HasDoubleSCorruption($"/{OldName}").Should().BeFalse();
    }
}
