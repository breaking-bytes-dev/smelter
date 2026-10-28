// Especificación DECLARATIVA de los nombres bilingües de las vistas custom del Import Job y del dashboard de Smelter.
// La consumen ViewLabelsProvisioner (deploy, paso 6a, DESPUÉS del [6] que crea/enlaza vistas y dashboard) y
// ViewLabelsAssertions (verify).
//
//  - savedquery.name / .description y systemform.name son localizables (SetLocLabels/RetrieveLocLabels los soportan, ver
//    Microsoft Learn). El valor BASE (1033, inglés) lo escribe SecurityProvisioner.UpsertViews con Update normal
//    (SecuritySpec.Views); acá solo se agrega la traducción 3082.
//  - Las vistas nacieron con el nombre en español bajo 1033 ("Jobs Activos" ...): LegacyName es ese nombre viejo para que
//    UpsertViews encuentre la vista existente y la RENOMBRE en vez de crear un duplicado.
//  - El dashboard conserva su nombre 1033 "Smelter Dashboard" (SecuritySpec.DashboardName); solo gana el 3082.
//
// NO se tocan las vistas del sistema ("Active TBL | SMLT | ..."): no las crea ni las renombra ninguna spec.

namespace Smelter.Deploy;

internal sealed record ViewLabelSpec(ViewSpec View, string NameEs, string DescriptionEs, string LegacyName)
{
    internal Bilingual Name => new(View.Name, NameEs);
    internal Bilingual Description => new(View.Description, DescriptionEs);
}

internal static class ViewLabelsCatalogo
{
    internal static readonly ViewLabelSpec[] Views =
    [
        new(SecuritySpec.ActiveJobsView, "Jobs activos",
            "Import Jobs en estado Queued (Pendiente) o Processing (En Progreso).", LegacyName: "Jobs Activos"),
        new(SecuritySpec.CompletedWithErrorsView, "Completados con errores",
            "Import Jobs con al menos una fila fallida (failedcount > 0).", LegacyName: "Completados con Errores"),
        new(SecuritySpec.AllJobsView, "Todos los jobs",
            "Todos los Import Jobs, sin filtro, orden descendente por fecha de creación.", LegacyName: "Todos los Jobs"),
    ];

    /// <summary>systemform.name del dashboard: 1033 = SecuritySpec.DashboardName (no cambia), 3082 = traducción.</summary>
    internal static readonly Bilingual Dashboard = new(SecuritySpec.DashboardName, "Panel de Smelter");

    /// <summary>Nombres con los que una vista pudo existir en Dev (el actual en inglés y el legacy en español), para el Upsert.</summary>
    internal static IReadOnlyList<string> KnownNames(ViewSpec view) =>
        Views.Where(v => v.View == view).SelectMany(v => new[] { v.View.Name, v.LegacyName }).Distinct().ToList();

    /// <summary>ParameterXml de PublishXml: la entidad Import Job (sus vistas) y, si se tocó, el dashboard.</summary>
    internal static string PublishXml(Guid? dashboardId) =>
        "<importexportxml>" +
        $"<entities><entity>{SecuritySpec.Tables.ImportJob}</entity></entities>" +
        (dashboardId is { } id ? $"<dashboards><dashboard>{{{id:D}}}</dashboard></dashboards>" : "") +
        "</importexportxml>";
}
