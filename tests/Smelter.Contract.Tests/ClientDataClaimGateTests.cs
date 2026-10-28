using System.Text.Json.Nodes;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// TDD (bug verificado en Dev con historial de flowrun): el trigger del orquestador no tenía columnas filtro ni
/// control de concurrencia, así que UN job encolado disparaba varias corridas en el mismo segundo; cada una llamaba a
/// PrepareImport (reset) y ProcessChunk en paralelo → upserts en carrera → DuplicateRecordEntityKey (0x80060892).
/// <c>ClientDataClaimGate</c> aplica las 3 capas del lado del flow: (1) filteringattributes, (2) sin concurrency=1 (jobs distintos en paralelo),
/// (3) el Scope del loop solo corre si PrepareImport devolvió Claimed=true. Pura e idempotente.
/// </summary>
public class ClientDataClaimGateTests
{
    private const string Original = """
    {"properties":{"definition":{
      "triggers":{"When_a_row_is_added,_modified_or_deleted":{"type":"OpenApiConnectionWebhook","inputs":{"parameters":{
        "subscriptionRequest/message":4,"subscriptionRequest/entityname":"sanic_tbl_smlt_importjob",
        "subscriptionRequest/scope":4,"subscriptionRequest/filterexpression":"sanic_smlt_ij_status eq 0"},
        "host":{"operationId":"SubscribeWebhookTrigger"}}}},
      "actions":{
      "Initialize_variable":{"runAfter":{},"type":"InitializeVariable"},
      "Perform_an_unbound_action_2":{"runAfter":{"Initialize_variable":["Succeeded"]},"type":"OpenApiConnection","inputs":{"parameters":{"actionName":"sanic_capi_smelter_prepareimport"},"host":{"operationId":"PerformUnboundAction"}}},
      "Scope":{"runAfter":{"Perform_an_unbound_action_2":["Succeeded"]},"type":"Scope","actions":{
        "Do_until":{"type":"Until","actions":{"Perform_an_unbound_action":{"type":"OpenApiConnection","inputs":{"host":{"operationId":"PerformUnboundAction"}}}}}}},
      "Update_a_row":{
        "runAfter":{"Scope":["Failed","Skipped","TimedOut"]},
        "type":"OpenApiConnection",
        "inputs":{"parameters":{"entityName":"sanic_tbl_smlt_importjobs","recordId":"@x","item/sanic_smlt_ij_status":4},
                  "host":{"operationId":"UpdateOnlyRecord","connectionName":"shared_commondataserviceforapps"}}}
    }}},"schemaVersion":"1.0.0.0"}
    """;

    private static JsonObject Definition(string json) => JsonNode.Parse(json)!["properties"]!["definition"]!.AsObject();
    private static JsonObject Trigger(string json) => Definition(json)["triggers"]!["When_a_row_is_added,_modified_or_deleted"]!.AsObject();
    private static JsonObject Actions(string json) => Definition(json)["actions"]!.AsObject();

    [Fact]
    public void Apply_Original_AddsFilteringAttributesToTheTrigger()
    {
        var (result, changed) = ClientDataClaimGate.Apply(Original);

        changed.Should().BeTrue();
        Trigger(result)["inputs"]!["parameters"]!["subscriptionRequest/filteringattributes"]!.GetValue<string>()
            .Should().Be("sanic_smlt_ij_status");
    }

    [Fact]
    public void Apply_Original_DoesNotAddRuntimeConfiguration_JobsRunInParallel()
    {
        var (result, _) = ClientDataClaimGate.Apply(Original);

        Trigger(result).ContainsKey("runtimeConfiguration").Should().BeFalse("sin concurrency=1 los jobs distintos corren en paralelo");
    }

    [Fact]
    public void Apply_Original_WrapsScopeInConditionOnClaimed()
    {
        var (result, _) = ClientDataClaimGate.Apply(Original);
        var actions = Actions(result);

        actions.ContainsKey("Scope").Should().BeFalse("el Scope se movió adentro del Condition");
        var cond = actions["Condition_claimed"]!.AsObject();
        cond["type"]!.GetValue<string>().Should().Be("If");
        cond["runAfter"]!["Perform_an_unbound_action_2"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().BeEquivalentTo("Succeeded");
        cond["expression"]!.GetValue<string>().Should().Be(ClientDataClaimGate.ClaimedExpression);
        ClientDataClaimGate.ClaimedExpression.Should().Be("@equals(outputs('Perform_an_unbound_action_2')?['body/Claimed'], true)");

        var scope = cond["actions"]!["Scope"]!.AsObject();
        scope["type"]!.GetValue<string>().Should().Be("Scope");
        scope["runAfter"]!.AsObject().Should().BeEmpty("adentro del Condition no depende de nada");
        scope["actions"]!["Do_until"].Should().NotBeNull("el loop de ProcessChunk viaja intacto");
        cond["else"]!["actions"]!.AsObject().Should().BeEmpty("no reclamado: no hace nada");
    }

    [Fact]
    public void Apply_Original_FailedHandlerNowHangsFromTheCondition_KeepingItsStatuses()
    {
        var (result, _) = ClientDataClaimGate.Apply(Original);
        var runAfter = Actions(result)["Update_a_row"]!["runAfter"]!.AsObject();

        runAfter.ContainsKey("Scope").Should().BeFalse();
        runAfter["Condition_claimed"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().BeEquivalentTo("Failed", "Skipped", "TimedOut");
    }

    [Fact]
    public void Apply_SecondRun_IsNoOp()
    {
        var (first, _) = ClientDataClaimGate.Apply(Original);

        var (second, changed) = ClientDataClaimGate.Apply(first);

        changed.Should().BeFalse();
        second.Should().Be(first);
    }

    [Fact]
    public void Apply_RestOfDocument_IsSemanticallyUnchanged()
    {
        var (result, _) = ClientDataClaimGate.Apply(Original);

        var expected = JsonNode.Parse(Original)!;
        var def = expected["properties"]!["definition"]!.AsObject();
        def["triggers"]!["When_a_row_is_added,_modified_or_deleted"]!["inputs"]!["parameters"]!["subscriptionRequest/filteringattributes"] = "sanic_smlt_ij_status";
        var acts = def["actions"]!.AsObject();
        var scope = acts["Scope"]!.AsObject();
        acts.Remove("Scope");
        scope["runAfter"] = new JsonObject();
        acts["Condition_claimed"] = new JsonObject
        {
            ["runAfter"] = new JsonObject { ["Perform_an_unbound_action_2"] = new JsonArray("Succeeded") },
            ["metadata"] = new JsonObject { ["operationMetadataId"] = ClientDataClaimGate.ConditionMetadataId },
            ["type"] = "If",
            ["expression"] = ClientDataClaimGate.ClaimedExpression,
            ["actions"] = new JsonObject { ["Scope"] = scope },
            ["else"] = new JsonObject { ["actions"] = new JsonObject() },
        };
        acts["Update_a_row"]!["runAfter"] = new JsonObject { ["Condition_claimed"] = new JsonArray("Failed", "Skipped", "TimedOut") };

        JsonNode.DeepEquals(JsonNode.Parse(result), expected).Should().BeTrue();
    }

    [Fact]
    public void Apply_TriggerHasConcurrency_RemovesItAndKeepsTheRestOfRuntimeConfiguration()
    {
        var json = Original.Replace("\"type\":\"OpenApiConnectionWebhook\",",
            "\"type\":\"OpenApiConnectionWebhook\",\"runtimeConfiguration\":{\"concurrency\":{\"runs\":1},\"x\":1},");

        var (result, changed) = ClientDataClaimGate.Apply(json);

        changed.Should().BeTrue();
        var rc = Trigger(result)["runtimeConfiguration"]!;
        rc.AsObject().ContainsKey("concurrency").Should().BeFalse();
        rc["x"]!.GetValue<int>().Should().Be(1);
        ClientDataClaimGate.Apply(result).Changed.Should().BeFalse();
    }

    [Fact]
    public void Apply_TriggerHasOnlyConcurrency_RemovesTheEmptyRuntimeConfiguration()
    {
        var json = Original.Replace("\"type\":\"OpenApiConnectionWebhook\",",
            "\"type\":\"OpenApiConnectionWebhook\",\"runtimeConfiguration\":{\"concurrency\":{\"runs\":1}},");

        var (result, _) = ClientDataClaimGate.Apply(json);

        Trigger(result).ContainsKey("runtimeConfiguration").Should().BeFalse();
    }

    [Fact]
    public void Apply_TriggerWithoutConcurrency_LeavesRuntimeConfigurationAlone()
    {
        var json = Original.Replace("\"type\":\"OpenApiConnectionWebhook\",",
            "\"type\":\"OpenApiConnectionWebhook\",\"runtimeConfiguration\":{\"x\":1},");

        var (result, _) = ClientDataClaimGate.Apply(json);

        Trigger(result)["runtimeConfiguration"]!["x"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void Apply_NoScopeAfterPrepare_StillFixesTriggerButDoesNotInventACondition()
    {
        var json = Original.Replace("\"Scope\":{\"runAfter\":{\"Perform_an_unbound_action_2\":[\"Succeeded\"]}", "\"Other\":{\"runAfter\":{\"Perform_an_unbound_action_2\":[\"Succeeded\"]}");

        var (result, changed) = ClientDataClaimGate.Apply(json);

        changed.Should().BeTrue();
        Actions(result).ContainsKey("Condition_claimed").Should().BeFalse();
        Trigger(result)["inputs"]!["parameters"]!["subscriptionRequest/filteringattributes"].Should().NotBeNull();
    }

    [Fact]
    public void Apply_NullEmptyOrMalformedOrUnrelated_IsNoOp()
    {
        ClientDataClaimGate.Apply(null).Changed.Should().BeFalse();
        ClientDataClaimGate.Apply("").Changed.Should().BeFalse();
        ClientDataClaimGate.Apply("{ no es json").Changed.Should().BeFalse();
        const string other = """{"properties":{"definition":{"triggers":{"T":{"type":"Request"}},"actions":{"Scope":{"type":"Scope"}}}}}""";
        var (result, changed) = ClientDataClaimGate.Apply(other);
        changed.Should().BeFalse();
        result.Should().Be(other);
    }

    [Fact]
    public void FailedHandlerFixer_AfterTheGate_ConvergesOnTheConditionAndStaysIdempotent()
    {
        var (gated, _) = ClientDataClaimGate.Apply(Original.Replace("[\"Failed\",\"Skipped\",\"TimedOut\"]", "[\"Failed\"]"));

        var (fixedJson, changed) = ClientDataFailedHandlerFixer.Apply(gated);

        changed.Should().BeTrue();
        Actions(fixedJson)["Update_a_row"]!["runAfter"]!["Condition_claimed"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().BeEquivalentTo("Failed", "Skipped", "TimedOut");
        ClientDataFailedHandlerFixer.Apply(fixedJson).Changed.Should().BeFalse();
    }

    [Fact]
    public void FailureReasonWriter_StillAppliesAfterTheGate()
    {
        var (gated, _) = ClientDataClaimGate.Apply(Original);

        var (withReason, changed) = ClientDataFailureReasonWriter.Apply(gated);

        changed.Should().BeTrue();
        Actions(withReason)["Update_a_row"]!["inputs"]!["parameters"]!["item/sanic_smlt_ij_failurereason"]!.GetValue<string>()
            .Should().Contain("actions('Perform_an_unbound_action')");
    }

    private static string UnpackedJson()
    {
        var dir = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Workflows");
        return File.ReadAllText(Directory.GetFiles(dir, "CloudFlowSmelterSmelterorchestrator-*.json").Single());
    }

    [Fact]
    public void UnpackedFlow_InTheRepo_AlreadyEqualsWhatTheDeployWrites()
    {
        var json = UnpackedJson();

        ClientDataClaimGate.Apply(json).Changed.Should().BeFalse("el JSON del repo ya tiene el claim gate (misma transformación que corre el deploy)");
        ClientDataFailedHandlerFixer.Apply(json).Changed.Should().BeFalse();
        ClientDataFailureReasonWriter.Apply(json).Changed.Should().BeFalse();

        Trigger(json)["inputs"]!["parameters"]!["subscriptionRequest/filteringattributes"]!.GetValue<string>().Should().Be("sanic_smlt_ij_status");
        Trigger(json).ContainsKey("runtimeConfiguration").Should().BeFalse("los jobs corren en paralelo: el claim neutraliza duplicados");
        var actions = Actions(json);
        actions.ContainsKey("Scope").Should().BeFalse();
        actions["Condition_claimed"]!["expression"]!.GetValue<string>().Should().Be(ClientDataClaimGate.ClaimedExpression);
        actions["Condition_claimed"]!["actions"]!["Scope"]!["actions"]!["Do_until"].Should().NotBeNull();
        actions["Update_a_row"]!["runAfter"]!["Condition_claimed"].Should().NotBeNull();
    }
}
