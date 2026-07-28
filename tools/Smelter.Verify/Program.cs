// Smelter.Verify — smoke test E2E del backend DESPLEGADO en Dev, por SDK.
//   Dogfooding completo: importa un template vía la Custom API ImportTemplate (JSON construido con
//   el TemplateSerializer del Core), crea un Import Job con un CSV chico, corre PrepareImport +
//   ProcessChunk hasta terminar, verifica resultados e IDEMPOTENCIA (re-corre sin duplicar), y
//   limpia. Target: la tabla `contact` con su alternate key en emailaddress1 (ya activa en Dev).
//
// Sale 0 si todo pasa; 1 con el detalle del assert que falló.
// Env: DATAVERSE_URL, PP_CLIENT_ID, PP_CLIENT_SECRET (o --interactive).

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Smelter.Verify;

const string JobTable = "sanic_tbl_smlt_importjob";
const string SmokeDomain = "@smelter-smoke.test";
const string SmokeJobName = "Smoke Test — Smelter.Verify";
const string SmokeTemplateName = "Smelter Smoke Test";

// Release N (Fase 6): dos entradas de Catalogo.Apis apuntan a ProcessChunkPlugin (anic_/sanic_) —
// ApiName(...) por PluginTypeName ya no es unívoco para processchunk, así que se resuelve por
// uniquename explícito, el NUEVO (el que el Cloud Flow debe estar usando tras el rename).
string ApiName(string pluginType) => Catalogo.Apis.Single(a => a.PluginTypeName.EndsWith(pluginType)).UniqueName;
string apiImportTemplate = ApiName("ImportTemplatePlugin");
string apiPrepare = ApiName("PrepareImportPlugin");
string apiProcessChunk = Catalogo.Apis.Single(a => a.UniqueName == "sanic_capi_smelter_processchunk").UniqueName;

bool interactive = args.Contains("--interactive") || args.Contains("--user");
var url = Environment.GetEnvironmentVariable("DATAVERSE_URL")
    ?? throw new InvalidOperationException("DATAVERSE_URL no configurada.");
var clientId = Environment.GetEnvironmentVariable("PP_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("PP_CLIENT_SECRET");
bool useSp = !interactive && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
var connectionString = useSp
    ? $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{clientSecret}';RequireNewInstance=true"
    : $"AuthType=OAuth;Url={url};AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Auto;RequireNewInstance=true";

Console.WriteLine($"[1] Conectando a {url} ({(useSp ? "service principal" : "interactivo")}) ...");
using var client = new ServiceClient(connectionString);
if (!client.IsReady)
{
    Console.Error.WriteLine($"[FATAL] Conexión no establecida: {client.LastError}");
    return 1;
}
Console.WriteLine($"[1] Conectado. Org: {client.ConnectedOrgFriendlyName}");

try
{
    // ── [SEC] Seguridad + UX provisionadas (Fases 3/4/6) — corre antes del smoke funcional: si
    // la config de seguridad regresionó, no tiene sentido seguir con el import de prueba. ──────
    SecurityAssertions.RunAll(client);

    // ── 2. Pre-cleanup (por si una corrida anterior murió a medias) ────────
    Cleanup(client);

    // ── 3. Template vía la Custom API ImportTemplate (dogfooding del serializer del Core) ──
    var dto = new TemplateExportDto
    {
        FormatVersion = "1.0",
        Template = new TemplateInfoDto
        {
            Name = SmokeTemplateName, SourceType = 0, HasHeaderRow = true, Delimiter = ",", ChunkSize = 0
        },
        TableMappings =
        [
            new TableMappingDto
            {
                Ref = "tm-contact", Name = "Contact (smoke)", TargetTable = "contact",
                ProcessingOrder = 1, DedupColumns = "emailaddress1",
                CreateIfNotExists = true, UpdateIfExists = true,
                ColumnMappings =
                [
                    new ColumnMappingDto { Name = "Email", SourceColumn = "Email", TargetColumn = "emailaddress1", DataType = (int)ColumnDataType.String },
                    new ColumnMappingDto { Name = "FirstName", SourceColumn = "FirstName", TargetColumn = "firstname", DataType = (int)ColumnDataType.String },
                    new ColumnMappingDto { Name = "LastName", SourceColumn = "LastName", TargetColumn = "lastname", DataType = (int)ColumnDataType.String },
                ]
            }
        ]
    };
    var templateJson = new TemplateSerializer().Serialize(dto);

    var importResp = client.Execute(new OrganizationRequest(apiImportTemplate) { ["Json"] = templateJson });
    var templateId = (Guid)importResp["TemplateId"];
    var warnings = (string)importResp["Warnings"];
    Console.WriteLine($"[3] Template '{SmokeTemplateName}' upserteado vía {apiImportTemplate}: {templateId}" +
        (string.IsNullOrEmpty(warnings) ? "" : $" (warnings: {warnings})"));

    // ── 4. Import Job + CSV (5 filas) ──────────────────────────────────────
    var jobId = client.Create(new Entity(JobTable)
    {
        ["sanic_smlt_ij_name"] = SmokeJobName,
        ["sanic_smlt_ij_importtemplateid"] = new EntityReference("sanic_tbl_smlt_importtemplate", templateId),
    });
    Console.WriteLine($"[4] Import Job creado: {jobId}");

    var csv = "Email,FirstName,LastName\n" + string.Join("\n",
        Enumerable.Range(1, 5).Select(i => $"smoke{i}{SmokeDomain},Smoke{i},Verify"));
    UploadFile(client, new EntityReference(JobTable, jobId), "sanic_smlt_ij_sourcefile", "smoke.csv", csv);
    Console.WriteLine("[4] CSV subido (5 filas).");

    // ── 5. Primera corrida: Prepare + ProcessChunk hasta terminar ──────────
    RunImport(client, apiPrepare, apiProcessChunk, jobId, expectTotal: 5);
    AssertJob(client, jobId, expectedSuccess: 5);
    Assert(CountSmokeContacts(client) == 5, "deben existir exactamente 5 contacts de smoke tras la 1ª corrida");
    Console.WriteLine("[5] 1ª corrida OK: job Completed, 5/5 contacts creados.");

    // ── 6. IDEMPOTENCIA: re-correr todo NO debe duplicar ───────────────────
    RunImport(client, apiPrepare, apiProcessChunk, jobId, expectTotal: 5);
    AssertJob(client, jobId, expectedSuccess: 5);
    Assert(CountSmokeContacts(client) == 5, "re-correr el import NO debe duplicar contacts (upsert por alternate key)");
    Console.WriteLine("[6] 2ª corrida OK: idempotente (siguen siendo 5, sin duplicados).");

    Console.WriteLine();
    Console.WriteLine("[OK] SMOKE TEST VERDE: el backend desplegado procesa, completa e idempotente contra Dev.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FAIL] {ex.Message}");
    return 1;
}
finally
{
    try { Cleanup(client); Console.WriteLine("[7] Cleanup OK (contacts + jobs de smoke eliminados; el template queda para reuso)."); }
    catch (Exception ex) { Console.Error.WriteLine($"[WARN] Cleanup incompleto: {ex.Message}"); }
}

// ───────────────────────────────────────────────────────────────────────────

static void RunImport(ServiceClient client, string apiPrepare, string apiProcessChunk, Guid jobId, int expectTotal)
{
    var prep = client.Execute(new OrganizationRequest(apiPrepare) { ["JobId"] = jobId });
    var total = (int)prep["TotalRows"];
    Assert(total == expectTotal, $"PrepareImport debía contar {expectTotal} filas, contó {total}");

    for (var i = 0; i < 10; i++)
    {
        var resp = client.Execute(new OrganizationRequest(apiProcessChunk) { ["JobId"] = jobId });
        if (!(bool)resp["HasMore"]) return;
    }
    throw new InvalidOperationException("ProcessChunk no terminó en 10 iteraciones (HasMore nunca fue false).");
}

static void AssertJob(ServiceClient client, Guid jobId, int expectedSuccess)
{
    var job = client.Retrieve("sanic_tbl_smlt_importjob", jobId,
        new ColumnSet("sanic_smlt_ij_status", "sanic_smlt_ij_successcount", "sanic_smlt_ij_failedcount"));
    var status = job.GetAttributeValue<OptionSetValue>("sanic_smlt_ij_status")?.Value;
    var success = job.GetAttributeValue<int?>("sanic_smlt_ij_successcount") ?? -1;
    var failed = job.GetAttributeValue<int?>("sanic_smlt_ij_failedcount") ?? -1;
    Assert(status == (int)ImportJobStatus.Completed, $"el job debía quedar Completed({(int)ImportJobStatus.Completed}), quedó {status}");
    Assert(success == expectedSuccess, $"successcount debía ser {expectedSuccess}, es {success}");
    Assert(failed == 0, $"failedcount debía ser 0, es {failed}");
}

static int CountSmokeContacts(ServiceClient client) => SmokeContacts(client).Count;

static List<Entity> SmokeContacts(ServiceClient client)
{
    var q = new QueryExpression("contact")
    {
        ColumnSet = new ColumnSet(false),
        Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, "@smelter-smoke.test") } },
    };
    return client.RetrieveMultiple(q).Entities.ToList();
}

static void Cleanup(ServiceClient client)
{
    foreach (var c in SmokeContacts(client))
        client.Delete("contact", c.Id);

    var jobs = client.RetrieveMultiple(new QueryExpression("sanic_tbl_smlt_importjob")
    {
        ColumnSet = new ColumnSet(false),
        Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ij_name", ConditionOperator.Equal, "Smoke Test — Smelter.Verify") } },
    }).Entities;
    foreach (var j in jobs)
        client.Delete("sanic_tbl_smlt_importjob", j.Id);
}

static void UploadFile(ServiceClient client, EntityReference target, string fileAttribute, string fileName, string content)
{
    var init = client.Execute(new OrganizationRequest("InitializeFileBlocksUpload")
    {
        ["Target"] = target,
        ["FileAttributeName"] = fileAttribute,
        ["FileName"] = fileName,
    });
    var token = (string)init["FileContinuationToken"];
    var blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

    client.Execute(new OrganizationRequest("UploadBlock")
    {
        ["FileContinuationToken"] = token,
        ["BlockId"] = blockId,
        ["BlockData"] = System.Text.Encoding.UTF8.GetBytes(content),
    });

    client.Execute(new OrganizationRequest("CommitFileBlocksUpload")
    {
        ["FileContinuationToken"] = token,
        ["FileName"] = fileName,
        ["MimeType"] = "text/csv",
        ["BlockList"] = new[] { blockId },
    });
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("ASSERT: " + message);
}
