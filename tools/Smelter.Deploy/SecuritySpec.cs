// Especificación DECLARATIVA de seguridad de Smelter — matriz rol×tabla×privilegio×profundidad,
// specs de las vistas custom del Import Job y datos del dashboard/sitemap de la MDA. Mismo patrón
// que Catalogo en ApiSpec.cs: fuente única de verdad, consumida por SecurityProvisioner.
//
// Convención de nombres de privilegio (prv{Tipo}{tabla}, p.ej. prvCreatesanic_tbl_smlt_importjob):
// es la convención REAL con la que Dataverse autogenera los privilegios CRUD+Append+AppendTo de
// toda tabla custom — no hay tabla de mapeo separada que mantener, se deriva del nombre lógico.

using Microsoft.Crm.Sdk.Messages;

namespace Smelter.Deploy;

/// <summary>Tipos de privilegio CRUD+Append de Dataverse (prefijo del nombre real del privilegio).</summary>
internal enum PrivilegeType
{
    Create,
    Read,
    Write,
    Delete,
    Append,
    AppendTo,
}

internal sealed record PrivilegeSpec(string Table, PrivilegeType Type, PrivilegeDepth Depth)
{
    /// <summary>Nombre real del privilegio en Dataverse: prv{Tipo}{tabla}.</summary>
    internal string PrivilegeName => SecuritySpec.PrivilegeName(Type, Table);
}

internal sealed record RoleSpec(string Name, PrivilegeSpec[] Privileges);

/// <summary>Vista pública (SavedQuery) de Import Job provisionada vía SDK.</summary>
internal sealed record ViewSpec(string Name, string Description, string FetchXml, string LayoutXml);

internal static class SecuritySpec
{
    // ── Tablas (las 6 sanic_tbl_smlt_*) ────────────────────────────────────────────────────────
    internal static class Tables
    {
        internal const string ImportTemplate = "sanic_tbl_smlt_importtemplate";
        internal const string TableMapping = "sanic_tbl_smlt_tablemapping";
        internal const string ColumnMapping = "sanic_tbl_smlt_columnmapping";
        internal const string RelationshipMapping = "sanic_tbl_smlt_relationshipmapping";
        internal const string ImportJob = "sanic_tbl_smlt_importjob";
        internal const string ImportJobLog = "sanic_tbl_smlt_importjoblog";

        internal static readonly string[] All =
        [
            ImportTemplate, TableMapping, ColumnMapping, RelationshipMapping, ImportJob, ImportJobLog,
        ];
    }

    /// <summary>Nombre real del privilegio en Dataverse: prv{Tipo}{tabla}.</summary>
    internal static string PrivilegeName(PrivilegeType type, string table) => $"prv{type}{table}";

    // ── Roles ───────────────────────────────────────────────────────────────────────────────────
    internal const string ConfiguradorRoleName = "Smelter Configurador";
    internal const string OperadorRoleName = "Smelter Operador";

    private static readonly PrivilegeType[] CrudCompleto =
    [
        PrivilegeType.Create, PrivilegeType.Read, PrivilegeType.Write,
        PrivilegeType.Delete, PrivilegeType.Append, PrivilegeType.AppendTo,
    ];

    // Requirement "Rol Smelter Configurador": CRUD Org (Create/Read/Write/Delete/Append/AppendTo)
    // en las 6 tablas, sin importar el owner (PrivilegeDepth.Global).
    internal static readonly RoleSpec Configurador = new(
        ConfiguradorRoleName,
        Tables.All
            .SelectMany(table => CrudCompleto.Select(type => new PrivilegeSpec(table, type, PrivilegeDepth.Global)))
            .ToArray());

    // Requirement "Rol Smelter Operador": Template Read User+AppendTo (visibilidad vía cascade del
    // team, no BU — decisión cerrada, ver header de tasks.md); Mappings Read Org sin Create/
    // Write/Delete; ImportJob Create Org + Read/Write User; Log Read User.
    internal static readonly RoleSpec Operador = new(
        OperadorRoleName,
        [
            new PrivilegeSpec(Tables.ImportTemplate, PrivilegeType.Read, PrivilegeDepth.Basic),
            new PrivilegeSpec(Tables.ImportTemplate, PrivilegeType.AppendTo, PrivilegeDepth.Basic),

            new PrivilegeSpec(Tables.TableMapping, PrivilegeType.Read, PrivilegeDepth.Global),
            new PrivilegeSpec(Tables.ColumnMapping, PrivilegeType.Read, PrivilegeDepth.Global),
            new PrivilegeSpec(Tables.RelationshipMapping, PrivilegeType.Read, PrivilegeDepth.Global),

            new PrivilegeSpec(Tables.ImportJob, PrivilegeType.Create, PrivilegeDepth.Global),
            new PrivilegeSpec(Tables.ImportJob, PrivilegeType.Read, PrivilegeDepth.Basic),
            new PrivilegeSpec(Tables.ImportJob, PrivilegeType.Write, PrivilegeDepth.Basic),

            new PrivilegeSpec(Tables.ImportJobLog, PrivilegeType.Read, PrivilegeDepth.Basic),
        ]);

    internal static readonly RoleSpec[] Roles = [Configurador, Operador];

    // ── Cascade Template → ImportJob ───────────────────────────────────────────────────────────
    // Nombre de relación confirmado en solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml.
    internal const string TemplateToJobRelationshipSchemaName =
        "sanic_tbl_smlt_importjob_smlt_ij_importtemplateid_sanic_tbl_smlt_importtemplate";

    // ── MDA ─────────────────────────────────────────────────────────────────────────────────────
    // Unique name confirmado en solutions/unpacked/AppModules/sanic_MDASMLTImportConfigurationdandMoni/AppModule.xml.
    internal const string AppModuleUniqueName = "sanic_MDASMLTImportConfigurationdandMoni";

    // ── Vistas custom de Import Job (Requirement "Vistas custom de Import Job", smelter-mda-ux) ─
    // Status: Queued=0 ("Pendiente"/"Preparando"), Processing=1 ("En Progreso") — ver
    // src/Smelter.Core/Models/ImportJobStatus.cs (contrato con el optionset real vía ChoicesContractTests).
    private const string ImportJobGridColumns =
        """
          <row name="result" id="sanic_tbl_smlt_importjobid">
            <cell name="sanic_smlt_ij_name" width="300" />
            <cell name="sanic_smlt_ij_status" width="150" />
            <cell name="sanic_smlt_ij_successcount" width="100" />
            <cell name="sanic_smlt_ij_failedcount" width="100" />
            <cell name="createdon" width="125" />
          </row>
        """;

    private static string ImportJobLayoutXml() =>
        $"""
        <grid name="resultset" jump="sanic_smlt_ij_name" select="1" icon="1" preview="1">
        {ImportJobGridColumns}
        </grid>
        """;

    private static string ImportJobFetchXml(string filterXml) =>
        $"""
        <fetch version="1.0" mapping="logical">
          <entity name="{Tables.ImportJob}">
            <attribute name="sanic_tbl_smlt_importjobid" />
            <attribute name="sanic_smlt_ij_name" />
            <attribute name="sanic_smlt_ij_status" />
            <attribute name="sanic_smlt_ij_successcount" />
            <attribute name="sanic_smlt_ij_failedcount" />
            <attribute name="createdon" />
            <order attribute="createdon" descending="true" />
            {filterXml}
          </entity>
        </fetch>
        """;

    internal static readonly ViewSpec ActiveJobsView = new(
        Name: "Jobs Activos",
        Description: "Import Jobs en estado Queued (Pendiente) o Processing (En Progreso).",
        FetchXml: ImportJobFetchXml(
            """
            <filter type="or">
              <condition attribute="sanic_smlt_ij_status" operator="eq" value="0" />
              <condition attribute="sanic_smlt_ij_status" operator="eq" value="1" />
            </filter>
            """),
        LayoutXml: ImportJobLayoutXml());

    internal static readonly ViewSpec CompletedWithErrorsView = new(
        Name: "Completados con Errores",
        Description: "Import Jobs con al menos una fila fallida (failedcount > 0).",
        FetchXml: ImportJobFetchXml(
            """
            <filter type="and">
              <condition attribute="sanic_smlt_ij_failedcount" operator="gt" value="0" />
            </filter>
            """),
        LayoutXml: ImportJobLayoutXml());

    internal static readonly ViewSpec AllJobsView = new(
        Name: "Todos los Jobs",
        Description: "Todos los Import Jobs, sin filtro, orden descendente por fecha de creación.",
        FetchXml: ImportJobFetchXml(filterXml: ""),
        LayoutXml: ImportJobLayoutXml());

    internal static readonly ViewSpec[] Views = [ActiveJobsView, CompletedWithErrorsView, AllJobsView];

    // ── Dashboard (Requirement "Dashboard Smelter Dashboard en el sitemap", smelter-mda-ux) ────
    internal const string DashboardName = "Smelter Dashboard";
    internal const string DashboardDescription =
        "Resumen de Import Jobs por estado y jobs con errores recientes.";

    // `uniqueid` de cada control <subgrid> del formxml del dashboard (SecurityProvisioner.
    // BuildDashboardCell). DETERMINÍSTICOS a propósito: si se generaran con Guid.NewGuid() en cada
    // corrida, el formxml cambiaría en TODO deploy aunque nada lógico haya cambiado — ruido en
    // diffs/logs y falso "actualizado" en un ambiente ya provisionado (rompe el espíritu idempotente
    // del resto de SecurityProvisioner). Fijos una vez, para siempre — cambiarlos no rompe nada
    // (Dataverse no les da significado propio más allá de distinguir controles dentro del mismo
    // form), pero cambiarlos sin necesidad genera el mismo churn que se está evitando acá.
    internal const string DashboardActiveJobsControlUniqueId = "64a99bb5-4fd9-4792-9a93-c609d577a2af";
    internal const string DashboardCompletedWithErrorsControlUniqueId = "d9be5fbb-5ee5-40d4-9b91-3b413dfa9bc4";
}
