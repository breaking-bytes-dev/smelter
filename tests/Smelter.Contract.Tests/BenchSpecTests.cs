using System.Text;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>Piezas puras del benchmark de 100k filas en dry run (BenchSpec): CSV, bloques de subida, estadísticas y gate.</summary>
public class BenchSpecTests
{
    [Fact]
    public void Defaults_Are100kRows_1000Chunk_And60sGate()
    {
        BenchSpec.Rows.Should().Be(100_000);
        BenchSpec.ChunkSize.Should().Be(1000);
        BenchSpec.MaxChunkSeconds.Should().Be(60);
    }

    [Fact]
    public void BuildCsv_HasHeaderAndExactRowCount_WithThreeShortUniqueColumns()
    {
        var csv = Encoding.UTF8.GetString(BenchSpec.BuildCsv(1000));
        var lines = csv.Split('\n');
        lines.Should().HaveCount(1001); // header + 1000, sin newline final
        lines[0].Should().Be("Email,FirstName,LastName");
        lines.Skip(1).Select(l => l.Split(',')[0]).Should().OnlyHaveUniqueItems().And.OnlyContain(e => e.EndsWith(BenchSpec.Domain));
        lines.Skip(1).Should().OnlyContain(l => l.Split(',').Length == 3);
    }

    [Fact]
    public void BuildCsv_At100kRows_ExceedsOneUploadBlock_SoItNeedsMultipleBlocks()
    {
        var bytes = BenchSpec.BuildCsv(BenchSpec.Rows);
        bytes.Length.Should().BeGreaterThan(BenchSpec.UploadBlockSize);
        BenchSpec.UploadBlockSize.Should().BeLessOrEqualTo(4 * 1024 * 1024);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(5, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(25, 10, 3)]
    public void BlockRanges_CoverTheWholeFileWithoutGapsOrOverlap(int length, int blockSize, int expectedBlocks)
    {
        var ranges = BenchSpec.BlockRanges(length, blockSize);
        ranges.Should().HaveCount(expectedBlocks);
        ranges.Sum(r => r.Count).Should().Be(length);
        var offset = 0;
        foreach (var r in ranges) { r.Offset.Should().Be(offset); r.Count.Should().BeInRange(1, blockSize); offset += r.Count; }
    }

    [Fact]
    public void BlockIds_AreBase64OfEqualLengthAndUrlSafe()
    {
        var ids = Enumerable.Range(0, 120).Select(BenchSpec.BlockId).ToList();
        ids.Select(i => i.Length).Distinct().Should().HaveCount(1);
        ids.Should().OnlyHaveUniqueItems().And.OnlyContain(i => !i.Contains('+') && !i.Contains('/'));
    }

    [Fact]
    public void Stats_ComputesFirstMiddleLastPercentilesAndTotal()
    {
        var ms = Enumerable.Range(1, 100).Select(i => (double)i).ToList(); // 1..100
        var s = BenchStats.Compute(ms);

        s.Count.Should().Be(100);
        s.TotalMs.Should().Be(5050);
        s.FirstMs.Should().Be(1);
        s.MiddleMs.Should().Be(51);
        s.LastMs.Should().Be(100);
        s.P50Ms.Should().Be(50);   // nearest-rank
        s.P95Ms.Should().Be(95);
        s.MaxMs.Should().Be(100);
    }

    [Fact]
    public void Stats_DoesNotDependOnInputOrder()
    {
        var s = BenchStats.Compute(new double[] { 300, 100, 200 });
        s.FirstMs.Should().Be(300);
        s.LastMs.Should().Be(200);
        s.MaxMs.Should().Be(300);
        s.P50Ms.Should().Be(200);
    }

    [Fact]
    public void Stats_SingleSample_AllMetricsEqualIt_AndEmptyThrows()
    {
        var s = BenchStats.Compute(new[] { 42.0 });
        new[] { s.FirstMs, s.MiddleMs, s.LastMs, s.P50Ms, s.P95Ms, s.MaxMs, s.TotalMs }.Should().OnlyContain(v => v == 42.0);
        var act = () => BenchStats.Compute(Array.Empty<double>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Gate_PassesUnderTheLimit_AndFailsWithAClearMessageAtOrAbove()
    {
        BenchStats.Gate(BenchStats.Compute(new[] { 1000.0, 59_999.0 }), 60).Should().BeNull();

        var msg = BenchStats.Gate(BenchStats.Compute(new[] { 1000.0, 60_000.0 }), 60);
        msg.Should().NotBeNull();
        msg.Should().Contain("60").And.Contain("max").And.Contain("60000");
    }

    [Fact]
    public void Format_PrintsEveryRequestedFigure()
    {
        var text = BenchStats.Format(BenchStats.Compute(Enumerable.Range(1, 10).Select(i => i * 100.0).ToList()));
        foreach (var token in new[] { "total", "first", "middle", "last", "p50", "p95", "max" })
            text.Should().Contain(token);
    }
}
