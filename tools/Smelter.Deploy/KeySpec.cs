// Especificación DECLARATIVA de las alternate keys (EntityKey) de Smelter — misma filosofía que
// ApiSpec/StepSpec/SecuritySpec: fuente única de verdad, consumida por KeyProvisioner (deploy) y
// KeyAssertions (verify).
//
// Historia (1.2.1.0): la key sanic_smlt_ijl_key_logidempotente del Import Job Log nació MAL definida
// — incluía sanic_smlt_ijl_id, que NO es el lookup al job sino el nombre primario AUTONUMBER
// (IJL-{SEQNUM:4}, nvarchar). Un autonumber generado en cada create jamás puede dar idempotencia, y
// el plugin (escrito contra la doc, que también estaba mal) le pasaba un EntityReference → CADA
// upsert de log fallaba con "Incorrect type of attribute value" y el Import Job Log quedaba vacío.
// KeyProvisioner detecta la composición incorrecta y recrea la key con el FK real.

namespace Smelter.Deploy;

internal sealed record EntityKeySpec(
    string EntityLogicalName,
    // SchemaName de la key (LogicalName = lowercase). Se conserva el nombre original para no
    // divergir de la doc ni del display name existente.
    string SchemaName,
    string DisplayName,
    // Columnas que componen la key — la comparación en el provisioner es por CONJUNTO
    // (case-insensitive, sin orden): si el ambiente tiene otra composición, se recrea.
    string[] KeyAttributes);

internal static class KeyCatalogo
{
    public static readonly EntityKeySpec[] Keys =
    [
        new(
            EntityLogicalName: SecuritySpec.Tables.ImportJobLog,
            SchemaName: "sanic_smlt_ijl_key_logidempotente",
            DisplayName: "KEY | IJL | Log Idempotente",
            KeyAttributes:
            [
                "sanic_smlt_ijl_importjobid", // lookup REAL al Import Job (no el autonumber sanic_smlt_ijl_id)
                "sanic_smlt_ijl_rownumber",
                "sanic_smlt_ijl_targettable",
            ]),
    ];
}
