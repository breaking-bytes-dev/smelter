using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="ColumnDescriptionsCatalogo"/> (#13/#14/#19): TODA columna custom de las 6 tablas de
/// Smelter tiene Description (tooltip en la MDA). Se cruza contra el Entity.xml REAL de solutions/unpacked para que una
/// columna nueva no quede sin descripción sin que un test lo note.
/// </summary>
public class ColumnDescriptionsSpecContractTests
{
    private static IEnumerable<(string Entity, string Column)> UnpackedCustomColumns()
    {
        foreach (var table in SecuritySpec.Tables.All)
        {
            var path = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Entities", table, "Entity.xml");
            foreach (var a in XDocument.Load(path).Descendants("attribute"))
            {
                var logical = (string?)a.Element("LogicalName");
                if (logical is not null && logical.StartsWith("sanic_smlt_", StringComparison.Ordinal))
                    yield return (table, logical);
            }
        }
    }

    [Fact]
    public void EveryCustomColumnOfTheSixTables_HasADescription_OrIsCreatedWithOneByTheAdditionsSpec()
    {
        var described = ColumnDescriptionsCatalogo.Descriptions.Select(d => (d.Entity, d.Column))
            .Concat(SchemaAdditionsCatalogo.Columns.Select(c => (c.EntityLogicalName, c.SchemaName)))
            .ToHashSet();
        var removed = SchemaCleanupCatalogo.Attributes.Select(a => (a.EntityLogicalName, a.AttributeLogicalName)).ToHashSet();

        var missing = UnpackedCustomColumns().Where(c => !removed.Contains(c) && !described.Contains(c)).ToList();

        missing.Should().BeEmpty("cada columna custom necesita Description (tooltip); agregala en ColumnDescriptionsSpec.cs");
    }

    [Fact]
    public void Descriptions_NeverTargetColumnsScheduledForDeletion_NorDuplicateAnAdditionsColumn()
    {
        var removed = SchemaCleanupCatalogo.Attributes.Select(a => a.AttributeLogicalName).ToHashSet();
        var added = SchemaAdditionsCatalogo.Columns.Select(c => c.SchemaName).ToHashSet();

        ColumnDescriptionsCatalogo.Descriptions.Should().OnlyContain(d => !removed.Contains(d.Column) && !added.Contains(d.Column));
        ColumnDescriptionsCatalogo.Descriptions.Select(d => (d.Entity, d.Column)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Descriptions_AreShortSpanishTextsOnExistingSmelterTables()
    {
        foreach (var d in ColumnDescriptionsCatalogo.Descriptions)
        {
            SecuritySpec.Tables.All.Should().Contain(d.Entity);
            d.Column.Should().StartWith("sanic_smlt_");
            d.Text.Should().NotBeNullOrWhiteSpace();
            d.Text.Length.Should().BeLessThanOrEqualTo(400, $"{d.Column}: 1-2 líneas");
        }
    }

    [Fact]
    public void KeyColumns_ExplainTheirSemantics()
    {
        string Text(string col) => ColumnDescriptionsCatalogo.Descriptions.Single(d => d.Column == col).Text;

        Text("sanic_smlt_tm_dedupcolumns").Should().Contain("DESTINO");
        Text("sanic_smlt_rm_parentdedupcolumns").Should().Contain("CSV");
        Text("sanic_smlt_ij_processingbatch").Should().Contain("offset");
        Text("sanic_smlt_ij_isdryrun").Should().Contain("sin escribir");
        Text("sanic_smlt_it_chunksize").Should().Contain("1000").And.Contain("100").And.Contain("10000");
        Text("sanic_smlt_it_delimiter").Should().Contain("TAB");
    }

    [Fact]
    public void ChunkSize_IsBoundedTo0Through10000_BySchemaTuning()
    {
        var tune = SchemaTuningCatalogo.Attributes.Single(a => a.Attribute == "sanic_smlt_it_chunksize");

        tune.Entity.Should().Be(SecuritySpec.Tables.ImportTemplate);
        tune.IntMin.Should().Be(0, "0 = usar el default del sistema (1000)");
        tune.IntMax.Should().Be(10000);
    }

    [Fact]
    public void DescriptionsNeedUpdate_OnlyWhenTheCurrentLabelDiffers()
    {
        ColumnDescriptionsCatalogo.NeedsUpdate(null, "x").Should().BeTrue();
        ColumnDescriptionsCatalogo.NeedsUpdate("", "x").Should().BeTrue();
        ColumnDescriptionsCatalogo.NeedsUpdate("viejo", "x").Should().BeTrue();
        ColumnDescriptionsCatalogo.NeedsUpdate("x", "x").Should().BeFalse();
    }
}
