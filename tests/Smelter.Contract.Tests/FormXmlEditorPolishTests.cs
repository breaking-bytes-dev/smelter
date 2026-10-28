using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// <see cref="FormXmlEditor"/> tanda 2 de la auditoría de campos: read-only, campos nuevos con control propio,
/// pestaña con subgrid y related-records filtering, contra los main forms PRE-apply congelados (<see cref="PreApplyFixtures"/>; unpacked ya es post-deploy).
/// </summary>
public class FormXmlEditorPolishTests
{
    private const string Text = "{4273EDBD-AC1D-40d3-9FB2-095C621B552D}";
    private const string WholeNumber = "{C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F}";

    private static XDocument MainForm(string table) => PreApplyFixtures.MainForm(table);

    private static XElement Control(XDocument doc, string field) =>
        doc.Descendants("control").Single(c => (string?)c.Attribute("datafieldname") == field);

    // ── TrySetDisabled ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TrySetDisabled_MarksTheControlReadOnly_AndIsIdempotent()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJob);

        FormXmlEditor.TrySetDisabled(doc, "sanic_smlt_ij_status", true).Should().BeTrue();
        FormXmlEditor.TrySetDisabled(doc, "sanic_smlt_ij_status", true).Should().BeFalse();

        ((string?)Control(doc, "sanic_smlt_ij_status").Attribute("disabled")).Should().Be("true");
    }

    [Fact]
    public void TrySetDisabled_ControlWithoutDisabledAttribute_GetsIt()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJob);
        Control(doc, "sanic_smlt_ij_name").Attribute("disabled").Should().BeNull("precondición: el control del nombre no lo trae");

        FormXmlEditor.TrySetDisabled(doc, "sanic_smlt_ij_name", true).Should().BeTrue();

        ((string?)Control(doc, "sanic_smlt_ij_name").Attribute("disabled")).Should().Be("true");
    }

    [Fact]
    public void TrySetDisabled_FieldNotOnTheForm_ReturnsFalse()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJob);
        var before = doc.ToString();

        FormXmlEditor.TrySetDisabled(doc, "sanic_smlt_ij_bytecursor", true).Should().BeFalse();

        doc.ToString().Should().Be(before);
    }

    // ── TryAddCellAfter con control propio ───────────────────────────────────────────────────

    [Fact]
    public void TryAddCellAfter_WithClassIdAndDisabled_OverridesTheClonedControl()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJobLog);

        FormXmlEditor.TryAddCellAfter(doc, "sanic_smlt_ijl_id", "sanic_smlt_ijl_rownumber", "[IJL] Row Number",
            classId: WholeNumber, disabled: true).Should().BeTrue();

        var c = Control(doc, "sanic_smlt_ijl_rownumber");
        ((string?)c.Attribute("classid")).Should().Be(WholeNumber);
        ((string?)c.Attribute("disabled")).Should().Be("true");
        c.Descendants("label").Count().Should().Be(0);
        c.Ancestors("cell").First().Descendants("label").Single().Attribute("description")!.Value.Should().Be("[IJL] Row Number");
    }

    [Fact]
    public void TryAddCellAfter_WithoutOverrides_KeepsTheAnchorClassId()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJobLog);

        FormXmlEditor.TryAddCellAfter(doc, "sanic_smlt_ijl_id", "sanic_smlt_ijl_targettable", "[IJL] Target Table");

        ((string?)Control(doc, "sanic_smlt_ijl_targettable").Attribute("classid")).Should().Be(Text);
    }

    // ── TryAddSubgridTab ─────────────────────────────────────────────────────────────────────

    private const string LogsRelationship = "sanic_tbl_smlt_importjoblog_smlt_ijl_importjobid_sanic_tbl_smlt_importjob";
    private const string LogView = "{6752F00A-34E0-43FD-BA73-9A9CBEC00991}";

    [Fact]
    public void TryAddSubgridTab_AddsAFullTabAfterTheExistingOnes_WithTheSubgridParameters()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJob);
        var tabsBefore = doc.Descendants("tab").Count();

        FormXmlEditor.TryAddSubgridTab(doc, "Logs de importación", "Subgrid_logs", LogsRelationship,
            SecuritySpec.Tables.ImportJobLog, LogView).Should().BeTrue();

        doc.Descendants("tab").Count().Should().Be(tabsBefore + 1);
        var tab = doc.Descendants("tab").Last();
        tab.Element("labels")!.Element("label")!.Attribute("description")!.Value.Should().Be("Logs de importación");
        var control = tab.Descendants("control").Single();
        control.Attribute("id")!.Value.Should().Be("Subgrid_logs");
        control.Attribute("classid")!.Value.Should().Be("{E7A81278-8635-4D9E-8D4D-59480B391C5B}");
        control.Attribute("indicationOfSubgrid")!.Value.Should().Be("true");
        var p = control.Element("parameters")!;
        p.Element("RelationshipName")!.Value.Should().Be(LogsRelationship);
        p.Element("TargetEntityType")!.Value.Should().Be(SecuritySpec.Tables.ImportJobLog);
        p.Element("ViewId")!.Value.Should().Be(LogView);
        p.Element("ViewIds")!.Value.Should().Be(LogView);
        var ids = doc.Descendants().Select(e => (string?)e.Attribute("id")).Where(i => i is { Length: > 0 }).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TryAddSubgridTab_IsIdempotent_BySubgridId()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJob);
        FormXmlEditor.TryAddSubgridTab(doc, "Logs", "Subgrid_logs", LogsRelationship, SecuritySpec.Tables.ImportJobLog, LogView);
        var after = doc.ToString();

        FormXmlEditor.TryAddSubgridTab(doc, "Logs", "Subgrid_logs", LogsRelationship, SecuritySpec.Tables.ImportJobLog, LogView)
            .Should().BeFalse();

        doc.ToString().Should().Be(after);
    }

    [Fact]
    public void TryAddSubgridTab_FormWithoutTabs_ReturnsFalse()
    {
        var doc = XDocument.Parse("<form />");

        FormXmlEditor.TryAddSubgridTab(doc, "x", "s", "r", "e", "{00000000-0000-0000-0000-000000000000}").Should().BeFalse();
    }

    // ── TrySetLookupFilter (related-records filtering) ───────────────────────────────────────

    private const string TmToTemplate = "sanic_tbl_smlt_tablemapping_smlt_tm_importtemplateid_sanic_tbl_smlt_importtemplate";

    [Fact]
    public void TrySetLookupFilter_WritesTheThreeFilteringParameters_AndIsIdempotent()
    {
        var doc = MainForm(SecuritySpec.Tables.RelationshipMapping);

        FormXmlEditor.TrySetLookupFilter(doc, "sanic_smlt_rm_parenttablemappingid", TmToTemplate, "sanic_smlt_rm_importtemplateid")
            .Should().BeTrue();
        var snapshot = doc.ToString();
        FormXmlEditor.TrySetLookupFilter(doc, "sanic_smlt_rm_parenttablemappingid", TmToTemplate, "sanic_smlt_rm_importtemplateid")
            .Should().BeFalse();

        doc.ToString().Should().Be(snapshot);
        var p = Control(doc, "sanic_smlt_rm_parenttablemappingid").Element("parameters")!;
        p.Element("FilterRelationshipName")!.Value.Should().Be(TmToTemplate);
        p.Element("DependentAttributeName")!.Value.Should().Be("sanic_smlt_rm_importtemplateid");
        p.Element("DependentAttributeType")!.Value.Should().Be("lookup");
        Control(doc, "sanic_smlt_rm_childtablemappingid").Element("parameters").Should().BeNull("el otro lookup no se toca");
    }

    [Fact]
    public void TrySetLookupFilter_KeepsExistingParameters()
    {
        var doc = XDocument.Parse("<form><control id=\"a\" datafieldname=\"a\"><parameters><AllowFilterOff>false</AllowFilterOff></parameters></control></form>");

        FormXmlEditor.TrySetLookupFilter(doc, "a", "rel", "dep").Should().BeTrue();

        var p = doc.Descendants("control").Single().Element("parameters")!;
        p.Element("AllowFilterOff")!.Value.Should().Be("false");
        p.Element("FilterRelationshipName")!.Value.Should().Be("rel");
    }

    [Fact]
    public void TrySetLookupFilter_FieldNotOnTheForm_ReturnsFalse()
    {
        var doc = MainForm(SecuritySpec.Tables.RelationshipMapping);

        FormXmlEditor.TrySetLookupFilter(doc, "nope", "r", "d").Should().BeFalse();
    }
}
