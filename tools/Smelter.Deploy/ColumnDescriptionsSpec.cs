// Especificación DECLARATIVA de las Description (tooltip en la MDA) de las columnas custom de las 6 tablas de
// Smelter (auditoría de campos #13/#14/#19: hoy ninguna tenía). La consume ColumnDescriptionsProvisioner (deploy,
// paso 4i) con UpdateAttributeRequest(MergeLabels) tocando SOLO la Description, y DescriptionsAssertions (verify)
// comprueba que ninguna columna sanic_smlt_* quede sin descripción.
//
// NO se tocan los display names: otras canvas apps los referencian en Power Fx. Las columnas creadas por
// SchemaAdditionsSpec nacen con su Description y no se repiten acá (lo garantiza un test de contrato).
// Los textos explican qué significa la columna y cómo la usa el motor (no repiten el nombre).

namespace Smelter.Deploy;

internal sealed record ColumnDescriptionSpec(string Entity, string Column, string Text);

internal static class ColumnDescriptionsCatalogo
{
    private const int Lcid = 1033;

    internal static int LanguageCode => Lcid;

    private static ColumnDescriptionSpec D(string entity, string column, string text) => new(entity, column, text);

    private const string IT = SecuritySpec.Tables.ImportTemplate;
    private const string TM = SecuritySpec.Tables.TableMapping;
    private const string CM = SecuritySpec.Tables.ColumnMapping;
    private const string RM = SecuritySpec.Tables.RelationshipMapping;
    private const string IJ = SecuritySpec.Tables.ImportJob;
    private const string IJL = SecuritySpec.Tables.ImportJobLog;

    internal static readonly ColumnDescriptionSpec[] Descriptions =
    [
        // ── Import Template ───────────────────────────────────────────────────────────────────
        D(IT, "sanic_smlt_it_name", "Nombre del template. Es la clave con la que el export/import de templates decide si crea uno nuevo o actualiza uno existente."),
        D(IT, "sanic_smlt_it_description", "Texto libre para el equipo: para qué archivo sirve este template. El motor no lo usa; viaja en el export/import."),
        D(IT, "sanic_smlt_it_sourcetype", "Tipo de archivo fuente. Solo existe CSV y el motor no lo lee; se conserva por compatibilidad con el export/import."),
        D(IT, "sanic_smlt_it_hasheaderrow", "Sí = la primera fila del CSV son los encabezados y los Source Column se resuelven por nombre. No = no hay encabezados y las columnas se llaman Column0, Column1, ..."),
        D(IT, "sanic_smlt_it_delimiter", "Separador de columnas del CSV (por defecto coma). Para tabulador escribí TAB o \\t (sin distinguir mayúsculas); cualquier otro texto se usa literal."),
        D(IT, "sanic_smlt_it_chunksize", "Filas que procesa cada invocación de ProcessChunk. 0 usa el default del sistema (1000); el valor se acota al rango 100 a 10000."),
        D(IT, "sanic_smlt_it_status", "Draft = en configuración, Active = listo, Archived = retirado. Solo un template Active puede correr imports: PrepareImport rechaza los demás."),

        // ── Table Mapping ─────────────────────────────────────────────────────────────────────
        D(TM, "sanic_smlt_tm_name", "Nombre descriptivo del mapping (por ejemplo, la tabla destino y su rol). Solo informativo."),
        D(TM, "sanic_smlt_tm_importtemplateid", "Template al que pertenece este mapping."),
        D(TM, "sanic_smlt_tm_targettable", "Nombre lógico de la tabla de Dataverse que recibe las filas (por ejemplo, contact)."),
        D(TM, "sanic_smlt_tm_processingorder", "Orden en que se procesan los mappings del template: el menor primero. Los padres deben ir antes que sus hijos (el motor además ordena por las relaciones)."),
        D(TM, "sanic_smlt_tm_dedupcolumns", "Nombres lógicos de las columnas DESTINO (separados por coma) que forman la clave para decidir si el registro existe. Cada una debe ser el Target Column de algún Column Mapping de este mapping."),
        D(TM, "sanic_smlt_tm_createifnotexists", "Si el registro no existe (la clave no coincide): Sí lo crea, No omite la fila y la cuenta como Skipped."),
        D(TM, "sanic_smlt_tm_updateifexists", "Si el registro ya existe (la clave coincide): Sí lo actualiza, No omite la fila y la cuenta como Skipped. Sin Dedup Columns no aplica."),

        // ── Column Mapping ────────────────────────────────────────────────────────────────────
        D(CM, "sanic_smlt_cm_name", "Nombre descriptivo del mapping de columna. Solo informativo."),
        D(CM, "sanic_smlt_cm_tablemappingid", "Table Mapping al que pertenece esta columna."),
        D(CM, "sanic_smlt_cm_sourcecolumn", "Nombre de la columna en el CSV (sin distinguir mayúsculas ni espacios alrededor). Opcional si el Transform Expression no lee del CSV, por ejemplo STATIC()."),
        D(CM, "sanic_smlt_cm_targetcolumn", "Nombre lógico de la columna de Dataverse donde se escribe el valor (por ejemplo, emailaddress1)."),
        D(CM, "sanic_smlt_cm_datatype", "Tipo al que se convierte el valor del CSV antes de escribirlo. Si el valor no se puede convertir, la fila falla con TypeError en el log."),
        D(CM, "sanic_smlt_cm_isrequired", "Sí = si el valor queda vacío (y no hay Default Value), la fila se rechaza con ValidationError."),
        D(CM, "sanic_smlt_cm_defaultvalue", "Valor que se usa cuando la celda del CSV viene vacía. Se aplica antes de validar Is Required."),
        D(CM, "sanic_smlt_cm_lookuptable", "Solo para Data Type Lookup: nombre lógico de la tabla a la que apunta el lookup. Obligatorio con ese tipo."),
        D(CM, "sanic_smlt_cm_lookupmatchcolumn", "Solo para Lookup: columna de la tabla referenciada por la que se busca el registro cuando el valor no es un GUID (debe ser clave alternativa)."),
        D(CM, "sanic_smlt_cm_optionsetmapping", "Solo para Data Type OptionSet: JSON que traduce el texto del CSV al valor numérico, por ejemplo {\"Activo\": 1}. Sin match se usa el valor tal cual."),
        D(CM, "sanic_smlt_cm_transformexpression", "Expresión del DSL de transformaciones (CONCAT, MAP, DATEPARSE, IF, SPLIT, HMAC, STATIC ...). Si está, tiene prioridad sobre el valor crudo del CSV."),

        // ── Relationship Mapping ──────────────────────────────────────────────────────────────
        D(RM, "sanic_smlt_rm_name", "Nombre descriptivo de la relación (por ejemplo, Cliente a Cuenta). Solo informativo."),
        D(RM, "sanic_smlt_rm_importtemplateid", "Template al que pertenece la relación. Filtra los Table Mappings elegibles como padre e hijo."),
        D(RM, "sanic_smlt_rm_parenttablemappingid", "Table Mapping del lado padre (uno). Debe tener Dedup Columns para poder resolver su clave."),
        D(RM, "sanic_smlt_rm_childtablemappingid", "Table Mapping del lado hijo (muchos): es el que recibe el lookup al padre."),
        D(RM, "sanic_smlt_rm_relationshiptype", "Tipo de relación. Solo se soporta OneToMany: el hijo tiene un lookup al padre."),
        D(RM, "sanic_smlt_rm_lookupcolumnonchild", "Nombre lógico de la columna lookup de la tabla hija que apunta al padre. Obligatorio: PrepareImport rechaza el template si falta."),
        D(RM, "sanic_smlt_rm_parentdedupcolumns", "Columnas del CSV (o del transform del padre), separadas por coma, que identifican al padre. Se emparejan por posición con las Dedup Columns del Table Mapping padre y deben ser la misma cantidad."),

        // ── Import Job ────────────────────────────────────────────────────────────────────────
        D(IJ, "sanic_smlt_ij_name", "Nombre descriptivo del job, por ejemplo template y fecha. Solo informativo."),
        D(IJ, "sanic_smlt_ij_importtemplateid", "Template que define cómo se interpreta el archivo. El template debe estar Active para poder ejecutar."),
        D(IJ, "sanic_smlt_ij_sourcefile", "Archivo CSV a importar (hasta 128 MB). Se lee por rangos de bytes, sin descargarlo entero en cada chunk."),
        D(IJ, "sanic_smlt_ij_isdryrun", "Sí = valida parseo, tipos y transforms de todas las filas sin escribir en Dataverse ni simular omitidos (Skipped) o claves existentes."),
        D(IJ, "sanic_smlt_ij_status", "Estado del job: Queued (en cola), Processing, Completed, CompletedWithErrors o Failed. Lo gobierna el Cloud Flow y el motor; no se edita a mano."),
        D(IJ, "sanic_smlt_ij_totalrows", "Total de filas de datos del CSV (sin encabezado). Lo calcula PrepareImport."),
        D(IJ, "sanic_smlt_ij_processedrows", "Filas ya procesadas, exitosas o no. Lo avanza ProcessChunk después de cada chunk."),
        D(IJ, "sanic_smlt_ij_successcount", "Filas escritas con éxito en Dataverse (creadas o actualizadas)."),
        D(IJ, "sanic_smlt_ij_failedcount", "Filas que fallaron. El detalle de cada una está en los Import Job Logs."),
        D(IJ, "sanic_smlt_ij_skippedcount", "Filas omitidas por los flags Create If Not Exists / Update If Exists del Table Mapping. No se registran en el log."),
        D(IJ, "sanic_smlt_ij_processingbatch", "Es el offset de filas ya procesadas: desde ahí retoma la siguiente invocación de ProcessChunk. Avanza al terminar cada chunk."),
        D(IJ, "sanic_smlt_ij_progress", "Porcentaje de avance, calculado por fórmula como filas procesadas sobre total de filas. Solo informativo."),
        D(IJ, "sanic_smlt_ij_starttime", "Momento en que PrepareImport inició el procesamiento."),
        D(IJ, "sanic_smlt_ij_endtime", "Momento en que ProcessChunk terminó la última fila del job."),

        // ── Import Job Log ────────────────────────────────────────────────────────────────────
        D(IJL, "sanic_smlt_ijl_id", "Identificador autonumérico del registro de log (IJL-0001 ...). No es un lookup al job."),
        D(IJL, "sanic_smlt_ijl_importjobid", "Job al que pertenece este registro."),
        D(IJL, "sanic_smlt_ijl_rownumber", "Número de fila del CSV donde ocurrió el fallo."),
        D(IJL, "sanic_smlt_ijl_targettable", "Nombre lógico de la tabla destino donde falló la operación."),
        D(IJL, "sanic_smlt_ijl_status", "Resultado registrado. El log guarda únicamente filas fallidas (Failed); éxitos y omitidos viven en los contadores del job."),
        D(IJL, "sanic_smlt_ijl_errorcategory", "Etapa del pipeline donde falló: SourceError, TransformError, TypeError, ValidationError, LookupUnresolved o TargetData."),
        D(IJL, "sanic_smlt_ijl_errormessage", "Detalle del error, con la columna implicada y la causa."),
    ];

    /// <summary>¿Hay que escribir la Description? (distinta de la actual, o actual vacía).</summary>
    internal static bool NeedsUpdate(string? current, string desired) =>
        !string.Equals(current, desired, StringComparison.Ordinal);
}
