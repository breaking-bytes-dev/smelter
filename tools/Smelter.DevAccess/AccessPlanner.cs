// Lógica PURA (sin Dataverse) de Smelter.DevAccess: elegir la copia de rol por BU y diffear
// el estado deseado contra el actual. Testeada en Smelter.Contract.Tests.

namespace Smelter.DevAccess;

/// <summary>Una copia de un rol (los roles existen una vez por Business Unit, con el mismo nombre).</summary>
internal sealed record RoleRef(Guid Id, string Name, Guid BusinessUnitId);

internal sealed record RoleChanges(IReadOnlyList<RoleRef> ToAdd, IReadOnlyList<RoleRef> ToRemove);

internal sealed record MembershipChanges(IReadOnlyList<string> ToAdd, IReadOnlyList<string> ToRemove);

internal static class AccessPlanner
{
    internal const string EnvVarName = "SMELTER_ENV";

    /// <summary>Misma regla que DeployGuards.CheckEnvironment: null si se puede correr.</summary>
    internal static string? CheckEnvironment(string? smelterEnv) =>
        string.Equals(smelterEnv?.Trim(), "dev", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Smelter.DevAccess es una herramienta de DEV y se niega a correr sin {EnvVarName}=dev " +
              $"(valor actual: '{smelterEnv ?? "<no definida>"}').";

    /// <summary>La copia del rol `name` cuya BU == la del usuario/team; null si no existe esa copia.</summary>
    internal static RoleRef? PickRoleForBu(IEnumerable<RoleRef> allRoles, string name, Guid businessUnitId) =>
        allRoles.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.Ordinal) && r.BusinessUnitId == businessUnitId);

    /// <summary>
    /// Diff de roles Smelter de un usuario. `desired` es la copia correcta (o null = ningún rol Smelter);
    /// `currentSmelterRoles` son los roles Smelter (cualquier copia/BU) que el usuario tiene hoy.
    /// Se agrega el deseado si falta y se quita todo rol Smelter que no sea exactamente el deseado
    /// (incluye la copia equivocada del mismo nombre). Los roles no-Smelter no se pasan ni se tocan.
    /// </summary>
    internal static RoleChanges DiffRoles(RoleRef? desired, IEnumerable<RoleRef> currentSmelterRoles)
    {
        var current = currentSmelterRoles.ToList();
        var toAdd = desired is not null && current.All(r => r.Id != desired.Id)
            ? new List<RoleRef> { desired }
            : [];
        var toRemove = current.Where(r => desired is null || r.Id != desired.Id).ToList();
        return new RoleChanges(toAdd, toRemove);
    }

    /// <summary>
    /// Diff de membresía de un usuario sobre los teams QA. `desiredTeam` null = en ninguno.
    /// `currentQaTeams` = nombres de los teams QA de los que ya es miembro. Se agrega al deseado si falta
    /// y se lo saca de todos los otros teams QA.
    /// </summary>
    internal static MembershipChanges DiffMembership(string? desiredTeam, IEnumerable<string> currentQaTeams)
    {
        var current = currentQaTeams.ToList();
        var toAdd = desiredTeam is not null && !current.Contains(desiredTeam, StringComparer.Ordinal)
            ? new List<string> { desiredTeam }
            : [];
        var toRemove = current.Where(t => desiredTeam is null || !string.Equals(t, desiredTeam, StringComparison.Ordinal)).ToList();
        return new MembershipChanges(toAdd, toRemove);
    }
}
