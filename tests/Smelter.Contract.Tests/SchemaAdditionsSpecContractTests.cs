using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de las columnas nuevas provisionadas por SDK (<see cref="SchemaAdditionsCatalogo"/>):
/// el cursor de bytes del Import Job y el flag "Solo inserción" del Table Mapping. Datos puros +
/// el editor de formxml (<see cref="FormXmlEditor"/>) probado contra el form REAL de solutions/unpacked.
/// </summary>
public class SchemaAdditionsSpecContractTests
{
    private static SchemaColumnSpec Column(string name) =>
        SchemaAdditionsCatalogo.Columns.Single(c => c.SchemaName == name);

    [Fact]
    public void ByteCursor_IsADecimalWithPrecisionZero_BecauseWholeNumberTopsAt2GB()
    {
        var c = Column("sanic_smlt_ij_bytecursor");

        c.EntityLogicalName.Should().Be(SecuritySpec.Tables.ImportJob);
        c.Kind.Should().Be(SchemaColumnKind.Decimal);
        SchemaAdditionsCatalogo.ByteCursorPrecision.Should().Be(0);
        SchemaAdditionsCatalogo.ByteCursorMin.Should().Be(0m);
        // Un File column llega hasta 10 GB: un whole number (int32, ~2.1 GB) no alcanzaría.
        SchemaAdditionsCatalogo.ByteCursorMax.Should().BeGreaterThan(10L * 1024 * 1024 * 1024);
        SchemaAdditionsCatalogo.ByteCursorMax.Should().BeLessOrEqualTo(100_000_000_000m, "tope de un Decimal de Dataverse");
        c.Form.Should().BeNull("no hay patrón de campos ocultos en el form del job: la columna no va al form");
    }

    [Fact]
    public void InsertOnly_IsABooleanDefaultingToNo_OnTheTableMappingMainForm()
    {
        var c = Column("sanic_smlt_tm_insertonly");

        c.EntityLogicalName.Should().Be(SecuritySpec.Tables.TableMapping);
        c.Kind.Should().Be(SchemaColumnKind.Boolean);
        SchemaAdditionsCatalogo.InsertOnlyDefault.Should().BeFalse();
        c.DisplayName.Should().Contain("Solo inserción (permite duplicados)");
        c.Form.Should().NotBeNull();
        c.Form!.AfterField.Should().Be("sanic_smlt_tm_updateifexists");
    }

    [Fact]
    public void FailureReason_IsA4000CharMultilineOnTheJob_ShownReadOnlyRightAfterStatus()
    {
        var c = Column("sanic_smlt_ij_failurereason");

        c.EntityLogicalName.Should().Be(SecuritySpec.Tables.ImportJob);
        c.Kind.Should().Be(SchemaColumnKind.Memo);
        SchemaAdditionsCatalogo.FailureReasonMaxLength.Should().Be(4000);
        c.Description.Should().NotBeNullOrWhiteSpace();
        c.Form.Should().NotBeNull();
        c.Form!.AfterField.Should().Be("sanic_smlt_ij_status");
        c.Form.ReadOnly.Should().BeTrue("lo escribe el Cloud Flow, el usuario no lo edita");
        c.Form.ClassId.Should().Be("{E0DECE4B-6FC8-4A8F-A065-082708572369}", "control de texto multilínea");
        // El handler del flow escribe exactamente este parámetro: la spec y el transformer no pueden divergir.
        ClientDataFailureReasonWriter.ReasonParameter.Should().Be("item/" + c.SchemaName);
        ClientDataFailureReasonWriter.MaxLength.Should().Be(SchemaAdditionsCatalogo.FailureReasonMaxLength);
    }

    [Fact]
    public void Columns_FollowTheNamingConventionAndLiveInTheSmelterTables()
    {
        foreach (var c in SchemaAdditionsCatalogo.Columns)
        {
            SecuritySpec.Tables.All.Should().Contain(c.EntityLogicalName);
            var prefix = c.EntityLogicalName == SecuritySpec.Tables.ImportJob ? "sanic_smlt_ij_" : "sanic_smlt_tm_";
            c.SchemaName.Should().StartWith(prefix).And.MatchRegex("^[a-z_]+$");
            SchemaCleanupCatalogo.Attributes.Select(a => a.AttributeLogicalName).Should().NotContain(c.SchemaName);
        }
        SchemaAdditionsCatalogo.Columns.Select(c => c.SchemaName).Should().OnlyHaveUniqueItems();
    }

    // ── FormXmlEditor, contra el main form real del Table Mapping ─────────────────────────────

    private static XDocument RealTableMappingForm()
    {
        var dir = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Entities",
            "sanic_tbl_smlt_tablemapping", "FormXml", "main");
        var file = Directory.GetFiles(dir, "*.xml").Single();
        return XDocument.Parse(XDocument.Load(file).Descendants("form").First().ToString());
    }

    [Fact]
    public void TryAddCellAfter_InsertsANewRowRightAfterTheAnchorRow_CloningItsControlClass()
    {
        var doc = RealTableMappingForm();

        var changed = FormXmlEditor.TryAddCellAfter(doc, "sanic_smlt_tm_updateifexists",
            "sanic_smlt_tm_insertonly", "[TM] Solo inserción (permite duplicados)");

        changed.Should().BeTrue();
        var anchorRow = doc.Descendants("control")
            .Single(c => (string?)c.Attribute("datafieldname") == "sanic_smlt_tm_updateifexists").Ancestors("row").First();
        var newRow = anchorRow.ElementsAfterSelf("row").First();
        var control = newRow.Descendants("control").Single();
        control.Attribute("datafieldname")!.Value.Should().Be("sanic_smlt_tm_insertonly");
        control.Attribute("id")!.Value.Should().Be("sanic_smlt_tm_insertonly");
        control.Attribute("classid")!.Value.Should().Be(
            anchorRow.Descendants("control").Single().Attribute("classid")!.Value, "mismo control (checkbox) que el ancla");
        newRow.Descendants("label").Single().Attribute("description")!.Value.Should().Be("[TM] Solo inserción (permite duplicados)");
        var cellIds = doc.Descendants("cell").Select(c => (string?)c.Attribute("id")).Where(i => i != null).ToList();
        cellIds.Should().OnlyHaveUniqueItems("el cell clonado recibe un id nuevo");
    }

    [Fact]
    public void TryAddCellAfter_IsIdempotent()
    {
        var doc = RealTableMappingForm();
        FormXmlEditor.TryAddCellAfter(doc, "sanic_smlt_tm_updateifexists", "sanic_smlt_tm_insertonly", "x").Should().BeTrue();

        FormXmlEditor.TryAddCellAfter(doc, "sanic_smlt_tm_updateifexists", "sanic_smlt_tm_insertonly", "x").Should().BeFalse();

        doc.Descendants("control").Count(c => (string?)c.Attribute("datafieldname") == "sanic_smlt_tm_insertonly").Should().Be(1);
    }

    [Fact]
    public void TryAddCellAfter_FormWithoutTheAnchor_IsLeftUntouched()
    {
        var doc = XDocument.Parse("<form><tabs><tab><columns><column><sections><section><rows><row><cell><control id=\"a\" datafieldname=\"a\" /></cell></row></rows></section></sections></column></columns></tab></tabs></form>");
        var before = doc.ToString();

        FormXmlEditor.TryAddCellAfter(doc, "sanic_smlt_tm_updateifexists", "sanic_smlt_tm_insertonly", "x").Should().BeFalse();

        doc.ToString().Should().Be(before);
    }
}
