// Especificación DECLARATIVA de las Custom APIs de Smelter — la fuente única de verdad del
// despliegue. Los valores (uniquenames, params, tipos) están CORROBORADOS contra la metadata
// real de la solución versionada en solutions/unpacked/customapis/ (release sol-v1.0.0.0).
// Patrón tomado de bitbreaker01/sigil (tools/Sigil.Deploy).

namespace Smelter.Deploy;

// Tipos de parámetro/propiedad de Custom API (customapirequestparameter.type /
// customapiresponseproperty.type). Solo los que usamos; valores del option set de la plataforma,
// corroborados contra la metadata (JobId type=12, ChunkSize type=7, HasMore type=0, Json type=10).
internal static class ParamType
{
    public const int Boolean = 0;
    public const int Integer = 7;
    public const int String = 10;
    public const int Guid = 12;
}

internal sealed record RequestParam(string Name, int Type, bool Optional);

internal sealed record ResponseProp(string Name, int Type);

internal sealed record CustomApiSpec(
    string UniqueName,
    string DisplayName,
    string Description,
    string PluginTypeName,
    RequestParam[] RequestParams,
    ResponseProp[] ResponseProps,
    // Gating de acceso (SecurityProvisioner.SetExecutePrivileges → customapi.executeprivilegename).
    // Valores: SecuritySpec (smelter-access-control, Requirement "Gating de Custom APIs por
    // ExecutePrivilegeName"). export/import-template reutilizan Write sobre Template (solo
    // Configurador); prepareimport/processchunk reutilizan Create sobre ImportJob (Operador puede).
    string ExecutePrivilegeName);

internal static class Catalogo
{
    // Nombre del plugin package en Dataverse (metadata real: solutions/unpacked/pluginpackages/).
    public const string PackageName = "sanic_Smelter.Plugins";
    public const string SolutionName = "smlt_core_smelter";

    public static readonly CustomApiSpec[] Apis =
    [
        new(
            UniqueName: "sanic_capi_smelter_prepareimport",
            DisplayName: "CAPI | Smelter | Prepare Import",
            Description: "Valida los headers del CSV, cuenta las filas y resetea el estado del Import Job.",
            PluginTypeName: "Smelter.Plugins.PrepareImportPlugin",
            RequestParams: [new("JobId", ParamType.Guid, Optional: false)],
            ResponseProps: [new("TotalRows", ParamType.Integer)],
            ExecutePrivilegeName: SecuritySpec.PrivilegeName(PrivilegeType.Create, SecuritySpec.Tables.ImportJob)),

        // La entrada legacy "anic_capi_smelter_processchunk" (typo histórico, sin la 's') vivió acá
        // durante el rename aditivo de Release N y se removió en Fase 9 tras backend-verify verde
        // (run 30364284364). El deploy borra el registro remanente de Dev vía DeleteRequest
        // (Program.cs, cleanup) — no debe re-upsertearse nunca más.
        new(
            // Nombre CORRECTO ("sanic_", con la 's') — la mitad nueva del rename aditivo de Release N
            // (ver comentario en la entrada "anic_..." arriba). Mismo PluginTypeName/params/response/
            // ExecutePrivilegeName a propósito: es el MISMO backing plugin, solo cambia el uniquename
            // por el que se invoca. NO existe todavía en solutions/unpacked/customapis/ (esa carpeta
            // es un espejo de metadata que solution-sync escribe DESDE Dev después de un release — se
            // actualiza sola tras el primer backend-deploy que incluya esta entrada; hasta entonces,
            // CustomApiContractTests.Api_ExistsInSolutionMetadata/RequestParams/ResponseProps quedan
            // en rojo para este uniquename PUNTUALMENTE — estado esperado de la ventana de Release N,
            // no un bug).
            UniqueName: "sanic_capi_smelter_processchunk",
            DisplayName: "CAPI | Smelter | Process Chunk",
            Description: "Procesa un lote del CSV (parse streaming + skip al offset, orden DAG, upsert por alternate key).",
            PluginTypeName: "Smelter.Plugins.ProcessChunkPlugin",
            RequestParams:
            [
                new("JobId", ParamType.Guid, Optional: false),
                new("ChunkSize", ParamType.Integer, Optional: true),
            ],
            ResponseProps:
            [
                new("HasMore", ParamType.Boolean),
                new("ProcessedSoFar", ParamType.Integer),
            ],
            ExecutePrivilegeName: SecuritySpec.PrivilegeName(PrivilegeType.Create, SecuritySpec.Tables.ImportJob)),

        new(
            UniqueName: "sanic_capi_smelter_exporttemplate",
            DisplayName: "Export Smelter Template",
            Description: "Serializa un Import Template (árbol completo) a JSON portable con refs estables.",
            PluginTypeName: "Smelter.Plugins.ExportTemplatePlugin",
            RequestParams: [new("TemplateId", ParamType.Guid, Optional: false)],
            ResponseProps: [new("Json", ParamType.String)],
            ExecutePrivilegeName: SecuritySpec.PrivilegeName(PrivilegeType.Write, SecuritySpec.Tables.ImportTemplate)),

        new(
            UniqueName: "sanic_capi_smelter_importtemplate",
            DisplayName: "Import Smelter Template",
            Description: "Valida el JSON, recrea el árbol del template (upsert por nombre) y remapea GUIDs por ref.",
            PluginTypeName: "Smelter.Plugins.ImportTemplatePlugin",
            RequestParams: [new("Json", ParamType.String, Optional: false)],
            ResponseProps:
            [
                new("TemplateId", ParamType.Guid),
                new("Warnings", ParamType.String),
            ],
            ExecutePrivilegeName: SecuritySpec.PrivilegeName(PrivilegeType.Write, SecuritySpec.Tables.ImportTemplate)),
    ];
}
