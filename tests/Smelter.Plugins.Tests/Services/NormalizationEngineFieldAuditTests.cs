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
    /// <summary>
    /// Auditoría de campos (1.0.10): header matching case-insensitive de punta a punta y resolución de la key del
    /// padre con el MISMO pipeline (transform + coerción) que usa el Table Mapping padre.
    /// </summary>
    public class NormalizationEngineFieldAuditTests
    {
        private sealed class FakeKeyProvider : IHmacKeyProvider
        {
            public byte[] GetKey(string keyName) => keyName == "k" ? Enumerable.Range(1, 32).Select(i => (byte)i).ToArray() : null;
        }

        private static NormalizationEngine Engine(IHmacKeyProvider keys = null) =>
            new NormalizationEngine(Substitute.For<IOrganizationService>(), Substitute.For<ITracingService>(), keys);

        private static ColumnMappingConfig Col(string source, string target, ColumnDataType type = ColumnDataType.String,
            string transform = null) =>
            new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), SourceColumn = source, TargetColumn = target, DataType = type, TransformExpression = transform
            };

        private static SourceRow Row(int number, params (string Key, string Value)[] values)
        {
            var row = new SourceRow { RowNumber = number };
            foreach (var v in values) row.Values[v.Key] = v.Value;
            return row;
        }

        private static IUpsertWriter CapturingWriter(Dictionary<string, TargetRecord> byTable)
        {
            var writer = Substitute.For<IUpsertWriter>();
            writer.Write(Arg.Any<IList<TargetRecord>>(), Arg.Any<TableMappingConfig>(), Arg.Any<bool>())
                  .Returns(ci =>
                  {
                      var records = ci.Arg<IList<TargetRecord>>();
                      var mapping = ci.Arg<TableMappingConfig>();
                      byTable[mapping.TargetTable] = records.First();
                      return records.Select(r => ProcessingResult.Success(r.SourceRowNumber, mapping.TargetTable, Guid.NewGuid(), false)).ToList();
                  });
            return writer;
        }

        private static ImportContext SingleTable(params ColumnMappingConfig[] cols)
        {
            var tm = new TableMappingConfig { Id = Guid.NewGuid(), Name = "T", TargetTable = "t", DedupColumns = "x_key", InsertOnly = true }; // el fixture no mapea la key: estos tests no son de dedup
            tm.ColumnMappings.AddRange(cols);
            var ctx = new ImportContext();
            ctx.TableMappings.Add(tm);
            return ctx;
        }

        // ── #5 SourceColumn case-insensitive ─────────────────────────────────────────────────────

        [Fact]
        public void SourceColumn_HeaderWithDifferentCase_StillResolves()
        {
            var ctx = SingleTable(Col("Email", "x_email"));
            var written = new Dictionary<string, TargetRecord>();

            Engine().ProcessBatch(ctx, new List<SourceRow> { Row(2, ("email", "a@b.c")) }, CapturingWriter(written));

            written["t"].Attributes["x_email"].Should().Be("a@b.c");
        }

        [Fact]
        public void SourceColumn_RowWithCaseSensitiveDictionary_StillResolves()
        {
            var ctx = SingleTable(Col("EMAIL", "x_email"), Col(" name ", "x_name"));
            var written = new Dictionary<string, TargetRecord>();
            var row = new SourceRow
            {
                RowNumber = 2,
                Values = new Dictionary<string, string> { ["email"] = "a@b.c", ["name"] = "Ann" }
            };

            Engine().ProcessBatch(ctx, new List<SourceRow> { row }, CapturingWriter(written));

            written["t"].Attributes["x_email"].Should().Be("a@b.c");
            written["t"].Attributes["x_name"].Should().Be("Ann", "SourceColumn se recorta igual que los headers");
        }

        [Fact]
        public void TransformColumnReference_IsCaseInsensitive()
        {
            var ctx = SingleTable(Col(null, "x_full", transform: "CONCAT(FIRST, \" \", last)"));
            var written = new Dictionary<string, TargetRecord>();
            var row = new SourceRow { RowNumber = 2, Values = new Dictionary<string, string> { ["first"] = "Ann", ["LAST"] = "Lee" } };

            Engine().ProcessBatch(ctx, new List<SourceRow> { row }, CapturingWriter(written));

            written["t"].Attributes["x_full"].Should().Be("Ann Lee");
        }

        [Fact]
        public void RequiredColumn_HeaderWithDifferentCase_IsNotReportedEmpty()
        {
            var cm = Col("Email", "x_email");
            cm.IsRequired = true;
            var ctx = SingleTable(cm);

            var results = Engine().ProcessBatch(ctx, new List<SourceRow> { Row(2, ("email", "a@b.c")) },
                CapturingWriter(new Dictionary<string, TargetRecord>()));

            results.Should().OnlyContain(r => r.Status != ImportLogStatus.Failed);
        }

        // ── #8 la key del padre usa el mismo pipeline que el Table Mapping padre ─────────────────

        private static (ImportContext Ctx, TableMappingConfig Parent) ParentChild(ColumnMappingConfig parentKey, string parentDedupColumns)
        {
            var parent = new TableMappingConfig { Id = Guid.NewGuid(), Name = "Parent", TargetTable = "parent", DedupColumns = "x_pankey" };
            parent.ColumnMappings.Add(parentKey);
            var child = new TableMappingConfig { Id = Guid.NewGuid(), Name = "Child", TargetTable = "child", DedupColumns = "x_name" };
            child.ColumnMappings.Add(Col("name", "x_name"));
            var ctx = new ImportContext();
            ctx.TableMappings.Add(child);
            ctx.TableMappings.Add(parent);
            ctx.RelationshipMappings.Add(new RelationshipMappingConfig
            {
                Id = Guid.NewGuid(), Name = "R", ParentTableMappingId = parent.Id, ChildTableMappingId = child.Id,
                RelationshipType = 0, LookupColumnOnChild = "x_parentid", ParentDedupColumns = parentDedupColumns
            });
            return (ctx, parent);
        }

        [Fact]
        public void ParentKey_ProducedByHmac_ResolvesForTheChild()
        {
            var keys = new FakeKeyProvider();
            var (ctx, _) = ParentChild(Col(null, "x_pankey", transform: "HMAC(Pan, \"k\")"), "Pan");
            var written = new Dictionary<string, TargetRecord>();

            Engine(keys).ProcessBatch(ctx, new List<SourceRow> { Row(2, ("Pan", "4111111111111111"), ("name", "c1")) },
                CapturingWriter(written));

            var expected = new TransformEngine(keys).Evaluate("HMAC(Pan, \"k\")", new Dictionary<string, string> { ["Pan"] = "4111111111111111" });
            var parentValue = written["parent"].Attributes["x_pankey"];
            parentValue.Should().Be(expected);
            var lookup = written["child"].Attributes["x_parentid"].Should().BeOfType<EntityReference>().Subject;
            lookup.KeyAttributes["x_pankey"].Should().Be(expected, "el hijo debe usar la misma key derivada que se escribió en el padre");
            lookup.KeyAttributes["x_pankey"].Should().NotBe("4111111111111111");
        }

        [Fact]
        public void ParentKey_WithCoercedType_UsesTheParentDataType()
        {
            var (ctx, _) = ParentChild(Col("code", "x_pankey", ColumnDataType.Integer), "code");
            var written = new Dictionary<string, TargetRecord>();

            Engine().ProcessBatch(ctx, new List<SourceRow> { Row(2, ("code", "007"), ("name", "c1")) }, CapturingWriter(written));

            written["parent"].Attributes["x_pankey"].Should().Be(7);
            var lookup = (EntityReference)written["child"].Attributes["x_parentid"];
            lookup.KeyAttributes["x_pankey"].Should().Be(7);
        }

        [Fact]
        public void ParentKey_WithoutTransform_UsesTheCsvColumnNamedByTheRelationship()
        {
            // El mapping del padre lee 'parent_code'; el hijo trae la columna 'pcode' y el Relationship Mapping la nombra.
            var (ctx, _) = ParentChild(Col("parent_code", "x_pankey"), " PCODE ");
            var written = new Dictionary<string, TargetRecord>();

            Engine().ProcessBatch(ctx, new List<SourceRow> { Row(2, ("pcode", "AR"), ("name", "c1")) }, CapturingWriter(written));

            var lookup = (EntityReference)written["child"].Attributes["x_parentid"];
            lookup.KeyAttributes["x_pankey"].Should().Be("AR");
        }

        [Fact]
        public void ParentKey_ParentDefaultValue_AppliesWhenTheChildCellIsBlank()
        {
            var key = Col("code", "x_pankey");
            key.DefaultValue = "GEN";
            var (ctx, _) = ParentChild(key, "code");
            var written = new Dictionary<string, TargetRecord>();

            Engine().ProcessBatch(ctx, new List<SourceRow> { Row(2, ("code", ""), ("name", "c1")) }, CapturingWriter(written));

            written["parent"].Attributes["x_pankey"].Should().Be("GEN");
            ((EntityReference)written["child"].Attributes["x_parentid"]).KeyAttributes["x_pankey"].Should().Be("GEN");
        }

        [Fact]
        public void ParentKey_InvalidForTheParentType_FailsTheRowAsTypeError()
        {
            var (ctx, _) = ParentChild(Col("code", "x_pankey", ColumnDataType.Integer), "code");

            var results = Engine().ProcessBatch(ctx, new List<SourceRow> { Row(2, ("code", "abc"), ("name", "c1")) },
                CapturingWriter(new Dictionary<string, TargetRecord>()));

            results.Should().Contain(r => r.Status == ImportLogStatus.Failed && r.Category == ErrorCategory.TypeError);
        }
    }
}
