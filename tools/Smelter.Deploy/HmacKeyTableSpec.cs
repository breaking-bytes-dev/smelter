// Especificación DECLARATIVA de la tabla DEV-only de keys HMAC (función HMAC del Transform DSL).
// Misma filosofía que ApiSpec/KeySpec/SchemaCleanupSpec: fuente única de verdad, consumida por
// HmacKeyTableProvisioner (deploy) y HmacKeyAssertions (verify).
//
// Los nombres duplican las constantes de DataverseHmacKeyProvider (src/Smelter.Plugins): ese
// assembly es net462 y no se puede referenciar desde acá — si cambian, cambian en los dos lados.
//
// DEV ONLY. Esta tabla guarda la key en Dataverse; PROD debe resolverla con managed identity +
// Azure Key Vault (un Custom API plugin no recibe secure config y los secret env vars de Key Vault
// no llegan a plugins). Por eso la tabla:
//   - NO está en SecuritySpec.Tables ni en ningún rol de Smelter: solo SYSTEM (el plugin) y
//     System Administrator la leen.
//   - se crea SIN SolutionUniqueName (Default solution): no viaja en el export de smlt_core_smelter,
//     así que no llega a PROD ni a solutions/unpacked.
//   - es Organization-owned, con audit de tabla (quién creó/rotó/borró una key) pero SIN audit de la
//     columna del valor y NO válida para Advanced Find.

namespace Smelter.Deploy;

internal static class HmacKeyTableSpec
{
    internal const string Table = "sanic_tbl_smlt_hmackey";

    /// <summary>Nombre primario: el nombre (label) de la key, p.ej. "cm-pan".</summary>
    internal const string NameColumn = "sanic_smlt_hk_name";

    /// <summary>Texto: la key en hex (64 chars para 32 bytes).</summary>
    internal const string ValueColumn = "sanic_smlt_hk_value";

    internal const string TableDisplayName = "TBL | SMLT | HMAC Key";
    internal const string TableDisplayCollectionName = "TBL | SMLT | HMAC Keys";
    internal const string TableDescription =
        "DEV ONLY. Keys HMAC resueltas por DataverseHmacKeyProvider. Fuera de todo rol de Smelter: " +
        "solo SYSTEM / System Administrator. En PROD usar managed identity + Azure Key Vault.";

    internal const string NameDisplayName = "[HK] Name";
    internal const string ValueDisplayName = "[HK] Value (hex)";

    /// <summary>128 chars: alcanza para keys de hasta 64 bytes en hex.</summary>
    internal const int ValueMaxLength = 128;
    internal const int NameMaxLength = 100;

    internal const bool TableAudited = true;
    internal const bool ValueColumnAudited = false;
    internal const bool ValueColumnValidForAdvancedFind = false;

    /// <summary>null a propósito: la tabla NO se agrega a smlt_core_smelter (DEV-only, no viaja a PROD).</summary>
    internal static readonly string? SolutionUniqueName = null;
}
