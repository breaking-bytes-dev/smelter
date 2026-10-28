// Smelter.Deploy — despliegue del backend por SDK puro (sin PRT, sin pac CLI).
//   Sube el plugin package sanic_Smelter.Plugins y upsertea las 4 Custom APIs (spec: ApiSpec.cs),
//   idempotente (re-corre sin duplicar), dentro de la solución smlt_core_smelter.
//   Patrón tomado de bitbreaker01/sigil (tools/Sigil.Deploy), con sus lecciones:
//   - usar `dotnet build` (NO pack) y preferir un build LIMPIO: un bin/Release incremental
//     empaqueta DLLs stale en el nupkg (visto acá mismo con ExcelDataReader tras eliminarlo).
//   - Dataverse cachea por versión del nupkg: para recargar código, bump de <Version> en el csproj.
//   - PublishAllXml al final: sin eso, el Web API rechaza parámetros nuevos hasta re-cachear metadata.
//
// Modos:
//   (default)        despliega: package + espera plugintypes + upsert de las 4 APIs.
//   --package-only   solo (re)sube el package (sin tocar APIs).
//   --interactive    login interactivo (browser) en vez del Service Principal.
//   --allow-destructive  habilita los pasos que borran schema/metadata/datos (4c: recrear key y
//                    dedupe de filas, 4d: SchemaCleanup — atributos y opciones de choices —, 8: Custom API legacy). También por
//                    SMELTER_ALLOW_DESTRUCTIVE=true. Sin esto se omiten (o fallan explícito, ver 4c).
//
// Guarda de entorno: exige SMELTER_ENV=dev. Esto es una herramienta de DEV; Test/Prod reciben la
//   solución managed por el workflow solution-import.
//
// Autenticación: si PP_CLIENT_ID/PP_CLIENT_SECRET están en el entorno → Service Principal
//   (sin browser, ideal para CI); si no (o con --interactive) → login interactivo.
//   DATAVERSE_URL es siempre obligatorio.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

var envError = DeployGuards.CheckEnvironment(Environment.GetEnvironmentVariable(DeployGuards.EnvVarName));
if (envError is not null)
{
    Console.Error.WriteLine($"[FATAL] {envError}");
    return 2;
}
DeployOptions.AllowDestructive = DeployGuards.IsDestructiveAllowed(
    args, Environment.GetEnvironmentVariable(DeployGuards.AllowDestructiveEnvVarName));

string? nupkgArg = args.FirstOrDefault(a => !a.StartsWith("--"));
bool packageOnly = args.Contains("--package-only");
bool interactive = args.Contains("--interactive") || args.Contains("--user");

var url = Environment.GetEnvironmentVariable("DATAVERSE_URL")
    ?? throw new InvalidOperationException("DATAVERSE_URL no configurada.");
var clientId = Environment.GetEnvironmentVariable("PP_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("PP_CLIENT_SECRET");

bool useSp = !interactive && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
var connectionString = useSp
    ? $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{clientSecret}';RequireNewInstance=true"
    // AppId público de muestra de Microsoft para conexiones interactivas (documentado en ServiceClient).
    : $"AuthType=OAuth;Url={url};AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Auto;RequireNewInstance=true";

Console.WriteLine($"[1] Conectando a {url} ({(useSp ? "service principal" : "login interactivo")}) ...");
using var client = new ServiceClient(connectionString);
if (!client.IsReady)
{
    Console.Error.WriteLine($"[FATAL] Conexión no establecida: {client.LastError}");
    return 1;
}
Console.WriteLine($"[1] Conectado. Org: {client.ConnectedOrgFriendlyName} ({client.ConnectedOrgId})");

// ── 2. Upsert del plugin package ───────────────────────────────────────────
string nupkgPath = ResolverNupkg(nupkgArg);
if (!File.Exists(nupkgPath))
{
    Console.Error.WriteLine($"[FATAL] nupkg no encontrado: {nupkgPath}");
    Console.Error.WriteLine("        Generalo con: dotnet clean src/Smelter.Plugins -c Release && dotnet build src/Smelter.Plugins -c Release");
    return 1;
}
byte[] nupkgBytes = File.ReadAllBytes(nupkgPath);
string contentB64 = Convert.ToBase64String(nupkgBytes);
string pkgVersion = LeerVersionDelNupkg(nupkgBytes);
Console.WriteLine($"[2] nupkg: {nupkgPath} ({nupkgBytes.Length:N0} bytes, versión {pkgVersion})");

Guid packageId = UpsertPackage(client, contentB64, pkgVersion);

// ── 3. Esperar los plugintypes auto-descubiertos ───────────────────────────
// Unión de backing plugins de Custom APIs (ApiSpec) y plugins de message steps (StepSpec):
// un plugin type que solo tiene step (AssignJobOwnerPlugin) no aparece en Catalogo.Apis.
var typesEsperados = Catalogo.Apis.Select(a => a.PluginTypeName)
    .Concat(StepCatalogo.Steps.Select(s => s.PluginTypeName))
    .Distinct().ToArray();
var typePorNombre = await EsperarPluginTypesAsync(client, typesEsperados);
if (typePorNombre is null)
    return 1;

if (packageOnly)
{
    Console.WriteLine("[OK] Package desplegado (--package-only).");
    return 0;
}

// ── 4. Upsert de las 4 Custom APIs ─────────────────────────────────────────
foreach (var api in Catalogo.Apis)
{
    Console.WriteLine($"[4] {api.UniqueName} ...");
    Guid apiId = UpsertCustomApi(client, api, typePorNombre[api.PluginTypeName]);
    ReemplazarRequestParams(client, apiId, api);
    ReemplazarResponseProps(client, apiId, api);
    Console.WriteLine($"[4]   OK ({api.RequestParams.Length} params, {api.ResponseProps.Length} response props).");
}

// ── 4b. Upsert de los message steps (StepSpec.cs) ──────────────────────────
StepProvisioner.Run(client, typePorNombre);

// ── 4c. Alternate keys (KeySpec.cs) — fix de la key del Import Job Log ─────
KeyProvisioner.Run(client);

// ── 4d. Limpieza de atributos muertos (SchemaCleanupSpec.cs, audit 1.3.0.0) ─
if (DeployOptions.AllowDestructive)
    SchemaCleanupProvisioner.Run(client);
else
    Console.WriteLine("[4d] SchemaCleanup OMITIDO (destructivo; usar --allow-destructive o SMELTER_ALLOW_DESTRUCTIVE=true).");

// ── 4e. Tabla DEV-only de keys HMAC (HmacKeyTableSpec.cs) — fuera de roles y de la solución ──
HmacKeyTableProvisioner.Run(client);

// ── 4f. Columnas nuevas (SchemaAdditionsSpec.cs): byte cursor del Import Job + "Solo inserción" del Table Mapping ──
SchemaAdditionsProvisioner.Run(client);

// ── 4g. Pin de forms y vistas Smelter en la MDA (MdaComponentsSpec.cs): sin esto ValidateApp avisa "all forms and views" ──
MdaComponentsProvisioner.Run(client);

// ── 4h. Ajustes de columnas/forms existentes (SchemaTuningSpec.cs): sourcetype opcional/oculto, UpdateIfExists default Sí ──
SchemaTuningProvisioner.Run(client);

// ── 4i. Description de TODAS las columnas custom (ColumnDescriptionsSpec.cs): tooltips en la MDA ──
ColumnDescriptionsProvisioner.Run(client);

// ── 4j. Forms y vistas (FormPolishSpec.cs): job read-only + logs, log form, filtro de lookups del RM, errorcategory en la vista ──
FormPolishProvisioner.Run(client);

// ── 4k. Etiquetas e íconos de la MDA (SiteMapSpec.cs): títulos 1033 (inglés) + 3082 (español) y VectorIcon del sitemap + nombre de la app ──
SiteMapProvisioner.Run(client);

// ── 4l. Friendly name de la solución (SolutionNameSpec.cs): "SMLT | Core | Smelter" ──
SolutionNameProvisioner.Run(client);

// ── 4m. Íconos (IconsSpec.cs): web resources SVG en la solución + IconVectorName de las tablas + ícono de la app.
//        (El VectorIcon del sitemap, 4k, es solo un atributo del XML: los web resources se publican acá y en el [5].) ──
IconsProvisioner.Run(client);

// ── 4n. Nombres en español (3082) de tablas, columnas y opciones locales (TableLabelsSpec.cs); el 1033 no se toca ──
TableLabelsProvisioner.Run(client);

// ── 5. Publicar: refresca la metadata OData del Web API ────────────────────
Console.WriteLine("[5] Publicando customizations (refresca la metadata OData del Web API) ...");
client.Execute(new PublishAllXmlRequest());
Console.WriteLine("[5]   PublishAllXml OK.");

// ── 6. Seguridad + UX (roles, cascade, ExecutePrivilegeName, MDA, vistas, ribbon, dashboard) ──
SecurityProvisioner.Run(client);

// ── 6a. Nombres 1033 + 3082 de las vistas custom del Import Job y del dashboard (ViewLabelsSpec.cs): van DESPUÉS del [6],
//        que es quien crea/renombra las vistas y enlaza el dashboard ──
ViewLabelsProvisioner.Run(client);

// ── 7. Cloud Flow: rename aditivo de processchunk (Fase 6) — anic_... → sanic_..., sin ventana rota ──
CloudFlowUpdater.UpdateProcessChunkReference(client);

// ── 8. Cleanup Fase 9 (Release N+1, gate: backend-verify verde run 30364284364): borrar la Custom
//       API legacy "anic_..." de Dev. Idempotente: si ya no existe, no hay nada que borrar. ──
var legacyApi = !DeployOptions.AllowDestructive ? null : client.RetrieveMultiple(new QueryExpression("customapi")
{
    ColumnSet = new ColumnSet("customapiid"),
    Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, ClientDataProcessChunkRenamer.OldActionUniqueName) } },
}).Entities.SingleOrDefault();

if (legacyApi is not null)
{
    client.Delete("customapi", legacyApi.Id);
    Console.WriteLine($"[8] Custom API legacy '{ClientDataProcessChunkRenamer.OldActionUniqueName}' eliminada de Dev ({legacyApi.Id}).");
}
else if (!DeployOptions.AllowDestructive)
{
    Console.WriteLine("[8] Borrado de Custom API legacy OMITIDO (destructivo; usar --allow-destructive o SMELTER_ALLOW_DESTRUCTIVE=true).");
}
else
{
    Console.WriteLine($"[8] Custom API legacy '{ClientDataProcessChunkRenamer.OldActionUniqueName}' ya no existe — nada que borrar.");
}

Console.WriteLine();
Console.WriteLine($"[OK] Despliegue completo. package={packageId}.");
return 0;

// ───────────────────────────────────────────────────────────────────────────
// Helpers
// ───────────────────────────────────────────────────────────────────────────

static string ResolverNupkg(string? arg)
{
    if (arg is not null) return Path.GetFullPath(arg);
    var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Smelter.sln")))
        dir = dir.Parent;
    var raiz = dir?.FullName ?? Directory.GetCurrentDirectory();
    var binDir = Path.Combine(raiz, "src", "Smelter.Plugins", "bin", "Release");
    return Directory.Exists(binDir)
        ? Directory.GetFiles(binDir, "Smelter.Plugins.*.nupkg")
              .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
          ?? Path.Combine(binDir, "Smelter.Plugins.nupkg")
        : Path.Combine(binDir, "Smelter.Plugins.nupkg");
}

// Lee <version> del .nuspec dentro del nupkg — la fuente de verdad del bump.
static string LeerVersionDelNupkg(byte[] nupkg)
{
    using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(nupkg), System.IO.Compression.ZipArchiveMode.Read);
    var nuspec = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    if (nuspec is null) return "1.0.0";
    using var r = new StreamReader(nuspec.Open());
    var m = System.Text.RegularExpressions.Regex.Match(r.ReadToEnd(), "<version>([^<]+)</version>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return m.Success ? m.Groups[1].Value.Trim() : "1.0.0";
}

// Crea un registro DENTRO de la solución (SolutionUniqueName) — clave para el pipeline ALM.
static Guid CrearEnSolucion(ServiceClient client, Entity e)
{
    var req = new CreateRequest { Target = e };
    req.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
    return ((CreateResponse)client.Execute(req)).id;
}

static Guid UpsertPackage(ServiceClient client, string contentB64, string version)
{
    var existentes = client.RetrieveMultiple(new QueryExpression("pluginpackage")
    {
        ColumnSet = new ColumnSet("pluginpackageid", "name"),
        Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, Catalogo.PackageName) } },
    }).Entities;

    if (existentes.Count > 0)
    {
        var id = existentes[0].Id;
        // SOLO content: Dataverse deriva la versión del nuspec y rechaza el update directo del
        // campo `version` (0x80040265). Para recargar código: bump de <Version> en el csproj.
        client.Update(new Entity("pluginpackage", id) { ["content"] = contentB64 });
        Console.WriteLine($"[2] pluginpackage existente ({id}) — content actualizado (versión {version}).");
        return id;
    }

    var creado = CrearEnSolucion(client, new Entity("pluginpackage")
    {
        ["name"] = Catalogo.PackageName,
        ["version"] = version,
        ["content"] = contentB64,
    });
    Console.WriteLine($"[2] pluginpackage creado: {creado}");
    return creado;
}

static async Task<Dictionary<string, Guid>?> EsperarPluginTypesAsync(ServiceClient client, string[] typeNames)
{
    Console.WriteLine($"[3] Esperando {typeNames.Length} plugintypes (propagación hasta ~120 s) ...");
    var deadline = DateTime.UtcNow.AddSeconds(120);
    while (DateTime.UtcNow < deadline)
    {
        var filas = client.RetrieveMultiple(new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("plugintypeid", "typename"),
            Criteria = { Conditions = { new ConditionExpression("typename", ConditionOperator.In, typeNames.Cast<object>().ToArray()) } },
        }).Entities;
        if (filas.Count >= typeNames.Length)
        {
            var mapa = filas.ToDictionary(f => f.GetAttributeValue<string>("typename"), f => f.Id);
            Console.WriteLine($"[3] {mapa.Count} plugintypes descubiertos.");
            return mapa;
        }
        Console.WriteLine($"    ... {filas.Count}/{typeNames.Length}, reintento en 6 s");
        await Task.Delay(TimeSpan.FromSeconds(6));
    }
    Console.Error.WriteLine("[FATAL] no aparecieron todos los plugintypes tras 120 s. ¿Falló el procesamiento del package?");
    return null;
}

static Guid UpsertCustomApi(ServiceClient client, CustomApiSpec api, Guid pluginTypeId)
{
    var existentes = client.RetrieveMultiple(new QueryExpression("customapi")
    {
        ColumnSet = new ColumnSet("customapiid"),
        Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, api.UniqueName) } },
    }).Entities;

    if (existentes.Count > 0)
    {
        var id = existentes[0].Id;
        // Solo campos mutables (bindingtype/isfunction NO se tocan post-creación).
        client.Update(new Entity("customapi", id)
        {
            ["description"] = api.Description,
            ["isprivate"] = false, // los flows leen del $metadata; IsPrivate no es control de acceso
            ["allowedcustomprocessingsteptype"] = new OptionSetValue(0), // None
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
        });
        Console.WriteLine($"[4]   customapi existente ({id}) — actualizada.");
        return id;
    }

    var creado = CrearEnSolucion(client, new Entity("customapi")
    {
        ["uniquename"] = api.UniqueName,
        ["name"] = api.DisplayName,
        ["displayname"] = api.DisplayName,
        ["description"] = api.Description,
        ["bindingtype"] = new OptionSetValue(0), // Global
        ["isfunction"] = false,
        ["isprivate"] = false,
        ["allowedcustomprocessingsteptype"] = new OptionSetValue(0), // None
        ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
    });
    Console.WriteLine($"[4]   customapi creada: {creado}");
    return creado;
}

// Reemplazo DECLARATIVO: el uniquename del parameter ES la clave que lee el plugin
// (context.InputParameters["JobId"]) → debe ser el nombre desnudo, case-sensitive.
// Se borran los existentes de esta API y se recrean desde la spec (fuente única de verdad).
static void ReemplazarRequestParams(ServiceClient client, Guid apiId, CustomApiSpec api)
{
    foreach (var e in client.RetrieveMultiple(new QueryExpression("customapirequestparameter")
    {
        ColumnSet = new ColumnSet("customapirequestparameterid"),
        Criteria = { Conditions = { new ConditionExpression("customapiid", ConditionOperator.Equal, apiId) } },
    }).Entities)
        client.Delete("customapirequestparameter", e.Id);

    foreach (var p in api.RequestParams)
        CrearEnSolucion(client, new Entity("customapirequestparameter")
        {
            ["customapiid"] = new EntityReference("customapi", apiId),
            ["uniquename"] = p.Name,
            ["name"] = p.Name,
            ["displayname"] = p.Name,
            ["description"] = p.Name,
            ["type"] = new OptionSetValue(p.Type),
            ["isoptional"] = p.Optional,
        });
}

static void ReemplazarResponseProps(ServiceClient client, Guid apiId, CustomApiSpec api)
{
    foreach (var e in client.RetrieveMultiple(new QueryExpression("customapiresponseproperty")
    {
        ColumnSet = new ColumnSet("customapiresponsepropertyid"),
        Criteria = { Conditions = { new ConditionExpression("customapiid", ConditionOperator.Equal, apiId) } },
    }).Entities)
        client.Delete("customapiresponseproperty", e.Id);

    foreach (var p in api.ResponseProps)
        CrearEnSolucion(client, new Entity("customapiresponseproperty")
        {
            ["customapiid"] = new EntityReference("customapi", apiId),
            ["uniquename"] = p.Name,
            ["name"] = p.Name,
            ["displayname"] = p.Name,
            ["description"] = p.Name,
            ["type"] = new OptionSetValue(p.Type),
        });
}
