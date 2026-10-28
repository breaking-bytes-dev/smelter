using System.Text.Json.Nodes;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// TDD (bug): el orquestador solo marcaba Failed si Scope fallaba (runAfter ["Failed"]). Si
/// PrepareImport lanza, Scope queda Skipped y el job se queda en Queued para siempre; idem TimedOut.
/// <c>ClientDataFailedHandlerFixer</c> amplía el runAfter a Failed/Skipped/TimedOut (pura, idempotente).
/// </summary>
public class ClientDataFailedHandlerFixerTests
{
    private static readonly string[] Expected = ["Failed", "Skipped", "TimedOut"];

    private const string Original = """
    {"properties":{"definition":{"actions":{
      "Initialize_variable":{"runAfter":{},"type":"InitializeVariable"},
      "Scope":{"runAfter":{"Initialize_variable":["Succeeded"]},"type":"Scope","actions":{}},
      "Update_a_row":{
        "runAfter":{"Scope":["Failed"]},
        "type":"OpenApiConnection",
        "inputs":{"parameters":{"entityName":"sanic_tbl_smlt_importjobs","recordId":"@x","item/sanic_smlt_ij_status":4},
                  "host":{"operationId":"UpdateOnlyRecord","connectionName":"shared_commondataserviceforapps"}}}
    }}},"schemaVersion":"1.0.0.0"}
    """;

    private static string[] RunAfter(string json, string action = "Update_a_row", string dependsOn = "Scope") =>
        JsonNode.Parse(json)!["properties"]!["definition"]!["actions"]![action]!["runAfter"]![dependsOn]!
            .AsArray().Select(n => n!.GetValue<string>()).ToArray();

    [Fact]
    public void Apply_OriginalJson_ExpandsRunAfterAndReportsChanged()
    {
        var (result, changed) = ClientDataFailedHandlerFixer.Apply(Original);

        changed.Should().BeTrue();
        RunAfter(result).Should().BeEquivalentTo(Expected);
    }

    [Fact]
    public void Apply_SecondRun_IsNoOp()
    {
        var (first, _) = ClientDataFailedHandlerFixer.Apply(Original);

        var (second, changed) = ClientDataFailedHandlerFixer.Apply(first);

        changed.Should().BeFalse();
        second.Should().Be(first);
    }

    [Fact]
    public void Apply_WithoutStatusAction_ReturnsUnchanged()
    {
        var json = """{"properties":{"definition":{"actions":{"A":{"runAfter":{"Scope":["Failed"]},"type":"Compose"}}}}}""";

        var (result, changed) = ClientDataFailedHandlerFixer.Apply(json);

        changed.Should().BeFalse();
        result.Should().Be(json);
    }

    [Fact]
    public void Apply_NullOrEmpty_IsNoOp()
    {
        ClientDataFailedHandlerFixer.Apply(null).Changed.Should().BeFalse();
        ClientDataFailedHandlerFixer.Apply("").Changed.Should().BeFalse();
    }

    [Fact]
    public void Apply_OtherUpdateWithDifferentStatus_IsNotTouched()
    {
        var json = Original.Replace("\"item/sanic_smlt_ij_status\":4", "\"item/sanic_smlt_ij_status\":3");

        var (_, changed) = ClientDataFailedHandlerFixer.Apply(json);

        changed.Should().BeFalse();
    }

    [Fact]
    public void Apply_RestOfDocument_IsSemanticallyUnchanged()
    {
        var (result, _) = ClientDataFailedHandlerFixer.Apply(Original);

        var expected = JsonNode.Parse(Original)!;
        expected["properties"]!["definition"]!["actions"]!["Update_a_row"]!["runAfter"]!["Scope"] =
            new JsonArray("Failed", "Skipped", "TimedOut");

        JsonNode.DeepEquals(JsonNode.Parse(result), expected).Should().BeTrue();
    }

    [Fact]
    public void UnpackedOrchestratorFlow_HasFailedHandlerRunningOnSkippedAndTimedOut()
    {
        var path = Directory.GetFiles(Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Workflows"),
            "CloudFlowSmelterSmelterorchestrator-*.json").Single();
        var json = File.ReadAllText(path);

        RunAfter(json, dependsOn: "Condition_claimed").Should().BeEquivalentTo(Expected);
        ClientDataFailedHandlerFixer.Apply(json).Changed.Should().BeFalse();
    }

    [Fact]
    public void Apply_HandlerHangingFromConditionClaimed_ExpandsThatDependencyAndIsIdempotent()
    {
        var json = Original.Replace("\"Scope\":[\"Failed\"]", "\"Condition_claimed\":[\"Failed\"]");

        var (result, changed) = ClientDataFailedHandlerFixer.Apply(json);

        changed.Should().BeTrue();
        RunAfter(result, dependsOn: "Condition_claimed").Should().BeEquivalentTo(Expected);
        ClientDataFailedHandlerFixer.Apply(result).Changed.Should().BeFalse();
    }
}
