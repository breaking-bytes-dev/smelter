// Especificación DECLARATIVA de los message steps (sdkmessageprocessingstep) de Smelter — la
// fuente única de verdad del registro de steps, misma filosofía que Catalogo en ApiSpec.cs y
// SecuritySpec: la consumen StepProvisioner (deploy idempotente) y StepAssertions (verify E2E).
//
// Hasta smelter-security v2 el assembly tenía CERO message steps (solo backing plugins de Custom
// API) — este archivo nace con el primero: AssignJobOwnerPlugin, que hace que los Import Jobs
// creados desde un template owned por team nazcan owned por ese mismo team.

namespace Smelter.Deploy;

// Valores de option sets de plataforma de sdkmessageprocessingstep — no son tipos SDK compilados,
// mismo estándar de evidencia que ParamType (ApiSpec.cs) y RoleComponentType (SecurityProvisioner):
// documentados públicamente por Microsoft en la referencia de la tabla sdkmessageprocessingstep.
internal static class StepStage
{
    public const int PreValidation = 10;
    public const int PreOperation = 20;
    public const int PostOperation = 40;
}

internal static class StepMode
{
    public const int Synchronous = 0;
    public const int Asynchronous = 1;
}

internal sealed record MessageStepSpec(
    string Name,
    string Description,
    string PluginTypeName,
    string Message,
    string PrimaryEntity,
    int Stage,
    int Mode,
    int Rank,
    // Solo tiene sentido en Update (limita el disparo a cambios de esas columnas); en Create va
    // vacío y el provisioner no lo envía.
    string FilteringAttributes);

internal static class StepCatalogo
{
    public static readonly MessageStepSpec[] Steps =
    [
        new(
            Name: "Smelter.Plugins.AssignJobOwnerPlugin: Create of sanic_tbl_smlt_importjob",
            Description: "Pre-operation sync en Create de Import Job: si el template referenciado es " +
                "owned por un team, el job nace owned por ese mismo team — visibilidad de equipo con " +
                "Read/Write Basic del rol Operador, sin tocar la matriz de privilegios.",
            PluginTypeName: "Smelter.Plugins.AssignJobOwnerPlugin",
            Message: "Create",
            PrimaryEntity: SecuritySpec.Tables.ImportJob,
            Stage: StepStage.PreOperation,
            Mode: StepMode.Synchronous,
            Rank: 1,
            FilteringAttributes: ""),
    ];
}
