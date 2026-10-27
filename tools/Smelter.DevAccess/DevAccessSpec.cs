// Especificación DECLARATIVA del acceso de prueba en Dev: 4 usuarios de test, 2 owner teams QA y
// 2 templates QA. Fuente única de verdad de Smelter.DevAccess (mismo patrón que SecuritySpec.cs).
// Los ids son de cuentas de test de Dev — no son secretos.
//
// Los nombres de rol y la Custom API se REPLICAN de Smelter.Deploy (SecuritySpec / ApiSpec) para no
// tocar el csproj de Deploy (InternalsVisibleTo); DevAccessSpecContractTests los corrobora contra
// la fuente real, así que si divergen → rojo.

namespace Smelter.DevAccess;

internal sealed record TestUserSpec(
    string Label,
    Guid UserId,
    string? RoleName,   // null = control negativo: SIN ningún rol Smelter
    string? TeamName);  // null = sin team QA

internal sealed record QaTemplateSpec(string Name, string OwnerTeamName);

internal static class DevAccessSpec
{
    internal const string ConfiguradorRoleName = "Smelter Configurador";
    internal const string OperadorRoleName = "Smelter Operador";
    internal static readonly string[] SmelterRoleNames = [ConfiguradorRoleName, OperadorRoleName];

    /// <summary>Custom API de import de template (ApiSpec.Catalogo) — upsert por nombre.</summary>
    internal const string ImportTemplateApi = "sanic_capi_smelter_importtemplate";

    internal const string TeamAName = "Smelter QA · Equipo A";
    internal const string TeamBName = "Smelter QA · Equipo B";
    internal static readonly string[] QaTeamNames = [TeamAName, TeamBName];

    /// <summary>Rol que se asocia a los teams QA (el doc de seguridad: el team necesita un rol para poder ser owner).</summary>
    internal const string TeamRoleName = OperadorRoleName;

    internal static readonly Guid BusinessUnitId = Guid.Parse("99c5be02-f1ee-f011-8406-7ced8d70b036");

    internal static readonly TestUserSpec[] Users =
    [
        new("user1", Guid.Parse("592f1177-6116-f111-8342-000d3a316609"), ConfiguradorRoleName, null),
        new("user2", Guid.Parse("7f9ef595-6116-f111-8342-000d3a316609"), OperadorRoleName, TeamAName),
        new("user3", Guid.Parse("75c684ab-6116-f111-8342-000d3a316609"), OperadorRoleName, TeamBName),
        new("user4", Guid.Parse("f78065ba-6116-f111-8342-000d3a316609"), null, null),
    ];

    internal const string TemplateAName = "QA · Template A";
    internal const string TemplateBName = "QA · Template B";

    internal static readonly QaTemplateSpec[] Templates =
    [
        new(TemplateAName, TeamAName),
        new(TemplateBName, TeamBName),
    ];
}
