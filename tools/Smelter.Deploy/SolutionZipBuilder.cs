// Fase 1 (spike descartable, ver openspec/changes/smelter-security/tasks.md) convertida en
// capacidad reusable: arma un solution.zip MÍNIMO en memoria para importar RibbonDiffXml vía
// ImportSolutionRequest, sin exportar/reempacar la solución completa `smlt_core_smelter`.
//
// Técnica: el zip lleva SIEMPRE los tres archivos en la raíz: solution.xml, customizations.xml
// y [Content_Types].xml — Dev rechaza el paquete sin el tercero (verificado contra Dev real,
// backend-deploy run 30359848587). Formato de RootComponent (type="1"=Entity, schemaName,
// behavior="0") corroborado contra solutions/unpacked/Other/Solution.xml (la solución real ya
// exportada de este mismo repo).

using System.IO.Compression;
using System.Text;

namespace Smelter.Deploy;

internal static class SolutionZipBuilder
{
    /// <summary>
    /// Arma un solution.zip no administrado mínimo, con un componente RibbonDiffXml por entidad.
    /// </summary>
    /// <param name="ribbonDiffXmlByEntity">
    /// Logical name de la tabla → elemento &lt;RibbonDiffXml&gt;...&lt;/RibbonDiffXml&gt; COMPLETO
    /// (con su propio nodo raíz), tal como vive en los assets versionados de Assets/.
    /// </param>
    // Prefix numérico del publisher sanic — de solutions/unpacked/Other/Solution.xml:22.
    // Dev exige un valor real entre 10000 y 99999 en el manifest (run 30360446260).
    internal const int PublisherOptionValuePrefix = 15946;

    internal static byte[] BuildRibbonSolution(
        string solutionUniqueName,
        string publisherUniqueName,
        string publisherPrefix,
        string version,
        IReadOnlyDictionary<string, string> ribbonDiffXmlByEntity,
        int optionValuePrefix = PublisherOptionValuePrefix)
    {
        if (ribbonDiffXmlByEntity.Count == 0)
            throw new ArgumentException("Debe incluir al menos una entidad con RibbonDiffXml.", nameof(ribbonDiffXmlByEntity));

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "solution.xml", BuildSolutionXml(solutionUniqueName, publisherUniqueName, publisherPrefix, version, ribbonDiffXmlByEntity.Keys, optionValuePrefix));
            WriteEntry(zip, "customizations.xml", BuildCustomizationsXml(ribbonDiffXmlByEntity));
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
        }
        return stream.ToArray();
    }

    // Dev lo exige siempre en la raíz del paquete (verificado en backend-deploy run 30359848587):
    // "The compressed file must contain ... solution.xml, customizations.xml, and [Content_Types].xml"
    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="xml" ContentType="text/xml" />
        </Types>
        """;

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    // El friendly name viaja en el manifest: importar este zip con el unique name como nombre pisaba el que deja
    // SolutionNameProvisioner [4l] ("SMLT | Core | Smelter").
    internal static string FriendlyNameOf(string solutionUniqueName) =>
        solutionUniqueName == SolutionNameCatalogo.UniqueName ? SolutionNameCatalogo.FriendlyName : solutionUniqueName;

    private static string BuildSolutionXml(
        string solutionUniqueName, string publisherUniqueName, string publisherPrefix, string version,
        IEnumerable<string> entities, int optionValuePrefix)
    {
        var rootComponents = new StringBuilder();
        foreach (var entity in entities)
            rootComponents.Append("      <RootComponent type=\"1\" schemaName=\"").Append(entity).Append("\" behavior=\"0\" />\n");

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
               "<ImportExportXml version=\"9.2.0.0\" SolutionPackageVersion=\"9.2\" languagecode=\"1033\" generatedBy=\"SmelterDeploy\">\n" +
               "  <SolutionManifest>\n" +
               $"    <UniqueName>{solutionUniqueName}</UniqueName>\n" +
               "    <LocalizedNames>\n" +
               $"      <LocalizedName description=\"{FriendlyNameOf(solutionUniqueName)}\" languagecode=\"1033\" />\n" +
               "    </LocalizedNames>\n" +
               $"    <Version>{version}</Version>\n" +
               "    <Managed>0</Managed>\n" +
               "    <Publisher>\n" +
               $"      <UniqueName>{publisherUniqueName}</UniqueName>\n" +
               "      <LocalizedNames>\n" +
               $"        <LocalizedName description=\"{publisherUniqueName}\" languagecode=\"1033\" />\n" +
               "      </LocalizedNames>\n" +
               $"      <CustomizationPrefix>{publisherPrefix}</CustomizationPrefix>\n" +
               $"      <CustomizationOptionValuePrefix>{optionValuePrefix}</CustomizationOptionValuePrefix>\n" +
               "    </Publisher>\n" +
               "    <RootComponents>\n" +
               rootComponents +
               "    </RootComponents>\n" +
               "    <MissingDependencies />\n" +
               "  </SolutionManifest>\n" +
               "</ImportExportXml>";
    }

    private static string BuildCustomizationsXml(IReadOnlyDictionary<string, string> ribbonDiffXmlByEntity)
    {
        var entities = new StringBuilder();
        foreach (var (entityName, ribbonDiffXml) in ribbonDiffXmlByEntity)
        {
            // La declaración <?xml ...?> del asset no puede quedar embebida dentro de
            // customizations.xml — Dev rechaza el import ("Unexpected XML declaration",
            // run 30360173757). Solo el documento contenedor lleva declaración.
            var embeddable = System.Text.RegularExpressions.Regex.Replace(
                ribbonDiffXml, @"^\s*<\?xml[^>]*\?>\s*", string.Empty);

            entities.Append("    <Entity>\n")
                .Append("      <Name>").Append(entityName).Append("</Name>\n")
                .Append(embeddable).Append('\n')
                .Append("    </Entity>\n");
        }

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
               "<ImportExportXml version=\"9.2.0.0\" SolutionPackageVersion=\"9.2\" languagecode=\"1033\" generatedBy=\"SmelterDeploy\">\n" +
               "  <Entities>\n" +
               entities +
               "  </Entities>\n" +
               "  <Roles />\n" +
               "  <Workflows />\n" +
               "  <FieldSecurityProfiles />\n" +
               "  <Templates />\n" +
               "  <EntityMaps />\n" +
               "  <EntityRelationships />\n" +
               "  <OrganizationSettings />\n" +
               "  <optionsets />\n" +
               "  <CustomControls />\n" +
               "  <Languages>\n" +
               "    <Language>1033</Language>\n" +
               "  </Languages>\n" +
               "</ImportExportXml>";
    }
}
