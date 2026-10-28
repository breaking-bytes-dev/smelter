using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Pin de forms y vistas en la MDA (MdaComponentsSpec): la lista se corrobora contra la metadata REAL de
/// solutions/unpacked (ids, tipos, activas) y el diff "solo agregar lo que falta" es puro.
/// </summary>
public class MdaComponentsSpecTests
{
    private static readonly string Entities = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "Entities");

    private sealed record UnpackedView(string Table, Guid Id, int QueryType, bool IsDefault, IReadOnlyList<string> Names)
    {
        /// <summary>El nombre base (primer LocalizedName: 1033).</summary>
        public string Name => Names.FirstOrDefault() ?? "";
    }

    // La solución trae además sanic_tbl_smlt_hmackey (keys HMAC, 1.4.0.0): tabla de sistema que NO va en la app a propósito
    // (HmacKeyTableSpec: no está en SecuritySpec.Tables ni en el sitemap). Los helpers miran solo las tablas de la MDA;
    // que la tabla HMAC siga fuera de la app lo cubre HmacKeyTable_IsNotPartOfTheApp.
    private static bool InApp(string table) => SecuritySpec.Tables.All.Contains(table);

    private static IEnumerable<UnpackedView> UnpackedViews() =>
        Directory.GetFiles(Entities, "*.xml", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "SavedQueries")
            .Where(f => InApp(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))!)!))
            .Select(f =>
            {
                var q = XDocument.Load(f).Descendants("savedquery").Single();
                return new UnpackedView(
                    Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))!)!,
                    Guid.Parse((string)q.Element("savedqueryid")!),
                    int.Parse((string)q.Element("querytype")!),
                    (string?)q.Element("isdefault") == "1",
                    q.Descendants("LocalizedName").Select(n => (string?)n.Attribute("description") ?? "").ToList());
            });

    private static IEnumerable<(string Table, Guid Id, int FormActivationState)> UnpackedMainForms() =>
        from dir in Directory.GetDirectories(Entities)
        let main = Path.Combine(dir, "FormXml", "main")
        where Directory.Exists(main) && InApp(Path.GetFileName(dir))
        from file in Directory.GetFiles(main, "*.xml")
        let f = XDocument.Load(file).Descendants("systemform").Single()
        select (Path.GetFileName(dir), Guid.Parse((string)f.Element("formid")!), int.Parse((string?)f.Element("FormActivationState") ?? "1"));

    [Fact]
    public void Spec_CoversExactlyTheSixSmelterTables()
    {
        MdaComponentsCatalogo.All.Select(c => c.Table).Distinct()
            .Should().BeEquivalentTo(SecuritySpec.Tables.All);
    }

    [Fact]
    public void Spec_HasNoDuplicateIds_AndOnlyFormOrViewTypes()
    {
        MdaComponentsCatalogo.All.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        MdaComponentsCatalogo.All.Select(c => c.ComponentType).Distinct()
            .Should().BeSubsetOf(new[] { MdaSpec.ComponentForm, MdaSpec.ComponentView });
        MdaComponentsCatalogo.All.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Reason) && !string.IsNullOrWhiteSpace(c.Name));
    }

    [Fact]
    public void Forms_AreExactlyTheActiveMainFormsOfTheSolution()
    {
        var expected = UnpackedMainForms().Where(f => f.FormActivationState == 1).Select(f => f.Id).ToHashSet();
        expected.Count.Should().Be(6, "una main form 'Information' por tabla");
        MdaComponentsCatalogo.All.Where(c => c.ComponentType == MdaSpec.ComponentForm).Select(c => c.Id)
            .Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Views_ArePublicDefaultCustomLookupAssociatedAndQuickFind_NeverInactiveOrAdvancedFind()
    {
        var views = UnpackedViews().ToList();
        // querytype: 0 público, 1 Advanced Find, 2 Associated, 4 Quick Find, 64 Lookup, 8192 "My ..." (no se pinea).
        var expected = views
            .Where(v => v.QueryType is 2 or 4 or 64 || (v.QueryType == 0 && !v.Name.StartsWith("Inactive", StringComparison.Ordinal)))
            .Select(v => v.Id).ToHashSet();

        MdaComponentsCatalogo.All.Where(c => c.ComponentType == MdaSpec.ComponentView).Select(c => c.Id)
            .Should().BeEquivalentTo(expected);

        var pinned = MdaComponentsCatalogo.All.Select(c => c.Id).ToHashSet();
        views.Where(v => v.QueryType == 1).Should().OnlyContain(v => !pinned.Contains(v.Id), "Advanced Find no se pinea");
        views.Where(v => v.Name.StartsWith("Inactive", StringComparison.Ordinal)).Should().OnlyContain(v => !pinned.Contains(v.Id));
    }

    [Fact]
    public void EveryTable_HasDefaultLookupAssociatedAndQuickFindView_SoLookupsAndSubgridsKeepWorking()
    {
        foreach (var table in SecuritySpec.Tables.All)
        {
            var kinds = MdaComponentsCatalogo.All.Where(c => c.Table == table).Select(c => c.Kind).ToHashSet();
            kinds.Should().Contain(new[] { MdaComponentKind.MainForm, MdaComponentKind.DefaultView, MdaComponentKind.LookupView,
                MdaComponentKind.AssociatedView, MdaComponentKind.QuickFindView }, $"tabla {table}");
        }
    }

    [Fact]
    public void ImportJob_PinsItsThreeCustomViews_IncludingCompletadosConErrores()
    {
        var custom = MdaComponentsCatalogo.All.Where(c => c.Kind == MdaComponentKind.CustomView && c.Table == SecuritySpec.Tables.ImportJob).Select(c => c.Name).ToList();
        custom.Should().BeEquivalentTo("Jobs Activos", "Completados con Errores", "Todos los Jobs");
    }

    [Fact]
    public void Names_MatchTheUnpackedLocalizedNames()
    {
        // Desde 1.4.0.0 las vistas son bilingües (1033 + 3082). El Name de la spec es una descripción interna (log del deploy):
        // tiene que ser el nombre de la vista en alguno de sus idiomas, o -en las 3 custom del Import Job, que nacieron en
        // español bajo 1033- el nombre legado que declara ViewLabelsSpec (ver docs/tecnica/06-app-de-configuracion.md).
        var names = UnpackedViews().ToDictionary(v => v.Id, v => v.Names);
        var legacyNames = ViewLabelsCatalogo.Views.Select(v => v.LegacyName).ToList();
        foreach (var c in MdaComponentsCatalogo.All.Where(c => c.ComponentType == MdaSpec.ComponentView))
        {
            var accepted = names[c.Id].ToList();
            if (c.Kind == MdaComponentKind.CustomView) accepted.AddRange(legacyNames);
            accepted.Should().Contain(c.Name, $"la vista {c.Id} de {c.Table}");
        }
    }

    [Fact]
    public void HmacKeyTable_IsNotPartOfTheApp_SoItsFormAndViewsAreNeverPinned()
    {
        var hmacDir = Path.Combine(Entities, HmacKeyTableSpec.Table);
        Directory.Exists(hmacDir).Should().BeTrue("la tabla HMAC está en la solución");
        var hmacIds = Directory.GetFiles(hmacDir, "*.xml", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) is "SavedQueries" or "main")
            .Select(f => Path.GetFileNameWithoutExtension(f).Trim('{', '}'))
            .Select(Guid.Parse).ToHashSet();
        hmacIds.Should().NotBeEmpty();

        MdaComponentsCatalogo.All.Select(c => c.Id).Should().NotIntersectWith(hmacIds);
        var appXml = File.ReadAllText(Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "AppModules", MdaSpec.AppUniqueName, "AppModule.xml"));
        appXml.Should().NotContain(HmacKeyTableSpec.Table, "la tabla de keys HMAC no es parte de la app (no se muestra en el sitemap)");
        foreach (var id in hmacIds) appXml.ToLowerInvariant().Should().NotContain(id.ToString("D"));
    }

    [Fact]
    public void AppModuleXml_DeclaresEveryPinnedComponent_SoManagedImportsKeepThePins()
    {
        var path = Path.Combine(SolutionMetadata.RepoRoot, "solutions", "unpacked", "AppModules", MdaSpec.AppUniqueName, "AppModule.xml");
        var declared = XDocument.Load(path).Descendants("AppModuleComponent")
            .Where(c => (string?)c.Attribute("id") is not null)
            .Select(c => (Type: int.Parse((string)c.Attribute("type")!), Id: Guid.Parse((string)c.Attribute("id")!)))
            .ToHashSet();

        MdaComponentsCatalogo.All.Select(c => (Type: c.ComponentType, c.Id)).Should().BeSubsetOf(declared);
    }

    [Fact]
    public void Missing_WithNothingPinned_ReturnsEverythingInSpecOrder()
    {
        MdaComponentsCatalogo.Missing(MdaComponentsCatalogo.All, Array.Empty<Guid>())
            .Should().Equal(MdaComponentsCatalogo.All);
    }

    [Fact]
    public void Missing_SkipsAlreadyPinned_AndIgnoresUnrelatedExistingIds()
    {
        var first = MdaComponentsCatalogo.All[0];
        var second = MdaComponentsCatalogo.All[1];
        var missing = MdaComponentsCatalogo.Missing(MdaComponentsCatalogo.All, new[] { first.Id, second.Id, Guid.NewGuid() });

        missing.Should().HaveCount(MdaComponentsCatalogo.All.Count - 2);
        missing.Should().NotContain(new[] { first, second });
    }

    [Fact]
    public void Missing_WhenAllPinned_IsEmpty_SoRerunsAreNoOps()
    {
        MdaComponentsCatalogo.Missing(MdaComponentsCatalogo.All, MdaComponentsCatalogo.All.Select(c => c.Id)).Should().BeEmpty();
    }

    [Fact]
    public void EntityName_MapsComponentTypeToTheDataverseTable()
    {
        MdaComponentsCatalogo.EntityName(MdaSpec.ComponentForm).Should().Be("systemform");
        MdaComponentsCatalogo.EntityName(MdaSpec.ComponentView).Should().Be("savedquery");
        var act = () => MdaComponentsCatalogo.EntityName(MdaSpec.ComponentEntity);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PublishAppXml_WrapsTheAppKeyInAnAppmodulesNode()
    {
        MdaComponentsCatalogo.PublishAppXml("abc")
            .Should().Be("<importexportxml><appmodules><appmodule>abc</appmodule></appmodules></importexportxml>");
    }

    [Fact]
    public void ValidateAppWarnings_FlagsOnlyTheMissingFormOrViewWarningsOfSmelterTables()
    {
        const string msg = "App users will see all forms and views";
        MdaComponentsCatalogo.IsUnpinnedWarning(msg).Should().BeTrue();
        MdaComponentsCatalogo.IsUnpinnedWarning("The app doesn't reference a form or view for table x").Should().BeTrue();
        MdaComponentsCatalogo.IsUnpinnedWarning("Dashboard is missing").Should().BeFalse();
        MdaComponentsCatalogo.IsUnpinnedWarning(null).Should().BeFalse();
    }
}
