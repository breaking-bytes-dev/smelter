using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class NormalizationEngineBatchTests
    {
        private static ColumnMappingConfig Col(string source, string target,
            ColumnDataType type = ColumnDataType.String, bool required = false, string def = null)
        {
            return new ColumnMappingConfig
            {
                Id = Guid.NewGuid(),
                SourceColumn = source,
                TargetColumn = target,
                DataType = type,
                IsRequired = required,
                DefaultValue = def
            };
        }

        private static SourceRow Row(int number, params (string Key, string Value)[] values)
        {
            var row = new SourceRow { RowNumber = number };
            foreach (var v in values) row.Values[v.Key] = v.Value;
            return row;
        }

        private static NormalizationEngine Engine() =>
            new NormalizationEngine(Substitute.For<IOrganizationService>(), Substitute.For<ITracingService>());

        private static IUpsertWriter CapturingWriter(Action<TableMappingConfig, IList<TargetRecord>> onWrite)
        {
            var writer = Substitute.For<IUpsertWriter>();
            writer.Write(Arg.Any<IList<TargetRecord>>(), Arg.Any<TableMappingConfig>(), Arg.Any<bool>())
                  .Returns(ci =>
                  {
                      var records = ci.Arg<IList<TargetRecord>>();
                      var mapping = ci.Arg<TableMappingConfig>();
                      onWrite(mapping, records);
                      return records
                          .Select(r => ProcessingResult.Success(r.SourceRowNumber, mapping.TargetTable, Guid.NewGuid(), false))
                          .ToList();
                  });
            return writer;
        }

        [Fact]
        public void ProcessBatch_ProcessesParentBeforeChild_AndResolvesParentLookupByAlternateKey()
        {
            var country = new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Country", TargetTable = "country", DedupColumns = "sanic_code",
                ColumnMappings = { Col("country_code", "sanic_code") }
            };
            var city = new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "City", TargetTable = "city", DedupColumns = "sanic_name",
                ColumnMappings = { Col("city_name", "sanic_name") }
            };
            var rel = new RelationshipMappingConfig
            {
                Id = Guid.NewGuid(), ParentTableMappingId = country.Id, ChildTableMappingId = city.Id,
                RelationshipType = 0, LookupColumnOnChild = "sanic_countryid", ParentDedupColumns = "country_code"
            };

            var context = new ImportContext { ImportJobId = Guid.NewGuid() };
            context.TableMappings.Add(city);     // reversed to prove ordering
            context.TableMappings.Add(country);
            context.RelationshipMappings.Add(rel);

            var order = new List<string>();
            TargetRecord cityRecord = null;
            var writer = CapturingWriter((map, recs) =>
            {
                order.Add(map.TargetTable);
                if (map.TargetTable == "city") cityRecord = recs.FirstOrDefault();
            });

            var results = Engine().ProcessBatch(context,
                new List<SourceRow> { Row(2, ("country_code", "AR"), ("city_name", "Buenos Aires")) }, writer);

            order.Should().ContainInOrder("country", "city");
            cityRecord.Should().NotBeNull();
            var lookup = cityRecord.Attributes["sanic_countryid"].Should().BeOfType<EntityReference>().Subject;
            lookup.LogicalName.Should().Be("country");
            lookup.KeyAttributes.Should().ContainKey("sanic_code");
            lookup.KeyAttributes["sanic_code"].Should().Be("AR");
            results.Should().HaveCount(2);
        }

        [Fact]
        public void ProcessBatch_RequiredFieldWithDefault_UsesDefaultInsteadOfSkipping()
        {
            // Regression for #6: default must be applied BEFORE required validation.
            var table = new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Acc", TargetTable = "account", DedupColumns = "sanic_key",
                ColumnMappings =
                {
                    Col("k", "sanic_key"),
                    Col("status", "sanic_status", required: true, def: "active")
                }
            };
            var context = new ImportContext { ImportJobId = Guid.NewGuid() };
            context.TableMappings.Add(table);

            TargetRecord captured = null;
            var writer = CapturingWriter((map, recs) => captured = recs.FirstOrDefault());

            Engine().ProcessBatch(context, new List<SourceRow> { Row(2, ("k", "K1"), ("status", "")) }, writer);

            captured.Should().NotBeNull("a required field with a default must NOT skip the row");
            captured.Attributes["sanic_status"].Should().Be("active");
        }

        [Fact]
        public void ProcessBatch_RequiredFieldMissingNoDefault_SkipsRowAsFailure()
        {
            var table = new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Acc", TargetTable = "account", DedupColumns = "sanic_key",
                ColumnMappings =
                {
                    Col("k", "sanic_key"),
                    Col("status", "sanic_status", required: true)
                }
            };
            var context = new ImportContext { ImportJobId = Guid.NewGuid() };
            context.TableMappings.Add(table);

            var writer = CapturingWriter((_, __) => { });

            var results = Engine().ProcessBatch(context, new List<SourceRow> { Row(2, ("k", "K1"), ("status", "")) }, writer);

            results.Should().ContainSingle();
            results[0].Status.Should().Be(ImportLogStatus.Failed);
            results[0].ErrorMessage.Should().Contain("sanic_status");
        }

        [Fact]
        public void ProcessBatch_ExternalLookupByBusinessKey_UsesAlternateKey()
        {
            // Regression for #7: a non-GUID lookup value must resolve via LookupMatchColumn (alternate key),
            // not be silently dropped.
            var table = new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Order", TargetTable = "order", DedupColumns = "sanic_number",
                ColumnMappings =
                {
                    Col("number", "sanic_number"),
                    new ColumnMappingConfig
                    {
                        Id = Guid.NewGuid(), SourceColumn = "cust_code", TargetColumn = "sanic_customerid",
                        DataType = ColumnDataType.Lookup, LookupTable = "account", LookupMatchColumn = "sanic_acctcode"
                    }
                }
            };
            var context = new ImportContext { ImportJobId = Guid.NewGuid() };
            context.TableMappings.Add(table);

            TargetRecord captured = null;
            var writer = CapturingWriter((_, recs) => captured = recs.FirstOrDefault());

            Engine().ProcessBatch(context,
                new List<SourceRow> { Row(2, ("number", "N1"), ("cust_code", "ACME")) }, writer);

            captured.Should().NotBeNull();
            var lookup = captured.Attributes["sanic_customerid"].Should().BeOfType<EntityReference>().Subject;
            lookup.LogicalName.Should().Be("account");
            lookup.KeyAttributes.Should().ContainKey("sanic_acctcode");
            lookup.KeyAttributes["sanic_acctcode"].Should().Be("ACME");
        }
    }
}
