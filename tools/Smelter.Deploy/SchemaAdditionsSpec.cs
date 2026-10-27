// Especificación DECLARATIVA de las columnas NUEVAS del schema (1.0.8) — misma filosofía que
// ApiSpec/StepSpec/KeySpec/SchemaCleanupSpec: fuente única de verdad, consumida por
// SchemaAdditionsProvisioner (deploy) y AdditionsAssertions (verify). Todo cambio de schema va por SDK.
//
//  - sanic_smlt_ij_bytecursor (Import Job): byte del próximo registro sin leer. Hace que ProcessChunk baje
//    solo el rango de su chunk (O(N)) en vez del archivo entero (O(N²)). Es Decimal con precisión 0 y NO
//    Whole Number: un File column llega a 10 GB y el whole number de Dataverse es int32 (~2.1 GB). Un Decimal
//    admite hasta 100.000.000.000. No va al form del job (no hay patrón de campos ocultos en ese form).
//  - sanic_smlt_tm_insertonly (Table Mapping): opt-in "Solo inserción (permite duplicados)". Sin dedup columns,
//    PrepareImport/ProcessChunk rechazan el template salvo que el mapping tenga este flag. Va al main form,
//    justo después de "Update If Exists".
//  - sanic_smlt_ij_failurereason (Import Job): motivo de la falla, texto multilínea 4000. Lo escribe SOLO el handler
//    "Failed" del Cloud Flow (ClientDataFailureReasonWriter) con el mensaje PII-free de la acción que falló. Va al
//    main form del job en solo lectura, justo después de Status.
//
// Las columnas nacen DENTRO de smlt_core_smelter (SolutionUniqueName): viajan a Test/Prod con el managed.
// Un atributo que ya existe NO se reconfigura (idempotente).

namespace Smelter.Deploy;

internal enum SchemaColumnKind { Decimal, Boolean, Memo }

/// <summary>Ubicación en el main form: se clona la fila del campo ancla y se inserta justo después.</summary>
/// <param name="ClassId">classid del control si no es el del ancla (p.ej. multilínea); null = el del ancla.</param>
/// <param name="ReadOnly">el control nace <c>disabled="true"</c>.</param>
internal sealed record FormPlacement(string AfterField, string Label, string? ClassId = null, bool ReadOnly = false);

internal sealed record SchemaColumnSpec(
    string EntityLogicalName,
    string SchemaName,
    SchemaColumnKind Kind,
    string DisplayName,
    string Description,
    FormPlacement? Form);

internal static class SchemaAdditionsCatalogo
{
    /// <summary>Whole number de bytes: precisión 0 (sin decimales).</summary>
    internal const int ByteCursorPrecision = 0;
    internal const decimal ByteCursorMin = 0m;
    /// <summary>Tope de un Decimal de Dataverse (10^11 bytes, ~93 GiB; un File column llega a 10 GB).</summary>
    internal const decimal ByteCursorMax = 100_000_000_000m;

    internal const bool InsertOnlyDefault = false;

    /// <summary>Largo máximo del motivo de falla; el Cloud Flow trunca a este valor.</summary>
    internal const int FailureReasonMaxLength = 4000;

    /// <summary>classid del control de texto multilínea en formxml.</summary>
    internal const string MemoControlClassId = "{E0DECE4B-6FC8-4A8F-A065-082708572369}";

    internal const string SolutionUniqueName = Catalogo.SolutionName;

    internal static readonly SchemaColumnSpec[] Columns =
    [
        new(
            EntityLogicalName: SecuritySpec.Tables.ImportJob,
            SchemaName: "sanic_smlt_ij_bytecursor",
            Kind: SchemaColumnKind.Decimal,
            DisplayName: "[IJ] Byte Cursor",
            Description: "Byte del proximo registro sin leer del CSV. Lo fija PrepareImport y lo avanza ProcessChunk; " +
                         "vacio = job preparado por una version anterior (ProcessChunk usa el camino de descarga completa).",
            Form: null),
        new(
            EntityLogicalName: SecuritySpec.Tables.TableMapping,
            SchemaName: "sanic_smlt_tm_insertonly",
            Kind: SchemaColumnKind.Boolean,
            DisplayName: "[TM] Solo inserción (permite duplicados)",
            Description: "Opt-in: el mapping se escribe siempre con Create, sin alternate key (reprocesar puede duplicar). " +
                         "Sin esto, un mapping sin Dedup Columns es rechazado por PrepareImport.",
            Form: new FormPlacement("sanic_smlt_tm_updateifexists", "[TM] Solo inserción (permite duplicados)")),
        new(
            EntityLogicalName: SecuritySpec.Tables.ImportJob,
            SchemaName: "sanic_smlt_ij_failurereason",
            Kind: SchemaColumnKind.Memo,
            DisplayName: "[IJ] Failure Reason",
            Description: "Motivo por el que el job quedo en Failed (mensaje del paso que fallo: preparacion o procesamiento). " +
                         "Lo escribe el Cloud Flow; vacio si el job no fallo. El detalle por fila esta en los Import Job Logs.",
            Form: new FormPlacement("sanic_smlt_ij_status", "[IJ] Failure Reason", SchemaAdditionsCatalogo.MemoControlClassId, ReadOnly: true)),
    ];
}
