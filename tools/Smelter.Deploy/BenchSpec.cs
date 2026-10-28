// Piezas PURAS del benchmark de 100k filas en dry run (Smelter.Verify --bench): generación del CSV,
// partición en bloques de subida (UploadBlock acepta hasta 4 MB por bloque), estadísticas por chunk y gate.

using System.Globalization;
using System.Text;

namespace Smelter.Deploy;

internal static class BenchSpec
{
    internal const string Domain = "@smelter-bench.test";
    internal const string JobName = "Bench 100k dry-run — Smelter.Verify";
    internal const string TemplateName = "Smelter Bench 100k";
    internal const int Rows = 100_000;
    internal const int ChunkSize = 1000;
    internal const int MaxChunkSeconds = 60;
    /// <summary>Bloque de subida de 2 MB: holgado bajo el máximo de 4 MB de UploadBlock.</summary>
    internal const int UploadBlockSize = 2 * 1024 * 1024;

    internal static byte[] BuildCsv(int rows)
    {
        var sb = new StringBuilder("Email,FirstName,LastName", rows * 44 + 32);
        for (var i = 1; i <= rows; i++)
            sb.Append('\n').Append('b').Append(i).Append(Domain).Append(",Fn").Append(i).Append(",Ln").Append(i);
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    internal static IReadOnlyList<(int Offset, int Count)> BlockRanges(int length, int blockSize)
    {
        var list = new List<(int, int)>();
        for (var offset = 0; offset < length; offset += blockSize)
            list.Add((offset, Math.Min(blockSize, length - offset)));
        return list;
    }

    /// <summary>BlockId base64 de largo fijo (todos los ids de un archivo deben medir lo mismo) y URL-safe.</summary>
    internal static string BlockId(int index) => Convert.ToBase64String(Encoding.ASCII.GetBytes(index.ToString("D6", CultureInfo.InvariantCulture)));
}

internal sealed record BenchStatsResult(int Count, double TotalMs, double FirstMs, double MiddleMs, double LastMs,
    double P50Ms, double P95Ms, double MaxMs);

internal static class BenchStats
{
    internal static BenchStatsResult Compute(IReadOnlyList<double> chunkMs)
    {
        if (chunkMs.Count == 0) throw new ArgumentException("sin muestras", nameof(chunkMs));
        var sorted = chunkMs.OrderBy(v => v).ToArray();
        double Percentile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(p / 100 * sorted.Length) - 1)]; // nearest-rank
        return new BenchStatsResult(chunkMs.Count, chunkMs.Sum(), chunkMs[0], chunkMs[chunkMs.Count / 2], chunkMs[^1],
            Percentile(50), Percentile(95), sorted[^1]);
    }

    /// <summary>null si el chunk más lento queda bajo el límite; si no, el mensaje del fallo.</summary>
    internal static string? Gate(BenchStatsResult s, int limitSeconds) =>
        s.MaxMs < limitSeconds * 1000.0
            ? null
            : $"BENCH: el chunk más lento (max) tardó {s.MaxMs:0} ms, igual o por encima del límite de {limitSeconds} s ({limitSeconds * 1000} ms).";

    internal static string Format(BenchStatsResult s) =>
        $"{s.Count} chunks | total {s.TotalMs / 1000:0.0} s | first {s.FirstMs:0} ms | middle {s.MiddleMs:0} ms | last {s.LastMs:0} ms | " +
        $"p50 {s.P50Ms:0} ms | p95 {s.P95Ms:0} ms | max {s.MaxMs:0} ms";
}
