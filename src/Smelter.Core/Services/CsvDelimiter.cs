using System;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Normaliza el campo Delimiter del template a lo que entiende el parser. El campo es texto libre y un
    /// tabulador no se puede tipear en un input, así que se aceptan alias: <c>\t</c> (2 caracteres), <c>TAB</c>
    /// (sin distinguir mayúsculas) y el tab real. Cualquier otro valor se usa literal; vacío/null = coma.
    /// </summary>
    public static class CsvDelimiter
    {
        /// <summary>Devuelve el delimitador efectivo para <paramref name="configured"/>.</summary>
        public static string Resolve(string configured)
        {
            if (string.IsNullOrEmpty(configured)) return ",";

            var trimmed = configured.Trim();
            if (trimmed == "\\t" || string.Equals(trimmed, "TAB", StringComparison.OrdinalIgnoreCase))
                return "\t";

            return configured;
        }
    }
}
