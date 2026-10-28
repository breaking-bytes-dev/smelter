// Paso [E2E-7f]: el rol Smelter Operador de verdad, no su matriz. El Service Principal impersona (CallerId) a un usuario de test con
// SOLO ese rol (Smelter.DevAccess) y hace lo que hace la app del cliente: crear el job con su template, subir el CSV y encolarlo.
// Es la prueba de que la spec estricta de SecuritySpec (sin privilegios de plataforma ni Append/Write de más) alcanza: si le faltara
// un privilegio necesario, este paso es el que falla.

using System.Text;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using static Smelter.Verify.ApiCalls;

namespace Smelter.Verify;

internal static partial class E2eRunner
{
    /// <summary>Team QA de Smelter.DevAccess al que pertenece un usuario de test con rol Operador.</summary>
    private const string OperatorTeamName = "Smelter QA · Equipo A";

    private static void OperatorEndToEnd(Env env)
    {
        var (teamId, userId) = FindOperatorUser(env);
        var key = "operator";
        var templateId = ImportTemplate(env, E2eSpec.EmptyDedupKey.Dto(key));
        // Como en producción: el template es del team del Operador (Read/AppendTo de nivel Usuario cubren los registros de sus teams).
        env.Client.Execute(new AssignRequest { Target = new EntityReference(TemplateTable, templateId), Assignee = new EntityReference("team", teamId) });

        using var op = env.Client.Clone() ?? throw new InvalidOperationException("ASSERT: no se pudo clonar el ServiceClient para impersonar al Operador");
        op.CallerId = userId;

        // 1. Crear el job con el lookup al template (exige Append sobre el job + AppendTo sobre el template) y subir el CSV (Write sobre el job).
        var job = new Entity(JobTable)
        {
            ["sanic_smlt_ij_name"] = E2eSpec.JobName(key),
            ["sanic_smlt_ij_importtemplateid"] = new EntityReference(TemplateTable, templateId),
        };
        var jobId = op.Create(job);
        var owner = env.Client.Retrieve(JobTable, jobId, new ColumnSet("ownerid")).GetAttributeValue<EntityReference>("ownerid");
        Ensure(owner?.Id == teamId, $"AssignJobOwner debía dejar el job a nombre del team del template ({teamId}) y quedó en {owner?.LogicalName}/{owner?.Id}");
        UploadBlocks(op, new EntityReference(JobTable, jobId), FileAttr, key + ".csv",
            new UTF8Encoding(false).GetBytes(E2eSpec.EmptyDedupKey.Csv("op", E2eSpec.EmptyDedupKey.Rows, emptyDataRow: 2)));

        // 2. Encolar (ValidateQueueAccess lo valida con la identidad del Operador) y esperar al flow.
        op.Update(new Entity(JobTable, jobId) { [StatusAttr] = new OptionSetValue((int)ImportJobStatus.Queued) });
        var ended = WaitForEnd(env, jobId, "7f");
        Ensure(ended.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value == (int)ImportJobStatus.CompletedWithErrors,
            $"el job del Operador debía terminar CompletedWithErrors (una fila sin clave), terminó {StatusName(ended.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value)}{Reason(ended)}");

        // 3. El Operador ve su job y los logs (los crea SYSTEM: Read de nivel Usuario los alcanza si siguen al job/team).
        var seen = op.Retrieve(JobTable, jobId, new ColumnSet(StatusAttr, "sanic_smlt_ij_failedcount"));
        Ensure(seen.GetAttributeValue<int?>("sanic_smlt_ij_failedcount") == 1, "el Operador debía leer su job con failedcount = 1");
        var visibleLogs = op.RetrieveMultiple(new QueryExpression(LogTable)
        {
            ColumnSet = new ColumnSet("sanic_smlt_ijl_rownumber"),
            Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ijl_importjobid", ConditionOperator.Equal, jobId) } },
        }).Entities;
        var logOwner = LogsOwner(env, jobId);
        Ensure(visibleLogs.Count == 1, $"el Operador debía ver 1 Import Job Log de su job y ve {visibleLogs.Count} (dueño de los logs: {logOwner})");

        // 4. Lo que NO puede: crear logs y escribir el template.
        ExpectDenied("crear un Import Job Log", () => op.Create(new Entity(LogTable)
        {
            ["sanic_smlt_ijl_importjobid"] = new EntityReference(JobTable, jobId),
            ["sanic_smlt_ijl_status"] = new OptionSetValue((int)ImportLogStatus.Failed),
        }));
        ExpectDenied("escribir el Import Template", () => op.Update(new Entity(TemplateTable, templateId) { ["sanic_smlt_it_name"] = E2eSpec.TemplateName(key) + " x" }));

        Console.WriteLine($"[E2E-7f]  Operador {userId} (team {OperatorTeamName}): creó el job con template, subió el CSV, lo encoló, el flow lo completó; " +
            $"ve su job y {visibleLogs.Count} log (dueño: {logOwner}); crear logs y escribir el template denegados.");
    }

    private static (Guid TeamId, Guid UserId) FindOperatorUser(Env env)
    {
        var team = env.Client.RetrieveMultiple(new QueryExpression("team")
        {
            ColumnSet = new ColumnSet(false), TopCount = 1,
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, OperatorTeamName) } },
        }).Entities.FirstOrDefault();
        Ensure(team is not null, $"no existe el team '{OperatorTeamName}': correr backend-verify con dev_access=provision (Smelter.DevAccess)");

        var members = env.Client.RetrieveMultiple(new QueryExpression("teammembership")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            Criteria = { Conditions = { new ConditionExpression("teamid", ConditionOperator.Equal, team!.Id) } },
        }).Entities;
        Ensure(members.Count > 0, $"el team '{OperatorTeamName}' no tiene miembros: correr backend-verify con dev_access=provision");
        return (team.Id, members[0].GetAttributeValue<Guid>("systemuserid"));
    }

    private static string LogsOwner(Env env, Guid jobId)
    {
        var log = env.Client.RetrieveMultiple(new QueryExpression(LogTable)
        {
            ColumnSet = new ColumnSet("ownerid"), TopCount = 1,
            Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ijl_importjobid", ConditionOperator.Equal, jobId) } },
        }).Entities.FirstOrDefault();
        var o = log?.GetAttributeValue<EntityReference>("ownerid");
        return o is null ? "(sin logs)" : $"{o.LogicalName}/{o.Id}";
    }

    private static void ExpectDenied(string what, Action action)
    {
        try { action(); }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault>) { return; }
        throw new InvalidOperationException($"ASSERT: el Operador NO debía poder {what} y pudo");
    }
}
