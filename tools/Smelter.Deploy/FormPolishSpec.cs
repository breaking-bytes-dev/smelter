// Especificación DECLARATIVA de ediciones sobre los MAIN FORMS existentes y sobre la vista default del Import Job Log
// (auditoría de campos tanda 2). La consume FormPolishProvisioner (deploy, paso 4j) y FormPolishAssertions (verify).
// Apply() es PURA (XDocument en memoria) e idempotente: devuelve la lista de cambios aplicados, vacía si el form ya
// está como pide la spec — por eso Verify la reutiliza como assert ("aplicar otra vez no cambia nada").
//
//  #10 Import Job: los campos de SALIDA del motor pasan a solo lectura (disabled="true" — UX, no seguridad; el motor y
//      el Cloud Flow escriben por API). Quedan editables: nombre, template, archivo e Is Dry Run.
//  #11 Import Job: pestaña "Logs de importación" con un subgrid de los Import Job Logs del job (vista default del log,
//      que está pineada en la MDA). El campo Failure Reason lo agrega SchemaAdditionsSpec.
//  #12 Import Job Log: el main form solo tenía el nombre autonumérico; se agregan, en solo lectura, job, fila, tabla,
//      estado, categoría y mensaje. La vista default suma Error Category.
//  #20 Relationship Mapping: los lookups de Parent/Child Table Mapping se filtran por el template de la relación
//      (related-records filtering). Requiere el campo Import Template en el form, que no estaba.
//
// Column Mapping pertenece a UN Table Mapping (no a un template): el filtro no aplica.
// NO se tocan display names de tablas ni columnas (otras canvas apps los usan en Power Fx): solo labels de controles
// nuevos dentro del form y las etiquetas de pestañas/secciones, que son bilingües (1033 inglés + 3082 español, Labels).

using System.Xml.Linq;

namespace Smelter.Deploy;

internal sealed record FormReadOnlySpec(string Entity, string Field, string Reason);

internal sealed record FormFieldAddSpec(string Entity, string AfterField, string Field, string Label, string ClassId, bool ReadOnly);

internal sealed record FormSubgridSpec(string Entity, Bilingual TabLabel, string SubgridId, string Relationship,
    string TargetEntity, string ViewId);

/// <summary>Etiqueta bilingüe de una pestaña/sección del main form, ubicada por su texto 1033 (o el legacy con el que nació).</summary>
internal sealed record FormLabelSpec(string Entity, Bilingual Label, string? LegacyEnglish = null);

internal sealed record FormLookupFilterSpec(string Entity, string Field, string FilterRelationship, string DependentAttribute);

internal sealed record ViewColumnSpec(string Entity, Guid ViewId, string AfterColumn, string Column, int Width);

internal static class FormPolishCatalogo
{
    // classid de los controles de formxml.
    private const string TextClass = "{4273EDBD-AC1D-40d3-9FB2-095C621B552D}";
    private const string WholeNumberClass = "{C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F}";
    private const string LookupClass = "{270BD3DB-D9AF-4782-9025-509E298DEC0A}";
    private const string ChoiceClass = "{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}";

    private const string Job = SecuritySpec.Tables.ImportJob;
    private const string Log = SecuritySpec.Tables.ImportJobLog;
    private const string Rm = SecuritySpec.Tables.RelationshipMapping;

    private const string OutputReason = "salida del motor/Cloud Flow: el usuario no la edita";

    internal static readonly FormReadOnlySpec[] ReadOnlyFields =
    [
        new(Job, "sanic_smlt_ij_status", OutputReason),
        new(Job, "sanic_smlt_ij_totalrows", OutputReason),
        new(Job, "sanic_smlt_ij_processedrows", OutputReason),
        new(Job, "sanic_smlt_ij_successcount", OutputReason),
        new(Job, "sanic_smlt_ij_failedcount", OutputReason),
        new(Job, "sanic_smlt_ij_skippedcount", OutputReason),
        new(Job, "sanic_smlt_ij_processingbatch", OutputReason),
        new(Job, "sanic_smlt_ij_starttime", OutputReason),
        new(Job, "sanic_smlt_ij_endtime", OutputReason),
    ];

    internal static readonly FormFieldAddSpec[] AddedFields =
    [
        // Import Job Log: cada campo cuelga del anterior para conservar el orden.
        new(Log, "sanic_smlt_ijl_id", "sanic_smlt_ijl_importjobid", "[IJL] Import Job", LookupClass, true),
        new(Log, "sanic_smlt_ijl_importjobid", "sanic_smlt_ijl_rownumber", "[IJL] Row Number", WholeNumberClass, true),
        new(Log, "sanic_smlt_ijl_rownumber", "sanic_smlt_ijl_targettable", "[IJL] Target Table", TextClass, true),
        new(Log, "sanic_smlt_ijl_targettable", "sanic_smlt_ijl_status", "[IJL] Status", ChoiceClass, true),
        new(Log, "sanic_smlt_ijl_status", "sanic_smlt_ijl_errorcategory", "[IJL] Error Category", ChoiceClass, true),
        new(Log, "sanic_smlt_ijl_errorcategory", "sanic_smlt_ijl_errormessage", "[IJL] Error Message",
            SchemaAdditionsCatalogo.MemoControlClassId, true),

        // Relationship Mapping: el filtro de los lookups depende de este campo, que no estaba en el form.
        new(Rm, "sanic_smlt_rm_name", "sanic_smlt_rm_importtemplateid", "[RM] Import Template", LookupClass, false),
    ];

    /// <summary>Relación Import Job → Import Job Log (nombre confirmado en solutions/unpacked/Other/Relationships).</summary>
    internal const string JobToLogsRelationship = "sanic_tbl_smlt_importjoblog_smlt_ijl_importjobid_sanic_tbl_smlt_importjob";

    /// <summary>Vista default del log ("Active TBL | SMLT | Import Job Logs"): pineada en la app (MdaComponentsSpec).</summary>
    internal const string LogDefaultViewId = "{6752F00A-34E0-43FD-BA73-9A9CBEC00991}";

    internal static readonly FormSubgridSpec[] Subgrids =
    [
        new(Job, new("Import logs", "Logs de importación"), "Subgrid_importjoblogs", JobToLogsRelationship, Log, LogDefaultViewId),
    ];

    /// <summary>Relación Table Mapping → Import Template (la que comparten el mapping y la relación a filtrar).</summary>
    internal const string TableMappingToTemplateRelationship =
        "sanic_tbl_smlt_tablemapping_smlt_tm_importtemplateid_sanic_tbl_smlt_importtemplate";

    internal static readonly FormLookupFilterSpec[] LookupFilters =
    [
        new(Rm, "sanic_smlt_rm_parenttablemappingid", TableMappingToTemplateRelationship, "sanic_smlt_rm_importtemplateid"),
        new(Rm, "sanic_smlt_rm_childtablemappingid", TableMappingToTemplateRelationship, "sanic_smlt_rm_importtemplateid"),
    ];

    internal static readonly ViewColumnSpec[] ViewColumns =
    [
        new(Log, Guid.Parse("6752f00a-34e0-43fd-ba73-9a9cbec00991"), "sanic_smlt_ijl_status", "sanic_smlt_ijl_errorcategory", 130),
    ];

    // Etiquetas de pestañas y secciones en los dos idiomas (1033 inglés, 3082 español). Las pestañas/secciones que trae el
    // form de fábrica ("General", los subgrids del template y del table mapping) ya estaban en inglés bajo 1033: solo ganan el
    // 3082. La pestaña "Logs de importación" nació en español bajo 1033: LegacyEnglish la reconoce y la pasa a inglés.
    // NO toca las etiquetas de los campos (siguen el display name de la columna).
    internal static readonly FormLabelSpec[] Labels =
    [
        new(SecuritySpec.Tables.ImportTemplate, new("General", "General")),
        new(SecuritySpec.Tables.ImportTemplate, new("Table mappings", "Mappings de tabla")),
        new(SecuritySpec.Tables.ImportTemplate, new("Relationship mappings", "Mappings de relación")),
        new(SecuritySpec.Tables.TableMapping, new("General", "General")),
        new(SecuritySpec.Tables.TableMapping, new("Column Mappings", "Mappings de columna")),
        new(SecuritySpec.Tables.ColumnMapping, new("General", "General")),
        new(SecuritySpec.Tables.RelationshipMapping, new("General", "General")),
        new(Job, new("General", "General")),
        new(Job, new("Import logs", "Logs de importación"), LegacyEnglish: "Logs de importación"),
        new(Log, new("General", "General")),
    ];

    /// <summary>Entidades con ediciones de form (para que el provisioner/verify sepan qué forms tocar).</summary>
    internal static IEnumerable<string> FormEntities =>
        ReadOnlyFields.Select(f => f.Entity)
            .Concat(AddedFields.Select(f => f.Entity))
            .Concat(Subgrids.Select(f => f.Entity))
            .Concat(LookupFilters.Select(f => f.Entity))
            .Concat(Labels.Select(f => f.Entity))
            .Distinct();

    /// <summary>
    /// Aplica a <paramref name="doc"/> (formxml del main form de <paramref name="entity"/>) todo lo que la spec pide y
    /// todavía no está. Devuelve una línea por cambio; vacía = el form ya estaba al día.
    /// </summary>
    internal static IReadOnlyList<string> Apply(XDocument doc, string entity)
    {
        var applied = new List<string>();

        foreach (var add in AddedFields.Where(a => a.Entity == entity))
            if (FormXmlEditor.TryAddCellAfter(doc, add.AfterField, add.Field, add.Label, add.ClassId, add.ReadOnly ? true : null))
                applied.Add($"{add.Field} agregado tras {add.AfterField}{(add.ReadOnly ? " (solo lectura)" : "")}");

        foreach (var ro in ReadOnlyFields.Where(r => r.Entity == entity))
            if (FormXmlEditor.TrySetDisabled(doc, ro.Field, true))
                applied.Add($"{ro.Field} solo lectura");

        foreach (var f in LookupFilters.Where(l => l.Entity == entity))
            if (FormXmlEditor.TrySetLookupFilter(doc, f.Field, f.FilterRelationship, f.DependentAttribute))
                applied.Add($"{f.Field} filtrado por {f.DependentAttribute}");

        foreach (var s in Subgrids.Where(g => g.Entity == entity))
            if (FormXmlEditor.TryAddSubgridTab(doc, s.TabLabel.En, s.SubgridId, s.Relationship, s.TargetEntity, s.ViewId, s.TabLabel.Es))
                applied.Add($"pestaña '{s.TabLabel.En}' / '{s.TabLabel.Es}' con subgrid {s.SubgridId}");

        foreach (var l in Labels.Where(l => l.Entity == entity))
            if (FormXmlEditor.TrySetLabels(doc, l.Label.En, l.Label.Es, l.LegacyEnglish))
                applied.Add($"etiqueta '{l.Label.En}' en 1033 + '{l.Label.Es}' en 3082");

        return applied;
    }
}
