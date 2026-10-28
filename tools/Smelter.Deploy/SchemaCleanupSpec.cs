// Especificación DECLARATIVA de los atributos a ELIMINAR del schema (audit 1.3.0.0) — campos
// muertos confirmados contra el código: nadie los carga, lee ni escribe. Misma filosofía que
// ApiSpec/StepSpec/KeySpec: la consumen SchemaCleanupProvisioner (deploy) y CleanupAssertions
// (verify). Un atributo ya eliminado se saltea (idempotente).

namespace Smelter.Deploy;

internal sealed record AttributeCleanupSpec(string EntityLogicalName, string AttributeLogicalName, string Reason);

internal static class SchemaCleanupCatalogo
{
    public static readonly AttributeCleanupSpec[] Attributes =
    [
        new(SecuritySpec.Tables.TableMapping, "sanic_smlt_tm_sourcesheet",
            "residuo de la era Excel — huérfano total (ni código, ni forms, ni export)"),
        new(SecuritySpec.Tables.ImportJob, "sanic_smlt_ij_isretry",
            "feature de retry por fila nunca implementada"),
        new(SecuritySpec.Tables.ImportJob, "sanic_smlt_ij_totalbatches",
            "nunca escrito ni leído; ni siquiera documentado"),
        new(SecuritySpec.Tables.ImportJob, "sanic_smlt_ij_errorsummary",
            "el Cloud Flow real nunca lo escribe (solo setea status=4) — siempre vacío"),
        new(SecuritySpec.Tables.RelationshipMapping, "sanic_smlt_rm_intersecttable",
            "mitad de la feature N:M nunca implementada; se perdía en el export/import"),
        new(SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_createdrecordid",
            "el log solo registra fallos — nunca hay record creado que apuntar"),
        new(SecuritySpec.Tables.TableMapping, "sanic_smlt_tm_isjunctiontable",
            "placeholder de N:M nunca implementado; ningún lector en el motor (auditoría de campos 1.0.10)"),
    ];
}

/// <summary>
/// Opción de un choice LOCAL a eliminar con DeleteOptionValueRequest (solo si ninguna fila la usa).
/// <paramref name="PurgeRowsInUse"/>: solo para datos descartables (Import Job Log = diagnóstico); las filas que
/// usan la opción se borran antes. Nunca para tablas de configuración.
/// </summary>
internal sealed record OptionCleanupSpec(
    string EntityLogicalName, string AttributeLogicalName, int Value, string Label, string Reason,
    bool PurgeRowsInUse = false);

/// <summary>
/// Opciones muertas (auditoría de campos 1.0.10): el Configurador las veía en el desplegable pero el motor nunca
/// las produce / las ignora. Las eliminan SchemaCleanupProvisioner (4d, destructivo) y las verifica CleanupAssertions.
/// El provisioner se NIEGA si alguna fila existente usa el valor (nunca deja datos con una opción inexistente), salvo
/// en los logs (PurgeRowsInUse): son filas de diagnóstico de la versión previa al rediseño (logueaba también los
/// éxitos) y se borran antes de quitar la opción.
/// </summary>
internal static class OptionCleanupCatalogo
{
    public static readonly OptionCleanupSpec[] Options =
    [
        new(SecuritySpec.Tables.RelationshipMapping, "sanic_smlt_rm_relationshiptype", 1, "ManyToMany",
            "N:M nunca se implementó: el motor ignoraba el mapping en silencio"),
        new(SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_status", 0, "Created",
            "el log solo registra fallos; Created nunca se escribe", PurgeRowsInUse: true),
        new(SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_status", 1, "Updated",
            "el log solo registra fallos; Updated nunca se escribe", PurgeRowsInUse: true),
        new(SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_status", 2, "Skipped",
            "el log solo registra fallos; Skipped solo alimenta el contador del job", PurgeRowsInUse: true),
        new(SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_errorcategory", 6, "Transient",
            "reintento por falla transitoria diferido: nunca se produce", PurgeRowsInUse: true),
    ];
}
