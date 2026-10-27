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
//
// Las columnas nacen DENTRO de smlt_core_smelter (SolutionUniqueName): viajan a Test/Prod con el managed.
// Un atributo que ya existe NO se reconfigura (idempotente).

namespace Smelter.Deploy;

internal enum SchemaColumnKind { Decimal, Boolean }

/// <summary>Ubicación en el main form: se clona la fila del campo ancla y se inserta justo después.</summary>
internal sealed record FormPlacement(string AfterField, string Label);

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
    ];
}
