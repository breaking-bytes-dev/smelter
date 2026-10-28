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
    public void Descriptions_AreShortBilingualTextsOnExistingSmelterTables()
    {
        foreach (var d in ColumnDescriptionsCatalogo.All)
        {
            SecuritySpec.Tables.All.Should().Contain(d.Entity);
            d.Column.Should().StartWith("sanic_smlt_");
            d.Text.En.Should().NotBeNullOrWhiteSpace(d.Column);
            d.Text.Es.Should().NotBeNullOrWhiteSpace(d.Column);
            d.Text.En.Should().NotBe(d.Text.Es, $"{d.Column}: el inglés (1033) tiene que ser una traducción, no el mismo texto");
            d.Text.En.Length.Should().BeLessThanOrEqualTo(400, $"{d.Column}: 1-2 líneas");
            d.Text.Es.Length.Should().BeLessThanOrEqualTo(400, $"{d.Column}: 1-2 líneas");
        }
    }

    [Fact]
    public void EnglishTexts_AreNotLeftInSpanish()
    {
        // Heurística: el inglés no debe llevar signos ni palabras típicas del español.
        string[] spanishMarks = ["ñ", "¿", "¡", " del ", " los ", " las ", " para ", " con "];
        foreach (var d in ColumnDescriptionsCatalogo.All)
        {
            var en = " " + d.Text.En.ToLowerInvariant() + " ";
            spanishMarks.Where(m => en.Contains(m)).Should()
                .BeEmpty($"{d.Column}: el texto 1033 parece español: {d.Text.En}");
        }
    }

    [Fact]
    public void AddedColumnDescriptions_CoverExactlyTheColumnsOfTheAdditionsSpec_AndAreIncludedInAll()
    {
        ColumnDescriptionsCatalogo.AddedColumnDescriptions.Select(d => (d.Entity, d.Column)).Should()
            .BeEquivalentTo(SchemaAdditionsCatalogo.Columns.Select(c => (c.EntityLogicalName, c.SchemaName)));
        ColumnDescriptionsCatalogo.All.Should().HaveCount(
            ColumnDescriptionsCatalogo.Descriptions.Length + ColumnDescriptionsCatalogo.AddedColumnDescriptions.Length);
        ColumnDescriptionsCatalogo.All.Select(d => (d.Entity, d.Column)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void KeyColumns_ExplainTheirSemantics()
    {
        string Text(string col) => ColumnDescriptionsCatalogo.Descriptions.Single(d => d.Column == col).Text.Es;

        Text("sanic_smlt_tm_dedupcolumns").Should().Contain("DESTINO");
        Text("sanic_smlt_rm_parentdedupcolumns").Should().Contain("CSV");
        Text("sanic_smlt_ij_processingbatch").Should().Contain("offset");
        Text("sanic_smlt_ij_isdryrun").Should().Contain("sin escribir");
        Text("sanic_smlt_it_chunksize").Should().Contain("1000").And.Contain("100").And.Contain("10000");
        Text("sanic_smlt_it_delimiter").Should().Contain("TAB");
    }

    [Fact]
    public void KeyColumns_ExplainTheirSemanticsInEnglishToo()
    {
        string En(string col) => ColumnDescriptionsCatalogo.Descriptions.Single(d => d.Column == col).Text.En;

        En("sanic_smlt_tm_dedupcolumns").Should().Contain("TARGET");
        En("sanic_smlt_ij_processingbatch").Should().Contain("Offset");
        En("sanic_smlt_it_chunksize").Should().Contain("1000").And.Contain("100").And.Contain("10000");
        En("sanic_smlt_it_delimiter").Should().Contain("TAB");
    }

    [Fact]
    public void ChunkSize_IsBoundedTo100Through10000_BySchemaTuning_AndTheDescriptionSaysItIsRejectedNotClamped()
    {
        var tune = SchemaTuningCatalogo.Attributes.Single(a => a.Attribute == "sanic_smlt_it_chunksize");

        tune.Entity.Should().Be(SecuritySpec.Tables.ImportTemplate);
        tune.IntMin.Should().Be(100, "la columna rechaza valores menores (el motor además acota, como defensa)");
        tune.IntMax.Should().Be(10000);

        var d = ColumnDescriptionsCatalogo.Descriptions.Single(c => c.Column == "sanic_smlt_it_chunksize").Text;
        d.En.Should().Contain("between 100 and 10000").And.NotContainEquivalentOf("clamp");
        d.Es.Should().Contain("entre 100 y 10000").And.NotContainEquivalentOf("acota");
        d.En.Should().Contain("empty").And.Contain("1000");
    }

    [Fact]
    public void BilingualDescription_NeedsUpdate_WhenEitherLanguageDiffers()
    {
        var wanted = new Bilingual("en", "es");
        ColumnDescriptionsCatalogo.NeedsUpdate((Microsoft.Xrm.Sdk.Label?)null, wanted).Should().BeTrue();
        ColumnDescriptionsCatalogo.NeedsUpdate(wanted.ToLabel(), wanted).Should().BeFalse();
        ColumnDescriptionsCatalogo.NeedsUpdate(new Microsoft.Xrm.Sdk.Label("en", 1033), wanted).Should().BeTrue("falta el 3082");
        ColumnDescriptionsCatalogo.NeedsUpdate(new Bilingual("viejo", "es").ToLabel(), wanted).Should().BeTrue("el 1033 difiere");
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
