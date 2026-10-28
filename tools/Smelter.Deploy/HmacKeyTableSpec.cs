// Especificación DECLARATIVA de la tabla de keys HMAC (función HMAC del Transform DSL).
// Misma filosofía que ApiSpec/KeySpec/SchemaCleanupSpec: fuente única de verdad, consumida por
// HmacKeyTableProvisioner (deploy) y HmacKeyAssertions (verify).
//
// Los nombres duplican las constantes de DataverseHmacKeyProvider (src/Smelter.Plugins): ese
// assembly es net462 y no se puede referenciar desde acá — si cambian, cambian en los dos lados.
//
// La tabla ES componente de la solución smlt_core_smelter: su SCHEMA viaja a Test/Prod con la
// solución managed. El VALOR de la key (la fila) NUNCA viaja: lo carga a mano un administrador en
// cada entorno (ni CI, ni repo, ni GitHub secret). Procedimiento: docs/tecnica/03-seguridad.md (Clave HMAC) y docs/tecnica/09-instalacion-en-otro-tenant.md.
//
// Riesgo aceptado conscientemente (documentado en docs/tecnica/03-seguridad.md): cualquier System
// Administrator del entorno puede leer la key; las copias/backups del entorno la arrastran; la
// columna del valor no se audita. Mitigaciones: la tabla NO está en SecuritySpec.Tables ni en
// ningún rol de Smelter (solo SYSTEM —el plugin— y System Administrator), es Organization-owned,
// tiene audit de tabla (quién creó/rotó/borró una key) pero SIN audit de la columna del valor y NO
// es válida para Advanced Find. Endurecimiento futuro posible: Azure Key Vault con managed identity
// detrás de IHmacKeyProvider (misma key => mismos hashes, la migración no rompe nada).

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
        "Keys HMAC resueltas por DataverseHmacKeyProvider. El schema viaja en la solucion; el valor " +
        "se carga a mano en cada entorno. Fuera de todo rol de Smelter: solo SYSTEM / System Administrator.";

    internal const string NameDisplayName = "[HK] Name";
    internal const string ValueDisplayName = "[HK] Value (hex)";

    /// <summary>128 chars: alcanza para keys de hasta 64 bytes en hex.</summary>
    internal const int ValueMaxLength = 128;
    internal const int NameMaxLength = 100;

    internal const bool TableAudited = true;
    internal const bool ValueColumnAudited = false;
    internal const bool ValueColumnValidForAdvancedFind = false;

    /// <summary>
    /// Column security (AttributeMetadata.IsSecured). Defensa en profundidad: el form muestra ****, y un rol
    /// que reciba acceso a la tabla por error igual no lee el valor. NO hay field security profile (nadie salvo
    /// SYSTEM —el plugin— y System Administrator). Limite documentado: la seguridad de columna NO oculta el
    /// dato a los System Administrators.
    /// </summary>
    internal const bool ValueColumnSecured = true;

    /// <summary>La tabla (schema) pertenece a la solución core: viaja a Test/Prod con el managed.</summary>
    internal const string SolutionUniqueName = Catalogo.SolutionName;

    /// <summary>ComponentType de Entity en solutioncomponent / AddSolutionComponentRequest.</summary>
    internal const int EntityComponentType = 1;
}
