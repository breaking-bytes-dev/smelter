// Regla de privilegios de los roles de Smelter (la usa SecurityProvisioner para QUITAR y Smelter.Verify [SEC] para FALLAR).
//
// Un rol de Smelter tiene EXACTAMENTE:
//   1. cada privilegio de RoleSpec.Privileges, con su profundidad exacta;
//   2. ningún RoleSpec.Denied;
//   3. ningún otro privilegio sobre una tabla de Smelter (nombre con "sanic_tbl_smlt_"): ni Append/AppendTo/Assign/Share de más, ni
//      nada sobre la tabla de claves HMAC;
//   4. ningún otro privilegio de plataforma (plugin assemblies, SharePoint, lo que sea), salvo los de RoleSpec.AllowedExtras.
//
// Por qué sin «baseline» tolerado: los privilegios básicos de plataforma que cualquier usuario necesita (ajustes de usuario, etc.) los
// aporta el rol que la organización le asigna al usuario junto a este (Basic User / Environment Maker / el de su app), no los roles de
// Smelter. Un privilegio de más en un rol de Smelter es superficie de acceso que nadie decidió: se quita en el deploy y el verify lo
// detecta si alguien lo vuelve a agregar a mano. Si algún día hace falta uno, se declara en AllowedExtraPrivileges con su motivo.

namespace Smelter.Deploy;

internal static class SecurityAudit
{
    /// <summary>Todos los privilegios de las tablas de Smelter contienen este fragmento (prv{Tipo}sanic_tbl_smlt_{tabla}).</summary>
    internal const string SmelterTableMarker = "sanic_tbl_smlt_";

    /// <summary>Lo que no cumple la regla, en lenguaje de quien lee el log del verify. Vacío = el rol está como dice la spec.</summary>
    internal static IReadOnlyList<string> Violations(RoleSpec role, IEnumerable<(string Name, Microsoft.Crm.Sdk.Messages.PrivilegeDepth Depth)> actual)
    {
        var actualByName = actual.ToDictionary(p => p.Name, p => p.Depth);
        var violations = new List<string>();

        foreach (var expected in role.Privileges)
        {
            if (!actualByName.TryGetValue(expected.PrivilegeName, out var depth))
                violations.Add($"falta '{expected.PrivilegeName}' (esperado {expected.Depth} por SecuritySpec)");
            else if (depth != expected.Depth)
                violations.Add($"'{expected.PrivilegeName}' tiene profundidad {depth}, esperado {expected.Depth}");
        }

        var denied = role.Denied.Select(d => d.PrivilegeName).ToHashSet();
        foreach (var name in Unexpected(role, actual))
        {
            if (denied.Contains(name))
                violations.Add($"tiene '{name}', prohibido por SecuritySpec");
            else if (name.Contains(SmelterTableMarker, StringComparison.Ordinal))
                violations.Add($"tiene '{name}': privilegio sobre una tabla de Smelter que SecuritySpec no declara");
            else
                violations.Add($"tiene '{name}': privilegio fuera de la spec (los roles de Smelter no llevan privilegios de plataforma de más)");
        }

        return violations;
    }

    /// <summary>
    /// Nombres de privilegios presentes que la spec NO declara ni tolera (los prohibidos incluidos): lo que el deploy debe quitar con
    /// RemovePrivilegeRole. No incluye los declarados con otra profundidad (a esos los corrige AddPrivilegesRole).
    /// </summary>
    internal static IReadOnlyList<string> Unexpected(RoleSpec role, IEnumerable<(string Name, Microsoft.Crm.Sdk.Messages.PrivilegeDepth Depth)> actual)
    {
        var declared = role.Privileges.Select(p => p.PrivilegeName).ToHashSet();
        var allowed = role.AllowedExtras.ToHashSet();
        var denied = role.Denied.Select(d => d.PrivilegeName).ToHashSet();

        return actual
            .Select(p => p.Name)
            .Where(name => denied.Contains(name) || (!declared.Contains(name) && !allowed.Contains(name)))
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }
}
