// Asserts E2E de los message steps provisionados por StepProvisioner (StepSpec.cs): para cada
// spec del catálogo, el sdkmessageprocessingstep debe existir en Dev con el stage/mode/rank
// declarados y estar habilitado. Mismo patrón de lectura por clave natural que SecurityAssertions
// (sin GUIDs hardcodeados) y mismo criterio: corre ANTES del smoke funcional — si el step no está
// registrado, el assert de ownership del smoke daría un falso negativo confuso.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class StepAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[STEP] Verificando message steps (StepSpec.cs) ...");

        foreach (var step in StepCatalogo.Steps)
        {
            var pluginTypeId = RetrieveSingleId(client, "plugintype", "plugintypeid",
                new ConditionExpression("typename", ConditionOperator.Equal, step.PluginTypeName),
                $"plugintype '{step.PluginTypeName}' no existe (¿el package no lo trae o no se procesó?)");

            var messageId = RetrieveSingleId(client, "sdkmessage", "sdkmessageid",
                new ConditionExpression("name", ConditionOperator.Equal, step.Message),
                $"sdkmessage '{step.Message}' no existe en el ambiente");

            var rows = client.RetrieveMultiple(new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("name", "stage", "mode", "rank", "statecode", "filteringattributes"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("plugintypeid", ConditionOperator.Equal, pluginTypeId),
                        new ConditionExpression("sdkmessageid", ConditionOperator.Equal, messageId),
                    },
                },
            }).Entities;

            Assert(rows.Count == 1,
                $"debía existir EXACTAMENTE 1 step de '{step.PluginTypeName}' sobre '{step.Message}', hay {rows.Count} (¿corriste Smelter.Deploy?)");
            var actual = rows[0];

            var stage = actual.GetAttributeValue<OptionSetValue>("stage")?.Value;
            var mode = actual.GetAttributeValue<OptionSetValue>("mode")?.Value;
            var state = actual.GetAttributeValue<OptionSetValue>("statecode")?.Value;

            Assert(stage == step.Stage, $"step '{step.Name}': stage es {stage}, esperado {step.Stage} (Pre-operation=20)");
            Assert(mode == step.Mode, $"step '{step.Name}': mode es {mode}, esperado {step.Mode} (Synchronous=0)");
            Assert(actual.GetAttributeValue<int?>("rank") == step.Rank,
                $"step '{step.Name}': rank es {actual.GetAttributeValue<int?>("rank")}, esperado {step.Rank}");
            Assert(state == 0, $"step '{step.Name}' está DESHABILITADO (statecode={state}) — el ownership por team no corre");

            Console.WriteLine($"[STEP]   '{step.Name}': stage={stage}, mode={mode}, rank={actual.GetAttributeValue<int?>("rank")}, habilitado. OK.");
        }

        Console.WriteLine($"[STEP] OK: {StepCatalogo.Steps.Length} message step(s) registrados y habilitados según StepSpec.");
    }

    private static Guid RetrieveSingleId(ServiceClient client, string table, string idColumn,
        ConditionExpression condition, string notFoundMessage)
    {
        var rows = client.RetrieveMultiple(new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(idColumn),
            Criteria = { Conditions = { condition } },
        }).Entities;
        Assert(rows.Count > 0, notFoundMessage);
        return rows[0].Id;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[STEP]: " + message);
    }
}
