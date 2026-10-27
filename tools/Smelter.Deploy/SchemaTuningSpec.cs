// Especificación DECLARATIVA de ajustes sobre columnas y forms EXISTENTES (auditoría de campos 1.0.10). Misma
// filosofía que SchemaAdditionsSpec/SchemaCleanupSpec: la consumen SchemaTuningProvisioner (deploy, paso 4h, NO
// destructivo) y TuningAssertions (verify). Un ajuste que ya está aplicado se saltea (idempotente).
//
// NO se tocan display names de tablas ni columnas: otras canvas apps los referencian en Power Fx.
//
//  - sanic_smlt_it_sourcetype: el choice solo tiene CSV y el motor nunca lo lee. Pasa a no requerido, con default
//    CSV, y se oculta del main form del template (la columna se conserva: viaja en el export/import).
//  - sanic_smlt_tm_updateifexists: el default del schema era No mientras los docs y el fallback del Loader decían Sí.
//    Se alinea a Sí. Solo cambia el default de registros NUEVOS; los existentes conservan su valor.
//  - sanic_smlt_it_chunksize: el motor acota a 100..10000 (0 = default 1000) pero la columna admitía hasta 100000.
//    Se limita a 0..10000 para que el formulario lo rechace en vez de acotar en silencio. Los valores ya guardados no
//    se tocan.

using Microsoft.Xrm.Sdk.Metadata;

namespace Smelter.Deploy;

internal sealed record AttributeTuningSpec(
    string Entity,
    string Attribute,
    AttributeRequiredLevel? RequiredLevel,
    int? PicklistDefault,
    bool? BooleanDefault,
    string Reason,
    int? IntMin = null,
    int? IntMax = null);

internal sealed record FormFieldVisibilitySpec(string Entity, string Field, bool Visible, string Reason);

internal static class SchemaTuningCatalogo
{
    /// <summary>Valor de la única opción de Source Type (CSV).</summary>
    internal const int SourceTypeCsv = 0;

    internal const bool UpdateIfExistsDefault = true;

    /// <summary>Chunk Size admite 0 (= default del sistema) y como máximo 10000 (tope del motor).</summary>
    internal const int ChunkSizeMin = 0, ChunkSizeMax = 10000;

    internal static readonly AttributeTuningSpec[] Attributes =
    [
        new(SecuritySpec.Tables.ImportTemplate, "sanic_smlt_it_sourcetype",
            AttributeRequiredLevel.None, SourceTypeCsv, null,
            "inerte: una sola opción que el motor nunca lee"),
        new(SecuritySpec.Tables.TableMapping, "sanic_smlt_tm_updateifexists",
            null, null, UpdateIfExistsDefault,
            "default alineado con los docs y con el fallback del Loader (antes No)"),
        new(SecuritySpec.Tables.ImportTemplate, "sanic_smlt_it_chunksize",
            null, null, null,
            "el motor acota a 100..10000 (0 = default): el form lo valida en vez de acotar en silencio",
            IntMin: ChunkSizeMin, IntMax: ChunkSizeMax),
    ];

    internal static readonly FormFieldVisibilitySpec[] HiddenFields =
    [
        new(SecuritySpec.Tables.ImportTemplate, "sanic_smlt_it_sourcetype", false,
            "el Configurador no debe elegir algo sin efecto"),
    ];
}
