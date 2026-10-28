// Piezas PURAS del smoke multi-chunk de Smelter.Verify: un CSV determinístico que obliga al import a recorrer
// el archivo por el cursor de bytes en 3 chunks (el último parcial), con campos entrecomillados con newline
// embebido y caracteres multibyte (ñ, é) cruzando los límites de chunk, y 2 filas malas (una al principio del
// 2º chunk y otra al final del 3º) para verificar el RowNumber de los logs.
//
// ProcessChunk clampa el chunk a [100, 10000] (MinChunkSize): no se puede forzar un chunk de 5 filas, así que
// el smoke usa ChunkSize = 100 (input opcional de la Custom API) sobre 250 filas => 100 + 100 + 50.
// RowNumber = índice de REGISTRO + 1 (cabecera = 1): un newline embebido NO suma una fila.

using System.Text;

namespace Smelter.Deploy;

internal sealed record ExpectedChunk(int Rows, int ProcessedSoFar, bool HasMore);

internal static class MultiChunkSmokeSpec
{
    internal const string Domain = "@smelter-chunk-smoke.test";
    internal const string JobName = "Smoke Test Multi-chunk — Smelter.Verify";
    internal const string TemplateName = "Smelter Multi-chunk Smoke Test";
    internal const int Rows = 250;
    internal const int ChunkSize = 100;

    /// <summary>Registros (1-based) con newline embebido: pegados a los límites 100|101 y 200|201 de los chunks.</summary>
    internal static readonly IReadOnlySet<int> NewlineRecords = new HashSet<int> { 1, 50, 99, 100, 101, 199, 200, 201, 250 };

    /// <summary>Registros (1-based) con Children no numérico (TypeError): primero del 2º chunk y último del 3º.</summary>
    internal static readonly IReadOnlySet<int> BadRecords = new HashSet<int> { 101, 250 };

    internal static IReadOnlyList<int> GoodRecords { get; } =
        Enumerable.Range(1, Rows).Where(i => !BadRecords.Contains(i)).ToList();

    internal static int ExpectedSuccess => GoodRecords.Count;

    /// <summary>RowNumber físico esperado en el log de las filas malas (cabecera = 1, registros desde 2).</summary>
    internal static IReadOnlyList<int> ExpectedFailedRowNumbers { get; } = BadRecords.OrderBy(i => i).Select(i => i + 1).ToList();

    internal static string Email(int record) => $"chunk{record:D3}{Domain}";

    internal static string ExpectedNotes(int record) =>
        NewlineRecords.Contains(record) ? $"Línea {record}\nSegunda línea ñé {record}" : $"Señor Pérez {record}";

    internal static string BuildCsv()
    {
        var sb = new StringBuilder("Email,Notes,Children");
        for (var i = 1; i <= Rows; i++)
        {
            var notes = ExpectedNotes(i);
            sb.Append('\n').Append(Email(i)).Append(',')
              .Append(NewlineRecords.Contains(i) ? $"\"{notes}\"" : notes).Append(',')
              .Append(BadRecords.Contains(i) ? "notanumber" : (i % 10).ToString());
        }
        return sb.ToString(); // sin newline final: el cursor de bytes debe terminar en el largo exacto del archivo
    }

    internal static long ByteLength(string csv) => new UTF8Encoding(false).GetByteCount(csv);

    /// <summary>Lo que devuelve cada llamada a ProcessChunk (Rows leídas, ProcessedSoFar, HasMore).</summary>
    internal static IReadOnlyList<ExpectedChunk> ExpectedChunks()
    {
        var list = new List<ExpectedChunk>();
        for (var done = 0; done < Rows;)
        {
            var n = Math.Min(ChunkSize, Rows - done);
            done += n;
            list.Add(new ExpectedChunk(n, done, n >= ChunkSize));
        }
        return list;
    }

    /// <summary>Dataverse normaliza los saltos de línea de un texto multilínea a CRLF: se compara en LF.</summary>
    internal static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n");
}
