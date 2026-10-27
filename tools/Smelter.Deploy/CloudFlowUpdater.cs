// Actualiza el Cloud Flow (Power Automate, workflow.category=5 "Modern Flow") que invoca la Custom
// API processchunk, para que apunte al uniquename corregido tras el rename ADITIVO de Release N
// (Fase 6, smelter-mda-ux, Requirement "Rename de processchunk sin ventana rota"). El Cloud Flow NO
// vive en solutions/unpacked/ (esa carpeta es un espejo de metadata COMPILADA que solution-sync
// escribe desde Dev; el flow es JSON de runtime en la columna `clientdata` del registro `workflow`)
// — se localiza y edita por SDK en tiempo de deploy, DESPUÉS de que la Custom API nueva ya exista
// (Program.cs corre esto tras el upsert de APIs y SecurityProvisioner.Run).
//
// RIESGO NO VALIDABLE ACÁ (mismo motivo que AssociateAppModuleRole/UpsertDashboardSubArea en
// SecurityProvisioner.cs): que el uniquename de la acción viva como texto plano dentro del JSON de
// `clientdata` (dentro de la URL de la acción de tipo "OpenApiConnection", p.ej.
// ".../customapi/sanic_capi_smelter_processchunk") es el comportamiento documentado de los conectores
// Custom API de Power Automate, pero no es un tipo SDK compilado — no corroborable sin un Cloud Flow
// real en Dev. Si el replace no encuentra nada, NO se aborta el deploy (no-op); si la ESCRITURA falla, el deploy falla (con restore del estado
// del flow). Confirmarlo contra el flow real en el primer backend-deploy y ajustar si el JSON tiene otra forma.
//
// La transformación del texto de `clientdata` vive en ClientDataProcessChunkRenamer.cs (lógica PURA,
// testeada sin Dataverse) — ESTE archivo es solo el I/O (buscar el flow, aplicar el resultado,
// deactivate/update/activate). Ver el comment de ese archivo para el bug CRÍTICO que motivó separarlo
// (naive string.Replace corrompía clientdata ya migrado en el segundo deploy en adelante).

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class CloudFlowUpdater
{
    private const string OldActionUniqueName = ClientDataProcessChunkRenamer.OldActionUniqueName;

    // `workflow.category`: 0=Workflow clásico, 5=Modern Flow (Cloud Flow / Power Automate).
    private const int ModernFlowCategory = 5;

    // `workflow.statecode`: 0=Draft, 1=Activated. Un Cloud Flow activo debe pasarse a Draft antes de
    // editar `clientdata` y reactivarse después (comportamiento estándar documentado del SDK para
    // `workflow`; el maker portal hace lo mismo internamente al editar un flow activo).
    private const int StateDraft = 0;
    private const int StateActivated = 1;
    private const int StatusDraft = 1;
    private const int StatusActivated = 2;

    /// <summary>
    /// Busca todo Cloud Flow cuyo <c>clientdata</c> referencie el uniquename viejo de processchunk y
    /// lo reemplaza por el nuevo, sin dejar al flow apuntando a una API inexistente en ningún momento
    /// posterior al deploy (ambas APIs coexisten en <see cref="Catalogo.Apis"/> durante Release N).
    /// </summary>
    internal static void UpdateProcessChunkReference(ServiceClient client)
    {
        Console.WriteLine("[7] CloudFlowUpdater: buscando Cloud Flow(s) que referencian processchunk ...");

        var flows = client.RetrieveMultiple(new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name", "clientdata", "statecode"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("category", ConditionOperator.Equal, ModernFlowCategory),
                    new ConditionExpression("clientdata", ConditionOperator.Like, $"%{OldActionUniqueName}%"),
                },
            },
        }).Entities;

        if (flows.Count == 0)
        {
            Console.WriteLine($"[7]   ningún Cloud Flow referencia '{OldActionUniqueName}' — nada que actualizar " +
                "(o ya fue migrado en un deploy anterior).");
            return;
        }

        foreach (var flow in flows)
            UpdateOne(client, flow);
    }

    private static void UpdateOne(ServiceClient client, Entity flow)
    {
        var flowId = flow.Id;
        var name = flow.GetAttributeValue<string>("name") ?? flowId.ToString();
        var clientData = flow.GetAttributeValue<string>("clientdata") ?? "";

        // Reemplazo por TOKEN exacto (no substring) — ver ClientDataProcessChunkRenamer.cs. Correr
        // esto sobre clientdata YA migrado (todo backend-deploy re-corre CloudFlowUpdater) es un
        // no-op real: Changed=false, nada que escribir, nada de Draft/Activate innecesario.
        var (renamedClientData, renamed) = ClientDataProcessChunkRenamer.Rename(clientData);

        // Fix del handler Failed (runAfter Failed/Skipped/TimedOut) — ver ClientDataFailedHandlerFixer.cs.
        // Las transformaciones se aplican en UN solo Draft→Update→Activate.
        var (fixedClientData, handlerFixed) = ClientDataFailedHandlerFixer.Apply(renamedClientData);

        // Motivo de falla en el job (sanic_smlt_ij_failurereason) — ver ClientDataFailureReasonWriter.cs.
        var (updatedClientData, reasonWritten) = ClientDataFailureReasonWriter.Apply(fixedClientData);
        var changed = renamed || handlerFixed || reasonWritten;
        if (!changed)
        {
            Console.WriteLine($"[7]   flow '{name}': clientdata ya migrado, con handler Failed correcto y motivo de falla — sin cambios.");
            return;
        }

        var wasActive = flow.GetAttributeValue<OptionSetValue>("statecode")?.Value == StateActivated;

        try
        {
            ApplyWithRestore(
                wasActive,
                toDraft: () => SetState(client, flowId, StateDraft, StatusDraft),
                update: () => client.Update(new Entity("workflow", flowId) { ["clientdata"] = updatedClientData }),
                activate: () => SetState(client, flowId, StateActivated, StatusActivated),
                log: Console.Error.WriteLine);
            Console.WriteLine($"[7]   flow '{name}' ({flowId}): clientdata actualizado " +
                $"(rename processchunk: {renamed}; handler Failed Failed/Skipped/TimedOut: {handlerFixed}; motivo de falla: {reasonWritten}).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[7]   ERROR: no se pudo actualizar el flow '{name}' ({flowId}): {ex.Message}. " +
                "Ver comentario de riesgo al inicio de CloudFlowUpdater.cs. El deploy falla.");
            throw;
        }
    }

    /// <summary>
    /// Draft → update → activate si el flow estaba activo. Si algo falla, intenta RESTAURAR el estado
    /// original (re-activar un flow que estaba activo) y re-lanza la excepción original: el deploy
    /// debe fallar, nunca dejar un flow apagado con exit 0. Pura (delegados) para testearla sin Dataverse.
    /// </summary>
    internal static void ApplyWithRestore(bool wasActive, Action toDraft, Action update, Action activate, Action<string> log)
    {
        var draftSet = false;
        try
        {
            if (wasActive)
            {
                toDraft();
                draftSet = true;
            }

            update();

            if (wasActive)
                activate();
        }
        catch
        {
            if (wasActive && draftSet)
            {
                try
                {
                    activate();
                    log("[7]   estado original (activo) restaurado tras el fallo.");
                }
                catch (Exception restoreEx)
                {
                    log($"[7]   ERROR: no se pudo restaurar el estado activo del flow: {restoreEx.Message}. " +
                        "El flow puede haber quedado en Draft.");
                }
            }
            throw;
        }
    }

    private static void SetState(ServiceClient client, Guid workflowId, int state, int status) =>
        client.Execute(new SetStateRequest
        {
            EntityMoniker = new EntityReference("workflow", workflowId),
            State = new OptionSetValue(state),
            Status = new OptionSetValue(status),
        });
}
