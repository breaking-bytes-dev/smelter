// Especificación DECLARATIVA de las Description (tooltip en la MDA) de las columnas custom de las 6 tablas de
// Smelter (auditoría de campos #13/#14/#19: hoy ninguna tenía), BILINGÜES: inglés bajo LCID 1033 (idioma base) y español
// bajo 3082. La consume ColumnDescriptionsProvisioner (deploy, paso 4i) con UpdateAttributeRequest(MergeLabels) tocando
// SOLO la Description, y DescriptionsAssertions (verify) comprueba que ninguna columna sanic_smlt_* quede sin descripción
// en ninguno de los dos idiomas.
//
// NO se tocan los display names: otras canvas apps los referencian en Power Fx. Las columnas creadas por
// SchemaAdditionsSpec nacen con su Description y no se repiten acá (lo garantiza un test de contrato).
// Los textos explican qué significa la columna y cómo la usa el motor (no repiten el nombre).

namespace Smelter.Deploy;

internal sealed record ColumnDescriptionSpec(string Entity, string Column, Bilingual Text);

internal static class ColumnDescriptionsCatalogo
{
    /// <summary>Idioma BASE de la Description (el otro, 3082, es la traducción).</summary>
    internal static int LanguageCode => Lcids.English;

    // El primer texto es el español (el original de la spec), el segundo su versión en inglés.
    private static ColumnDescriptionSpec D(string entity, string column, string es, string en) => new(entity, column, new Bilingual(en, es));

    private const string IT = SecuritySpec.Tables.ImportTemplate;
    private const string TM = SecuritySpec.Tables.TableMapping;
    private const string CM = SecuritySpec.Tables.ColumnMapping;
    private const string RM = SecuritySpec.Tables.RelationshipMapping;
    private const string IJ = SecuritySpec.Tables.ImportJob;
    private const string IJL = SecuritySpec.Tables.ImportJobLog;

    internal static readonly ColumnDescriptionSpec[] Descriptions =
    [
        // ── Import Template ───────────────────────────────────────────────────────────────────
        D(IT, "sanic_smlt_it_name", "Nombre del template. Es la clave con la que el export/import de templates decide si crea uno nuevo o actualiza uno existente.",
            "Template name. It is the key the template export/import uses to decide whether to create a new one or update an existing one."),
        D(IT, "sanic_smlt_it_description", "Texto libre para el equipo: para qué archivo sirve este template. El motor no lo usa; viaja en el export/import.",
            "Free text for the team: what file this template is for. The engine does not use it; it travels with the export/import."),
        D(IT, "sanic_smlt_it_sourcetype", "Tipo de archivo fuente. Solo existe CSV y el motor no lo lee; se conserva por compatibilidad con el export/import.",
            "Source file type. Only CSV exists and the engine does not read it; kept for export/import compatibility."),
        D(IT, "sanic_smlt_it_hasheaderrow", "Sí = la primera fila del CSV son los encabezados y los Source Column se resuelven por nombre. No = no hay encabezados y las columnas se llaman Column0, Column1, ...",
            "Yes = the first CSV row holds the headers and Source Column values are resolved by name. No = there are no headers and columns are named Column0, Column1, ..."),
        D(IT, "sanic_smlt_it_delimiter", "Separador de columnas del CSV (por defecto coma). Para tabulador escribí TAB o \\t (sin distinguir mayúsculas); cualquier otro texto se usa literal.",
            "CSV column separator (comma by default). For tab, type TAB or \\t (case-insensitive); any other text is used literally."),
        D(IT, "sanic_smlt_it_chunksize", "Filas que procesa cada invocación de ProcessChunk. 0 usa el default del sistema (1000); el valor se acota al rango 100 a 10000.",
            "Rows processed by each ProcessChunk call. 0 uses the system default (1000); the value is clamped to the range 100 to 10000."),
        D(IT, "sanic_smlt_it_status", "Draft = en configuración, Active = listo, Archived = retirado. Solo un template Active puede correr imports: PrepareImport rechaza los demás.",
            "Draft = being configured, Active = ready, Archived = retired. Only an Active template can run imports: PrepareImport rejects the others."),

        // ── Table Mapping ─────────────────────────────────────────────────────────────────────
        D(TM, "sanic_smlt_tm_name", "Nombre descriptivo del mapping (por ejemplo, la tabla destino y su rol). Solo informativo.",
            "Descriptive name of the mapping (for example, the target table and its role). Informational only."),
        D(TM, "sanic_smlt_tm_importtemplateid", "Template al que pertenece este mapping.",
            "Template this mapping belongs to."),
        D(TM, "sanic_smlt_tm_targettable", "Nombre lógico de la tabla de Dataverse que recibe las filas (por ejemplo, contact).",
            "Logical name of the Dataverse table that receives the rows (for example, contact)."),
        D(TM, "sanic_smlt_tm_processingorder", "Orden en que se procesan los mappings del template: el menor primero. Los padres deben ir antes que sus hijos (el motor además ordena por las relaciones).",
            "Order in which the template's mappings are processed: lowest first. Parents must come before their children (the engine also orders by relationships)."),
        D(TM, "sanic_smlt_tm_dedupcolumns", "Nombres lógicos de las columnas DESTINO (separados por coma) que forman la clave para decidir si el registro existe. Cada una debe ser el Target Column de algún Column Mapping de este mapping.",
            "Logical names of the TARGET columns (comma-separated) that form the key used to decide whether the record exists. Each one must be the Target Column of some Column Mapping of this mapping."),
        D(TM, "sanic_smlt_tm_createifnotexists", "Si el registro no existe (la clave no coincide): Sí lo crea, No omite la fila y la cuenta como Skipped.",
            "If the record does not exist (the key does not match): Yes creates it, No skips the row and counts it as Skipped."),
        D(TM, "sanic_smlt_tm_updateifexists", "Si el registro ya existe (la clave coincide): Sí lo actualiza, No omite la fila y la cuenta como Skipped. Sin Dedup Columns no aplica.",
            "If the record already exists (the key matches): Yes updates it, No skips the row and counts it as Skipped. Does not apply without Dedup Columns."),

        // ── Column Mapping ────────────────────────────────────────────────────────────────────
        D(CM, "sanic_smlt_cm_name", "Nombre descriptivo del mapping de columna. Solo informativo.",
            "Descriptive name of the column mapping. Informational only."),
        D(CM, "sanic_smlt_cm_tablemappingid", "Table Mapping al que pertenece esta columna.",
            "Table Mapping this column belongs to."),
        D(CM, "sanic_smlt_cm_sourcecolumn", "Nombre de la columna en el CSV (sin distinguir mayúsculas ni espacios alrededor). Opcional si el Transform Expression no lee del CSV, por ejemplo STATIC().",
            "Name of the column in the CSV (case-insensitive, surrounding spaces ignored). Optional if the Transform Expression does not read from the CSV, for example STATIC()."),
        D(CM, "sanic_smlt_cm_targetcolumn", "Nombre lógico de la columna de Dataverse donde se escribe el valor (por ejemplo, emailaddress1).",
            "Logical name of the Dataverse column the value is written to (for example, emailaddress1)."),
        D(CM, "sanic_smlt_cm_datatype", "Tipo al que se convierte el valor del CSV antes de escribirlo. Si el valor no se puede convertir, la fila falla con TypeError en el log.",
            "Type the CSV value is converted to before it is written. If the value cannot be converted, the row fails with TypeError in the log."),
        D(CM, "sanic_smlt_cm_isrequired", "Sí = si el valor queda vacío (y no hay Default Value), la fila se rechaza con ValidationError.",
            "Yes = if the value ends up empty (and there is no Default Value), the row is rejected with ValidationError."),
        D(CM, "sanic_smlt_cm_defaultvalue", "Valor que se usa cuando la celda del CSV viene vacía. Se aplica antes de validar Is Required.",
            "Value used when the CSV cell is empty. Applied before Is Required is validated."),
        D(CM, "sanic_smlt_cm_lookuptable", "Solo para Data Type Lookup: nombre lógico de la tabla a la que apunta el lookup. Obligatorio con ese tipo.",
            "Only for Data Type Lookup: logical name of the table the lookup points to. Required with that type."),
        D(CM, "sanic_smlt_cm_lookupmatchcolumn", "Solo para Lookup: columna de la tabla referenciada por la que se busca el registro cuando el valor no es un GUID (debe ser clave alternativa).",
            "Only for Lookup: column of the referenced table used to find the record when the value is not a GUID (must be an alternate key)."),
        D(CM, "sanic_smlt_cm_optionsetmapping", "Solo para Data Type OptionSet: JSON que traduce el texto del CSV al valor numérico, por ejemplo {\"Activo\": 1}. Sin match se usa el valor tal cual.",
            "Only for Data Type OptionSet: JSON that translates the CSV text to the numeric value, for example {\"Active\": 1}. With no match the value is used as is."),
        D(CM, "sanic_smlt_cm_transformexpression", "Expresión del DSL de transformaciones (CONCAT, MAP, DATEPARSE, IF, SPLIT, HMAC, STATIC ...). Si está, tiene prioridad sobre el valor crudo del CSV.",
            "Expression in the transform DSL (CONCAT, MAP, DATEPARSE, IF, SPLIT, HMAC, STATIC ...). If present, it takes priority over the raw CSV value."),

        // ── Relationship Mapping ──────────────────────────────────────────────────────────────
        D(RM, "sanic_smlt_rm_name", "Nombre descriptivo de la relación (por ejemplo, Cliente a Cuenta). Solo informativo.",
            "Descriptive name of the relationship (for example, Customer to Account). Informational only."),
        D(RM, "sanic_smlt_rm_importtemplateid", "Template al que pertenece la relación. Filtra los Table Mappings elegibles como padre e hijo.",
            "Template the relationship belongs to. Filters the Table Mappings that can be chosen as parent and child."),
        D(RM, "sanic_smlt_rm_parenttablemappingid", "Table Mapping del lado padre (uno). Debe tener Dedup Columns para poder resolver su clave.",
            "Table Mapping on the parent side (one). It must have Dedup Columns so its key can be resolved."),
        D(RM, "sanic_smlt_rm_childtablemappingid", "Table Mapping del lado hijo (muchos): es el que recibe el lookup al padre.",
            "Table Mapping on the child side (many): the one that receives the lookup to the parent."),
        D(RM, "sanic_smlt_rm_relationshiptype", "Tipo de relación. Solo se soporta OneToMany: el hijo tiene un lookup al padre.",
            "Relationship type. Only OneToMany is supported: the child has a lookup to the parent."),
        D(RM, "sanic_smlt_rm_lookupcolumnonchild", "Nombre lógico de la columna lookup de la tabla hija que apunta al padre. Obligatorio: PrepareImport rechaza el template si falta.",
            "Logical name of the lookup column on the child table that points to the parent. Required: PrepareImport rejects the template if it is missing."),
        D(RM, "sanic_smlt_rm_parentdedupcolumns", "Columnas del CSV (o del transform del padre), separadas por coma, que identifican al padre. Se emparejan por posición con las Dedup Columns del Table Mapping padre y deben ser la misma cantidad.",
            "CSV columns (or parent transform outputs), comma-separated, that identify the parent. They are matched by position with the parent Table Mapping's Dedup Columns and must be the same number."),

        // ── Import Job ────────────────────────────────────────────────────────────────────────
        D(IJ, "sanic_smlt_ij_name", "Nombre descriptivo del job, por ejemplo template y fecha. Solo informativo.",
            "Descriptive name of the job, for example template and date. Informational only."),
        D(IJ, "sanic_smlt_ij_importtemplateid", "Template que define cómo se interpreta el archivo. El template debe estar Active para poder ejecutar.",
            "Template that defines how the file is interpreted. The template must be Active for the job to run."),
        D(IJ, "sanic_smlt_ij_sourcefile", "Archivo CSV a importar (hasta 128 MB). Se lee por rangos de bytes, sin descargarlo entero en cada chunk.",
            "CSV file to import (up to 128 MB). It is read by byte ranges, without downloading it whole on every chunk."),
        D(IJ, "sanic_smlt_ij_isdryrun", "Sí = valida parseo, tipos y transforms de todas las filas sin escribir en Dataverse ni simular omitidos (Skipped) o claves existentes.",
            "Yes = validates parsing, types and transforms of all rows without writing to Dataverse or simulating skipped (Skipped) rows or existing keys."),
        D(IJ, "sanic_smlt_ij_status", "Estado del job: Queued (en cola), Processing, Completed, CompletedWithErrors o Failed. Lo gobierna el Cloud Flow y el motor; no se edita a mano.",
            "Job status: Queued, Processing, Completed, CompletedWithErrors or Failed. Driven by the Cloud Flow and the engine; do not edit by hand."),
        D(IJ, "sanic_smlt_ij_totalrows", "Total de filas de datos del CSV (sin encabezado). Lo calcula PrepareImport.",
            "Total data rows in the CSV (excluding the header). Calculated by PrepareImport."),
        D(IJ, "sanic_smlt_ij_processedrows", "Filas ya procesadas, exitosas o no. Lo avanza ProcessChunk después de cada chunk.",
            "Rows already processed, successful or not. ProcessChunk advances it after each chunk."),
        D(IJ, "sanic_smlt_ij_successcount", "Filas escritas con éxito en Dataverse (creadas o actualizadas).",
            "Rows successfully written to Dataverse (created or updated)."),
        D(IJ, "sanic_smlt_ij_failedcount", "Filas que fallaron. El detalle de cada una está en los Import Job Logs.",
            "Rows that failed. The detail of each one is in the Import Job Logs."),
        D(IJ, "sanic_smlt_ij_skippedcount", "Filas omitidas por los flags Create If Not Exists / Update If Exists del Table Mapping. No se registran en el log.",
            "Rows skipped by the Table Mapping's Create If Not Exists / Update If Exists flags. They are not recorded in the log."),
        D(IJ, "sanic_smlt_ij_processingbatch", "Es el offset de filas ya procesadas: desde ahí retoma la siguiente invocación de ProcessChunk. Avanza al terminar cada chunk.",
            "Offset of rows already processed: the next ProcessChunk call resumes from there. It advances when each chunk finishes."),
        D(IJ, "sanic_smlt_ij_progress", "Porcentaje de avance, calculado por fórmula como filas procesadas sobre total de filas. Solo informativo.",
            "Progress percentage, calculated by formula as processed rows over total rows. Informational only."),
        D(IJ, "sanic_smlt_ij_starttime", "Momento en que PrepareImport inició el procesamiento.",
            "Moment PrepareImport started processing."),
        D(IJ, "sanic_smlt_ij_endtime", "Momento en que ProcessChunk terminó la última fila del job.",
            "Moment ProcessChunk finished the last row of the job."),

        // ── Import Job Log ────────────────────────────────────────────────────────────────────
        D(IJL, "sanic_smlt_ijl_id", "Identificador autonumérico del registro de log (IJL-0001 ...). No es un lookup al job.",
            "Autonumber identifier of the log record (IJL-0001 ...). It is not a lookup to the job."),
        D(IJL, "sanic_smlt_ijl_importjobid", "Job al que pertenece este registro.",
            "Job this record belongs to."),
        D(IJL, "sanic_smlt_ijl_rownumber", "Número de fila del CSV donde ocurrió el fallo.",
            "CSV row number where the failure occurred."),
        D(IJL, "sanic_smlt_ijl_targettable", "Nombre lógico de la tabla destino donde falló la operación.",
            "Logical name of the target table where the operation failed."),
        D(IJL, "sanic_smlt_ijl_status", "Resultado registrado. El log guarda únicamente filas fallidas (Failed); éxitos y omitidos viven en los contadores del job.",
            "Recorded result. The log stores only failed rows (Failed); successes and skipped rows live in the job counters."),
        D(IJL, "sanic_smlt_ijl_errorcategory", "Etapa del pipeline donde falló: SourceError, TransformError, TypeError, ValidationError, LookupUnresolved o TargetData.",
            "Pipeline stage where it failed: SourceError, TransformError, TypeError, ValidationError, LookupUnresolved or TargetData."),
        D(IJL, "sanic_smlt_ijl_errormessage", "Detalle del error, con la columna implicada y la causa.",
            "Error detail, with the column involved and the cause."),
    ];

    /// <summary>
    /// Description de las columnas que crea SchemaAdditionsSpec (paso 4f). Nacen con su texto (en español, bajo 1033) y este
    /// paso las deja bilingües. No se repiten en <see cref="Descriptions"/> (un test lo garantiza).
    /// </summary>
    internal static readonly ColumnDescriptionSpec[] AddedColumnDescriptions =
    [
        D(IJ, "sanic_smlt_ij_bytecursor",
            "Byte del próximo registro sin leer del CSV. Lo fija PrepareImport y lo avanza ProcessChunk; vacío = job preparado por una versión anterior (ProcessChunk usa el camino de descarga completa).",
            "Byte of the next unread record in the CSV. Set by PrepareImport and advanced by ProcessChunk; empty = job prepared by an earlier version (ProcessChunk takes the full-download path)."),
        D(TM, "sanic_smlt_tm_insertonly",
            "Opt-in: el mapping se escribe siempre con Create, sin alternate key (reprocesar puede duplicar). Sin esto, un mapping sin Dedup Columns es rechazado por PrepareImport.",
            "Opt-in: the mapping is always written with Create, without an alternate key (reprocessing may duplicate). Without this, a mapping with no Dedup Columns is rejected by PrepareImport."),
        D(IJ, "sanic_smlt_ij_failurereason",
            "Motivo por el que el job quedó en Failed (mensaje del paso que falló: preparación o procesamiento). Lo escribe el Cloud Flow; vacío si el job no falló. El detalle por fila está en los Import Job Logs.",
            "Reason the job ended in Failed (message of the step that failed: preparation or processing). Written by the Cloud Flow; empty if the job did not fail. The per-row detail is in the Import Job Logs."),
    ];

    /// <summary>Todas las Description que provisiona el paso 4i: las de las columnas existentes + las de las columnas nuevas.</summary>
    internal static IEnumerable<ColumnDescriptionSpec> All => Descriptions.Concat(AddedColumnDescriptions);

    /// <summary>¿Hay que escribir la Description? (distinta de la actual, o actual vacía). Para un solo idioma.</summary>
    internal static bool NeedsUpdate(string? current, string desired) =>
        !string.Equals(current, desired, StringComparison.Ordinal);

    /// <summary>¿Hay que escribir la Description bilingüe? Sí si cualquiera de los dos idiomas difiere del actual.</summary>
    internal static bool NeedsUpdate(Microsoft.Xrm.Sdk.Label? current, Bilingual desired) =>
        NeedsUpdate(LabelEditor.TextFor(current, Lcids.English), desired.En)
        || NeedsUpdate(LabelEditor.TextFor(current, Lcids.Spanish), desired.Es);
}
