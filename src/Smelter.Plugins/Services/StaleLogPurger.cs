using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Borra los Import Job Logs que quedaron de una corrida anterior del mismo job (Reintentar). Sin esto el job mostraba
    /// los errores de la corrida vieja junto con los de la nueva, y los contadores y los logs dejaban de coincidir.
    /// </summary>
    public interface IStaleLogPurger
    {
        /// <summary>
        /// Borra, dentro del presupuesto de tiempo del purger, los logs del job que NO se tocaron desde
        /// <paramref name="runStartedAtUtc"/> (el <c>starttime</c> que Prepare escribe al arrancar la corrida actual).
        /// </summary>
        /// <returns>Cantidad de logs borrados en esta llamada.</returns>
        int Purge(Guid importJobId, DateTime runStartedAtUtc);
    }

    /// <summary>
    /// <para>
    /// «Vieja» = <c>modifiedon</c> anterior al inicio de la corrida actual. No se usa <c>createdon</c> a propósito: ProcessChunk
    /// escribe los logs con un upsert por (job, fila, tabla), así que una fila que vuelve a fallar en la corrida nueva
    /// <i>actualiza</i> el log viejo (conserva su <c>createdon</c>, pero <c>modifiedon</c> pasa a ser posterior al inicio). Con
    /// <c>modifiedon</c> ese log ya es de la corrida actual y se conserva; con <c>createdon</c> se borraría un log vigente.
    /// </para>
    /// <para>
    /// El borrado corre con el servicio SYSTEM de los plugins (los logs los crea SYSTEM y ningún rol de Smelter tiene Create sobre
    /// ellos; el Delete del Configurador no alcanza al Operador). Está acotado por tiempo y por lote para no acercarse al límite de
    /// 2 minutos del sandbox: Prepare lo llama con un presupuesto y cada ProcessChunk continúa con lo que haya quedado. Los lotes
    /// van en <c>ExecuteMultiple</c> con <c>ContinueOnError</c>: un fault de un registro no se lanza (atraparlo dentro de la
    /// transacción de la Custom API la doomearía), se cuenta, y si un lote entero falla se corta para no girar en falso.
    /// </para>
    /// </summary>
    public sealed class StaleLogPurger : IStaleLogPurger
    {
        /// <summary>Logical name of the Import Job Log table.</summary>
        public const string ImportJobLogTable = "sanic_tbl_smlt_importjoblog";

        /// <summary>Lookup del log hacia su Import Job.</summary>
        public const string LogJobLookupAttribute = "sanic_smlt_ijl_importjobid";

        /// <summary>Logs por consulta y por <c>ExecuteMultiple</c> (el máximo de la plataforma es 1000).</summary>
        public const int BatchSize = 500;

        private readonly IOrganizationService _service;
        private readonly ITracingService _tracing;
        private readonly TimeSpan _budget;

        /// <param name="service">Servicio SYSTEM.</param>
        /// <param name="tracing">Optional tracing service; pass <c>null</c> in unit tests.</param>
        /// <param name="budget">Tiempo máximo que esta instancia dedica a borrar por llamada a <see cref="Purge"/>.</param>
        public StaleLogPurger(IOrganizationService service, ITracingService tracing, TimeSpan budget)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _tracing = tracing;
            _budget = budget;
        }

        /// <inheritdoc/>
        public int Purge(Guid importJobId, DateTime runStartedAtUtc)
        {
            var clock = Stopwatch.StartNew();
            var deleted = 0;

            while (clock.Elapsed < _budget)
            {
                var stale = _service.RetrieveMultiple(StaleLogsQuery(importJobId, runStartedAtUtc)).Entities;
                if (stale.Count == 0) return deleted;

                var requests = new OrganizationRequestCollection();
                foreach (var log in stale)
                    requests.Add(new DeleteRequest { Target = new EntityReference(ImportJobLogTable, log.Id) });

                var response = (ExecuteMultipleResponse)_service.Execute(new ExecuteMultipleRequest
                {
                    Requests = requests,
                    Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true },
                });

                var faulted = response.Responses.Count(r => r.Fault != null);
                deleted += stale.Count - faulted;
                if (faulted == stale.Count)
                {
                    _tracing?.Trace($"StaleLogPurger: all {faulted} deletes of the batch faulted; stopping.");
                    return deleted;
                }
            }

            _tracing?.Trace($"StaleLogPurger: time budget ({_budget.TotalSeconds:F0}s) exhausted after {deleted} deletes; " +
                "the next ProcessChunk continues.");
            return deleted;
        }

        private static QueryExpression StaleLogsQuery(Guid importJobId, DateTime runStartedAtUtc) => new QueryExpression(ImportJobLogTable)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = BatchSize,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(LogJobLookupAttribute, ConditionOperator.Equal, importJobId),
                    new ConditionExpression("modifiedon", ConditionOperator.LessThan, runStartedAtUtc),
                },
            },
        };
    }
}
