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

        private static TableMappingConfig Mapping(string table, string dedup) =>
            new TableMappingConfig { Id = Guid.NewGuid(), TargetTable = table, DedupColumns = dedup };

        private static ExecuteMultipleResponse OkResponse()
        {
            var r = new ExecuteMultipleResponse();
            r["Responses"] = new ExecuteMultipleResponseItemCollection();
            r["IsFaulted"] = false;
            return r;
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

        [Fact]
        public void Write_EmptyInput_ReturnsEmptyAndDoesNotCallService()
        {
            var service = Substitute.For<IOrganizationService>();
            var writer = new UpsertWriter(service);

            writer.Write(new List<TargetRecord>(), Mapping("city", "sanic_name"), false)
                .Should().BeEmpty();
            service.DidNotReceive().Execute(Arg.Any<OrganizationRequest>());
        }
    }
}
