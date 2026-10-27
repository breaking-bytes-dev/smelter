using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de <see cref="FormPolishCatalogo"/> (#10/#11/#12/#20): ediciones declarativas de los main forms y de la
/// vista default del log. Se aplican (en memoria) sobre los forms/vistas REALES de solutions/unpacked.
/// </summary>
public class FormPolishSpecContractTests
{
    private static XDocument MainForm(string table)
    {
        var dir = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Entities", table, "FormXml", "main");
        var file = Directory.GetFiles(dir, "*.xml").Single();
        return XDocument.Parse(XDocument.Load(file).Descendants("form").First().ToString());
    }

    private static XElement? Control(XDocument doc, string field) =>
        doc.Descendants("control").FirstOrDefault(c => (string?)c.Attribute("datafieldname") == field);

    private static bool IsDisabled(XDocument doc, string field) =>
        (string?)Control(doc, field)?.Attribute("disabled") == "true";

    [Fact]
    public void ImportJobForm_OutputFieldsBecomeReadOnly_OnlyTheInputsStayEditable()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJob);

        var applied = FormPolishCatalogo.Apply(doc, SecuritySpec.Tables.ImportJob);

        applied.Should().NotBeEmpty();
        string[] outputs =
        [
            "sanic_smlt_ij_status", "sanic_smlt_ij_totalrows", "sanic_smlt_ij_processedrows", "sanic_smlt_ij_successcount",
            "sanic_smlt_ij_failedcount", "sanic_smlt_ij_skippedcount", "sanic_smlt_ij_processingbatch",
            "sanic_smlt_ij_starttime", "sanic_smlt_ij_endtime",
        ];
        foreach (var f in outputs) IsDisabled(doc, f).Should().BeTrue($"{f} es salida del motor");
        string[] inputs = ["sanic_smlt_ij_name", "sanic_smlt_ij_importtemplateid", "sanic_smlt_ij_sourcefile", "sanic_smlt_ij_isdryrun"];
        foreach (var f in inputs) IsDisabled(doc, f).Should().BeFalse($"{f} lo completa el usuario");
    }

    [Fact]
    public void ImportJobForm_GetsALogsSubgridTab_PointingAtThePinnedLogView()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJob);

        FormPolishCatalogo.Apply(doc, SecuritySpec.Tables.ImportJob);

        var sub = doc.Descendants("control").Single(c => (string?)c.Attribute("indicationOfSubgrid") == "true");
        sub.Element("parameters")!.Element("TargetEntityType")!.Value.Should().Be(SecuritySpec.Tables.ImportJobLog);
        var viewId = Guid.Parse(sub.Element("parameters")!.Element("ViewId")!.Value);
        MdaComponentsCatalogo.All.Should().Contain(c => c.Id == viewId && c.Table == SecuritySpec.Tables.ImportJobLog,
            "la vista del subgrid tiene que estar pineada en la app (si no, la MDA no la ofrece)");
        // La relación existe en la solución real.
        var rels = File.ReadAllText(Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Other", "Relationships",
            "sanic_tbl_smlt_importjob.xml"));
        rels.Should().Contain(sub.Element("parameters")!.Element("RelationshipName")!.Value);
    }

    [Theory]
    [InlineData("sanic_tbl_smlt_importjob")]
    [InlineData("sanic_tbl_smlt_importjoblog")]
    [InlineData("sanic_tbl_smlt_relationshipmapping")]
    public void Apply_IsIdempotent_SecondPassChangesNothing(string table)
    {
        var doc = MainForm(table);
        FormPolishCatalogo.Apply(doc, table).Should().NotBeEmpty();
        var snapshot = doc.ToString();

        FormPolishCatalogo.Apply(doc, table).Should().BeEmpty();

        doc.ToString().Should().Be(snapshot);
    }

    [Fact]
    public void ImportJobLogForm_ShowsTheSixDiagnosticFieldsReadOnly()
    {
        var doc = MainForm(SecuritySpec.Tables.ImportJobLog);

        FormPolishCatalogo.Apply(doc, SecuritySpec.Tables.ImportJobLog);

        string[] fields =
        [
            "sanic_smlt_ijl_importjobid", "sanic_smlt_ijl_rownumber", "sanic_smlt_ijl_targettable",
            "sanic_smlt_ijl_status", "sanic_smlt_ijl_errorcategory", "sanic_smlt_ijl_errormessage",
        ];
        foreach (var f in fields)
        {
            Control(doc, f).Should().NotBeNull($"{f} debe estar en el form del log");
            IsDisabled(doc, f).Should().BeTrue($"{f} es de solo lectura");
        }
        doc.Descendants("control").Select(c => (string?)c.Attribute("datafieldname"))
            .Should().ContainInOrder("sanic_smlt_ijl_id", "sanic_smlt_ijl_importjobid", "sanic_smlt_ijl_rownumber",
                "sanic_smlt_ijl_targettable", "sanic_smlt_ijl_status", "sanic_smlt_ijl_errorcategory", "sanic_smlt_ijl_errormessage");
        Control(doc, "sanic_smlt_ijl_errormessage")!.Attribute("classid")!.Value.Should().Be(SchemaAdditionsCatalogo.MemoControlClassId);
    }

    [Fact]
    public void RelationshipMappingForm_FiltersBothTableMappingLookupsByTheTemplate_AndShowsTheTemplateField()
    {
        var doc = MainForm(SecuritySpec.Tables.RelationshipMapping);

        FormPolishCatalogo.Apply(doc, SecuritySpec.Tables.RelationshipMapping);

        Control(doc, "sanic_smlt_rm_importtemplateid").Should().NotBeNull("el filtro depende de este campo en el form");
        foreach (var f in new[] { "sanic_smlt_rm_parenttablemappingid", "sanic_smlt_rm_childtablemappingid" })
        {
            var p = Control(doc, f)!.Element("parameters")!;
            p.Element("FilterRelationshipName")!.Value.Should()
                .Be("sanic_tbl_smlt_tablemapping_smlt_tm_importtemplateid_sanic_tbl_smlt_importtemplate");
            p.Element("DependentAttributeName")!.Value.Should().Be("sanic_smlt_rm_importtemplateid");
        }
        var order = doc.Descendants("control").Select(c => (string?)c.Attribute("datafieldname")).ToList();
        order.IndexOf("sanic_smlt_rm_importtemplateid").Should().BeLessThan(order.IndexOf("sanic_smlt_rm_parenttablemappingid"));
    }

    [Fact]
    public void FilterRelationship_ExistsInTheRealSolution()
    {
        var rels = File.ReadAllText(Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Other", "Relationships",
            "sanic_tbl_smlt_importtemplate.xml"));

        foreach (var f in FormPolishCatalogo.LookupFilters)
            rels.Should().Contain($"Name=\"{f.FilterRelationship}\"");
    }

    [Fact]
    public void Apply_OnATableWithoutSpec_ReturnsNothing()
    {
        FormPolishCatalogo.Apply(MainForm(SecuritySpec.Tables.ColumnMapping), SecuritySpec.Tables.ColumnMapping).Should().BeEmpty();
    }

    [Fact]
    public void Spec_OnlyTargetsSmelterTables()
    {
        var entities = FormPolishCatalogo.ReadOnlyFields.Select(f => f.Entity)
            .Concat(FormPolishCatalogo.AddedFields.Select(f => f.Entity))
            .Concat(FormPolishCatalogo.Subgrids.Select(f => f.Entity))
            .Concat(FormPolishCatalogo.LookupFilters.Select(f => f.Entity))
            .Concat(FormPolishCatalogo.ViewColumns.Select(f => f.Entity));
        entities.Should().OnlyContain(e => SecuritySpec.Tables.All.Contains(e));
    }

    // ── Vista default del log: errorcategory ──────────────────────────────────────────────

    private static (string Layout, string Fetch) RealLogView()
    {
        var path = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Entities", SecuritySpec.Tables.ImportJobLog,
            "SavedQueries", "{6752f00a-34e0-43fd-ba73-9a9cbec00991}.xml");
        var sq = XDocument.Load(path).Descendants("savedquery").Single();
        return (sq.Element("layoutxml")!.Elements().Single().ToString(), sq.Element("fetchxml")!.Elements().Single().ToString());
    }

    [Fact]
    public void LogDefaultView_GetsErrorCategoryRightAfterStatus_InLayoutAndFetch_Idempotently()
    {
        var (layout, fetch) = RealLogView();
        var spec = FormPolishCatalogo.ViewColumns.Single();

        var (l1, f1, changed) = ViewXmlEditor.TryAddColumn(layout, fetch, spec.AfterColumn, spec.Column, spec.Width);

        changed.Should().BeTrue();
        XDocument.Parse(l1).Descendants("cell").Select(c => (string?)c.Attribute("name"))
            .Should().ContainInOrder("sanic_smlt_ijl_status", "sanic_smlt_ijl_errorcategory", "sanic_smlt_ijl_errormessage");
        XDocument.Parse(f1).Descendants("entity").Single().Elements("attribute").Select(a => (string?)a.Attribute("name"))
            .Should().Contain("sanic_smlt_ijl_errorcategory");

        var (l2, f2, changed2) = ViewXmlEditor.TryAddColumn(l1, f1, spec.AfterColumn, spec.Column, spec.Width);
        changed2.Should().BeFalse();
        (l2, f2).Should().Be((l1, f1));
        MdaComponentsCatalogo.All.Should().Contain(c => c.Id == spec.ViewId, "la vista que se edita es una de las pineadas en la app");
    }

    [Fact]
    public void ViewXmlEditor_AnchorMissing_AppendsTheCellAtTheEnd()
    {
        const string layout = "<grid><row name=\"result\" id=\"x\"><cell name=\"a\" width=\"100\" /></row></grid>";
        const string fetch = "<fetch><entity name=\"t\"><attribute name=\"a\" /></entity></fetch>";

        var (l, _, changed) = ViewXmlEditor.TryAddColumn(layout, fetch, "nope", "b", 50);

        changed.Should().BeTrue();
        XDocument.Parse(l).Descendants("cell").Select(c => (string?)c.Attribute("name")).Should().Equal("a", "b");
    }
}
