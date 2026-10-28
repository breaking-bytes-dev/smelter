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

internal sealed record RoleSpec(string Name, PrivilegeSpec[] Privileges, PrivilegeSpec[]? DeniedPrivileges = null,
    string[]? AllowedExtraPrivileges = null)
{
    /// <summary>Privilegios que el rol NO debe tener: el deploy los quita si están (AddPrivilegesRole es aditivo) y Verify los exige ausentes.</summary>
    internal PrivilegeSpec[] Denied => DeniedPrivileges ?? [];

    /// <summary>
    /// Privilegios de plataforma (cualquier nombre que NO sea de una tabla de Smelter) que se toleran en el rol además de
    /// <see cref="Privileges"/>. Vacío para los dos roles reales: ver <see cref="SecurityAudit"/>.
    /// </summary>
    internal string[] AllowedExtras => AllowedExtraPrivileges ?? [];
}

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
    // EXCEPTO Create sobre el Import Job Log: los logs los escribe SOLO el motor (ProcessChunkPlugin crea con un
    // IOrganizationService de SYSTEM, que ignora los privilegios del usuario), así que ningún rol necesita crearlos. Sin el
    // privilegio Create la plataforma esconde los botones "Nuevo Log de importación" (subgrid del job, lista, form) sin
    // tocar el ribbon. Read/Write/Delete/Append/AppendTo siguen (consultar y limpiar logs).
    private static readonly PrivilegeSpec LogCreateDenied = new(Tables.ImportJobLog, PrivilegeType.Create, PrivilegeDepth.Global);

    internal static readonly RoleSpec Configurador = new(
        ConfiguradorRoleName,
        Tables.All
            .SelectMany(table => CrudCompleto.Select(type => new PrivilegeSpec(table, type, PrivilegeDepth.Global)))
            .Where(p => !(p.Table == Tables.ImportJobLog && p.Type == PrivilegeType.Create))
            .ToArray(),
        [LogCreateDenied]);

    // Requirement "Rol Smelter Operador": Template Read User+AppendTo (visibilidad vía cascade del
    // team, no BU — decisión cerrada, ver header de tasks.md); Mappings Read Org sin Create/
    // Write/Delete; ImportJob Create Org + Append/Read/Write User; Log Read User.
    // NADA MÁS: ni privilegios de plataforma (plugin assemblies, SharePoint...) ni Append/Write sobre los logs. Ver SecurityAudit.
    internal static readonly RoleSpec Operador = new(
        OperadorRoleName,
        [
            new PrivilegeSpec(Tables.ImportTemplate, PrivilegeType.Read, PrivilegeDepth.Basic),
            new PrivilegeSpec(Tables.ImportTemplate, PrivilegeType.AppendTo, PrivilegeDepth.Basic),

            new PrivilegeSpec(Tables.TableMapping, PrivilegeType.Read, PrivilegeDepth.Global),
            new PrivilegeSpec(Tables.ColumnMapping, PrivilegeType.Read, PrivilegeDepth.Global),
            new PrivilegeSpec(Tables.RelationshipMapping, PrivilegeType.Read, PrivilegeDepth.Global),

            new PrivilegeSpec(Tables.ImportJob, PrivilegeType.Create, PrivilegeDepth.Global),
            // Append sobre el job: setear el lookup al template al crear el job exige Append en el registro que lleva el lookup (el job)
            // y AppendTo en el referenciado (el template, arriba). Sin él, crear un job con template por API/app falla. AppendTo sobre
            // el job NO hace falta: solo se usa para colgarle registros (los logs, que crea SYSTEM).
            new PrivilegeSpec(Tables.ImportJob, PrivilegeType.Append, PrivilegeDepth.Basic),
            new PrivilegeSpec(Tables.ImportJob, PrivilegeType.Read, PrivilegeDepth.Basic),
            new PrivilegeSpec(Tables.ImportJob, PrivilegeType.Write, PrivilegeDepth.Basic),

            new PrivilegeSpec(Tables.ImportJobLog, PrivilegeType.Read, PrivilegeDepth.Basic),
        ],
        [LogCreateDenied]);

    internal static readonly RoleSpec[] Roles = [Configurador, Operador];

    // ── Cascade Template → ImportJob ───────────────────────────────────────────────────────────
    // Nombre de relación confirmado en solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml.
    internal const string TemplateToJobRelationshipSchemaName =
        "sanic_tbl_smlt_importjob_smlt_ij_importtemplateid_sanic_tbl_smlt_importtemplate";

    // ── MDA ─────────────────────────────────────────────────────────────────────────────────────
    // Unique name confirmado en solutions/unpacked/AppModules/sanic_MDASMLTImportConfigurationdandMoni/AppModule.xml.
    internal const string AppModuleUniqueName = "sanic_MDASMLTImportConfigurationdandMoni";

    // ── Vistas custom de Import Job (Requirement "Vistas custom de Import Job", smelter-mda-ux) ─
    // Name/Description son el valor BASE (1033, inglés); la traducción 3082 (y el nombre legacy en español con el que nacieron
    // en Dev, que UpsertViews renombra) viven en ViewLabelsSpec.cs.
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

    // Dataverse exige el atributo object (ObjectTypeCode) en el grid del layoutxml;
    // para tablas custom es específico de cada org, así que va como placeholder y
    // SecurityProvisioner.UpsertViews lo resuelve en runtime contra metadata.
    public const string ObjectTypeCodePlaceholder = "__OBJECT_TYPE_CODE__";

    private static string ImportJobLayoutXml() =>
        $"""
        <grid name="resultset" object="{ObjectTypeCodePlaceholder}" jump="sanic_smlt_ij_name" select="1" icon="1" preview="1">
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
        Name: "Active jobs",
        Description: "Import Jobs in Queued or Processing status.",
        FetchXml: ImportJobFetchXml(
            """
            <filter type="or">
              <condition attribute="sanic_smlt_ij_status" operator="eq" value="0" />
              <condition attribute="sanic_smlt_ij_status" operator="eq" value="1" />
            </filter>
            """),
        LayoutXml: ImportJobLayoutXml());

    internal static readonly ViewSpec CompletedWithErrorsView = new(
        Name: "Completed with errors",
        Description: "Import Jobs with at least one failed row (failedcount > 0).",
        FetchXml: ImportJobFetchXml(
            """
            <filter type="and">
              <condition attribute="sanic_smlt_ij_failedcount" operator="gt" value="0" />
            </filter>
            """),
        LayoutXml: ImportJobLayoutXml());

    internal static readonly ViewSpec AllJobsView = new(
        Name: "All jobs",
        Description: "All Import Jobs, unfiltered, newest first.",
        FetchXml: ImportJobFetchXml(filterXml: ""),
        LayoutXml: ImportJobLayoutXml());

    internal static readonly ViewSpec[] Views = [ActiveJobsView, CompletedWithErrorsView, AllJobsView];

    // ── Dashboard (Requirement "Dashboard Smelter Dashboard en el sitemap", smelter-mda-ux) ────
    // El dashboard ya existe en el ambiente (viaja con la solución) — el provisioner solo lo
    // busca por este nombre y lo enlaza en el sitemap; no crea ni modifica su formxml.
    internal const string DashboardName = "Smelter Dashboard";

    // Roles que la PLATAFORMA asocia automáticamente a toda model-driven app y que no se pueden
    // desasociar (Dataverse los re-deriva al instante — verificado contra Dev, run 30363240939).
    // La MDA queda: Configurador + estos. Los operadores no los tienen, así que no ven la app.
    internal static readonly IReadOnlySet<string> PlatformAppRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System Administrator", "System Customizer" };
}
