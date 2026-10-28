// Soporte del smoke E2E de HMAC (la lógica pura vive en Smelter.Deploy/HmacSmokeSpec.cs):
// crea la key temporal "smoke-verify" en la tabla de keys (valor aleatorio generado en proceso,
// NUNCA impreso), y limpia key + contacts + jobs del smoke. Prueba que SYSTEM (el plugin) sigue
// leyendo la columna con column security.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class HmacSmoke
{
    internal const string Domain = "@smelter-hmac-smoke.test";
    internal const string JobName = "Smoke Test HMAC — Smelter.Verify";
    internal const string TemplateName = "Smelter HMAC Smoke Test";
    internal const string TargetColumn = "jobtitle";

    /// <summary>Reemplaza cualquier fila previa con el label reservado y crea una nueva. Devuelve la key (hex), sin loguearla.</summary>
    internal static string CreateFreshKey(ServiceClient client)
    {
        DeleteKeys(client);
        var keyHex = HmacSmokeSpec.NewKeyHex();
        client.Create(new Entity(HmacKeyTableSpec.Table)
        {
            [HmacKeyTableSpec.NameColumn] = HmacSmokeSpec.KeyLabel,
            [HmacKeyTableSpec.ValueColumn] = keyHex,
        });
        return keyHex;
    }

    /// <summary>Borra TODAS las filas (activas o no) con el label del smoke.</summary>
    internal static void DeleteKeys(ServiceClient client)
    {
        var rows = client.RetrieveMultiple(new QueryExpression(HmacKeyTableSpec.Table)
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression(HmacKeyTableSpec.NameColumn, ConditionOperator.Equal, HmacSmokeSpec.KeyLabel) } },
        }).Entities;
        foreach (var r in rows) client.Delete(HmacKeyTableSpec.Table, r.Id);
    }

    internal static void DeleteData(ServiceClient client)
    {
        foreach (var c in Contacts(client, "emailaddress1")) client.Delete("contact", c.Id);
        var jobs = client.RetrieveMultiple(new QueryExpression("sanic_tbl_smlt_importjob")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("sanic_smlt_ij_name", ConditionOperator.Equal, JobName) } },
        }).Entities;
        foreach (var j in jobs) client.Delete("sanic_tbl_smlt_importjob", j.Id);
    }

    internal static List<Entity> Contacts(ServiceClient client, params string[] columns) =>
        client.RetrieveMultiple(new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(columns),
            Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.EndsWith, Domain) } },
        }).Entities.ToList();

    /// <summary>Cada contact debe tener en jobtitle el HMAC-SHA256 hex de su email calculado localmente con la misma key.</summary>
    internal static void AssertHashes(ServiceClient client, string keyHex, int expectedCount)
    {
        var contacts = Contacts(client, "emailaddress1", TargetColumn);
        if (contacts.Count != expectedCount)
            throw new InvalidOperationException($"ASSERT[HMAC-E2E]: debían existir {expectedCount} contacts de smoke HMAC, hay {contacts.Count}");
        foreach (var c in contacts)
        {
            var expected = HmacSmokeSpec.ComputeHex(keyHex, c.GetAttributeValue<string>("emailaddress1"));
            var actual = c.GetAttributeValue<string>(TargetColumn);
            // Nunca se imprime el hash ni la key: solo el veredicto.
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "ASSERT[HMAC-E2E]: el valor transformado NO coincide con el HMAC local " +
                    $"(vacío={string.IsNullOrEmpty(actual)}); ¿el plugin (SYSTEM) no pudo leer la columna asegurada?");
        }
    }
}
