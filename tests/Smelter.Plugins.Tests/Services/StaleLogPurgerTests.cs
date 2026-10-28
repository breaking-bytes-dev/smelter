using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    /// <summary>
    /// Borrado de los Import Job Logs de la corrida anterior (1.0.16). «Vieja» = modificada ANTES del inicio de la corrida
    /// actual: un upsert de la corrida nueva sobre la misma clave (job, fila, tabla) actualiza modifiedon y deja de ser vieja.
    /// El borrado está acotado por tiempo (límite de 2 min del sandbox) y por lote, y es continuable en llamadas siguientes.
    /// </summary>
    public class StaleLogPurgerTests
    {
        private const string LogTable = "sanic_tbl_smlt_importjoblog";
        private static readonly DateTime StartedAt = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        private static ExecuteMultipleResponse Response(int total, int faulted)
        {
            var items = new ExecuteMultipleResponseItemCollection();
            for (var i = 0; i < faulted; i++)
                items.Add(new ExecuteMultipleResponseItem { RequestIndex = i, Fault = new OrganizationServiceFault { ErrorCode = -1 } });
            var r = new ExecuteMultipleResponse();
            r["Responses"] = items;
            r["IsFaulted"] = faulted > 0;
            return r;
        }

        private static EntityCollection Logs(params Guid[] ids) =>
            new EntityCollection(ids.Select(id => new Entity(LogTable, id)).ToList());

        /// <summary>Servicio cuyas consultas devuelven, en orden, los lotes dados (y vacío después); captura los ExecuteMultiple.</summary>
        private static IOrganizationService ServiceReturning(List<QueryExpression> queries, List<ExecuteMultipleRequest> deletes,
            Func<ExecuteMultipleRequest, ExecuteMultipleResponse> respond, params EntityCollection[] batches)
        {
            var service = Substitute.For<IOrganizationService>();
            var next = 0;
            service.RetrieveMultiple(Arg.Do<QueryBase>(q => queries.Add((QueryExpression)q)))
                   .Returns(_ => next < batches.Length ? batches[next++] : Logs());
            service.Execute(Arg.Do<OrganizationRequest>(r => deletes.Add((ExecuteMultipleRequest)r)))
                   .Returns(ci => respond((ExecuteMultipleRequest)ci.Arg<OrganizationRequest>()));
            return service;
        }

        private static StaleLogPurger Purger(IOrganizationService service, TimeSpan? budget = null) =>
            new StaleLogPurger(service, Substitute.For<ITracingService>(), budget ?? TimeSpan.FromMinutes(5));

        [Fact]
        public void Purge_NoStaleLogs_DeletesNothingAndIssuesNoWrite()
        {
            var queries = new List<QueryExpression>();
            var deletes = new List<ExecuteMultipleRequest>();
            var service = ServiceReturning(queries, deletes, r => Response(r.Requests.Count, 0));

            Purger(service).Purge(Guid.NewGuid(), StartedAt).Should().Be(0);

            deletes.Should().BeEmpty();
        }

        [Fact]
        public void Purge_QueriesOnlyLogsOfThatJobModifiedBeforeTheRunStarted()
        {
            var jobId = Guid.NewGuid();
            var queries = new List<QueryExpression>();
            var service = ServiceReturning(queries, new List<ExecuteMultipleRequest>(), r => Response(r.Requests.Count, 0));

            Purger(service).Purge(jobId, StartedAt);

            var q = queries.Should().ContainSingle().Subject;
            q.EntityName.Should().Be(LogTable);
            q.TopCount.Should().Be(StaleLogPurger.BatchSize);
            q.ColumnSet.AllColumns.Should().BeFalse();
            q.Criteria.Conditions.Should().Contain(c =>
                c.AttributeName == "sanic_smlt_ijl_importjobid" && c.Operator == ConditionOperator.Equal && (Guid)c.Values[0] == jobId);
            q.Criteria.Conditions.Should().Contain(c =>
                c.AttributeName == "modifiedon" && c.Operator == ConditionOperator.LessThan && (DateTime)c.Values[0] == StartedAt);
        }

        [Fact]
        public void Purge_DeletesEachBatchWithOneExecuteMultipleAndKeepsGoingUntilNoneRemain()
        {
            var first = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var second = new[] { Guid.NewGuid() };
            var deletes = new List<ExecuteMultipleRequest>();
            var service = ServiceReturning(new List<QueryExpression>(), deletes, r => Response(r.Requests.Count, 0),
                Logs(first), Logs(second));

            var deleted = Purger(service).Purge(Guid.NewGuid(), StartedAt);

            deleted.Should().Be(4);
            deletes.Should().HaveCount(2);
            deletes[0].Requests.Select(r => ((DeleteRequest)r).Target.Id).Should().Equal(first);
            deletes[1].Requests.Select(r => ((DeleteRequest)r).Target.Id).Should().Equal(second);
            deletes.Should().OnlyContain(d => d.Requests.All(r => ((DeleteRequest)r).Target.LogicalName == LogTable));
            deletes.Should().OnlyContain(d => d.Settings.ContinueOnError && d.Settings.ReturnResponses);
        }

        [Fact]
        public void Purge_ABatchWhereEveryDeleteFaults_StopsInsteadOfLoopingForever()
        {
            var queries = new List<QueryExpression>();
            var always = Logs(Guid.NewGuid(), Guid.NewGuid());
            var service = Substitute.For<IOrganizationService>();
            service.RetrieveMultiple(Arg.Do<QueryBase>(q => queries.Add((QueryExpression)q))).Returns(always);
            service.Execute(Arg.Any<OrganizationRequest>()).Returns(ci => Response(2, 2));

            var deleted = Purger(service).Purge(Guid.NewGuid(), StartedAt);

            deleted.Should().Be(0);
            queries.Should().HaveCount(1);
        }

        [Fact]
        public void Purge_PartialFaults_CountsOnlyTheDeletedAndContinues()
        {
            var deletes = new List<ExecuteMultipleRequest>();
            var service = ServiceReturning(new List<QueryExpression>(), deletes, r => Response(r.Requests.Count, 1),
                Logs(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Logs());

            Purger(service).Purge(Guid.NewGuid(), StartedAt).Should().Be(2);
        }

        [Fact]
        public void Purge_WithoutTimeBudget_DoesNotTouchDataverseAtAll()
        {
            var service = Substitute.For<IOrganizationService>();

            Purger(service, TimeSpan.Zero).Purge(Guid.NewGuid(), StartedAt).Should().Be(0);

            service.ReceivedCalls().Should().BeEmpty();
        }
    }
}
