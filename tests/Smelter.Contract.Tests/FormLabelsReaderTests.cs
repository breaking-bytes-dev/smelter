using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="FormLabelsReader.MergeSpanishLabels"/>: Dataverse devuelve el formxml solo en el idioma de quien
/// lee; la fusión deja en el documento 1033 el label 3082 de cada tab/section/cell, emparejando por id.
/// </summary>
public class FormLabelsReaderTests
{
    private const string English =
        "<form><tabs><tab id='t1'><labels><label description='Import logs' languagecode='1033' /></labels>" +
        "<columns><column><sections><section id='s1'><labels><label description='General' languagecode='1033' /></labels></section></sections></column></columns>" +
        "</tab></tabs></form>";

    private const string Spanish =
        "<form><tabs><tab id='t1'><labels><label description='Logs de importación' languagecode='3082' /></labels>" +
        "<columns><column><sections><section id='s1'><labels><label description='General' languagecode='3082' /></labels></section></sections></column></columns>" +
        "</tab></tabs></form>";

    private static string? Label(string xml, string id, string lcid) =>
        (string?)XDocument.Parse(xml).Descendants().First(e => (string?)e.Attribute("id") == id)
            .Element("labels")!.Elements("label").FirstOrDefault(l => (string?)l.Attribute("languagecode") == lcid)?.Attribute("description");

    [Fact]
    public void Merge_AddsTheSpanishLabelOfEachElement_AndKeepsTheEnglishOne()
    {
        var merged = FormLabelsReader.MergeSpanishLabels(English, Spanish);

        Label(merged, "t1", "1033").Should().Be("Import logs");
        Label(merged, "t1", "3082").Should().Be("Logs de importación");
        Label(merged, "s1", "1033").Should().Be("General");
        Label(merged, "s1", "3082").Should().Be("General");
    }

    [Fact]
    public void Merge_IsIdempotent_AndNeverDuplicatesThe3082Label()
    {
        var once = FormLabelsReader.MergeSpanishLabels(English, Spanish);
        var twice = FormLabelsReader.MergeSpanishLabels(once, Spanish);

        twice.Should().Be(once);
        XDocument.Parse(twice).Descendants("label").Count(l => (string?)l.Attribute("languagecode") == "3082").Should().Be(2);
    }

    [Fact]
    public void Merge_FedTheSameLanguageTwice_ChangesNothing()
    {
        // Si el idioma no cambió (sin 3082 provisionado), el documento queda igual en contenido.
        var merged = FormLabelsReader.MergeSpanishLabels(English, English);

        Label(merged, "t1", "1033").Should().Be("Import logs");
        Label(merged, "t1", "3082").Should().BeNull();
    }
}
