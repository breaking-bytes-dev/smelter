// Especificación DECLARATIVA de los nombres en ESPAÑOL (LCID 3082) de las tablas de Smelter, sus columnas sanic_smlt_* y las
// opciones de sus choices locales. La consume TableLabelsProvisioner (deploy, paso 4n) y TableLabelsAssertions (verify).
//
// RESTRICCIÓN DURA: el 1033 (inglés, base) de tablas y columnas NO se toca — otras canvas apps los referencian en Power Fx
// ("TBL | SMLT | Import Job", "[IJ] Status" ...). El provisioner LEE el label actual y le AGREGA/ACTUALIZA solo el 3082
// (UpdateEntity/UpdateAttribute con MergeLabels = true reenviando el 1033 tal cual; opciones con UpdateOptionValueRequest
// MergeLabels = true). Los nombres no llevan los prefijos técnicos del 1033 ("TBL | SMLT |", "[IJ]").
//
// Alcance: solo lo que existe HOY en Dev. Las columnas y opciones que SchemaCleanupSpec elimina (isjunctiontable,
// ManyToMany, Created/Updated/Skipped y Transient del log ...) no están acá; si alguna sobreviviera en un entorno, el
// provisioner la saltea (no existe en la metadata de la tabla) y TableLabelsAssertions la señalaría como sin 3082.
// Los textos de las columnas coinciden con el 1033 de solutions/unpacked/Entities (un test lo cruza) y con las columnas que
// agrega SchemaAdditionsSpec; la tabla de keys HMAC (HmacKeyTableSpec) no está en el unpacked: se verifica contra su spec.

namespace Smelter.Deploy;

internal sealed record TableLabelSpec(string Table, string DisplayEs, string CollectionEs);

internal sealed record ColumnLabelSpec(string Table, string Column, string Es);

internal sealed record OptionLabelSpec(string Table, string Column, int Value, string Es);

internal static class TableLabelsCatalogo
{
    private const string IT = SecuritySpec.Tables.ImportTemplate;
    private const string TM = SecuritySpec.Tables.TableMapping;
    private const string CM = SecuritySpec.Tables.ColumnMapping;
    private const string RM = SecuritySpec.Tables.RelationshipMapping;
    private const string IJ = SecuritySpec.Tables.ImportJob;
    private const string IJL = SecuritySpec.Tables.ImportJobLog;
    private const string HK = HmacKeyTableSpec.Table;

    private static ColumnLabelSpec C(string table, string column, string es) => new(table, column, es);
    private static OptionLabelSpec O(string table, string column, int value, string es) => new(table, column, value, es);

    internal static readonly TableLabelSpec[] Tables =
    [
        new(IT, "Template de importación", "Templates de importación"),
        new(TM, "Mapping de tabla", "Mappings de tabla"),
        new(CM, "Mapping de columna", "Mappings de columna"),
        new(RM, "Mapping de relación", "Mappings de relación"),
        new(IJ, "Import Job", "Import Jobs"),                       // término de producto: se conserva
        new(IJL, "Log de importación", "Logs de importación"),
        new(HK, "Clave HMAC", "Claves HMAC"),
    ];

    internal static readonly ColumnLabelSpec[] Columns =
    [
        // ── Import Template ───────────────────────────────────────────────────────────────────
        C(IT, "sanic_smlt_it_name", "Nombre"),
        C(IT, "sanic_smlt_it_description", "Descripción"),
        C(IT, "sanic_smlt_it_sourcetype", "Tipo de origen"),
        C(IT, "sanic_smlt_it_hasheaderrow", "Tiene fila de encabezados"),
        C(IT, "sanic_smlt_it_delimiter", "Delimitador"),
        C(IT, "sanic_smlt_it_chunksize", "Tamaño de chunk"),
        C(IT, "sanic_smlt_it_status", "Estado"),

        // ── Table Mapping ─────────────────────────────────────────────────────────────────────
        C(TM, "sanic_smlt_tm_name", "Nombre"),
        C(TM, "sanic_smlt_tm_importtemplateid", "Template de importación"),
        C(TM, "sanic_smlt_tm_targettable", "Tabla destino"),
        C(TM, "sanic_smlt_tm_processingorder", "Orden de procesamiento"),
        C(TM, "sanic_smlt_tm_dedupcolumns", "Columnas de deduplicación"),
        C(TM, "sanic_smlt_tm_createifnotexists", "Crear si no existe"),
        C(TM, "sanic_smlt_tm_updateifexists", "Actualizar si existe"),
        C(TM, "sanic_smlt_tm_insertonly", "Solo inserción (permite duplicados)"),

        // ── Column Mapping ────────────────────────────────────────────────────────────────────
        C(CM, "sanic_smlt_cm_name", "Nombre"),
        C(CM, "sanic_smlt_cm_tablemappingid", "Mapping de tabla"),
        C(CM, "sanic_smlt_cm_sourcecolumn", "Columna de origen"),
        C(CM, "sanic_smlt_cm_targetcolumn", "Columna destino"),
        C(CM, "sanic_smlt_cm_datatype", "Tipo de dato"),
        C(CM, "sanic_smlt_cm_isrequired", "Es obligatoria"),
        C(CM, "sanic_smlt_cm_defaultvalue", "Valor por defecto"),
        C(CM, "sanic_smlt_cm_lookuptable", "Tabla del lookup"),
        C(CM, "sanic_smlt_cm_lookupmatchcolumn", "Columna de coincidencia del lookup"),
        C(CM, "sanic_smlt_cm_optionsetmapping", "Mapeo de opciones"),
        C(CM, "sanic_smlt_cm_transformexpression", "Expresión de transformación"),

        // ── Relationship Mapping ──────────────────────────────────────────────────────────────
        C(RM, "sanic_smlt_rm_name", "Nombre"),
        C(RM, "sanic_smlt_rm_importtemplateid", "Template de importación"),
        C(RM, "sanic_smlt_rm_parenttablemappingid", "Mapping de tabla padre"),
        C(RM, "sanic_smlt_rm_childtablemappingid", "Mapping de tabla hija"),
        C(RM, "sanic_smlt_rm_relationshiptype", "Tipo de relación"),
        C(RM, "sanic_smlt_rm_lookupcolumnonchild", "Columna lookup en la tabla hija"),
        C(RM, "sanic_smlt_rm_parentdedupcolumns", "Columnas de deduplicación del padre"),

        // ── Import Job ────────────────────────────────────────────────────────────────────────
        C(IJ, "sanic_smlt_ij_name", "Nombre"),
        C(IJ, "sanic_smlt_ij_importtemplateid", "Template de importación"),
        C(IJ, "sanic_smlt_ij_sourcefile", "Archivo de origen"),
        C(IJ, "sanic_smlt_ij_isdryrun", "Es simulación (Dry Run)"),
        C(IJ, "sanic_smlt_ij_status", "Estado"),
        C(IJ, "sanic_smlt_ij_totalrows", "Total de filas"),
        C(IJ, "sanic_smlt_ij_processedrows", "Filas procesadas"),
        C(IJ, "sanic_smlt_ij_successcount", "Filas exitosas"),
        C(IJ, "sanic_smlt_ij_failedcount", "Filas fallidas"),
        C(IJ, "sanic_smlt_ij_skippedcount", "Filas omitidas"),
        C(IJ, "sanic_smlt_ij_processingbatch", "Offset de procesamiento"),
        C(IJ, "sanic_smlt_ij_progress", "Progreso"),
        C(IJ, "sanic_smlt_ij_starttime", "Hora de inicio"),
        C(IJ, "sanic_smlt_ij_endtime", "Hora de fin"),
        C(IJ, "sanic_smlt_ij_bytecursor", "Cursor de bytes"),
        C(IJ, "sanic_smlt_ij_failurereason", "Motivo de la falla"),

        // ── Import Job Log ────────────────────────────────────────────────────────────────────
        C(IJL, "sanic_smlt_ijl_id", "Identificador"),
        C(IJL, "sanic_smlt_ijl_importjobid", "Import Job"),
        C(IJL, "sanic_smlt_ijl_rownumber", "Número de fila"),
        C(IJL, "sanic_smlt_ijl_targettable", "Tabla destino"),
        C(IJL, "sanic_smlt_ijl_status", "Estado"),
        C(IJL, "sanic_smlt_ijl_errorcategory", "Categoría del error"),
        C(IJL, "sanic_smlt_ijl_errormessage", "Mensaje de error"),

        // ── HMAC Key (tabla DEV-only de keys; HmacKeyTableSpec) ───────────────────────────────
        C(HK, HmacKeyTableSpec.NameColumn, "Nombre"),
        C(HK, HmacKeyTableSpec.ValueColumn, "Valor (hex)"),
    ];

    /// <summary>
    /// Opciones de los choices locales. Para las columnas Sí/No (boolean) el valor 1 es la opción verdadera y el 0 la falsa
    /// (se actualizan con la columna misma; UpdateOptionValueRequest no aplica a booleans).
    /// </summary>
    internal static readonly OptionLabelSpec[] Options =
    [
        // Sí/No
        O(IT, "sanic_smlt_it_hasheaderrow", 1, "Sí"), O(IT, "sanic_smlt_it_hasheaderrow", 0, "No"),
        O(TM, "sanic_smlt_tm_createifnotexists", 1, "Sí"), O(TM, "sanic_smlt_tm_createifnotexists", 0, "No"),
        O(TM, "sanic_smlt_tm_updateifexists", 1, "Sí"), O(TM, "sanic_smlt_tm_updateifexists", 0, "No"),
        O(TM, "sanic_smlt_tm_insertonly", 1, "Sí"), O(TM, "sanic_smlt_tm_insertonly", 0, "No"),
        O(CM, "sanic_smlt_cm_isrequired", 1, "Sí"), O(CM, "sanic_smlt_cm_isrequired", 0, "No"),
        O(IJ, "sanic_smlt_ij_isdryrun", 1, "Sí"), O(IJ, "sanic_smlt_ij_isdryrun", 0, "No"),

        // Import Template
        O(IT, "sanic_smlt_it_sourcetype", 0, "CSV"),
        O(IT, "sanic_smlt_it_status", 0, "Borrador"),
        O(IT, "sanic_smlt_it_status", 1, "Activo"),
        O(IT, "sanic_smlt_it_status", 2, "Archivado"),

        // Column Mapping: tipo de dato
        O(CM, "sanic_smlt_cm_datatype", 0, "Texto"),
        O(CM, "sanic_smlt_cm_datatype", 1, "Entero"),
        O(CM, "sanic_smlt_cm_datatype", 2, "Decimal"),
        O(CM, "sanic_smlt_cm_datatype", 3, "Fecha y hora"),
        O(CM, "sanic_smlt_cm_datatype", 4, "Booleano"),
        O(CM, "sanic_smlt_cm_datatype", 5, "Lookup"),
        O(CM, "sanic_smlt_cm_datatype", 6, "Conjunto de opciones"),
        O(CM, "sanic_smlt_cm_datatype", 7, "Moneda"),
        O(CM, "sanic_smlt_cm_datatype", 8, "GUID"),

        // Relationship Mapping (ManyToMany lo elimina SchemaCleanupSpec)
        O(RM, "sanic_smlt_rm_relationshiptype", 0, "Uno a muchos"),

        // Import Job: estado
        O(IJ, "sanic_smlt_ij_status", 0, "En cola"),
        O(IJ, "sanic_smlt_ij_status", 1, "Procesando"),
        O(IJ, "sanic_smlt_ij_status", 2, "Completado"),
        O(IJ, "sanic_smlt_ij_status", 3, "Completado con errores"),
        O(IJ, "sanic_smlt_ij_status", 4, "Fallido"),

        // Import Job Log (solo registra fallos: SchemaCleanupSpec dejó únicamente Failed y las 6 categorías)
        O(IJL, "sanic_smlt_ijl_status", 3, "Fallido"),
        O(IJL, "sanic_smlt_ijl_errorcategory", 0, "Error de origen"),
        O(IJL, "sanic_smlt_ijl_errorcategory", 1, "Error de transformación"),
        O(IJL, "sanic_smlt_ijl_errorcategory", 2, "Error de tipo"),
        O(IJL, "sanic_smlt_ijl_errorcategory", 3, "Error de validación"),
        O(IJL, "sanic_smlt_ijl_errorcategory", 4, "Lookup sin resolver"),
        O(IJL, "sanic_smlt_ijl_errorcategory", 5, "Datos de destino"),
    ];

    /// <summary>Tablas de la spec que existen siempre (las 6 de roles); la de keys HMAC solo si ya fue creada.</summary>
    internal static bool IsOptional(string table) => table == HmacKeyTableSpec.Table;

    // ── Aplicación PURA sobre la metadata recuperada (lee el 1033, escribe solo el 3082) ───────────

    /// <summary>Agrega/actualiza el 3082 de DisplayName y DisplayCollectionName. Devuelve si cambió algo.</summary>
    internal static bool ApplyToEntity(Microsoft.Xrm.Sdk.Metadata.EntityMetadata entity, TableLabelSpec spec)
    {
        var changed = LabelEditor.Set(entity.DisplayName, Lcids.Spanish, spec.DisplayEs, out var name);
        entity.DisplayName = name;
        changed |= LabelEditor.Set(entity.DisplayCollectionName, Lcids.Spanish, spec.CollectionEs, out var collection);
        entity.DisplayCollectionName = collection;
        return changed;
    }

    /// <summary>Agrega/actualiza el 3082 del DisplayName de la columna. Devuelve si cambió algo.</summary>
    internal static bool ApplyToAttribute(Microsoft.Xrm.Sdk.Metadata.AttributeMetadata attribute, string es)
    {
        var changed = LabelEditor.Set(attribute.DisplayName, Lcids.Spanish, es, out var label);
        attribute.DisplayName = label;
        return changed;
    }

    /// <summary>Agrega/actualiza el 3082 de una opción (picklist u opción true/false de un boolean).</summary>
    internal static bool ApplyToOption(Microsoft.Xrm.Sdk.Metadata.OptionMetadata option, string es)
    {
        var changed = LabelEditor.Set(option.Label, Lcids.Spanish, es, out var label);
        option.Label = label;
        return changed;
    }
}
