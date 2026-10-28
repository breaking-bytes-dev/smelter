using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Dev == spec, después del sync. <c>solutions/unpacked/</c> lo reescribe el workflow <c>solution-sync</c> con el estado
/// de Dev, donde el deploy YA aplicó las specs. Entonces aplicar cada spec (en memoria) sobre ese estado tiene que dar
/// "no hay nada para cambiar": si algo cambia, Dev quedó desalineado de la spec (o la spec cambió y falta deployar).
/// Los tests que necesitan el estado ANTERIOR al deploy (para probar que el editor aplica el cambio) usan
/// <see cref="PreApplyFixtures"/>, no esto.
/// </summary>
public class PostSyncSolutionTests
{
    private static readonly string Unpacked = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked");

    private static XDocument RealMainForm(string table)
    {
        var dir = Path.Combine(Unpacked, "Entities", table, "FormXml", "main");
        var file = Directory.GetFiles(dir, "*.xml").Single();
        return XDocument.Parse(XDocument.Load(file).Descendants("form").First().ToString());
    }

    private static (string Layout, string Fetch) RealView(string table, Guid id)
    {
        var path = Path.Combine(Unpacked, "Entities", table, "SavedQueries", $"{{{id:D}}}.xml");
        var sq = XDocument.Load(path).Descendants("savedquery").Single();
        return (sq.Element("layoutxml")!.Elements().Single().ToString(), sq.Element("fetchxml")!.Elements().Single().ToString());
    }

    public static IEnumerable<object[]> SmelterTables() => SecuritySpec.Tables.All.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(SmelterTables))]
    public void FormPolish_AppliedToTheSyncedMainForm_ChangesNothing(string table)
    {
        var doc = RealMainForm(table);
        var before = doc.ToString();

        var applied = FormPolishCatalogo.Apply(doc, table);

        applied.Should().BeEmpty($"el main form de {table} en Dev ya cumple FormPolishSpec");
        doc.ToString().Should().Be(before);
    }

    [Fact]
    public void SchemaAdditions_FormPlacements_AreAlreadyOnTheSyncedForms()
    {
        foreach (var c in SchemaAdditionsCatalogo.Columns.Where(c => c.Form is not null))
        {
            var doc = RealMainForm(c.EntityLogicalName);
            doc.Descendants("control").Should().Contain(x => (string?)x.Attribute("datafieldname") == c.SchemaName,
                $"{c.SchemaName} está en el form de {c.EntityLogicalName}");
            FormXmlEditor.TryAddCellAfter(doc, c.Form!.AfterField, c.SchemaName, c.Form.Label, c.Form.ClassId,
                c.Form.ReadOnly ? true : null).Should().BeFalse($"{c.SchemaName} ya está colocado");
        }
    }

    [Fact]
    public void SchemaCleanup_RemovedFields_AreGoneFromTheSyncedForms()
    {
        foreach (var a in SchemaCleanupCatalogo.Attributes)
            FormXmlEditor.TryRemoveField(RealMainForm(a.EntityLogicalName), a.AttributeLogicalName)
                .Should().BeFalse($"{a.AttributeLogicalName} ya no está en el form de {a.EntityLogicalName}");
    }

    [Fact]
    public void SchemaTuning_FieldVisibility_IsAlreadyAppliedOnTheSyncedForms()
    {
        foreach (var v in SchemaTuningCatalogo.HiddenFields)
            FormXmlEditor.TrySetVisible(RealMainForm(v.Entity), v.Field, v.Visible)
                .Should().BeFalse($"{v.Field} ya tiene visible={v.Visible} en el form de {v.Entity}");
    }

    [Fact]
    public void FormPolish_ViewColumnsAndOrders_AreAlreadyOnTheSyncedViews()
    {
        foreach (var s in FormPolishCatalogo.ViewColumns)
        {
            var (layout, fetch) = RealView(s.Entity, s.ViewId);
            ViewXmlEditor.TryAddColumn(layout, fetch, s.AfterColumn, s.Column, s.Width).Changed
                .Should().BeFalse($"la vista {s.ViewId} ya tiene la columna {s.Column}");
        }
        foreach (var o in FormPolishCatalogo.ViewOrders)
        {
            var (_, fetch) = RealView(o.Entity, o.ViewId);
            ViewXmlEditor.TrySetOrder(fetch, o.Attribute, o.Descending).Changed
                .Should().BeFalse($"la vista {o.ViewId} ya ordena por {o.Attribute}");
        }
    }

    [Fact]
    public void SiteMap_AppliedToTheSyncedSiteMap_ChangesNothing_AndHasNoViolations()
    {
        var dir = Path.Combine(Unpacked, "AppModuleSiteMaps");
        var file = Directory.GetFiles(dir, "AppModuleSiteMap.xml", SearchOption.AllDirectories).Single();
        var xml = XDocument.Load(file).Descendants("SiteMap").Single().ToString();

        SiteMapCatalogo.Violations(xml).Should().BeEmpty("títulos bilingües y sin prefijos técnicos");
        SiteMapCatalogo.IconViolations(xml).Should().BeEmpty("cada SubArea con su VectorIcon");
        SiteMapCatalogo.Apply(xml).Changed.Should().BeFalse("el sitemap de Dev ya cumple SiteMapSpec");
    }
}
