using System.Text.Json.Nodes;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// TDD (#11 auditoría): el handler Failed del Cloud Flow solo seteaba status=4 y el job quedaba sin motivo.
/// <c>ClientDataFailureReasonWriter</c> agrega a esa MISMA acción "Update a row" el seteo de
/// sanic_smlt_ij_failurereason con el mensaje (PII-free) de la acción que falló. Pura e idempotente.
/// </summary>
public class ClientDataFailureReasonWriterTests
{
    private const string ReasonParameter = "item/sanic_smlt_ij_failurereason";

    private const string Original = """
    {"properties":{"definition":{"actions":{
      "Perform_an_unbound_action_2":{"runAfter":{},"type":"OpenApiConnection","inputs":{"parameters":{"actionName":"sanic_capi_smelter_prepareimport"},"host":{"operationId":"PerformUnboundAction"}}},
      "Scope":{"runAfter":{"Perform_an_unbound_action_2":["Succeeded"]},"type":"Scope","actions":{
        "Do_until":{"type":"Until","actions":{"Perform_an_unbound_action":{"type":"OpenApiConnection","inputs":{"host":{"operationId":"PerformUnboundAction"}}}}}}},
      "Update_a_row":{
        "runAfter":{"Scope":["Failed","Skipped","TimedOut"]},
        "type":"OpenApiConnection",
        "inputs":{"parameters":{"entityName":"sanic_tbl_smlt_importjobs","recordId":"@x","item/sanic_smlt_ij_status":4},
                  "host":{"operationId":"UpdateOnlyRecord","connectionName":"shared_commondataserviceforapps"}}}
    }}},"schemaVersion":"1.0.0.0"}
    """;

    private static JsonNode UpdateParams(string json) =>
        JsonNode.Parse(json)!["properties"]!["definition"]!["actions"]!["Update_a_row"]!["inputs"]!["parameters"]!;

    [Fact]
    public void Apply_OriginalJson_AddsFailureReasonExpressionAndReportsChanged()
    {
        var (result, changed) = ClientDataFailureReasonWriter.Apply(Original);

        changed.Should().BeTrue();
        UpdateParams(result)[ReasonParameter]!.GetValue<string>().Should().Be(ClientDataFailureReasonWriter.ReasonExpression);
    }

    [Fact]
    public void ReasonExpression_CoversPrepareFailureAndScopeFailure_AndFitsTheColumn()
    {
        var expr = ClientDataFailureReasonWriter.ReasonExpression;

        expr.Should().StartWith("@");
        expr.Should().Contain("actions('Perform_an_unbound_action_2')?['error']?['message']", "falla de Prepare (el Scope queda Skipped)");
        expr.Should().Contain("actions('Perform_an_unbound_action')?['error']?['message']", "falla de ProcessChunk dentro del Scope");
        expr.Should().Contain("take(").And.Contain("4000", "la columna admite 4000 caracteres");
        expr.Should().NotContain("triggerOutputs", "el motivo no debe incluir datos del registro disparador");
    }

    [Fact]
    public void Apply_SecondRun_IsNoOp()
    {
        var (first, _) = ClientDataFailureReasonWriter.Apply(Original);

        var (second, changed) = ClientDataFailureReasonWriter.Apply(first);

        changed.Should().BeFalse();
        second.Should().Be(first);
    }

    [Fact]
    public void Apply_StaleReasonValue_IsConvergedToTheCurrentExpression()
    {
        var (first, _) = ClientDataFailureReasonWriter.Apply(Original);
        var node = JsonNode.Parse(first)!;
        node["properties"]!["definition"]!["actions"]!["Update_a_row"]!["inputs"]!["parameters"]![ReasonParameter] = "@{'viejo'}";

        var (result, changed) = ClientDataFailureReasonWriter.Apply(node.ToJsonString());

        changed.Should().BeTrue();
        UpdateParams(result)[ReasonParameter]!.GetValue<string>().Should().Be(ClientDataFailureReasonWriter.ReasonExpression);
    }

    [Fact]
    public void Apply_RestOfDocument_IsSemanticallyUnchanged()
    {
        var (result, _) = ClientDataFailureReasonWriter.Apply(Original);

        var expected = JsonNode.Parse(Original)!;
        expected["properties"]!["definition"]!["actions"]!["Update_a_row"]!["inputs"]!["parameters"]![ReasonParameter] =
            ClientDataFailureReasonWriter.ReasonExpression;

        JsonNode.DeepEquals(JsonNode.Parse(result), expected).Should().BeTrue();
    }

    [Fact]
    public void Apply_OtherUpdateWithDifferentStatus_IsNotTouched()
    {
        var json = Original.Replace("\"item/sanic_smlt_ij_status\":4", "\"item/sanic_smlt_ij_status\":3");

        var (result, changed) = ClientDataFailureReasonWriter.Apply(json);

        changed.Should().BeFalse();
        result.Should().Be(json);
    }

    [Fact]
    public void Apply_NullEmptyOrMalformed_IsNoOp()
    {
        ClientDataFailureReasonWriter.Apply(null).Changed.Should().BeFalse();
        ClientDataFailureReasonWriter.Apply("").Changed.Should().BeFalse();
        ClientDataFailureReasonWriter.Apply("{ no es json").Changed.Should().BeFalse();
    }

    [Fact]
    public void UnpackedFlow_InTheRepo_AlreadyCarriesTheReason_SoTheRepoMatchesWhatDeployWrites()
    {
        var dir = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Workflows");
        var json = File.ReadAllText(Directory.GetFiles(dir, "*.json").Single());

        var (result, changed) = ClientDataFailureReasonWriter.Apply(json);

        changed.Should().BeFalse("el JSON del repo ya tiene el motivo de falla (misma transformación que corre el deploy)");
        var actions = JsonNode.Parse(result)!["properties"]!["definition"]!["actions"]!.AsObject();
        actions["Update_a_row"]!["inputs"]!["parameters"]![ReasonParameter]!.GetValue<string>()
            .Should().Be(ClientDataFailureReasonWriter.ReasonExpression);
        actions["Perform_an_unbound_action_2"]!.ToJsonString().Should().NotContain("failurereason");
    }
}
