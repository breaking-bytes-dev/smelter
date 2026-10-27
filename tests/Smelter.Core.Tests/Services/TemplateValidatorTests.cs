using System;
using System.Linq;
using FluentAssertions;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class TemplateValidatorTests
    {
        private static ColumnMappingConfig Col(string target, string source = null, ColumnDataType type = ColumnDataType.String,
            string lookupTable = null) =>
            new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), Name = "cm_" + target, SourceColumn = source ?? target, TargetColumn = target,
                DataType = type, LookupTable = lookupTable
            };

        private static TableMappingConfig Table(string name, string dedup, params ColumnMappingConfig[] cols)
        {
            var tm = new TableMappingConfig { Id = Guid.NewGuid(), Name = name, TargetTable = name.ToLowerInvariant(), DedupColumns = dedup };
            tm.ColumnMappings.AddRange(cols);
            return tm;
        }

        private static ImportContext Ctx(params TableMappingConfig[] tables)
        {
            var ctx = new ImportContext();
            ctx.TableMappings.AddRange(tables);
            return ctx;
        }

        private static RelationshipMappingConfig Rel(TableMappingConfig parent, TableMappingConfig child,
            string lookup = "x_parentid", string parentDedup = "k", int type = 0) =>
            new RelationshipMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Rel", ParentTableMappingId = parent.Id, ChildTableMappingId = child.Id,
                RelationshipType = type, LookupColumnOnChild = lookup, ParentDedupColumns = parentDedup
            };

        [Fact]
        public void Validate_ConsistentTemplate_HasNoErrors()
        {
            var parent = Table("Parent", "k", Col("k"));
            var child = Table("Child", "c", Col("c"));
            var ctx = Ctx(parent, child);
            ctx.RelationshipMappings.Add(Rel(parent, child));

            TemplateValidator.Validate(ctx).Should().BeEmpty();
        }

        [Fact]
        public void Validate_DedupColumnWithoutColumnMapping_NamesMappingAndColumn()
        {
            var ctx = Ctx(Table("Facturas", "k, missing", Col("k")));

            var errors = TemplateValidator.Validate(ctx);

            errors.Should().ContainSingle().Which.Should().Contain("Facturas").And.Contain("missing");
        }

        [Fact]
        public void Validate_DedupColumnMatchesTargetColumnIgnoringCaseAndSpaces()
        {
            var ctx = Ctx(Table("T", " K ,b", Col("k"), Col("B")));

            TemplateValidator.Validate(ctx).Should().BeEmpty();
        }

        [Fact]
        public void Validate_InsertOnlyMappingWithoutColumns_IsNotChecked()
        {
            var tm = Table("T", null);
            tm.InsertOnly = true;

            TemplateValidator.Validate(Ctx(tm)).Should().BeEmpty();
        }

        [Fact]
        public void Validate_LookupColumnWithoutLookupTable_IsAnError()
        {
            var ctx = Ctx(Table("T", "k", Col("k"), Col("owner", type: ColumnDataType.Lookup)));

            TemplateValidator.Validate(ctx).Should().ContainSingle().Which.Should().Contain("owner").And.Contain("Lookup Table");
        }

        [Fact]
        public void Validate_LookupColumnWithLookupTable_IsFine()
        {
            var ctx = Ctx(Table("T", "k", Col("k"), Col("owner", type: ColumnDataType.Lookup, lookupTable: "account")));

            TemplateValidator.Validate(ctx).Should().BeEmpty();
        }

        [Fact]
        public void Validate_OneToManyWithoutLookupColumnOnChild_IsAnError()
        {
            var parent = Table("Parent", "k", Col("k"));
            var child = Table("Child", "c", Col("c"));
            var ctx = Ctx(parent, child);
            ctx.RelationshipMappings.Add(Rel(parent, child, lookup: "  "));

            TemplateValidator.Validate(ctx).Should().ContainSingle().Which.Should().Contain("Rel").And.Contain("Lookup column on child");
        }

        [Fact]
        public void Validate_ManyToMany_IsRejected()
        {
            var parent = Table("Parent", "k", Col("k"));
            var child = Table("Child", "c", Col("c"));
            var ctx = Ctx(parent, child);
            ctx.RelationshipMappings.Add(Rel(parent, child, type: 1));

            TemplateValidator.Validate(ctx).Should().ContainSingle().Which.Should().Contain("ManyToMany");
        }

        [Fact]
        public void Validate_ParentWithoutDedupColumns_IsAnError()
        {
            var parent = Table("Parent", null, Col("k"));
            parent.InsertOnly = true;
            var child = Table("Child", "c", Col("c"));
            var ctx = Ctx(parent, child);
            ctx.RelationshipMappings.Add(Rel(parent, child));

            TemplateValidator.Validate(ctx).Should().ContainSingle().Which.Should().Contain("Parent").And.Contain("no dedup columns");
        }

        [Fact]
        public void Validate_ParentDedupCountMismatch_IsAnError()
        {
            var parent = Table("Parent", "k1,k2", Col("k1"), Col("k2"));
            var child = Table("Child", "c", Col("c"));
            var ctx = Ctx(parent, child);
            ctx.RelationshipMappings.Add(Rel(parent, child, parentDedup: "k1"));

            TemplateValidator.Validate(ctx).Should().ContainSingle().Which.Should().Contain("1").And.Contain("2").And.Contain("Parent Dedup Columns");
        }

        [Fact]
        public void Validate_ParentDedupColumnsAreTrimmedBeforeCounting()
        {
            var parent = Table("Parent", "k1,k2", Col("k1"), Col("k2"));
            var child = Table("Child", "c", Col("c"));
            var ctx = Ctx(parent, child);
            ctx.RelationshipMappings.Add(Rel(parent, child, parentDedup: " a , b ,"));

            TemplateValidator.Validate(ctx).Should().BeEmpty();
        }

        [Fact]
        public void Validate_ReportsEveryProblem()
        {
            var ctx = Ctx(Table("A", "x", Col("k")), Table("B", "k", Col("k"), Col("l", type: ColumnDataType.Lookup)));

            TemplateValidator.Validate(ctx).Should().HaveCount(2);
        }
    }
}
