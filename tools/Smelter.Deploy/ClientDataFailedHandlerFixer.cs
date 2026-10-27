// Lógica PURA (sin Dataverse) que corrige el handler "marcar job Failed" del Cloud Flow orquestador.
//
// Bug: la acción que setea sanic_smlt_ij_status = 4 (Failed) corría solo con runAfter {"Scope":["Failed"]}.
// Si PrepareImport lanza (template no Active, header mismatch, sin acceso, gate HMAC...) el Scope queda
// Skipped → el handler también → el job queda Queued para siempre. Idem si el Scope hace TimedOut.
// Fix: runAfter {"Scope":["Failed","Skipped","TimedOut"]}. Desde que ClientDataClaimGate envuelve el Scope en
// Condition_claimed, el handler cuelga de "Condition_claimed" con los mismos estados (este fixer converge cualquiera
// de las dos dependencias).
//
// La acción se identifica por forma (OpenApiConnection UpdateOnlyRecord sobre sanic_tbl_smlt_importjobs
// con item/sanic_smlt_ij_status == 4), no por display name. Idempotente: sobre clientdata ya corregido
// devuelve Changed=false y el mismo texto (CloudFlowUpdater corre en TODO backend-deploy).

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Smelter.Deploy;

internal static class ClientDataFailedHandlerFixer
{
    private const string ImportJobsEntity = "sanic_tbl_smlt_importjobs";
    private const string StatusParameter = "item/sanic_smlt_ij_status";
    private const int FailedStatus = 4;

    private static readonly string[] RequiredStatuses = ["Failed", "Skipped", "TimedOut"];

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
        if (root is null) return (text, false);

        var changed = Visit(root);
        return changed ? (root.ToJsonString(WriteOptions), true) : (text, false);
    }

    // Recorre todo el árbol: las acciones pueden estar anidadas (Scope, Condition, Foreach...).
    private static bool Visit(JsonNode node)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                if (IsFailedStatusUpdate(obj))
                    changed |= EnsureRunAfter(obj);
                foreach (var child in obj.Select(p => p.Value).OfType<JsonNode>().ToList())
                    changed |= Visit(child);
                break;
            case JsonArray arr:
                foreach (var child in arr.OfType<JsonNode>().ToList())
                    changed |= Visit(child);
                break;
        }
        return changed;
    }

    internal static bool IsFailedStatusUpdate(JsonObject action)
    {
        if (action["type"]?.GetValueKind() != JsonValueKind.String || action["type"]!.GetValue<string>() != "OpenApiConnection")
            return false;
        if (action["inputs"] is not JsonObject inputs) return false;
        if (inputs["host"]?["operationId"] is not JsonValue op || !op.TryGetValue<string>(out var opId) || opId != "UpdateOnlyRecord")
            return false;
        if (inputs["parameters"] is not JsonObject p) return false;
        if (p["entityName"] is not JsonValue e || !e.TryGetValue<string>(out var entity) || entity != ImportJobsEntity)
            return false;
        return p[StatusParameter] is JsonValue s && s.TryGetValue<int>(out var status) && status == FailedStatus;
    }

    private static bool EnsureRunAfter(JsonObject action)
    {
        if (action["runAfter"] is not JsonObject runAfter) return false;

        // El handler cuelga del Condition_claimed (ClientDataClaimGate) o, en flows aún sin el gate, del Scope principal.
        var key = runAfter["Condition_claimed"] is JsonArray ? "Condition_claimed" : "Scope";
        if (runAfter[key] is not JsonArray current) return false;

        var existing = current.Select(n => n?.GetValue<string>()).ToList();
        if (RequiredStatuses.All(existing.Contains)) return false;

        var merged = existing.OfType<string>().Union(RequiredStatuses).ToList();
        runAfter[key] = new JsonArray(merged.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        return true;
    }
}
