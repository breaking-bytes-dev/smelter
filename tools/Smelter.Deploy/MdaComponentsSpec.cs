// Spec DECLARATIVA del pin de forms y vistas de la model-driven app de Smelter (MdaSpec).
// Sin forms/vistas referenciados, ValidateApp avisa "App users will see all forms and views" por cada tabla:
// la app muestra TODAS las forms/vistas de la tabla (incluidas las de otras soluciones o ajenas a Smelter).
// Se pinean (AddAppComponentsRequest, componenttype 60 = systemform, 26 = savedquery) SOLO las que Smelter diseñó:
//   - MainForm        la main form "Information" (type 2, activa) de cada tabla.
//   - DefaultView     la vista pública por defecto ("Active ...", querytype 0, isdefault).
//   - CustomView      vistas públicas diseñadas para Smelter (Import Job: Jobs Activos, Completados con Errores, Todos los Jobs).
//   - LookupView      querytype 64: sin ella los lookups (job -> template, mapping -> template ...) pierden la vista del selector.
//   - AssociatedView  querytype 2: subgrids / pestañas relacionadas (p.ej. mappings dentro del template).
//   - QuickFindView   querytype 4: la caja de búsqueda de las grillas.
// NO se pinean: "Inactive ..." (no es una vista de trabajo), Advanced Find (querytype 1, solo lo usa Advanced Find) ni "My ..." (8192).
// Los ids son los de solutions/unpacked (estables entre ambientes porque la solución los transporta);
// MdaComponentsSpecTests los corrobora contra esos XML y contra AppModule.xml.

namespace Smelter.Deploy;

internal enum MdaComponentKind { MainForm, DefaultView, CustomView, LookupView, AssociatedView, QuickFindView }

internal sealed record MdaComponent(string Table, int ComponentType, MdaComponentKind Kind, Guid Id, string Name)
{
    internal string Reason => Kind switch
    {
        MdaComponentKind.MainForm => "main form de Smelter (type 2, activa)",
        MdaComponentKind.DefaultView => "vista pública por defecto de la tabla",
        MdaComponentKind.CustomView => "vista pública diseñada para Smelter",
        MdaComponentKind.LookupView => "vista de lookup (querytype 64): selector de los campos lookup",
        MdaComponentKind.AssociatedView => "vista asociada (querytype 2): subgrids / pestañas relacionadas",
        MdaComponentKind.QuickFindView => "vista Quick Find (querytype 4): búsqueda de la grilla",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };
}

internal static class MdaComponentsCatalogo
{
    private const int Form = MdaSpec.ComponentForm, View = MdaSpec.ComponentView;
    private static MdaComponent New(string table, int type, MdaComponentKind kind, string id, string name) =>
        new(table, type, kind, Guid.Parse(id), name);

    // Orden estable: por tabla, y dentro de cada una form -> default -> custom -> lookup -> associated -> quick find.
    internal static readonly IReadOnlyList<MdaComponent> All = BuildAll();

    private static IReadOnlyList<MdaComponent> BuildAll()
    {
        return new[]
        {
        New(SecuritySpec.Tables.ColumnMapping, Form, MdaComponentKind.MainForm, "7a4c1222-d324-459c-9860-5a650d4bece3", "Information"),
        New(SecuritySpec.Tables.ColumnMapping, View, MdaComponentKind.DefaultView, "165c2073-91c6-40c0-93cd-3c96ec221d5f", "Active TBL | SMLT | Column Mappings"),
        New(SecuritySpec.Tables.ColumnMapping, View, MdaComponentKind.LookupView, "e5678925-bb6b-493b-91d4-ad49dc9c51ed", "TBL | SMLT | Column Mapping Lookup View"),
        New(SecuritySpec.Tables.ColumnMapping, View, MdaComponentKind.AssociatedView, "85389342-f7b7-42be-92cc-ea1c5de45b70", "TBL | SMLT | Column Mapping Associated View"),
        New(SecuritySpec.Tables.ColumnMapping, View, MdaComponentKind.QuickFindView, "beec1575-a6c6-408b-aca2-02105f40a803", "Quick Find Active TBL | SMLT | Column Mappings"),
        New(SecuritySpec.Tables.ImportJob, Form, MdaComponentKind.MainForm, "c256332e-bcda-4295-9605-a7566ff69a79", "Information"),
        New(SecuritySpec.Tables.ImportJob, View, MdaComponentKind.DefaultView, "27b8d129-9055-4ca5-a051-961ff5d054ca", "Active TBL | SMLT | Import Jobs"),
        New(SecuritySpec.Tables.ImportJob, View, MdaComponentKind.CustomView, "ca51cf90-818a-f111-8076-70a8a59c703e", "Completados con Errores"),
        New(SecuritySpec.Tables.ImportJob, View, MdaComponentKind.CustomView, "c751cf90-818a-f111-8076-70a8a59c703e", "Jobs Activos"),
        New(SecuritySpec.Tables.ImportJob, View, MdaComponentKind.CustomView, "cd51cf90-818a-f111-8076-70a8a59c703e", "Todos los Jobs"),
        New(SecuritySpec.Tables.ImportJob, View, MdaComponentKind.LookupView, "0225a5fb-2dc2-4fe6-bd0c-649e441a353a", "TBL | SMLT | Import Job Lookup View"),
        New(SecuritySpec.Tables.ImportJob, View, MdaComponentKind.AssociatedView, "60e4bf31-65b9-4cbf-ad0c-b3250896f910", "TBL | SMLT | Import Job Associated View"),
        New(SecuritySpec.Tables.ImportJob, View, MdaComponentKind.QuickFindView, "6b546dd4-5bbb-433e-9724-16e3bed81c66", "Quick Find Active TBL | SMLT | Import Jobs"),
        New(SecuritySpec.Tables.ImportJobLog, Form, MdaComponentKind.MainForm, "c9d0d4c0-b060-49e5-bba7-2d141955ef11", "Information"),
        New(SecuritySpec.Tables.ImportJobLog, View, MdaComponentKind.DefaultView, "6752f00a-34e0-43fd-ba73-9a9cbec00991", "Active TBL | SMLT | Import Job Logs"),
        New(SecuritySpec.Tables.ImportJobLog, View, MdaComponentKind.LookupView, "7fc4b3cc-40bd-4ee4-8ab2-03d8ba58c319", "TBL | SMLT | Import Job Log Lookup View"),
        New(SecuritySpec.Tables.ImportJobLog, View, MdaComponentKind.AssociatedView, "383a7a42-ed26-473a-8394-b7c7441fe58b", "TBL | SMLT | Import Job Log Associated View"),
        New(SecuritySpec.Tables.ImportJobLog, View, MdaComponentKind.QuickFindView, "f35f567a-fd58-4f3e-856d-efd8b975c097", "Quick Find Active TBL | SMLT | Import Job Logs"),
        New(SecuritySpec.Tables.ImportTemplate, Form, MdaComponentKind.MainForm, "fe11c991-7d4f-4613-b003-f5bc133ef932", "Information"),
        New(SecuritySpec.Tables.ImportTemplate, View, MdaComponentKind.DefaultView, "6372b15d-f053-4b18-967a-3a28309ffb08", "Active TBL | SMLT | Import Templates"),
        New(SecuritySpec.Tables.ImportTemplate, View, MdaComponentKind.LookupView, "d84ffa28-70d6-4e28-b784-02fea7ad2de9", "TBL | SMLT | Import Template Lookup View"),
        New(SecuritySpec.Tables.ImportTemplate, View, MdaComponentKind.AssociatedView, "5ef056f3-b445-403a-bc26-8caf23ab5a22", "TBL | SMLT | Import Template Associated View"),
        New(SecuritySpec.Tables.ImportTemplate, View, MdaComponentKind.QuickFindView, "5d83844b-e7a7-4f2a-9ac9-fb4257ab21d2", "Quick Find Active TBL | SMLT | Import Templates"),
        New(SecuritySpec.Tables.RelationshipMapping, Form, MdaComponentKind.MainForm, "9208ce19-3095-42b4-9a87-82d493591acc", "Information"),
        New(SecuritySpec.Tables.RelationshipMapping, View, MdaComponentKind.DefaultView, "374a1ac9-d89b-4e99-9235-c73743eedc81", "Active TBL | SMLT | Relationship Mappings"),
        New(SecuritySpec.Tables.RelationshipMapping, View, MdaComponentKind.LookupView, "8f0fbb73-fc6c-42f9-a294-85803927cc50", "TBL | SMLT | Relationship Mapping Lookup View"),
        New(SecuritySpec.Tables.RelationshipMapping, View, MdaComponentKind.AssociatedView, "bab6af12-344e-4892-ba68-cd8dd43b2b60", "TBL | SMLT | Relationship Mapping Associated View"),
        New(SecuritySpec.Tables.RelationshipMapping, View, MdaComponentKind.QuickFindView, "735ab0aa-0be6-4584-bb34-74aa35a21456", "Quick Find Active TBL | SMLT | Relationship Mappings"),
        New(SecuritySpec.Tables.TableMapping, Form, MdaComponentKind.MainForm, "cf0424cc-bbc7-4d44-b59c-ee76ba5f36a7", "Information"),
        New(SecuritySpec.Tables.TableMapping, View, MdaComponentKind.DefaultView, "a1e5e35d-6d1a-4b75-89ee-d1eb468c796b", "Active TBL | SMLT | Table Mappings"),
        New(SecuritySpec.Tables.TableMapping, View, MdaComponentKind.LookupView, "05d80160-2197-4721-a5ef-27e66484a5ae", "TBL | SMLT | Table Mapping Lookup View"),
        New(SecuritySpec.Tables.TableMapping, View, MdaComponentKind.AssociatedView, "01a1cfb6-c503-42f5-a3c9-70c6233fb6a6", "TBL | SMLT | Table Mapping Associated View"),
        New(SecuritySpec.Tables.TableMapping, View, MdaComponentKind.QuickFindView, "23bfccb8-fcc6-4a1c-81b6-e3e2bbccccb2", "Quick Find Active TBL | SMLT | Table Mappings"),
        };
    }

    /// <summary>Los componentes de la spec que todavía NO están en la app (diff puro; conserva el orden de la spec).</summary>
    internal static IReadOnlyList<MdaComponent> Missing(IEnumerable<MdaComponent> spec, IEnumerable<Guid> pinnedIds)
    {
        var pinned = pinnedIds.ToHashSet();
        return spec.Where(c => !pinned.Contains(c.Id)).ToList();
    }

    /// <summary>Tabla de Dataverse que guarda el componente (para armar el EntityReference de AddAppComponents).</summary>
    internal static string EntityName(int componentType) => componentType switch
    {
        MdaSpec.ComponentForm => "systemform",
        MdaSpec.ComponentView => "savedquery",
        _ => throw new ArgumentOutOfRangeException(nameof(componentType), componentType, "solo forms (60) y vistas (26)"),
    };

    /// <summary>ParameterXml de PublishXml para publicar SOLO la app (nodo appmodules).</summary>
    internal static string PublishAppXml(string appKey) =>
        $"<importexportxml><appmodules><appmodule>{appKey}</appmodule></appmodules></importexportxml>";

    /// <summary>¿Es el warning de ValidateApp "la app no referencia un form/vista" (all forms and views)?</summary>
    internal static bool IsUnpinnedWarning(string? message) =>
        message is not null
        && (message.Contains("see all forms and views", StringComparison.OrdinalIgnoreCase)
            || message.Contains("reference a form or view", StringComparison.OrdinalIgnoreCase));
}
