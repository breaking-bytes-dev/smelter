using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using NSubstitute;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class UpsertWriterTests
    {
        private static TargetRecord Rec(int row, params KeyValuePair<string, object>[] attrs)
        {
            var r = new TargetRecord { TableLogicalName = "city", SourceRowNumber = row };
            foreach (var a in attrs) r.Attributes[a.Key] = a.Value;
            return r;
        }

        private static KeyValuePair<string, object> Attr(string k, object v) =>
            new KeyValuePair<string, object>(k, v);

        private static TableMappingConfig Mapping(string table, string dedup,
            bool createIfNotExists = true, bool updateIfExists = true) =>
            new TableMappingConfig
            {
                Id = Guid.NewGuid(), TargetTable = table, DedupColumns = dedup,
                CreateIfNotExists = createIfNotExists, UpdateIfExists = updateIfExists
            };

        private static ExecuteMultipleResponse OkResponse()
        {
            var r = new ExecuteMultipleResponse();
            r["Responses"] = new ExecuteMultipleResponseItemCollection();
            r["IsFaulted"] = false;
            return r;
        }

        // ── Helpers del probe de existencia (flags CreateIfNotExists/UpdateIfExists, 1.3.0.0) ────

        /// <summary>Respuesta del probe: (index, id) — id null simula el fault "record not found".</summary>
        private static ExecuteMultipleResponse ProbeResponse(params (int Index, Guid? Id)[] items)
        {
            var col = new ExecuteMultipleResponseItemCollection();
            foreach (var (index, id) in items)
            {
                var item = new ExecuteMultipleResponseItem { RequestIndex = index };
                if (id.HasValue)
                {
                    var retrieve = new RetrieveResponse();
                    retrieve["Entity"] = new Entity("city", id.Value);
                    item.Response = retrieve;
                }
                else
                {
                    item.Fault = new OrganizationServiceFault { Message = "record with the specified key values does not exist" };
                }
                col.Add(item);
            }
            var r = new ExecuteMultipleResponse();
            r["Responses"] = col;
            r["IsFaulted"] = true;
            return r;
        }

        /// <summary>
        /// Servicio que responde el 1er ExecuteMultiple (probe de RetrieveRequests) con
        /// <paramref name="probe"/> y captura los siguientes (batch de escritura) devolviendo OK.
        /// </summary>
        private static IOrganizationService FlaggedService(ExecuteMultipleResponse probe, List<ExecuteMultipleRequest> calls)
        {
            var service = Substitute.For<IOrganizationService>();
            service.Execute(Arg.Do<OrganizationRequest>(r => calls.Add((ExecuteMultipleRequest)r)))
                   .Returns(ci =>
                   {
                       var req = (ExecuteMultipleRequest)ci.Arg<OrganizationRequest>();
                       return req.Requests.OfType<RetrieveRequest>().Any() ? probe : OkResponse();
                   });
            return service;
        }

        [Fact]
        public void Write_WithAlternateKey_BuildsUpsertWithKeyAttributes()
        {
            var service = Substitute.For<IOrganizationService>();
            ExecuteMultipleRequest captured = null;
            service.Execute(Arg.Do<OrganizationRequest>(r => captured = r as ExecuteMultipleRequest))
                   .Returns(_ => OkResponse());

            var writer = new UpsertWriter(service);
            var record = Rec(2, Attr("sanic_name", "Springfield"), Attr("sanic_pop", 1000));

            var results = writer.Write(new List<TargetRecord> { record }, Mapping("city", "sanic_name"), false);

            var upserts = captured.Requests.OfType<UpsertRequest>().ToList();
            upserts.Should().ContainSingle();
            var target = (Entity)upserts[0].Target;
            target.KeyAttributes.Should().ContainKey("sanic_name");
            target.KeyAttributes["sanic_name"].Should().Be("Springfield");
            target["sanic_pop"].Should().Be(1000);
            results.Should().ContainSingle();
        }

        [Fact]
        public void Write_WithoutAlternateKey_FallsBackToCreate()
        {
            var service = Substitute.For<IOrganizationService>();
            ExecuteMultipleRequest captured = null;
            service.Execute(Arg.Do<OrganizationRequest>(r => captured = r as ExecuteMultipleRequest))
                   .Returns(_ => OkResponse());

            var writer = new UpsertWriter(service);
            writer.Write(new List<TargetRecord> { Rec(2, Attr("sanic_name", "X")) }, Mapping("city", null), false);

            captured.Requests.OfType<CreateRequest>().Should().ContainSingle();
            captured.Requests.OfType<UpsertRequest>().Should().BeEmpty();
        }

        [Fact]
        public void Write_PreservesParentLookupByAlternateKey()
        {
            var service = Substitute.For<IOrganizationService>();
            ExecuteMultipleRequest captured = null;
            service.Execute(Arg.Do<OrganizationRequest>(r => captured = r as ExecuteMultipleRequest))
                   .Returns(_ => OkResponse());

            var writer = new UpsertWriter(service);
            // Parent lookup resolved by business key, NOT a GUID
            var parentRef = new EntityReference("country", "sanic_code", "AR");
            var record = Rec(2, Attr("sanic_name", "X"), Attr("sanic_countryid", parentRef));

            writer.Write(new List<TargetRecord> { record }, Mapping("city", "sanic_name"), false);

            var target = (Entity)captured.Requests.OfType<UpsertRequest>().Single().Target;
            target["sanic_countryid"].Should().BeOfType<EntityReference>()
                .Which.KeyAttributes.Should().ContainKey("sanic_code");
        }

        [Fact]
        public void Write_DryRun_DoesNotCallServiceAndReturnsSuccess()
        {
            var service = Substitute.For<IOrganizationService>();
            var writer = new UpsertWriter(service);

            var results = writer.Write(
                new List<TargetRecord> { Rec(2, Attr("sanic_name", "X")), Rec(3, Attr("sanic_name", "Y")) },
                Mapping("city", "sanic_name"), true);

            service.DidNotReceive().Execute(Arg.Any<OrganizationRequest>());
            results.Should().HaveCount(2);
            results.Should().OnlyContain(r => r.IsSuccess);
        }

        // ── CreateIfNotExists / UpdateIfExists (1.3.0.0 — antes se cargaban pero se ignoraban) ────

        [Fact]
        public void Write_UpdateIfExistsFalse_ExistingRowSkippedMissingRowCreated()
        {
            var existingId = Guid.NewGuid();
            var calls = new List<ExecuteMultipleRequest>();
            // "A" (índice 0 del probe) existe; "B" (índice 1) no.
            var service = FlaggedService(ProbeResponse((0, existingId), (1, null)), calls);

            var writer = new UpsertWriter(service);
            var results = writer.Write(
                new List<TargetRecord> { Rec(2, Attr("sanic_name", "A")), Rec(3, Attr("sanic_name", "B")) },
                Mapping("city", "sanic_name", updateIfExists: false), false);

            results.Should().HaveCount(2);
            var skipped = results.Single(r => r.Status == ImportLogStatus.Skipped);
            skipped.RowNumber.Should().Be(2);
            skipped.RecordId.Should().Be(existingId, "el skip debe reportar el registro existente que NO se tocó");
            results.Single(r => r.Status == ImportLogStatus.Created).RowNumber.Should().Be(3);

            calls.Should().HaveCount(2, "un probe + un batch de escritura");
            var writeBatch = calls[1];
            writeBatch.Requests.OfType<CreateRequest>().Should().ContainSingle()
                .Which.Target.GetAttributeValue<string>("sanic_name").Should().Be("B");
            writeBatch.Requests.OfType<UpdateRequest>().Should().BeEmpty("la fila existente se skipea, no se actualiza");
            writeBatch.Requests.OfType<UpsertRequest>().Should().BeEmpty();
        }

        [Fact]
        public void Write_CreateIfNotExistsFalse_MissingRowSkippedExistingRowUpdatedByGuid()
        {
            var existingId = Guid.NewGuid();
            var calls = new List<ExecuteMultipleRequest>();
            var service = FlaggedService(ProbeResponse((0, existingId), (1, null)), calls);

            var writer = new UpsertWriter(service);
            var results = writer.Write(
                new List<TargetRecord> { Rec(2, Attr("sanic_name", "A")), Rec(3, Attr("sanic_name", "B")) },
                Mapping("city", "sanic_name", createIfNotExists: false), false);

            results.Single(r => r.Status == ImportLogStatus.Skipped).RowNumber.Should().Be(3);
            results.Single(r => r.Status == ImportLogStatus.Updated).RowNumber.Should().Be(2);

            var writeBatch = calls[1];
            var update = writeBatch.Requests.OfType<UpdateRequest>().Should().ContainSingle().Subject;
            ((Entity)update.Target).Id.Should().Be(existingId, "el update va por el GUID que resolvió el probe");
            writeBatch.Requests.OfType<CreateRequest>().Should().BeEmpty("sin CreateIfNotExists no se crea la fila faltante");
        }

        [Fact]
        public void Write_BothFlagsOn_SingleUpsertRoundTripWithoutProbe()
        {
            var calls = new List<ExecuteMultipleRequest>();
            var service = FlaggedService(ProbeResponse(), calls);

            var writer = new UpsertWriter(service);
            writer.Write(new List<TargetRecord> { Rec(2, Attr("sanic_name", "A")) },
                Mapping("city", "sanic_name"), false);

            calls.Should().HaveCount(1, "con ambos flags prendidos NO hay probe — un solo round-trip");
            calls[0].Requests.OfType<UpsertRequest>().Should().ContainSingle();
        }

        [Fact]
        public void Write_BothFlagsOff_EverythingSkippedWithoutWriting()
        {
            var calls = new List<ExecuteMultipleRequest>();
            var service = FlaggedService(ProbeResponse((0, Guid.NewGuid()), (1, null)), calls);

            var writer = new UpsertWriter(service);
            var results = writer.Write(
                new List<TargetRecord> { Rec(2, Attr("sanic_name", "A")), Rec(3, Attr("sanic_name", "B")) },
                Mapping("city", "sanic_name", createIfNotExists: false, updateIfExists: false), false);

            results.Should().OnlyContain(r => r.Status == ImportLogStatus.Skipped);
            calls.Should().HaveCount(1, "solo el probe — no hay nada que escribir");
        }

        [Fact]
        public void Write_EmptyInput_ReturnsEmptyAndDoesNotCallService()
        {
            var service = Substitute.For<IOrganizationService>();
            var writer = new UpsertWriter(service);

            writer.Write(new List<TargetRecord>(), Mapping("city", "sanic_name"), false)
                .Should().BeEmpty();
            service.DidNotReceive().Execute(Arg.Any<OrganizationRequest>());
        }

        [Fact]
        public void Write_InsertOnly_UsesCreateEvenWhenDedupColumnsAreConfigured_NoProbeNoUpsert()
        {
            var service = Substitute.For<IOrganizationService>();
            var calls = new List<ExecuteMultipleRequest>();
            service.Execute(Arg.Do<OrganizationRequest>(r => calls.Add((ExecuteMultipleRequest)r))).Returns(_ => OkResponse());
            var mapping = Mapping("city", "sanic_name");
            mapping.InsertOnly = true;

            new UpsertWriter(service).Write(new List<TargetRecord> { Rec(2, Attr("sanic_name", "X")) }, mapping, false);

            calls.Should().ContainSingle("sin probe de existencia");
            calls[0].Requests.OfType<CreateRequest>().Should().ContainSingle();
            calls[0].Requests.OfType<UpsertRequest>().Should().BeEmpty();
            calls[0].Requests.OfType<RetrieveRequest>().Should().BeEmpty();
        }

        [Fact]
        public void Write_InsertOnlyWithoutDedupColumns_UsesCreateAndKeepsTheNotIdempotentTraceWarning()
        {
            var service = Substitute.For<IOrganizationService>();
            ExecuteMultipleRequest captured = null;
            service.Execute(Arg.Do<OrganizationRequest>(r => captured = r as ExecuteMultipleRequest)).Returns(_ => OkResponse());
            var tracing = Substitute.For<ITracingService>();
            var mapping = Mapping("city", null);
            mapping.InsertOnly = true;

            new UpsertWriter(service, tracing).Write(new List<TargetRecord> { Rec(2, Attr("sanic_name", "X")) }, mapping, false);

            captured.Requests.OfType<CreateRequest>().Should().ContainSingle();
            tracing.Received().Trace(Arg.Is<string>(m => m.Contains("NOT idempotent")));
        }
    }
}
