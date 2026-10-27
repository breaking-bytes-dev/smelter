// Piezas PURAS del smoke E2E de HMAC (Smelter.Verify): la key temporal "smoke-verify" se genera en
// proceso (nunca se imprime), y el hash esperado se calcula localmente con el MISMO algoritmo del
// TransformEngine (HMAC-SHA256 sobre UTF-8, key = bytes del hex, salida hex minúscula).
// Sirve para probar de punta a punta que SYSTEM (el plugin) sigue leyendo la columna asegurada.

using System.Security.Cryptography;
using System.Text;

namespace Smelter.Deploy;

internal static class HmacSmokeSpec
{
    /// <summary>Label de la key temporal; reservada para el smoke (si existe, se reemplaza).</summary>
    internal const string KeyLabel = "smoke-verify";

    internal static string TransformExpression(string sourceColumn) => $"HMAC({sourceColumn}, \"{KeyLabel}\")";

    internal static string NewKeyHex() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    internal static string ComputeHex(string keyHex, string value)
    {
        var key = Convert.FromHexString(keyHex);
        using var h = new HMACSHA256(key);
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
    }
}
