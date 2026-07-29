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
    ];
}
