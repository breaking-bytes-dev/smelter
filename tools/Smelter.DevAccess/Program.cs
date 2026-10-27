// Smelter.DevAccess — provisiona el acceso de PRUEBA en Dev (4 usuarios de test + 2 teams QA +
// 2 templates QA) para probar el modelo de seguridad con sesiones reales de browser. Todo por SDK.
//
//   (default)  provisiona, idempotente (re-corre sin duplicar).
//   --remove   deshace todo: roles de los usuarios, templates QA (con jobs/mappings) y teams QA.
//
// Guarda: exige SMELTER_ENV=dev. Auth: PP_CLIENT_ID/PP_CLIENT_SECRET (Service Principal) +
// DATAVERSE_URL; sin SP, login interactivo (--interactive). Spec declarativa: DevAccessSpec.cs.

using Microsoft.PowerPlatform.Dataverse.Client;
using Smelter.DevAccess;

var envError = AccessPlanner.CheckEnvironment(Environment.GetEnvironmentVariable(AccessPlanner.EnvVarName));
if (envError is not null)
{
    Console.Error.WriteLine($"[FATAL] {envError}");
    return 2;
}

bool remove = args.Contains("--remove");
bool interactive = args.Contains("--interactive") || args.Contains("--user");

var url = Environment.GetEnvironmentVariable("DATAVERSE_URL")
    ?? throw new InvalidOperationException("DATAVERSE_URL no configurada.");
var clientId = Environment.GetEnvironmentVariable("PP_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("PP_CLIENT_SECRET");
bool useSp = !interactive && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
var connectionString = useSp
    ? $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{clientSecret}';RequireNewInstance=true"
    : $"AuthType=OAuth;Url={url};AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Auto;RequireNewInstance=true";

Console.WriteLine($"Conectando a {url} ({(useSp ? "service principal" : "login interactivo")}) ...");
using var client = new ServiceClient(connectionString);
if (!client.IsReady)
{
    Console.Error.WriteLine($"[FATAL] Conexión no establecida: {client.LastError}");
    return 1;
}
Console.WriteLine($"Conectado. Org: {client.ConnectedOrgFriendlyName}. Modo: {(remove ? "REMOVE" : "PROVISION")}");

try
{
    if (remove) DevAccessProvisioner.Remove(client);
    else DevAccessProvisioner.Provision(client);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FATAL] {ex}");
    return 1;
}
