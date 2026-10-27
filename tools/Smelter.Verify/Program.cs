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
const string TemplateTable = "sanic_tbl_smlt_importtemplate";
const string TableMappingTable = "sanic_tbl_smlt_tablemapping";
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

// ── --bench: SOLO el benchmark de 100k filas en dry run (no forma parte del verify por defecto) ──
if (args.Contains("--bench"))
{
    try
    {
        BenchRunner.Run(client, apiImportTemplate, apiPrepare, apiProcessChunk);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[FAIL] {ex.Message}");
        return 1;
    }
}

try
{
    // ── [SEC] Seguridad + UX provisionadas (Fases 3/4/6) — corre antes del smoke funcional: si
    // la config de seguridad regresionó, no tiene sentido seguir con el import de prueba. ──────
    SecurityAssertions.RunAll(client);

    // ── [STEP] Message steps registrados según StepSpec (AssignJobOwner) ───
    StepAssertions.RunAll(client);

    // ── [KEY] Alternate keys según KeySpec (key idempotente del Import Job Log) ──
    KeyAssertions.RunAll(client);

    // ── [CLEAN] Atributos muertos eliminados según SchemaCleanupSpec (1.3.0.0) ──
    CleanupAssertions.RunAll(client);

    // ── [ADD] Columnas nuevas según SchemaAdditionsSpec (byte cursor del job + "Solo inserción" del mapping) ──
    AdditionsAssertions.RunAll(client);

    // ── [TUNE] Ajustes de columnas/forms existentes según SchemaTuningSpec (1.0.10) ──
    TuningAssertions.RunAll(client);

    // ── [DESC] Toda columna custom de las 6 tablas tiene Description (ColumnDescriptionsSpec.cs) ──
    DescriptionsAssertions.RunAll(client);

    // ── [POLISH] Forms y vistas según FormPolishSpec (job read-only + logs, log form, filtro del RM) ──
    FormPolishAssertions.RunAll(client);

    // ── [SITEMAP] Etiquetas de la MDA en español, sin prefijos técnicos (SiteMapSpec.cs) ──
    SiteMapAssertions.RunAll(client);

    // ── [HMAC] Tabla de keys HMAC según HmacKeyTableSpec (incluye column security) ──
    HmacKeyAssertions.RunAll(client);

    // ── [MDA] Salud de la model-driven app a nivel metadata (solo lectura) ──
    MdaAssertions.RunAll(client);

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
                    // Integer a propósito: permite fabricar una fila fallida determinística (valor no
                    // numérico → TypeError) para el assert del Import Job Log en [6b].
                    new ColumnMappingDto { Name = "Children", SourceColumn = "Children", TargetColumn = "numberofchildren", DataType = (int)ColumnDataType.Integer },
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

    // ── 3b. Gate de estado del template (1.3.0.0): un template Draft NO debe poder prepararse. ──
    client.Update(new Entity(TemplateTable, templateId)
    {
        ["sanic_smlt_it_status"] = new OptionSetValue((int)ImportTemplateStatus.Draft),
    });
    var jobGate = client.Create(new Entity(JobTable)
    {
        ["sanic_smlt_ij_name"] = SmokeJobName,
        ["sanic_smlt_ij_importtemplateid"] = new EntityReference(TemplateTable, templateId),
    });
    try
    {
        client.Execute(new OrganizationRequest(apiPrepare) { ["JobId"] = jobGate });
        Assert(false, "PrepareImport debía fallar con el template en Draft (gate de status)");
    }
    catch (System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fx)
    {
        Assert(FaultChainContains(fx.Detail, "not Active"),
            $"el fault del gate debía mencionar 'not Active', fue: {fx.Detail.Message}");
    }
    client.Delete(JobTable, jobGate);
    client.Update(new Entity(TemplateTable, templateId)
    {
        ["sanic_smlt_it_status"] = new OptionSetValue((int)ImportTemplateStatus.Active),
    });
    Console.WriteLine("[3b] Gate de status OK: Draft rechazado, Active restaurado.");

    // ── 4. Import Job + CSV (5 filas) ──────────────────────────────────────
    var jobId = client.Create(new Entity(JobTable)
    {
        ["sanic_smlt_ij_name"] = SmokeJobName,
        ["sanic_smlt_ij_importtemplateid"] = new EntityReference("sanic_tbl_smlt_importtemplate", templateId),
    });
    Console.WriteLine($"[4] Import Job creado: {jobId}");

    // ── 4b. Ownership del job (AssignJobOwnerPlugin, step pre-op de Create): si el template es
    // owned por un team, el job debe nacer owned por ese MISMO team; si es owned por un user
    // (el caso del smoke: lo crea el Service Principal), el owner queda en el default. ─────────
    var templateOwner = client.Retrieve("sanic_tbl_smlt_importtemplate", templateId, new ColumnSet("ownerid"))
        .GetAttributeValue<EntityReference>("ownerid");
    var jobOwner = client.Retrieve(JobTable, jobId, new ColumnSet("ownerid"))
        .GetAttributeValue<EntityReference>("ownerid");
    if (templateOwner?.LogicalName == "team")
    {
        Assert(jobOwner is not null && jobOwner.LogicalName == "team" && jobOwner.Id == templateOwner.Id,
            $"el job debía nacer owned por el team del template ({templateOwner.Id}), su owner es " +
            $"{jobOwner?.LogicalName}/{jobOwner?.Id}");
        Console.WriteLine($"[4b] Ownership OK: job owned por el team del template ({templateOwner.Id}).");
    }
    else
    {
        Assert(jobOwner?.LogicalName == "systemuser",
            $"con template owned por user, el job debía conservar un owner systemuser (default), es {jobOwner?.LogicalName}");
        Console.WriteLine("[4b] Ownership OK: template owned por user — el job conserva el owner default (path pass-through del step).");
    }

    var csv = "Email,FirstName,LastName,Children\n" + string.Join("\n",
        Enumerable.Range(1, 5).Select(i => $"smoke{i}{SmokeDomain},Smoke{i},Verify,{i}"));
    UploadFile(client, new EntityReference(JobTable, jobId), "sanic_smlt_ij_sourcefile", "smoke.csv", csv);
    Console.WriteLine("[4] CSV subido (5 filas).");

    // ── 5. Primera corrida: Prepare + ProcessChunk hasta terminar ──────────
    RunImport(client, apiPrepare, apiProcessChunk, jobId, expectTotal: 5);
    AssertJob(client, jobId, expectedSuccess: 5);
    Assert(CountSmokeContacts(client) == 5, "deben existir exactamente 5 contacts de smoke tras la 1ª corrida");
    Console.WriteLine("[5] 1ª corrida OK: job Completed, 5/5 contacts creados.");

    // ── 5b. Cursor de bytes: Prepare lo dejó en el primer dato y ProcessChunk lo avanzó (por rango, sin
    // re-descargar el archivo entero) hasta el final del CSV. El CSV no termina en newline: fin == su largo en bytes. ──
    var finalCursor = client.Retrieve(JobTable, jobId, new ColumnSet("sanic_smlt_ij_bytecursor"))
        .GetAttributeValue<decimal?>("sanic_smlt_ij_bytecursor");
    var expectedCursor = System.Text.Encoding.UTF8.GetByteCount(csv);
    Assert(finalCursor == expectedCursor, $"el cursor de bytes debía terminar en {expectedCursor} (fin del CSV), es {finalCursor?.ToString() ?? "null"}");
    Console.WriteLine($"[5b] Cursor de bytes OK: terminó en {expectedCursor} (fin del archivo).");

    // ── 6. IDEMPOTENCIA: re-correr todo NO debe duplicar ───────────────────
    RunImport(client, apiPrepare, apiProcessChunk, jobId, expectTotal: 5);
    AssertJob(client, jobId, expectedSuccess: 5);
    Assert(CountSmokeContacts(client) == 5, "re-correr el import NO debe duplicar contacts (upsert por alternate key)");
    Console.WriteLine("[6] 2ª corrida OK: idempotente (siguen siendo 5, sin duplicados).");

    // ── 6b. Fila fallida → Import Job Log (regresión 1.2.1.0: la key del log usaba el autonumber
    // y NINGÚN log se escribía). Job aparte con 5 filas buenas + 1 con Children no numérico. ──────
    var jobId2 = client.Create(new Entity(JobTable)
    {
        ["sanic_smlt_ij_name"] = SmokeJobName,
        ["sanic_smlt_ij_importtemplateid"] = new EntityReference("sanic_tbl_smlt_importtemplate", templateId),
    });
    var csvConError = "Email,FirstName,LastName,Children\n" + string.Join("\n",
        Enumerable.Range(1, 5).Select(i => $"smoke{i}{SmokeDomain},Smoke{i},Verify,{i}"))
        + $"\nbad{SmokeDomain},Bad,Row,notanumber";
    UploadFile(client, new EntityReference(JobTable, jobId2), "sanic_smlt_ij_sourcefile", "smoke-badrow.csv", csvConError);

    // RowNumber es la fila FÍSICA del archivo (header = 1, ver CsvParserService): la 6ª fila de
    // datos es la fila física 7.
    RunImport(client, apiPrepare, apiProcessChunk, jobId2, expectTotal: 6);
    AssertJob(client, jobId2, expectedSuccess: 5, expectedFailed: 1, expectedStatus: ImportJobStatus.CompletedWithErrors);
    AssertFailureLog(client, jobId2, expectedRowNumber: 7);
    Assert(CountSmokeContacts(client) == 5, "la fila con TypeError NO debe crear contact (siguen 5)");
    Console.WriteLine("[6b] 1ª corrida con fila mala OK: CompletedWithErrors 5/1 y el log registró la fila fallida.");

    // Idempotencia del LOG: re-correr no debe duplicar el registro (upsert por la key recreada).
    RunImport(client, apiPrepare, apiProcessChunk, jobId2, expectTotal: 6);
    AssertJob(client, jobId2, expectedSuccess: 5, expectedFailed: 1, expectedStatus: ImportJobStatus.CompletedWithErrors);
    AssertFailureLog(client, jobId2, expectedRowNumber: 7);
    Console.WriteLine("[6b] 2ª corrida OK: el Import Job Log sigue teniendo 1 solo registro (key idempotente).");

    // ── 6c. UpdateIfExists=false (1.3.0.0): las filas que matchean por alternate key se SKIPEAN. ──
    var tableMapping = client.RetrieveMultiple(new QueryExpression(TableMappingTable)
    {
        ColumnSet = new ColumnSet(false),
        Criteria = { Conditions = { new ConditionExpression("sanic_smlt_tm_importtemplateid", ConditionOperator.Equal, templateId) } },
    }).Entities.Single();
    client.Update(new Entity(TableMappingTable, tableMapping.Id) { ["sanic_smlt_tm_updateifexists"] = false });

    var jobId3 = client.Create(new Entity(JobTable)
    {
        ["sanic_smlt_ij_name"] = SmokeJobName,
        ["sanic_smlt_ij_importtemplateid"] = new EntityReference(TemplateTable, templateId),
    });
    UploadFile(client, new EntityReference(JobTable, jobId3), "sanic_smlt_ij_sourcefile", "smoke-skip.csv", csv);
    RunImport(client, apiPrepare, apiProcessChunk, jobId3, expectTotal: 5);
    AssertJob(client, jobId3, expectedSuccess: 0, expectedFailed: 0, expectedStatus: ImportJobStatus.Completed);
    var skipped = client.Retrieve(JobTable, jobId3, new ColumnSet("sanic_smlt_ij_skippedcount"))
        .GetAttributeValue<int?>("sanic_smlt_ij_skippedcount") ?? -1;
    Assert(skipped == 5, $"con UpdateIfExists=false los 5 contacts existentes debían quedar Skipped, skippedcount={skipped}");
    Assert(CountSmokeContacts(client) == 5, "skip no debe crear ni duplicar contacts");

    client.Update(new Entity(TableMappingTable, tableMapping.Id) { ["sanic_smlt_tm_updateifexists"] = true });
    Console.WriteLine("[6c] UpdateIfExists=false OK: 5 filas Skipped, nada escrito, flag restaurado.");

    // ── 6e. Gate de idempotencia (1.0.8): un mapping sin dedup columns es rechazado por PrepareImport salvo que
    // tenga "Solo inserción". Se reusa el mapping del smoke y se restaura al final (el import del paso 3 lo recrea igual). ──
    client.Update(new Entity(TableMappingTable, tableMapping.Id) { ["sanic_smlt_tm_dedupcolumns"] = null, ["sanic_smlt_tm_insertonly"] = false });
    try
    {
        client.Execute(new OrganizationRequest(apiPrepare) { ["JobId"] = jobId3 });
        Assert(false, "PrepareImport debía rechazar un mapping sin dedup columns y sin 'Solo inserción'");
    }
    catch (System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fx)
    {
        Assert(FaultChainContains(fx.Detail, "has no dedup columns"),
            $"el fault del gate debía mencionar 'has no dedup columns', fue: {fx.Detail.Message}");
    }

    client.Update(new Entity(TableMappingTable, tableMapping.Id) { ["sanic_smlt_tm_insertonly"] = true });
    var gateTotal = (int)ExecuteWithTransientBlobRetry(client, new OrganizationRequest(apiPrepare) { ["JobId"] = jobId3 })["TotalRows"];
    Assert(gateTotal == 5, $"con 'Solo inserción' PrepareImport debía aceptar el template y contar 5 filas, contó {gateTotal}");

    client.Update(new Entity(TableMappingTable, tableMapping.Id)
    {
        ["sanic_smlt_tm_dedupcolumns"] = "emailaddress1", ["sanic_smlt_tm_insertonly"] = false
    });
    Console.WriteLine("[6e] Gate de idempotencia OK: sin dedup + sin flag rechazado; con 'Solo inserción' aceptado; mapping restaurado.");

    // ── 6d. HMAC E2E: prueba que SYSTEM (el plugin) lee la columna ASEGURADA de la key. Key temporal
    // "smoke-verify" generada en proceso (nunca se imprime); el hash esperado se calcula local. ──────
    var hmacKeyHex = HmacSmoke.CreateFreshKey(client);
    var hmacDto = new TemplateExportDto
    {
        FormatVersion = "1.0",
        Template = new TemplateInfoDto
        {
            Name = HmacSmoke.TemplateName, SourceType = 0, HasHeaderRow = true, Delimiter = ",", ChunkSize = 0
        },
        TableMappings =
        [
            new TableMappingDto
            {
                Ref = "tm-contact-hmac", Name = "Contact (smoke HMAC)", TargetTable = "contact",
                ProcessingOrder = 1, DedupColumns = "emailaddress1",
                CreateIfNotExists = true, UpdateIfExists = true,
                ColumnMappings =
                [
                    new ColumnMappingDto { Name = "Email", SourceColumn = "Email", TargetColumn = "emailaddress1", DataType = (int)ColumnDataType.String },
                    new ColumnMappingDto { Name = "LastName", SourceColumn = "LastName", TargetColumn = "lastname", DataType = (int)ColumnDataType.String },
                    new ColumnMappingDto
                    {
                        Name = "Hash", SourceColumn = null, TargetColumn = HmacSmoke.TargetColumn, DataType = (int)ColumnDataType.String,
                        TransformExpression = HmacSmokeSpec.TransformExpression("Email"),
                    },
                ]
            }
        ]
    };
    var hmacTemplateId = (Guid)client.Execute(new OrganizationRequest(apiImportTemplate) { ["Json"] = new TemplateSerializer().Serialize(hmacDto) })["TemplateId"];
    client.Update(new Entity(TemplateTable, hmacTemplateId)
    {
        ["sanic_smlt_it_status"] = new OptionSetValue((int)ImportTemplateStatus.Active),
    });
    var hmacJobId = client.Create(new Entity(JobTable)
    {
        ["sanic_smlt_ij_name"] = HmacSmoke.JobName,
        ["sanic_smlt_ij_importtemplateid"] = new EntityReference(TemplateTable, hmacTemplateId),
    });
    var hmacCsv = "Email,LastName\n" + string.Join("\n",
        Enumerable.Range(1, 3).Select(i => $"hmac{i}{HmacSmoke.Domain},Verify{i}"));
    UploadFile(client, new EntityReference(JobTable, hmacJobId), "sanic_smlt_ij_sourcefile", "smoke-hmac.csv", hmacCsv);
    RunImport(client, apiPrepare, apiProcessChunk, hmacJobId, expectTotal: 3);
    AssertJob(client, hmacJobId, expectedSuccess: 3);
    HmacSmoke.AssertHashes(client, hmacKeyHex, expectedCount: 3);
    Console.WriteLine("[6d] HMAC E2E OK: SYSTEM leyó la columna asegurada y los 3 hashes coinciden con el cálculo local (key temporal, no logueada).");

    // ── [CHUNK] Multi-chunk: 250 filas por el cursor de bytes en 3 chunks (el último parcial), newline embebido + multibyte ──
    ChunkSmoke.Run(client, apiImportTemplate, apiPrepare, apiProcessChunk);

    Console.WriteLine();
    Console.WriteLine("[OK] SMOKE TEST VERDE: el backend desplegado procesa, completa e idempotente contra Dev.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FAIL] {ex.Message}");

    // Un plugin que tira una excepción no controlada llega como fault genérico ("An unexpected
    // error occurred") — el detalle real (incluido el trace del plugin) viene en el
    // OrganizationServiceFault: TraceText + cadena de InnerFaults. Sin esto se debuggea a ciegas.
    if (ex is System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fx)
    {
        for (var fault = fx.Detail; fault is not null; fault = fault.InnerFault)
        {
            Console.Error.WriteLine($"[FAIL]   Fault: 0x{fault.ErrorCode:X8} {fault.Message}");
            if (!string.IsNullOrWhiteSpace(fault.TraceText))
                Console.Error.WriteLine($"[FAIL]   TraceText:\n{fault.TraceText}");
        }
    }
    return 1;
}
finally
{
    try { Cleanup(client); Console.WriteLine("[7] Cleanup OK (contacts + jobs de smoke y key temporal HMAC eliminados; el template queda para reuso)."); }
    catch (Exception ex) { Console.Error.WriteLine($"[WARN] Cleanup incompleto: {ex.Message}"); }
}

// ───────────────────────────────────────────────────────────────────────────

static void RunImport(ServiceClient client, string apiPrepare, string apiProcessChunk, Guid jobId, int expectTotal)
{
    var prep = ExecuteWithTransientBlobRetry(client, new OrganizationRequest(apiPrepare) { ["JobId"] = jobId });
    var total = (int)prep["TotalRows"];
    Assert(total == expectTotal, $"PrepareImport debía contar {expectTotal} filas, contó {total}");

    for (var i = 0; i < 10; i++)
    {
        // ProcessChunk también descarga la columna File en cada chunk — el mismo 400 transitorio
        // de blob storage que Prepare (visto en run 30461450578, 2ª corrida) merece el mismo retry.
        var resp = ExecuteWithTransientBlobRetry(client, new OrganizationRequest(apiProcessChunk) { ["JobId"] = jobId });
        if (!(bool)resp["HasMore"]) return;
    }
    throw new InvalidOperationException("ProcessChunk no terminó en 10 iteraciones (HasMore nunca fue false).");
}

// La descarga de la columna File dentro de PrepareImport falla INTERMITENTE con un 400
// "InvalidQueryParameterValue" de Azure Blob Storage cuando el CSV se subió segundos antes
// (timing/SAS — visto en backend-verify runs 30364063750 y 30365035368; el mismo flujo dio
// verde en 30364284364 sin tocar nada). Retry acotado SOLO para esa firma transitoria: cualquier
// otro error (p.ej. el chequeo de acceso del plugin) se propaga al primer intento.
static OrganizationResponse ExecuteWithTransientBlobRetry(ServiceClient client, OrganizationRequest request)
{
    const int maxAttempts = 4;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            return client.Execute(request);
        }
        catch (System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fx)
            when (attempt < maxAttempts && FaultChainContains(fx.Detail, "InvalidQueryParameterValue"))
        {
            Console.WriteLine($"[5]   WARN: descarga del File transitoriamente rechazada por storage (400 InvalidQueryParameterValue) — retry {attempt}/{maxAttempts - 1} en 15s ...");
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(15));
        }
    }
}

static bool FaultChainContains(Microsoft.Xrm.Sdk.OrganizationServiceFault? fault, string needle)
{
    for (; fault is not null; fault = fault.InnerFault)
    {
        if (fault.Message?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true) return true;
        if (fault.TraceText?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true) return true;
    }
    return false;
}

static void AssertJob(ServiceClient client, Guid jobId, int expectedSuccess,
    int expectedFailed = 0, ImportJobStatus expectedStatus = ImportJobStatus.Completed)
{
    var job = client.Retrieve("sanic_tbl_smlt_importjob", jobId,
        new ColumnSet("sanic_smlt_ij_status", "sanic_smlt_ij_successcount", "sanic_smlt_ij_failedcount"));
    var status = job.GetAttributeValue<OptionSetValue>("sanic_smlt_ij_status")?.Value;
    var success = job.GetAttributeValue<int?>("sanic_smlt_ij_successcount") ?? -1;
    var failed = job.GetAttributeValue<int?>("sanic_smlt_ij_failedcount") ?? -1;
    Assert(status == (int)expectedStatus, $"el job debía quedar {expectedStatus}({(int)expectedStatus}), quedó {status}");
    Assert(success == expectedSuccess, $"successcount debía ser {expectedSuccess}, es {success}");
    Assert(failed == expectedFailed, $"failedcount debía ser {expectedFailed}, es {failed}");
}

// El corazón del assert de 1.2.1.0: la fila fallida debe quedar REGISTRADA en el Import Job Log
// (antes del fix, la key mal tipada hacía fallar todos los upserts y el log quedaba vacío), con
// categoría TypeError y mensaje no vacío — lo que el operador necesita para saber qué columna falló.
static void AssertFailureLog(ServiceClient client, Guid jobId, int expectedRowNumber)
{
    var logs = client.RetrieveMultiple(new QueryExpression("sanic_tbl_smlt_importjoblog")
    {
        ColumnSet = new ColumnSet("sanic_smlt_ijl_rownumber", "sanic_smlt_ijl_targettable",
            "sanic_smlt_ijl_status", "sanic_smlt_ijl_errorcategory", "sanic_smlt_ijl_errormessage"),
        Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ijl_importjobid", ConditionOperator.Equal, jobId) } },
    }).Entities;

    Assert(logs.Count == 1, $"debía haber EXACTAMENTE 1 Import Job Log para el job (idempotente), hay {logs.Count}");
    var log = logs[0];
    var rowNumber = log.GetAttributeValue<int?>("sanic_smlt_ijl_rownumber");
    var category = log.GetAttributeValue<OptionSetValue>("sanic_smlt_ijl_errorcategory")?.Value;
    var message = log.GetAttributeValue<string>("sanic_smlt_ijl_errormessage");
    Assert(rowNumber == expectedRowNumber, $"el log debía apuntar a la fila {expectedRowNumber}, apunta a {rowNumber}");
    Assert(log.GetAttributeValue<string>("sanic_smlt_ijl_targettable") == "contact",
        $"targettable debía ser 'contact', es '{log.GetAttributeValue<string>("sanic_smlt_ijl_targettable")}'");
    Assert(log.GetAttributeValue<OptionSetValue>("sanic_smlt_ijl_status")?.Value == (int)ImportLogStatus.Failed,
        "el log debía tener status Failed");
    Assert(category == (int)ErrorCategory.TypeError, $"errorcategory debía ser TypeError({(int)ErrorCategory.TypeError}), es {category}");
    Assert(!string.IsNullOrWhiteSpace(message), "errormessage no puede venir vacío — es lo que le dice al operador qué columna falló");
    Assert(message!.Contains("numberofchildren"),
        $"errormessage debe NOMBRAR la columna que falló (1.2.1.0) — mensaje actual: '{message}'");
    Console.WriteLine($"[6b]   log fila {rowNumber}: [{(ErrorCategory)category!}] {message}");
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
    // Lo sensible primero: la key temporal "smoke-verify" se borra aunque falle lo demás (y también si
    // una corrida previa murió a medias).
    HmacSmoke.DeleteKeys(client);

    foreach (var c in SmokeContacts(client))
        client.Delete("contact", c.Id);

    var jobs = client.RetrieveMultiple(new QueryExpression("sanic_tbl_smlt_importjob")
    {
        ColumnSet = new ColumnSet(false),
        Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ij_name", ConditionOperator.Equal, "Smoke Test — Smelter.Verify") } },
    }).Entities;
    foreach (var j in jobs)
        client.Delete("sanic_tbl_smlt_importjob", j.Id);

    // Smoke multi-chunk: contacts + jobs (el bench se limpia solo, en BenchRunner).
    ChunkSmoke.DeleteData(client);

    // Smoke HMAC: contacts + jobs de la corrida (la key temporal ya se borró al principio de Cleanup).
    HmacSmoke.DeleteData(client);
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
    // BlockId fijo y URL-safe ("MDAwMDAx"): un base64 de Guid aleatorio a veces trae '+' o '/', y el blob
    // storage lo rechaza con 400 InvalidQueryParameterValue (fallo intermitente). Un solo bloque por archivo.
    var blockId = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("000001"));

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
