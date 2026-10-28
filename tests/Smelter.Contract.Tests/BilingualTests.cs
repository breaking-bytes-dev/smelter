using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="Bilingual"/> / <see cref="LabelEditor"/> / <see cref="LocLabels"/>: inglés bajo 1033 (base),
/// español bajo 3082; editar el 3082 de un label NUNCA toca el 1033 existente.
/// </summary>
public class BilingualTests
{
    [Fact]
    public void For_ReturnsEnglishFor1033_AndSpanishFor3082_AndRejectsOtherLanguages()
    {
        var b = new Bilingual("Hello", "Hola");

        b.For(Lcids.English).Should().Be("Hello");
        b.For(Lcids.Spanish).Should().Be("Hola");
        Lcids.English.Should().Be(1033);
        Lcids.Spanish.Should().Be(3082);
        ((Func<string>)(() => b.For(1046))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToLabel_CarriesBothLanguages_With1033AsTheUserLabel()
    {
        var label = new Bilingual("Hello", "Hola").ToLabel();

        label.LocalizedLabels.Select(l => (l.LanguageCode, l.Label)).Should().BeEquivalentTo(new[] { (1033, "Hello"), (3082, "Hola") });
        LabelEditor.TextFor(label, 3082).Should().Be("Hola");
        LabelEditor.TextFor(null, 3082).Should().BeNull();
    }

    [Fact]
    public void LabelEditorSet_AddsThe3082_AndKeepsThe1033Untouched()
    {
        var existing = new Label("TBL | SMLT | Import Job", 1033);

        LabelEditor.Set(existing, 3082, "Import Job", out var result).Should().BeTrue();

        LabelEditor.TextFor(result, 1033).Should().Be("TBL | SMLT | Import Job");
        LabelEditor.TextFor(result, 3082).Should().Be("Import Job");
    }

    [Fact]
    public void LabelEditorSet_UpdatesOnlyWhenTheTextDiffers_Idempotently()
    {
        var label = new Bilingual("a", "b").ToLabel();

        LabelEditor.Set(label, 3082, "b", out _).Should().BeFalse();
        LabelEditor.Set(label, 3082, "c", out var result).Should().BeTrue();
        LabelEditor.Set(result, 3082, "c", out _).Should().BeFalse();
        LabelEditor.TextFor(result, 1033).Should().Be("a");
    }

    [Fact]
    public void LabelEditorSet_OnANullLabel_CreatesOne()
    {
        LabelEditor.Set(null, 3082, "x", out var result).Should().BeTrue();

        LabelEditor.TextFor(result, 3082).Should().Be("x");
    }

    [Fact]
    public void LocLabelsMatches_RequiresBothLanguagesExactly()
    {
        var wanted = new Bilingual("en", "es");

        LocLabels.Matches(new Dictionary<int, string> { [1033] = "en", [3082] = "es" }, wanted).Should().BeTrue();
        LocLabels.Matches(new Dictionary<int, string> { [1033] = "en" }, wanted).Should().BeFalse();
        LocLabels.Matches(new Dictionary<int, string> { [1033] = "en", [3082] = "otro" }, wanted).Should().BeFalse();
        LocLabels.Matches(new Dictionary<int, string> { [1033] = "viejo", [3082] = "es" }, wanted).Should().BeFalse();
        LocLabels.Matches(new Dictionary<int, string>(), wanted).Should().BeFalse();
    }

    [Fact]
    public void FormXmlEditorTrySetLabels_AddsThe3082_KeepsTheEnglish_AndIsIdempotent()
    {
        var doc = XDocument.Parse("<form><tabs><tab><labels><label description=\"General\" languagecode=\"1033\" /></labels>" +
            "<columns><column><sections><section><labels><label description=\"General\" languagecode=\"1033\" /></labels></section></sections></column></columns></tab></tabs></form>");

        FormXmlEditor.TrySetLabels(doc, "General", "General").Should().BeTrue();
        FormXmlEditor.TrySetLabels(doc, "General", "General").Should().BeFalse();

        doc.Descendants("labels").Should().OnlyContain(l => l.Elements("label").Count() == 2);
        FormXmlEditor.TrySetLabels(doc, "Nope", "Nada").Should().BeFalse();
    }

    [Fact]
    public void FormXmlEditorTrySetLabels_DoesNotTouchFieldCellLabels()
    {
        var doc = XDocument.Parse("<form><tabs><tab><labels><label description=\"X\" languagecode=\"1033\" /></labels><columns><column><sections><section>" +
            "<labels><label description=\"X\" languagecode=\"1033\" /></labels><rows><row><cell><labels><label description=\"General\" languagecode=\"1033\" /></labels>" +
            "<control id=\"f\" datafieldname=\"f\" /></cell></row></rows></section></sections></column></columns></tab></tabs></form>");

        FormXmlEditor.TrySetLabels(doc, "General", "General").Should().BeFalse("la celda de un campo no es pestaña, sección ni subgrid");
    }
}
