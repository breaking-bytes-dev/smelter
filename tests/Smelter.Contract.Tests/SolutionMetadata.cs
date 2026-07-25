using System.Xml.Linq;

namespace Smelter.Contract.Tests;

/// <summary>
/// Lector de la metadata REAL de la solución, versionada en <c>solutions/unpacked/</c> por el
/// workflow <c>solution-sync</c>. Es el otro lado de los contratos: los enums del código y la
/// spec de deploy se corroboran contra ESTO (no contra un doc que puede quedar stale).
/// </summary>
internal static class SolutionMetadata
{
    /// <summary>Raíz del repo (sube desde el bin del test hasta encontrar Smelter.sln).</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Smelter.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("No se encontró Smelter.sln subiendo desde el bin del test.");
    }

    /// <summary>
    /// Lee las opciones (value → label) de un optionset local definido en el Entity.xml de una tabla.
    /// </summary>
    /// <param name="entityFolder">Nombre de la carpeta bajo solutions/unpacked/Entities (logical name de la tabla).</param>
    /// <param name="optionSetName">Atributo Name del &lt;optionset&gt; (formato {tabla}_{columna}).</param>
    internal static IReadOnlyDictionary<int, string> OptionSet(string entityFolder, string optionSetName)
    {
        var path = Path.Combine(RepoRoot, "solutions", "unpacked", "Entities", entityFolder, "Entity.xml");
        File.Exists(path).Should(path);

        var optionset = XDocument.Load(path).Descendants("optionset")
            .FirstOrDefault(o => (string?)o.Attribute("Name") == optionSetName)
            ?? throw new InvalidOperationException($"optionset '{optionSetName}' no encontrado en {path}.");

        return optionset.Descendants("option")
            .ToDictionary(
                o => int.Parse((string)o.Attribute("value")!),
                o => (string?)o.Element("labels")?.Descendants("label").FirstOrDefault()?.Attribute("description")
                     ?? (string?)o.Descendants("label").FirstOrDefault()?.Attribute("description")
                     ?? "");
    }

    /// <summary>Carpeta de una Custom API en la metadata (o null si no existe).</summary>
    internal static string? CustomApiDir(string uniqueName)
    {
        var dir = Path.Combine(RepoRoot, "solutions", "unpacked", "customapis", uniqueName);
        return Directory.Exists(dir) ? dir : null;
    }

    internal sealed record MetaParam(string Name, int Type, bool Optional);

    /// <summary>Request parameters de una Custom API según la metadata (uniquename, type, isoptional).</summary>
    internal static IReadOnlyList<MetaParam> RequestParams(string apiDir)
        => ReadParams(Path.Combine(apiDir, "customapirequestparameters"), withOptional: true);

    /// <summary>Response properties de una Custom API según la metadata (uniquename, type).</summary>
    internal static IReadOnlyList<MetaParam> ResponseProps(string apiDir)
        => ReadParams(Path.Combine(apiDir, "customapiresponseproperties"), withOptional: false);

    private static IReadOnlyList<MetaParam> ReadParams(string dir, bool withOptional)
    {
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir)
            .Select(d => Directory.GetFiles(d, "*.xml").Single())
            .Select(XDocument.Load)
            .Select(x =>
            {
                var root = x.Root!;
                return new MetaParam(
                    Name: (string)root.Attribute("uniquename")!,
                    Type: int.Parse(root.Element("type")!.Value),
                    Optional: withOptional && root.Element("isoptional")?.Value == "1");
            })
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static void Should(this bool condition, string path)
    {
        if (!condition)
            throw new FileNotFoundException(
                $"Metadata no encontrada: {path}. ¿Corriste el workflow solution-sync (release de la solución)?");
    }
}
