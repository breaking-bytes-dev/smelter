using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// <see cref="FormXmlEditor"/> quitar / ocultar campos (auditoría 1.0.10), contra los main forms PRE-apply congelados
/// (<see cref="PreApplyFixtures"/>; unpacked ya es post-deploy): junction table sale del form del Table Mapping;
/// source type se oculta en el del Import Template.
/// </summary>
public class FormXmlEditorTests
{
    private static XDocument MainForm(string table) => PreApplyFixtures.MainForm(table);

    private static IEnumerable<XElement> Controls(XDocument doc, string field) =>
        doc.Descendants("control").Where(c => (string?)c.Attribute("datafieldname") == field);

    [Fact]
    public void TryRemoveField_RemovesTheWholeRowOfTheJunctionControl_AndKeepsTheRest()
    {
        var doc = MainForm(SecuritySpec.Tables.TableMapping);
        var emptyRowsBefore = doc.Descendants("row").Count(r => !r.Elements("cell").Any());
        var others = doc.Descendants("control").Select(c => (string?)c.Attribute("datafieldname"))
            .Where(f => f != "sanic_smlt_tm_isjunctiontable").ToList();

        FormXmlEditor.TryRemoveField(doc, "sanic_smlt_tm_isjunctiontable").Should().BeTrue();

        Controls(doc, "sanic_smlt_tm_isjunctiontable").Should().BeEmpty();
        doc.Descendants("control").Select(c => (string?)c.Attribute("datafieldname")).Should().Equal(others);
        doc.Descendants("row").Count(r => !r.Elements("cell").Any()).Should().Be(emptyRowsBefore, "no deja filas vacías nuevas");
    }

    [Fact]
    public void TryRemoveField_IsIdempotent()
    {
        var doc = MainForm(SecuritySpec.Tables.TableMapping);
        FormXmlEditor.TryRemoveField(doc, "sanic_smlt_tm_isjunctiontable").Should().BeTrue();
        var after = doc.ToString();

        FormXmlEditor.TryRemoveField(doc, "sanic_smlt_tm_isjunctiontable").Should().BeFalse();

        doc.ToString().Should().Be(after);
    }

    [Fact]
    public void TrySetVisible_False_HidesTheCellButKeepsTheControl()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportTemplate);

        FormXmlEditor.TrySetVisible(doc, "sanic_smlt_it_sourcetype", visible: false).Should().BeTrue();

        var control = Controls(doc, "sanic_smlt_it_sourcetype").Single();
        control.Ancestors("cell").First().Attribute("visible")!.Value.Should().Be("false");
    }

    [Fact]
    public void TrySetVisible_IsIdempotent_AndIgnoresMissingFields()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportTemplate);
        FormXmlEditor.TrySetVisible(doc, "sanic_smlt_it_sourcetype", false).Should().BeTrue();

        FormXmlEditor.TrySetVisible(doc, "sanic_smlt_it_sourcetype", false).Should().BeFalse();
        FormXmlEditor.TrySetVisible(doc, "no_such_field", false).Should().BeFalse();
    }

    [Fact]
    public void TrySetVisible_True_RemovesTheHiddenFlag()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportTemplate);
        FormXmlEditor.TrySetVisible(doc, "sanic_smlt_it_sourcetype", false);

        FormXmlEditor.TrySetVisible(doc, "sanic_smlt_it_sourcetype", true).Should().BeTrue();

        Controls(doc, "sanic_smlt_it_sourcetype").Single().Ancestors("cell").First().Attribute("visible").Should().BeNull();
    }
}
