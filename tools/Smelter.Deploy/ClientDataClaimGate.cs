// Lógica PURA (sin Dataverse) que blinda el Cloud Flow orquestador contra corridas duplicadas del MISMO job.
//
// Bug (verificado en Dev con historial de flowrun): el trigger "When a row is added, modified or deleted" no tenía
// columnas filtro ni control de concurrencia → un job encolado disparaba varias corridas en el mismo segundo (cualquier
// Update de una fila con status Queued re-dispara; además los triggers de Dataverse son at-least-once). Cada corrida
// llamaba PrepareImport (reset) y ProcessChunk en paralelo → upserts en carrera → DuplicateRecordEntityKey (0x80060892).
//
// Capas (las dos primeras son del lado del flow; la atómica vive en PrepareImportHandler):
//   1. Trigger "Select columns": subscriptionRequest/filteringattributes = sanic_smlt_ij_status (Create sigue
//      disparando; Update solo cuando cambia el status).
//      SIN concurrency=1 en el trigger (se QUITA si estaba): una concurrencia global serializaría jobs DISTINTOS y
//      cobranza encola varios jobs "partes" para que corran en paralelo. Los duplicados del MISMO job los neutraliza
//      el claim atómico de PrepareImport.
//   2. El Scope del loop de ProcessChunk se envuelve en Condition_claimed: solo corre si PrepareImport devolvió
//      Claimed=true (path de salida del conector: body/<Propiedad>, igual que body/HasMore de ProcessChunk). El handler
//      "marcar Failed" pasa a colgar de Condition_claimed: Prepare falla → Condition Skipped → handler corre;
//      no reclamado → Condition Succeeded → handler no corre (la otra corrida dueña del job sigue su curso).
//
// Idempotente: sobre clientdata ya transformado devuelve Changed=false y el mismo texto (CloudFlowUpdater corre en
// TODO backend-deploy). Se ubica por forma (trigger sobre sanic_tbl_smlt_importjob; Scope colgado de PrepareImport).

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Smelter.Deploy;

internal static class ClientDataClaimGate
{
    internal const string ImportJobEntity = "sanic_tbl_smlt_importjob";
    internal const string FilteringAttributesParameter = "subscriptionRequest/filteringattributes";
    internal const string FilteringAttributes = "sanic_smlt_ij_status";

    internal const string PrepareAction = ClientDataFailureReasonWriter.PrepareAction;
    internal const string ScopeName = "Scope";
    internal const string ConditionName = "Condition_claimed";
    // Fijo para que la transformación sea determinística (y el JSON del repo pueda ser idéntico a lo que escribe el deploy).
    internal const string ConditionMetadataId = "6f1d2a40-3b7c-4e58-9a21-5c8d0e7b4f13";

    internal const string ClaimedExpression = "@equals(outputs('" + PrepareAction + "')?['body/Claimed'], true)";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static (string ClientData, bool Changed) Apply(string? clientData)
    {
        var text = clientData ?? "";
        if (string.IsNullOrWhiteSpace(text)) return (text, false);

        JsonNode? root;
        try { root = JsonNode.Parse(text); }
        catch (JsonException) { return (text, false); }
        if (root?["properties"]?["definition"] is not JsonObject definition) return (text, false);

        var changed = false;
        if (definition["triggers"] is JsonObject triggers)
            foreach (var trigger in triggers.Select(p => p.Value).OfType<JsonObject>().Where(IsJobTrigger).ToList())
                changed |= EnsureTrigger(trigger);

        if (definition["actions"] is JsonObject actions)
            changed |= EnsureCondition(actions);

        return changed ? (root.ToJsonString(WriteOptions), true) : (text, false);
    }

    private static bool IsJobTrigger(JsonObject trigger) =>
        trigger["type"] is JsonValue t && t.TryGetValue<string>(out var type) && type == "OpenApiConnectionWebhook"
        && trigger["inputs"]?["parameters"]?["subscriptionRequest/entityname"] is JsonValue e
        && e.TryGetValue<string>(out var entity) && entity == ImportJobEntity;

    private static bool EnsureTrigger(JsonObject trigger)
    {
        var changed = false;
        var parameters = (JsonObject)trigger["inputs"]!["parameters"]!;
        if (!(parameters[FilteringAttributesParameter] is JsonValue f && f.TryGetValue<string>(out var current) && current == FilteringAttributes))
        {
            parameters[FilteringAttributesParameter] = FilteringAttributes;
            changed = true;
        }

        // Idempotente: quita concurrency (si una versión anterior la puso) y la runtimeConfiguration que quede vacía.
        if (trigger["runtimeConfiguration"] is JsonObject runtime && runtime.ContainsKey("concurrency"))
        {
            runtime.Remove("concurrency");
            if (runtime.Count == 0) trigger.Remove("runtimeConfiguration");
            changed = true;
        }
        return changed;
    }

    private static bool EnsureCondition(JsonObject actions)
    {
        if (actions.ContainsKey(ConditionName)) return false;
        if (actions[ScopeName] is not JsonObject scope
            || scope["type"] is not JsonValue st || !st.TryGetValue<string>(out var scopeType) || scopeType != "Scope"
            || scope["runAfter"] is not JsonObject scopeRunAfter || !scopeRunAfter.ContainsKey(PrepareAction))
            return false;

        // El Scope viaja intacto adentro del Condition; ahí ya no depende de nada (la dependencia pasa al Condition).
        var conditionRunAfter = scopeRunAfter.DeepClone().AsObject();
        actions.Remove(ScopeName);
        scope["runAfter"] = new JsonObject();

        actions[ConditionName] = new JsonObject
        {
            ["runAfter"] = conditionRunAfter,
            ["metadata"] = new JsonObject { ["operationMetadataId"] = ConditionMetadataId },
            ["type"] = "If",
            ["expression"] = ClaimedExpression,
            ["actions"] = new JsonObject { [ScopeName] = scope },
            ["else"] = new JsonObject { ["actions"] = new JsonObject() },
        };

        // Los handlers que colgaban del Scope (el de "marcar Failed") pasan a colgar del Condition, con los mismos estados.
        foreach (var action in actions.Select(p => p.Value).OfType<JsonObject>().ToList())
        {
            if (action["runAfter"] is JsonObject ra && ra.ContainsKey(ScopeName) && ra[ScopeName] is JsonNode statuses)
            {
                ra.Remove(ScopeName);
                ra[ConditionName] = statuses.DeepClone();
            }
        }
        return true;
    }
}
