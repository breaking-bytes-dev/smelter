using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="TableLabelsCatalogo"/>: nombres en español (3082) de tablas, columnas y opciones locales. Se cruza
/// contra el Entity.xml REAL de solutions/unpacked (más las columnas de SchemaAdditionsSpec y la tabla de keys HMAC, que no
/// están en el unpacked). Lo que NO se puede romper: el 1033 de tablas y columnas (otras canvas apps lo referencian).
/// </summary>
public class TableLabelsSpecContractTests
{
    private sealed record UnpackedColumn(string Table, string Column, string Type, string DisplayName, IReadOnlyDictionary<int, string> Options);

    private static IEnumerable<UnpackedColumn> UnpackedColumns()
    {
        foreach (var table in SecuritySpec.Tables.All)
        {
            var path = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Entities", table, "Entity.xml");
            foreach (var a in XDocument.Load(path).Descendants("attribute"))
            {
                var logical = (string?)a.Element("LogicalName");
                if (logical is null || !logical.StartsWith("sanic_smlt_", StringComparison.Ordinal)) continue;
                var options = a.Descendants("option").ToDictionary(
                    o => int.Parse((string)o.Attribute("value")!),
                    o => (string?)o.Descendants("label").FirstOrDefault()?.Attribute("description") ?? "");
                yield return new UnpackedColumn(table, logical, (string?)a.Element("Type") ?? "",
                    (string?)a.Descendants("displayname").FirstOrDefault()?.Attribute("description") ?? "", options);
            }
        }
    }

    private static HashSet<(string Table, string Column)> Removed =>
        SchemaCleanupCatalogo.Attributes.Select(a => (a.EntityLogicalName, a.AttributeLogicalName)).ToHashSet();

    private static HashSet<(string Table, string Column, int Value)> RemovedOptions =>
        OptionCleanupCatalogo.Options.Select(o => (o.EntityLogicalName, o.AttributeLogicalName, o.Value)).ToHashSet();

    private static HashSet<(string, string)> KnownColumns() =>
        UnpackedColumns().Select(c => (c.Table, c.Column))
            .Concat(SchemaAdditionsCatalogo.Columns.Select(c => (c.EntityLogicalName, c.SchemaName)))
            .Concat(new[] { (HmacKeyTableSpec.Table, HmacKeyTableSpec.NameColumn), (HmacKeyTableSpec.Table, HmacKeyTableSpec.ValueColumn) })
            .ToHashSet();

    [Fact]
    public void EveryColumnInTheSpec_ExistsInTheSolution_OrIsCreatedByTheAdditionsOrHmacSpecs()
    {
        var known = KnownColumns();

        TableLabelsCatalogo.Columns.Where(c => !known.Contains((c.Table, c.Column))).Should().BeEmpty();
    }

    [Fact]
    public void Columns_HaveNoDuplicates_NoEmptyTexts_AndNoTechnicalPrefixes()
    {
        TableLabelsCatalogo.Columns.Select(c => (c.Table, c.Column)).Should().OnlyHaveUniqueItems();
        TableLabelsCatalogo.Options.Select(o => (o.Table, o.Column, o.Value)).Should().OnlyHaveUniqueItems();
        TableLabelsCatalogo.Tables.Select(t => t.Table).Should().OnlyHaveUniqueItems();

        TableLabelsCatalogo.Columns.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Es) && c.Es == c.Es.Trim());
        TableLabelsCatalogo.Options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Es));
        TableLabelsCatalogo.Tables.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.DisplayEs) && !string.IsNullOrWhiteSpace(t.CollectionEs));
        var all = TableLabelsCatalogo.Columns.Select(c => c.Es)
            .Concat(TableLabelsCatalogo.Options.Select(o => o.Es))
            .Concat(TableLabelsCatalogo.Tables.SelectMany(t => new[] { t.DisplayEs, t.CollectionEs }));
        all.Should().OnlyContain(t => !t.Contains("TBL |") && !t.Contains("SMLT |") && !System.Text.RegularExpressions.Regex.IsMatch(t, @"^\[[A-Z]+\]"));
    }

    [Fact]
    public void EveryTableOfSecuritySpec_AndTheHmacTable_HaveATableLabel()
    {
        TableLabelsCatalogo.Tables.Select(t => t.Table).Should().BeEquivalentTo(SecuritySpec.Tables.All.Append(HmacKeyTableSpec.Table));
    }

    [Fact]
    public void EveryLiveColumnOfTheSixTables_HasASpanishName()
    {
        var specified = TableLabelsCatalogo.Columns.Select(c => (c.Table, c.Column)).ToHashSet();
        var live = UnpackedColumns().Select(c => (c.Table, c.Column)).Where(c => !Removed.Contains(c))
            .Concat(SchemaAdditionsCatalogo.Columns.Select(c => (c.EntityLogicalName, c.SchemaName)));

        live.Where(c => !specified.Contains(c)).Should().BeEmpty("cada columna sanic_smlt_* necesita su nombre 3082 en TableLabelsSpec.cs");
    }

    [Fact]
    public void Spec_NeverTargetsColumnsScheduledForDeletion()
    {
        TableLabelsCatalogo.Columns.Select(c => (c.Table, c.Column)).Should().NotIntersectWith(Removed);
        TableLabelsCatalogo.Options.Select(o => (o.Table, o.Column, o.Value)).Should().NotIntersectWith(RemovedOptions);
    }

    [Fact]
    public void EveryOptionInTheSpec_ExistsInTheSolutionChoice_OrIsAYesNoOfAnAddedBoolean()
    {
        var unpacked = UnpackedColumns().ToDictionary(c => (c.Table, c.Column));

        foreach (var o in TableLabelsCatalogo.Options)
        {
            if (unpacked.TryGetValue((o.Table, o.Column), out var col))
                col.Options.Keys.Should().Contain(o.Value, $"{o.Table}.{o.Column} opción {o.Value}");
            else
            {
                SchemaAdditionsCatalogo.Columns.Should().Contain(c => c.SchemaName == o.Column && c.Kind == SchemaColumnKind.Boolean);
                o.Value.Should().BeOneOf(0, 1);
            }
        }
    }

    [Fact]
    public void EveryLiveOptionOfTheLocalChoices_HasASpanishLabel()
    {
        var specified = TableLabelsCatalogo.Options.Select(o => (o.Table, o.Column, o.Value)).ToHashSet();
        var removed = RemovedOptions;

        foreach (var col in UnpackedColumns().Where(c => c.Options.Count > 0 && !Removed.Contains((c.Table, c.Column))))
            foreach (var value in col.Options.Keys.Where(v => !removed.Contains((col.Table, col.Column, v))))
                specified.Should().Contain((col.Table, col.Column, value), $"{col.Table}.{col.Column} opción {value} ('{col.Options[value]}') sin 3082");

        foreach (var added in SchemaAdditionsCatalogo.Columns.Where(c => c.Kind == SchemaColumnKind.Boolean))
            specified.Should().Contain(new[] { (added.EntityLogicalName, added.SchemaName, 0), (added.EntityLogicalName, added.SchemaName, 1) });
    }

    [Fact]
    public void YesNoColumns_UseSiAndNo()
    {
        var yesNo = UnpackedColumns().Where(c => c.Type == "bit" && !Removed.Contains((c.Table, c.Column)));

        foreach (var col in yesNo)
        {
            TableLabelsCatalogo.Options.Single(o => o.Table == col.Table && o.Column == col.Column && o.Value == 1).Es.Should().Be("Sí");
            TableLabelsCatalogo.Options.Single(o => o.Table == col.Table && o.Column == col.Column && o.Value == 0).Es.Should().Be("No");
        }
    }

    [Fact]
    public void SpanishColumnNames_AreNotLeftWithTheEnglishPrefixOrSuffix()
    {
        // El 1033 versionado lleva prefijos "[IJ] " etc.: el 3082 nunca los copia.
        var unpacked = UnpackedColumns().ToDictionary(c => (c.Table, c.Column), c => c.DisplayName);
        foreach (var c in TableLabelsCatalogo.Columns.Where(c => unpacked.ContainsKey((c.Table, c.Column))))
            c.Es.Should().NotBe(unpacked[(c.Table, c.Column)]);
    }

    // ── Aplicación PURA: el 1033 existente nunca se toca ──────────────────────────────────────

    [Fact]
    public void ApplyToEntity_Adds3082OfNameAndCollection_KeepingThe1033()
    {
        var entity = new EntityMetadata
        {
            DisplayName = new Label("TBL | SMLT | Import Job", 1033),
            DisplayCollectionName = new Label("TBL | SMLT | Import Jobs", 1033),
        };
        var spec = TableLabelsCatalogo.Tables.Single(t => t.Table == SecuritySpec.Tables.ImportJob);

        TableLabelsCatalogo.ApplyToEntity(entity, spec).Should().BeTrue();

        LabelEditor.TextFor(entity.DisplayName, 1033).Should().Be("TBL | SMLT | Import Job");
        LabelEditor.TextFor(entity.DisplayCollectionName, 1033).Should().Be("TBL | SMLT | Import Jobs");
        LabelEditor.TextFor(entity.DisplayName, 3082).Should().Be("Import Job");
        LabelEditor.TextFor(entity.DisplayCollectionName, 3082).Should().Be("Import Jobs");
        TableLabelsCatalogo.ApplyToEntity(entity, spec).Should().BeFalse("idempotente");
    }

    [Fact]
    public void ApplyToAttribute_AndOption_Add3082_KeepingThe1033_Idempotently()
    {
        var attribute = new StringAttributeMetadata { DisplayName = new Label("[IJ] Status", 1033) };
        var option = new OptionMetadata(new Label("CompletedWithErrors", 1033), 3);

        TableLabelsCatalogo.ApplyToAttribute(attribute, "Estado").Should().BeTrue();
        TableLabelsCatalogo.ApplyToOption(option, "Completado con errores").Should().BeTrue();

        LabelEditor.TextFor(attribute.DisplayName, 1033).Should().Be("[IJ] Status");
        LabelEditor.TextFor(attribute.DisplayName, 3082).Should().Be("Estado");
        LabelEditor.TextFor(option.Label, 1033).Should().Be("CompletedWithErrors");
        LabelEditor.TextFor(option.Label, 3082).Should().Be("Completado con errores");
        TableLabelsCatalogo.ApplyToAttribute(attribute, "Estado").Should().BeFalse();
        TableLabelsCatalogo.ApplyToOption(option, "Completado con errores").Should().BeFalse();
    }

    [Fact]
    public void ProductTerms_AreKept_AndTemplatesAreTranslated()
    {
        TableLabelsCatalogo.Tables.Single(t => t.Table == SecuritySpec.Tables.ImportJob).DisplayEs.Should().Be("Import Job");
        TableLabelsCatalogo.Tables.Single(t => t.Table == SecuritySpec.Tables.ImportTemplate).CollectionEs.Should().Be("Templates de importación");
        TableLabelsCatalogo.Tables.Single(t => t.Table == SecuritySpec.Tables.ImportJobLog).CollectionEs.Should().Be("Logs de importación");
    }
}
