// Guardas de seguridad de Smelter.Deploy (lógica PURA, testeable sin Dataverse).
//   - Smelter.Deploy es una herramienta de provisioning de DEV: Test/Prod reciben la solución
//     MANAGED por el workflow solution-import, nunca por este tool.
//   - Los pasos que borran schema/metadata/datos son opt-in (--allow-destructive o
//     SMELTER_ALLOW_DESTRUCTIVE=true).

namespace Smelter.Deploy;

internal static class DeployGuards
{
    internal const string EnvVarName = "SMELTER_ENV";
    internal const string AllowDestructiveEnvVarName = "SMELTER_ALLOW_DESTRUCTIVE";
    internal const string AllowDestructiveArg = "--allow-destructive";

    /// <summary>Devuelve null si se puede correr; si no, el mensaje de rechazo.</summary>
    internal static string? CheckEnvironment(string? smelterEnv) =>
        string.Equals(smelterEnv?.Trim(), "dev", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Smelter.Deploy es una herramienta de provisioning de DEV y se niega a correr sin {EnvVarName}=dev " +
              $"(valor actual: '{smelterEnv ?? "<no definida>"}'). Test/Prod reciben la solución managed por el " +
              "workflow solution-import, no por Smelter.Deploy.";

    internal static bool IsDestructiveAllowed(string[] args, string? envValue) =>
        args.Contains(AllowDestructiveArg)
        || string.Equals(envValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Opciones de la corrida actual (seteadas una vez por Program.cs).</summary>
internal static class DeployOptions
{
    internal static bool AllowDestructive { get; set; }
}
