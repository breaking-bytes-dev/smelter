// Lógica PURA (sin Dataverse) que hace que el handler "marcar job Failed" del Cloud Flow también escriba el MOTIVO
// de la falla en sanic_smlt_ij_failurereason (auditoría de campos #11). Misma acción que corrige
// ClientDataFailedHandlerFixer (OpenApiConnection UpdateOnlyRecord con item/sanic_smlt_ij_status == 4), ubicada por
// forma y no por display name. Idempotente: si el parámetro ya vale la expresión vigente devuelve Changed=false;
// si tiene otro valor lo converge a la expresión actual.
//
// Fuente del mensaje (PII-free: son los mensajes de InvalidPluginExecutionException / errores de plataforma, nunca
// datos de filas — el detalle por fila va al Import Job Log):
//   - Prepare falló (Perform_an_unbound_action_2): el Scope queda Skipped, el error está en esa acción.
//   - ProcessChunk falló (Perform_an_unbound_action, dentro de Scope/Do_until): el error está en esa acción.
// El `if` solo evalúa la rama elegida, así no se referencia una acción interna que nunca corrió.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Smelter.Deploy;

internal static class ClientDataFailureReasonWriter
{
    internal const string ReasonParameter = "item/sanic_smlt_ij_failurereason";

    /// <summary>Tope de la columna (multiline text 4000).</summary>
    internal const int MaxLength = 4000;

    internal const string PrepareAction = "Perform_an_unbound_action_2";
    internal const string ProcessChunkAction = "Perform_an_unbound_action";

    internal const string PrepareFallback = "Falló la preparación del import. Revisá el historial de ejecuciones del flujo.";
    internal const string ProcessFallback = "Falló el procesamiento del import o excedió el tiempo límite. Revisá el historial de ejecuciones del flujo.";

    internal static readonly string ReasonExpression =
        $"@take(if(equals(actions('{PrepareAction}')?['status'], 'Failed'), " +
        $"coalesce(actions('{PrepareAction}')?['error']?['message'], '{PrepareFallback}'), " +
        $"coalesce(actions('{ProcessChunkAction}')?['error']?['message'], '{ProcessFallback}')), {MaxLength})";

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

    private static bool Visit(JsonNode node)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                if (ClientDataFailedHandlerFixer.IsFailedStatusUpdate(obj))
                    changed |= EnsureReason((JsonObject)obj["inputs"]!["parameters"]!);
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

    private static bool EnsureReason(JsonObject parameters)
    {
        if (parameters[ReasonParameter] is JsonValue v && v.TryGetValue<string>(out var current) && current == ReasonExpression)
            return false;
        parameters[ReasonParameter] = ReasonExpression;
        return true;
    }
}
