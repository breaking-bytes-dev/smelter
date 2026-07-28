using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO ApiSpec (la spec declarativa de tools/Smelter.Deploy) ↔ metadata real de las Custom APIs
/// (solutions/unpacked/customapis/). Los NOMBRES de parámetros se comparan case-sensitive: el
/// uniquename ES la clave que lee el plugin (context.InputParameters["JobId"]) y la que devuelve
/// al flow. Si alguien toca una API en Dev sin actualizar la spec (o viceversa) → rojo.
/// </summary>
public class CustomApiContractTests
{
    // TODO(openspec/changes/smelter-security tasks.md, Fase 6.2/9.4): quitar esta exclusión cuando
    // solution-sync haya escrito la metadata de esta API en solutions/unpacked/customapis/ — eso pasa
    // recién DESPUÉS del primer backend-deploy que la incluya (Release N; ver ApiSpec.cs, la entrada
    // "sanic_capi_smelter_processchunk" es aditiva y nueva). Comparar contra unpacked/ ahora sería
    // comparar contra un directorio que no puede existir todavía — no es una regresión real, es una
    // ventana de release esperada. El resto de las Custom APIs SIGUE cubierto normalmente por estos
    // 3 tests (no se relaja nada más).
    private static readonly HashSet<string> PendingSolutionSync = new(StringComparer.Ordinal)
    {
        "sanic_capi_smelter_processchunk",
    };

    public static TheoryData<string> ApiUniqueNames()
    {
        var data = new TheoryData<string>();
        foreach (var api in Catalogo.Apis)
            if (!PendingSolutionSync.Contains(api.UniqueName))
                data.Add(api.UniqueName);
        return data;
    }

    [Theory]
    [MemberData(nameof(ApiUniqueNames))]
    public void Api_ExistsInSolutionMetadata(string uniqueName)
    {
        SolutionMetadata.CustomApiDir(uniqueName).Should().NotBeNull(
            $"la Custom API '{uniqueName}' de la ApiSpec debe existir en solutions/unpacked/customapis/");
    }

    [Theory]
    [MemberData(nameof(ApiUniqueNames))]
    public void Api_RequestParams_MatchSolutionMetadata(string uniqueName)
    {
        var api = Catalogo.Apis.Single(a => a.UniqueName == uniqueName);
        var dir = SolutionMetadata.CustomApiDir(uniqueName)!;

        var expected = api.RequestParams
            .Select(p => new SolutionMetadata.MetaParam(p.Name, p.Type, p.Optional))
            .OrderBy(p => p.Name, StringComparer.Ordinal);

        SolutionMetadata.RequestParams(dir).Should().BeEquivalentTo(expected,
            o => o.WithStrictOrdering(),
            $"los request parameters de '{uniqueName}' (nombre exacto, tipo, opcionalidad) deben coincidir con la ApiSpec");
    }

    [Theory]
    [MemberData(nameof(ApiUniqueNames))]
    public void Api_ResponseProps_MatchSolutionMetadata(string uniqueName)
    {
        var api = Catalogo.Apis.Single(a => a.UniqueName == uniqueName);
        var dir = SolutionMetadata.CustomApiDir(uniqueName)!;

        var expected = api.ResponseProps
            .Select(p => new SolutionMetadata.MetaParam(p.Name, p.Type, Optional: false))
            .OrderBy(p => p.Name, StringComparer.Ordinal);

        SolutionMetadata.ResponseProps(dir).Should().BeEquivalentTo(expected,
            o => o.WithStrictOrdering(),
            $"las response properties de '{uniqueName}' (nombre exacto, tipo) deben coincidir con la ApiSpec");
    }
}
