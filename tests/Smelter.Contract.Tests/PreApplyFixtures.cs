using System.Xml.Linq;

namespace Smelter.Contract.Tests;

/// <summary>
/// Estado PRE-apply CONGELADO de la solución (sol-v1.3.0.0, commit 7723fd2): los main forms, las vistas y el sitemap
/// tal como estaban ANTES de que el deploy aplicara las specs (FormPolish, SiteMap, SchemaAdditions...).
/// <para>
/// Por qué existe: <c>solutions/unpacked/</c> lo reescribe el workflow <c>solution-sync</c> con el estado de Dev, que ya
/// tiene todo aplicado (POST-deploy). Un test que necesita "un form al que todavía le falta el cambio" no puede leerlo
/// de ahí: el cambio ya está y el test pasa a fallar (o a probar nada). Esos tests leen de acá; los que hablan de la
/// solución REAL (<see cref="SolutionMetadata"/>) siguen leyendo de <c>solutions/unpacked/</c>.
/// </para>
/// Los archivos son copias exactas (<c>git show 7723fd2:solutions/unpacked/...</c>) con el mismo layout que unpacked.
/// </summary>
internal static class PreApplyFixtures
{
    internal static string Root { get; } =
        Path.Combine(SolutionMetadata.RepoRoot, "tests", "Smelter.Contract.Tests", "Fixtures", "pre-apply-sol-1.3.0.0");

    private static string Dir(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>El elemento &lt;form&gt; del main form PRE-apply de la tabla.</summary>
    internal static XDocument MainForm(string table)
    {
        var file = Directory.GetFiles(Dir("Entities", table, "FormXml", "main"), "*.xml").Single();
        return XDocument.Parse(XDocument.Load(file).Descendants("form").First().ToString());
    }

    /// <summary>El elemento &lt;SiteMap&gt; PRE-apply (como string).</summary>
    internal static string SiteMapXml()
    {
        var file = Directory.GetFiles(Dir("AppModuleSiteMaps"), "AppModuleSiteMap.xml", SearchOption.AllDirectories).Single();
        return XDocument.Load(file).Descendants("SiteMap").Single().ToString();
    }

    /// <summary>(layoutxml, fetchxml) de una vista PRE-apply.</summary>
    internal static (string Layout, string Fetch) View(string table, Guid id)
    {
        var path = Dir("Entities", table, "SavedQueries", $"{{{id:D}}}.xml");
        var sq = XDocument.Load(path).Descendants("savedquery").Single();
        return (sq.Element("layoutxml")!.Elements().Single().ToString(), sq.Element("fetchxml")!.Elements().Single().ToString());
    }
}
