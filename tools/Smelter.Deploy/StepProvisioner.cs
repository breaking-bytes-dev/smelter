// Provisioning idempotente de los message steps (StepSpec.cs) — mismo patrón que Program.cs y
// SecurityProvisioner: consultar por clave natural (plugintype + sdkmessage + sdkmessagefilter),
// crear con SolutionUniqueName si no existe, actualizar lo mutable si existe — re-ejecutable sin
// duplicar. Corre después del upsert de Custom APIs (necesita los plugintypeids ya descubiertos
// del package) y antes del PublishAllXml de Program.cs.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class StepProvisioner
{
    internal static void Run(ServiceClient client, IReadOnlyDictionary<string, Guid> pluginTypeIds)
    {
        Console.WriteLine("[4b] StepProvisioner: message steps (StepSpec.cs) ...");

        foreach (var step in StepCatalogo.Steps)
        {
            if (!pluginTypeIds.TryGetValue(step.PluginTypeName, out var pluginTypeId))
                throw new InvalidOperationException(
                    $"plugintype '{step.PluginTypeName}' no está entre los descubiertos del package — " +
                    "¿EsperarPluginTypesAsync no lo incluyó en typesEsperados?");

            var messageId = RetrieveMessageId(client, step.Message);
            var filterId = RetrieveMessageFilterId(client, messageId, step.PrimaryEntity, step.Message);

            UpsertStep(client, step, pluginTypeId, messageId, filterId);
        }
    }

    private static Guid RetrieveMessageId(ServiceClient client, string message)
    {
        var rows = client.RetrieveMultiple(new QueryExpression("sdkmessage")
        {
            ColumnSet = new ColumnSet("sdkmessageid"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, message) } },
        }).Entities;

        if (rows.Count == 0)
            throw new InvalidOperationException($"sdkmessage '{message}' no existe en el ambiente.");
        return rows[0].Id;
    }

    private static Guid RetrieveMessageFilterId(ServiceClient client, Guid messageId, string primaryEntity, string message)
    {
        // primaryobjecttypecode es un atributo EntityName: en ConditionExpression se filtra por el
        // nombre lógico de la entidad (mismo criterio que usa el Plugin Registration Tool).
        var rows = client.RetrieveMultiple(new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet("sdkmessagefilterid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageid", ConditionOperator.Equal, messageId),
                    new ConditionExpression("primaryobjecttypecode", ConditionOperator.Equal, primaryEntity),
                },
            },
        }).Entities;

        if (rows.Count == 0)
            throw new InvalidOperationException(
                $"sdkmessagefilter de '{message}' sobre '{primaryEntity}' no existe — ¿la tabla está en el ambiente?");
        return rows[0].Id;
    }

    private static void UpsertStep(ServiceClient client, MessageStepSpec step, Guid pluginTypeId, Guid messageId, Guid filterId)
    {
        var existentes = client.RetrieveMultiple(new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("plugintypeid", ConditionOperator.Equal, pluginTypeId),
                    new ConditionExpression("sdkmessageid", ConditionOperator.Equal, messageId),
                    new ConditionExpression("sdkmessagefilterid", ConditionOperator.Equal, filterId),
                },
            },
        }).Entities;

        if (existentes.Count > 0)
        {
            var id = existentes[0].Id;
            // Solo campos mutables; message/filter/plugintype son la clave natural y no se tocan.
            // statecode/statuscode re-habilitan un step que alguien haya deshabilitado a mano.
            client.Update(new Entity("sdkmessageprocessingstep", id)
            {
                ["name"] = step.Name,
                ["description"] = step.Description,
                ["rank"] = step.Rank,
                ["filteringattributes"] = string.IsNullOrEmpty(step.FilteringAttributes) ? null : step.FilteringAttributes,
                ["statecode"] = new OptionSetValue(0),  // Enabled
                ["statuscode"] = new OptionSetValue(1), // Enabled
            });
            Console.WriteLine($"[4b]   step '{step.Name}' existente ({id}) — actualizado y habilitado.");
            return;
        }

        var attrs = new Entity("sdkmessageprocessingstep")
        {
            ["name"] = step.Name,
            ["description"] = step.Description,
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
            ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
            ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId),
            ["stage"] = new OptionSetValue(step.Stage),
            ["mode"] = new OptionSetValue(step.Mode),
            ["rank"] = step.Rank,
            ["supporteddeployment"] = new OptionSetValue(0), // Server Only
        };
        if (!string.IsNullOrEmpty(step.FilteringAttributes))
            attrs["filteringattributes"] = step.FilteringAttributes;

        var creado = CrearEnSolucion(client, attrs);
        Console.WriteLine($"[4b]   step '{step.Name}' creado: {creado}.");
    }

    // Crea un registro DENTRO de la solución `smlt_core_smelter` — mismo helper que Program.cs
    // (local function, no accesible entre archivos top-level statements), duplicado acá a propósito.
    private static Guid CrearEnSolucion(ServiceClient client, Entity e)
    {
        var req = new CreateRequest { Target = e };
        req.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
        return ((CreateResponse)client.Execute(req)).id;
    }
}
