// Lee los assets versionados de UX (RibbonDiffXml + JS) de tools/Smelter.Deploy/Assets/,
// embebidos en el assembly (ver EmbeddedResource en Smelter.Deploy.csproj). Evita depender del
// working directory del proceso que corre el deploy (distinto en CI vs. local).

namespace Smelter.Deploy;

internal static class AssetLoader
{
    internal static string ReadText(string fileName)
    {
        var assembly = typeof(AssetLoader).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Assets.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Asset embebido '{resourceName}' no encontrado. ¿Está en Assets/ con <EmbeddedResource Include=\"Assets\\*\" /> en Smelter.Deploy.csproj?");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
