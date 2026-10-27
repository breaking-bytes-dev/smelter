// Pasos [E2E-7a], [E2E-7b], [E2E-7c] y [E2E-7d]: el Cloud Flow de punta a punta, igual que lo dispara el botón Ejecutar de la MDA
// (job con status vacío -> se pone Queued -> el flow llama a PrepareImport y a ProcessChunk en loop).
// Cubre también el camino POSITIVO de ValidateQueueAccess (quien pone Queued puede leer el template). Ver E2eRunner.cs.

using System.Diagnostics;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using static Smelter.Verify.ApiCalls;

namespace Smelter.Verify;

internal static partial class E2eRunner
{
    private static readonly TimeSpan FlowTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FlowPollEvery = TimeSpan.FromSeconds(5);

    private static void CloudFlowCompletes(Env env)
    {
        var templateId = ImportTemplate(env, E2eSpec.Simple.Dto("flow", ImportTemplateStatus.Active));
        var jobId = CreateJob(env, templateId, "flow", E2eSpec.Simple.Csv("flow", E2eSpec.Simple.Rows));

        var queuedAt = DateTime.UtcNow;
        Queue(env, jobId);
        var job = WaitForEnd(env, jobId, "7a");
        var completed = job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value == (int)ImportJobStatus.Completed;
        if (!completed) DumpLogs(env, jobId); // sin esto el FAIL no dice POR QUÉ quedó con errores
        DumpConcurrency(env, jobId, queuedAt); // siempre: 2 corridas del flow en un éxito también delatan la carrera
        Ensure(job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value == (int)ImportJobStatus.Completed,
            $"el Cloud Flow debía dejar el job Completed, lo dejó {StatusName(job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value)}" +
            $"{Reason(job)}");

        try
        {
            AssertJob(env, jobId, new JobExpect(ImportJobStatus.Completed, E2eSpec.Simple.Rows, E2eSpec.Simple.Rows));
        }
        catch
        {
            DumpLogs(env, jobId);
            throw;
        }
        Ensure(CountContactsWithPrefix(env, "flow") == E2eSpec.Simple.Rows,
            $"el flow debía crear {E2eSpec.Simple.Rows} contacts, hay {CountContactsWithPrefix(env, "flow")}");
        // Anti-duplicados: si dos corridas del flow hubieran procesado el mismo job habría logs de clave duplicada
        // (DuplicateRecordEntityKey) y/o contadores corridos. AssertJob ya exige processed == success == rows.
        AssertNoLogs(env, jobId);
        Console.WriteLine($"[E2E-7a]  flow completo: Queued -> Completed con {E2eSpec.Simple.Rows}/{E2eSpec.Simple.Rows} contacts y contadores coherentes.");
    }

    private static void CloudFlowFails(Env env)
    {
        // Template Draft: Prepare lanza ("not Active") y el handler del flow marca el job Failed escribiendo el motivo.
        if (!env.Pending.Remove("7b", out var jobId)) // si 7a ya lo encoló en paralelo, solo se espera
            jobId = StartFlowFailureJob(env);
        var job = WaitForEnd(env, jobId, "7b");
        Ensure(job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value == (int)ImportJobStatus.Failed,
            $"con el template Draft el job debía terminar Failed({(int)ImportJobStatus.Failed}), terminó {StatusName(job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value)}");

        var reason = job.GetAttributeValue<string>("sanic_smlt_ij_failurereason");
        Ensure(!string.IsNullOrWhiteSpace(reason),
            "el job Failed debía traer el motivo en sanic_smlt_ij_failurereason y está vacío (¿el Cloud Flow desplegado no escribe el motivo?)");
        Ensure(reason!.Length <= 4000, $"el motivo excede el tope de la columna (4000): {reason.Length}");
        foreach (var token in E2eSpec.Simple.PiiTokens)
            Ensure(!reason.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"el motivo de falla NO debe llevar datos del CSV (encontré '{token}'): {reason}");
        Ensure(CountContactsWithPrefix(env, "flowdraft") == 0, "un job que falló en Prepare no debe haber creado contacts");

        if (!reason.Contains("not Active", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("[E2E-7b]  WARN: el motivo no menciona 'not Active' (¿cayó en el texto genérico de respaldo del flow?).");
        Console.WriteLine($"[E2E-7b]  flow Failed OK, motivo sin PII: {reason}");
    }

    /// <summary>
    /// Paso 7c (claim atómico): el MISMO job se encola dos veces seguidas (Queued, se toca otro campo estando Queued, Queued
    /// otra vez). Sin filteringattributes/claim eso arrancaba varias corridas del orquestador que reseteaban y
    /// procesaban en paralelo → DuplicateRecordEntityKey. Con el fix debe terminar Completed, con conteos exactos y sin logs.
    /// </summary>
    private static void CloudFlowDoubleQueue(Env env)
    {
        var templateId = ImportTemplate(env, E2eSpec.Simple.Dto("claim", ImportTemplateStatus.Active));
        var jobId = CreateJob(env, templateId, "claim", E2eSpec.Simple.Csv("claim", E2eSpec.Simple.Rows));

        var queuedAt = DateTime.UtcNow;
        Queue(env, jobId);
        env.Client.Update(new Entity(JobTable, jobId) { ["sanic_smlt_ij_name"] = E2eSpec.JobName("claim") + "-touch" }); // update con status Queued
        // El guard de ValidateQueueAccess puede rechazar este segundo Queued si el flow ya pasó el job a Processing; si el job
        // sigue Queued se acepta. Ambos resultados son válidos: lo que se exige es que el job termine bien.
        try { Queue(env, jobId); Console.WriteLine("[E2E-7c]  segundo Queued aceptado (el job seguía Queued)."); }
        catch (Exception ex) when (ex.Message.Contains(RequeueBlockedMessage, StringComparison.Ordinal))
        { Console.WriteLine("[E2E-7c]  segundo Queued rechazado por el guard (el job ya estaba Processing)."); }

        var job = WaitForEnd(env, jobId, "7c");
        var status = job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value;
        if (status != (int)ImportJobStatus.Completed) DumpLogs(env, jobId);
        DumpConcurrency(env, jobId, queuedAt);
        Ensure(status == (int)ImportJobStatus.Completed,
            $"encolado dos veces, el job debía terminar Completed, terminó {StatusName(status)}{Reason(job)}");

        try
        {
            AssertJob(env, jobId, new JobExpect(ImportJobStatus.Completed, E2eSpec.Simple.Rows, E2eSpec.Simple.Rows));
            AssertNoLogs(env, jobId);
        }
        catch
        {
            DumpLogs(env, jobId);
            throw;
        }
        Ensure(CountContactsWithPrefix(env, "claim") == E2eSpec.Simple.Rows,
            $"el job debía crear exactamente {E2eSpec.Simple.Rows} contacts, hay {CountContactsWithPrefix(env, "claim")}");
        Console.WriteLine($"[E2E-7c]  doble encolado: Completed con {E2eSpec.Simple.Rows}/{E2eSpec.Simple.Rows} contacts, contadores exactos y 0 logs de error.");
    }

    private const string RequeueBlockedMessage = "El Import Job se está procesando: no se puede volver a poner en cola hasta que termine.";

    /// <summary>
    /// Paso 7d (guard de re-encolado, determinístico y sin depender del flow): Prepare directo deja el job Processing con
    /// Claimed=true; poner Queued debe ser rechazado por ValidateQueueAccess con el mensaje del guard. Después se corre
    /// ProcessChunk hasta el final y el job debe terminar Completed con conteos exactos y sin logs (el intento no lo reseteó).
    /// </summary>
    private static void RequeueWhileProcessingRejected(Env env)
    {
        var templateId = ImportTemplate(env, E2eSpec.Simple.Dto("requeue", ImportTemplateStatus.Active));
        var jobId = CreateJob(env, templateId, "requeue", E2eSpec.Simple.Csv("requeue", E2eSpec.Simple.Rows));

        try
        {
            var prep = ExecuteRetryingBlob(env.Client, new OrganizationRequest(env.Apis.Prepare) { ["JobId"] = jobId });
            Ensure((bool)prep["Claimed"], "PrepareImport debía reclamar el job (Claimed=true) y no lo hizo");

            var before = env.Client.Retrieve(JobTable, jobId, new ColumnSet(StatusAttr)).GetAttributeValue<OptionSetValue>(StatusAttr)?.Value;
            Ensure(before == (int)ImportJobStatus.Processing, $"tras Prepare el job debía estar Processing, está {StatusName(before)}");

            string? rejection = null;
            try { Queue(env, jobId); }
            catch (Exception ex) { rejection = ex.Message; }
            Ensure(rejection is not null, "poner Queued sobre un job Processing debía ser rechazado y fue aceptado (¿plugin 1.0.13 sin desplegar?)");
            Ensure(rejection!.Contains(RequeueBlockedMessage, StringComparison.Ordinal), $"el rechazo debía traer el mensaje del guard, trajo: {rejection}");

            var after = env.Client.Retrieve(JobTable, jobId, new ColumnSet(StatusAttr)).GetAttributeValue<OptionSetValue>(StatusAttr)?.Value;
            Ensure(after == (int)ImportJobStatus.Processing, $"el intento rechazado no debía cambiar el status, quedó {StatusName(after)}");

            for (var i = 0; i < 30; i++)
            {
                var resp = ExecuteRetryingBlob(env.Client, new OrganizationRequest(env.Apis.ProcessChunk) { ["JobId"] = jobId });
                if (!(bool)resp["HasMore"]) break;
                Ensure(i < 29, "ProcessChunk no terminó en 30 iteraciones (HasMore nunca fue false).");
            }

            AssertJob(env, jobId, new JobExpect(ImportJobStatus.Completed, E2eSpec.Simple.Rows, E2eSpec.Simple.Rows));
            AssertNoLogs(env, jobId);
        }
        catch
        {
            DumpLogs(env, jobId);
            throw;
        }
        Ensure(CountContactsWithPrefix(env, "requeue") == E2eSpec.Simple.Rows,
            $"el job debía crear exactamente {E2eSpec.Simple.Rows} contacts, hay {CountContactsWithPrefix(env, "requeue")}");
        Console.WriteLine("[E2E-7d]  re-encolado con el job Processing rechazado; el job terminó Completed con conteos exactos y 0 logs.");
    }

    private static void AssertNoLogs(Env env, Guid jobId)
    {
        var logs = LogsOf(env, jobId);
        Ensure(logs.Count == 0, $"el job no debía tener Import Job Logs y tiene {logs.Count} (¿clave duplicada por corridas concurrentes del flow?)");
    }

    private static Guid StartFlowFailureJob(Env env)
    {
        var templateId = ImportTemplate(env, E2eSpec.Simple.Dto("flow-draft", ImportTemplateStatus.Draft));
        var jobId = CreateJob(env, templateId, "flow-draft", E2eSpec.Simple.Csv("flowdraft", 3));
        Queue(env, jobId);
        return jobId;
    }

    private static void PrequeueFlowFailure(Env env)
    {
        try
        {
            env.Pending["7b"] = StartFlowFailureJob(env);
            Console.WriteLine("[E2E-7b]  job encolado en paralelo con 7a.");
        }
        catch (Exception ex)
        {
            // No es el paso 7a el que falla: 7b lo reintenta solo (y reporta el error si persiste).
            Console.Error.WriteLine($"[E2E-7b]  no se pudo encolar por adelantado ({ex.Message}); se encola al llegar su turno.");
        }
    }

    /// <summary>Lo mismo que hace el botón: un Update que pone el status en Queued (dispara el trigger del flow y el plugin ValidateQueueAccess).</summary>
    /// <summary>
    /// Diagnóstico de concurrencia (solo ids y horarios, sin datos de filas): ¿cuántos Cloud Flows escuchan el Import
    /// Job y cuántas veces corrieron Prepare/ProcessChunk para ESTE job (Plugin Trace Log)? Un DuplicateRecordEntityKey
    /// en un Upsert por alternate key solo se explica con dos escrituras concurrentes de la misma clave.
    /// </summary>
    private static void DumpConcurrency(Env env, Guid jobId, DateTime queuedAt)
    {
        try
        {
            var runs = env.Client.RetrieveMultiple(new QueryExpression("flowrun")
            {
                ColumnSet = new ColumnSet("starttime", "endtime", "status", "triggertype"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("workflow", ConditionOperator.Equal, OrchestratorFlowId),
                        new ConditionExpression("starttime", ConditionOperator.OnOrAfter, queuedAt.AddMinutes(-1)),
                    },
                },
                Orders = { new OrderExpression("starttime", OrderType.Ascending) },
            }).Entities;
            Console.WriteLine($"[E2E-7a]  corridas del flow orquestador desde que se encoló: {runs.Count}");
            foreach (var r in runs)
                Console.WriteLine($"[E2E-7a]    - start {r.GetAttributeValue<DateTime?>("starttime"):HH:mm:ss.fff} " +
                    $"end {r.GetAttributeValue<DateTime?>("endtime"):HH:mm:ss.fff} status={r.GetAttributeValue<string>("status")} " +
                    $"trigger={r.GetAttributeValue<string>("triggertype")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E-7a]  (no se pudo leer flowrun: {ex.Message})");
        }

        try
        {
            var flows = env.Client.RetrieveMultiple(new QueryExpression("workflow")
            {
                ColumnSet = new ColumnSet("name", "statecode", "workflowid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("category", ConditionOperator.Equal, 5),
                        new ConditionExpression("clientdata", ConditionOperator.Like, "%sanic_tbl_smlt_importjob%"),
                    },
                },
            }).Entities;
            Console.Error.WriteLine($"      Cloud Flows que referencian el Import Job: {flows.Count}");
            foreach (var f in flows)
                Console.Error.WriteLine($"        - {f.GetAttributeValue<string>("name")} ({f.Id}) statecode={f.GetAttributeValue<OptionSetValue>("statecode")?.Value}");

            var traces = env.Client.RetrieveMultiple(new QueryExpression("plugintracelog")
            {
                ColumnSet = new ColumnSet("typename", "createdon", "correlationid", "depth", "performanceexecutionduration"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("messageblock", ConditionOperator.Like, $"%{jobId}%"),
                        new ConditionExpression("typename", ConditionOperator.Like, "Smelter.Plugins.%"),
                    },
                },
                Orders = { new OrderExpression("createdon", OrderType.Ascending) },
            }).Entities;
            Console.Error.WriteLine($"      Ejecuciones de plugins para este job (Plugin Trace Log): {traces.Count}");
            foreach (var t in traces)
                Console.Error.WriteLine($"        - {t.GetAttributeValue<DateTime>("createdon"):HH:mm:ss.fff} " +
                    $"{t.GetAttributeValue<string>("typename")?.Split(',')[0]} corr={t.GetAttributeValue<Guid?>("correlationid")} " +
                    $"depth={t.GetAttributeValue<int?>("depth")} {t.GetAttributeValue<int?>("performanceexecutionduration")}ms");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"      (no se pudo leer el diagnóstico de concurrencia: {ex.Message})");
        }
    }

    /// <summary>Id del Cloud Flow orquestador de Smelter (Workflows/CloudFlowSmelterSmelterorchestrator-0EB777E3-...).</summary>
    private static readonly Guid OrchestratorFlowId = new("0eb777e3-6760-f111-ab0c-0022480a5be8");

    private static void Queue(Env env, Guid jobId) =>
        env.Client.Update(new Entity(JobTable, jobId) { [StatusAttr] = new OptionSetValue((int)ImportJobStatus.Queued) });

    /// <summary>Espera (poll cada 5 s, tope 5 min) a que el job llegue a un estado final (Completed / CompletedWithErrors / Failed).</summary>
    private static Entity WaitForEnd(Env env, Guid jobId, string tag)
    {
        var sw = Stopwatch.StartNew();
        int? last = -1;
        while (true)
        {
            var job = env.Client.Retrieve(JobTable, jobId, new ColumnSet(StatusAttr, "sanic_smlt_ij_failurereason"));
            var status = job.GetAttributeValue<OptionSetValue>(StatusAttr)?.Value;
            if (status != last)
            {
                Console.WriteLine($"[E2E-{tag}]   +{sw.Elapsed.TotalSeconds:F0}s status = {StatusName(status)}");
                last = status;
            }

            if (status == (int)ImportJobStatus.Completed || status == (int)ImportJobStatus.CompletedWithErrors || status == (int)ImportJobStatus.Failed)
                return job;

            if (sw.Elapsed > FlowTimeout)
                throw new InvalidOperationException(
                    $"el job {jobId} sigue en {StatusName(status)} tras {FlowTimeout.TotalMinutes:F0} min: el Cloud Flow no lo tomó o no terminó " +
                    "(¿flujo apagado o con la conexión rota? trigger: status eq 0 [Queued] sobre sanic_tbl_smlt_importjob; revisá el historial de ejecuciones).");
            Thread.Sleep(FlowPollEvery);
        }
    }

    private static string Reason(Entity job)
    {
        var reason = job.GetAttributeValue<string>("sanic_smlt_ij_failurereason");
        return string.IsNullOrWhiteSpace(reason) ? "" : $" — motivo: {reason}";
    }
}
