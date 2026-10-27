// Catálogo y selección (--only) de los pasos [E2E-n] de Smelter.Verify. PURO: el runner (Verify) y los tests de
// contrato comparten esta lógica, así `--only 4` / `--only 5c,7a` se comporta igual en CI y en la máquina de quien depura.
//
// Ids: un número solo selecciona el GRUPO (el "4" trae 4a y 4b; el "1" NO trae el 10 ni el 11); el id con letra, solo ese paso.
// "e2e" / "all" (o sin --only) = todos. El caso 9 (contadores) no es un paso: se aserta dentro de los demás (AssertJobState).

namespace Smelter.Deploy;

internal sealed record E2eStep(string Id, string Title);

internal static class E2eSteps
{
    internal static readonly IReadOnlyList<E2eStep> All =
    [
        new("1", "CreateIfNotExists=false: filas que no existen quedan Skipped"),
        new("2", "Tipos de dato en contact (String/Integer/Decimal/Money/DateTime/Boolean/OptionSet/Lookup)"),
        new("2g", "Tipo Guid (aislado: escribe la clave primaria)"),
        new("3", "IsRequired + DefaultValue"),
        new("4a", "Relación OneToMany: ProcessingOrder invertido, FK resuelto, padre e hijo en chunks distintos"),
        new("4b", "Relación OneToMany con padre keyed por HMAC (regresión bug #8)"),
        new("5a", "HasHeaderRow=false (Column0..N)"),
        new("5b", "Delimiter ';'"),
        new("5c", "Delimiter TAB"),
        new("6", "Template Archived rechazado por Prepare y por ProcessChunk"),
        new("7a", "Cloud Flow de punta a punta: job vacío -> Queued -> Completed"),
        new("7b", "Cloud Flow camino Failed: template Draft -> job Failed con motivo sin PII"),
        new("7c", "Cloud Flow anti-duplicados: el MISMO job encolado dos veces seguidas termina Completed, con conteos exactos y sin logs de clave duplicada"),
        new("7d", "Guard de re-encolado: con el job Processing (Prepare directo) poner Queued es rechazado; el job igual termina Completed"),
        new("8", "Dry run: no crea nada y termina Completed"),
        new("10", "Roundtrip Export -> Import con todos los campos del DTO"),
        new("11", "SourceColumn sin distinguir mayúsculas (header 'email' vs mapping 'Email')"),
        new("12", "Transforms CONCAT/UPPER/MAP/DATEPARSE/IF/SPLIT por el plugin real"),
        new("13", "ValidateQueueAccess: camino negativo (NO se prueba acá: necesita una identidad sin privilegios)"),
        new("14", "ManyToMany rechazado por la Custom API de import"),
    ];

    /// <summary>Valor de <c>--only</c> (<c>--only x</c> o <c>--only=x</c>); <c>null</c> si no está.</summary>
    internal static string? OnlyArgument(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("--only=", StringComparison.OrdinalIgnoreCase))
                return a["--only=".Length..];
            if (!a.Equals("--only", StringComparison.OrdinalIgnoreCase)) continue;

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"--only requiere un valor: pasos separados por coma (válidos: {ValidIds}), p. ej. --only 4a,5c");
            return args[i + 1];
        }
        return null;
    }

    private static string ValidIds => string.Join(", ", All.Select(s => s.Id)) + ", e2e";

    /// <summary>Pasos pedidos, en el orden del catálogo y sin repetidos. <c>null</c>/vacío/"e2e"/"all" = todos.</summary>
    internal static IReadOnlyList<E2eStep> Select(string? only)
    {
        if (string.IsNullOrWhiteSpace(only)) return All;

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in only.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = Normalize(raw);
            if (token is "e2e" or "all") return All;

            var matches = All.Where(s => Matches(s.Id, token)).ToList();
            if (matches.Count == 0)
                throw new ArgumentException($"--only: paso desconocido '{raw}'. Válidos: {ValidIds}");
            foreach (var m in matches) wanted.Add(m.Id);
        }
        return All.Where(s => wanted.Contains(s.Id)).ToList();
    }

    private static string Normalize(string raw)
    {
        var t = raw.Trim().Trim('[', ']').ToLowerInvariant();
        return t.StartsWith("e2e-", StringComparison.Ordinal) ? t["e2e-".Length..] : t;
    }

    /// <summary>Id exacto, o número puro que identifica el grupo (id = número + letras).</summary>
    private static bool Matches(string id, string token)
    {
        if (id.Equals(token, StringComparison.OrdinalIgnoreCase)) return true;
        return token.Length > 0 && token.All(char.IsDigit)
            && id.StartsWith(token, StringComparison.OrdinalIgnoreCase)
            && id.Length > token.Length && id[token.Length..].All(char.IsLetter);
    }
}
