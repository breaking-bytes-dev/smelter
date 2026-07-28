// Fase 1 (spike descartable, ver openspec/changes/smelter-security/tasks.md) convertida en
// capacidad reusable: arma un solution.zip MÍNIMO en memoria para importar RibbonDiffXml vía
// ImportSolutionRequest, sin exportar/reempacar la solución completa `smlt_core_smelter`.
//
// Técnica: un solution.zip para import solo necesita solution.xml + customizations.xml — el
// [Content_Types].xml solo hace falta cuando el paquete trae contenido binario (p.ej. webresource
// nuevo); acá no se toca ningún webresource, solo RibbonDiffXml de entidades existentes.
// Formato de RootComponent (type="1"=Entity, schemaName, behavior="0") corroborado contra
// solutions/unpacked/Other/Solution.xml (la solución real ya exportada de este mismo repo).
//
// GOTCHA (task 1.2, spike): esta clase NO fue validada contra un Dev real en este sandbox — sin
// acceso a Dataverse por restricción del entorno de desarrollo. La confirmación de "el botón
// aparece sin error de import y sin publish manual extra" queda pendiente del primer
// backend-deploy en CI que ejecute SecurityProvisioner.UpsertRibbon (Fase 4.3). Si el import
// falla, los sospechosos más probables son: namespace/orden de nodos en customizations.xml, o la
// ausencia de [Content_Types].xml.

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
    internal static byte[] BuildRibbonSolution(
        string solutionUniqueName,
        string publisherUniqueName,
        string publisherPrefix,
        string version,
        IReadOnlyDictionary<string, string> ribbonDiffXmlByEntity)
    {
        if (ribbonDiffXmlByEntity.Count == 0)
            throw new ArgumentException("Debe incluir al menos una entidad con RibbonDiffXml.", nameof(ribbonDiffXmlByEntity));

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "solution.xml", BuildSolutionXml(solutionUniqueName, publisherUniqueName, publisherPrefix, version, ribbonDiffXmlByEntity.Keys));
            WriteEntry(zip, "customizations.xml", BuildCustomizationsXml(ribbonDiffXmlByEntity));
        }
        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string BuildSolutionXml(
        string solutionUniqueName, string publisherUniqueName, string publisherPrefix, string version,
        IEnumerable<string> entities)
    {
        var rootComponents = new StringBuilder();
        foreach (var entity in entities)
            rootComponents.Append("      <RootComponent type=\"1\" schemaName=\"").Append(entity).Append("\" behavior=\"0\" />\n");

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
               "<ImportExportXml version=\"9.2.0.0\" SolutionPackageVersion=\"9.2\" languagecode=\"1033\" generatedBy=\"SmelterDeploy\">\n" +
               "  <SolutionManifest>\n" +
               $"    <UniqueName>{solutionUniqueName}</UniqueName>\n" +
               "    <LocalizedNames>\n" +
               $"      <LocalizedName description=\"{solutionUniqueName}\" languagecode=\"1033\" />\n" +
               "    </LocalizedNames>\n" +
               $"    <Version>{version}</Version>\n" +
               "    <Managed>0</Managed>\n" +
               "    <Publisher>\n" +
               $"      <UniqueName>{publisherUniqueName}</UniqueName>\n" +
               "      <LocalizedNames>\n" +
               $"        <LocalizedName description=\"{publisherUniqueName}\" languagecode=\"1033\" />\n" +
               "      </LocalizedNames>\n" +
               $"      <CustomizationPrefix>{publisherPrefix}</CustomizationPrefix>\n" +
               "      <CustomizationOptionValuePrefix>0</CustomizationOptionValuePrefix>\n" +
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
            entities.Append("    <Entity>\n")
                .Append("      <Name>").Append(entityName).Append("</Name>\n")
                .Append(ribbonDiffXml).Append('\n')
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
