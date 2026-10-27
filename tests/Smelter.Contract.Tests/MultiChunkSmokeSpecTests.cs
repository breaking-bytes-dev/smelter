using System.Text;
using FluentAssertions;
using Smelter.Deploy;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Smoke multi-chunk (MultiChunkSmokeSpec): el CSV generado se recorre con el MISMO CsvParserService que usa
/// ProcessChunk, así los números esperados (chunks, cursor final, RowNumber de las filas fallidas) no son suposiciones.
/// </summary>
public class MultiChunkSmokeSpecTests
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private static List<(List<Plugins.Models.SourceRow> Rows, long Next)> Walk(string csv, int chunkSize)
    {
        var sut = new CsvParserService(512); // bloques chicos: fuerza que los límites de bloque caigan en medio de registros
        var src = new ByteArrayRangeSource(Utf8.GetBytes(csv));
        var info = sut.ReadHeaderInfo(src, true, ",");
        info.Supported.Should().BeTrue();
        var cursor = info.DataStartOffset;
        var chunks = new List<(List<Plugins.Models.SourceRow>, long)>();
        var next = 2;
        while (true)
        {
            var c = sut.ReadChunk(src, cursor, info.Headers, ",", next, chunkSize);
            if (c.Rows.Count == 0) break;
            chunks.Add((c.Rows.ToList(), c.NextOffset));
            next += c.Rows.Count;
            cursor = c.NextOffset;
        }
        return chunks;
    }

    [Fact]
    public void Constants_MatchTheProcessChunkFloorAndYieldThreeChunksWithAPartialLast()
    {
        // ProcessChunk clampa el chunk a [100, 10000]: el mínimo observable es 100, por eso el smoke usa 250 filas.
        MultiChunkSmokeSpec.ChunkSize.Should().BeGreaterOrEqualTo(100);
        MultiChunkSmokeSpec.Rows.Should().BeGreaterThan(2 * MultiChunkSmokeSpec.ChunkSize);
        MultiChunkSmokeSpec.Rows.Should().BeLessThan(3 * MultiChunkSmokeSpec.ChunkSize);
        MultiChunkSmokeSpec.ExpectedChunks().Select(c => c.Rows).Should().Equal(100, 100, 50);
        MultiChunkSmokeSpec.ExpectedChunks().Select(c => c.ProcessedSoFar).Should().Equal(100, 200, 250);
        MultiChunkSmokeSpec.ExpectedChunks().Select(c => c.HasMore).Should().Equal(true, true, false);
    }

    [Fact]
    public void Csv_HasThreeColumnsHeaderAndExactlyTheDeclaredRows()
    {
        var chunks = Walk(MultiChunkSmokeSpec.BuildCsv(), MultiChunkSmokeSpec.ChunkSize);
        chunks.SelectMany(c => c.Rows).Should().HaveCount(MultiChunkSmokeSpec.Rows);
        chunks.First().Rows[0].Values.Keys.Should().Equal("Email", "Notes", "Children");
    }

    [Fact]
    public void Csv_WalkedByByteCursor_ProducesTheExpectedChunksAndEndsAtFileLength()
    {
        var csv = MultiChunkSmokeSpec.BuildCsv();
        var chunks = Walk(csv, MultiChunkSmokeSpec.ChunkSize);

        chunks.Select(c => c.Rows.Count).Should().Equal(MultiChunkSmokeSpec.ExpectedChunks().Select(c => c.Rows));
        chunks.Last().Next.Should().Be(MultiChunkSmokeSpec.ByteLength(csv));
        MultiChunkSmokeSpec.ByteLength(csv).Should().BeGreaterThan(csv.Length, "hay caracteres multibyte (ñ/é)");
    }

    [Fact]
    public void Csv_EmbeddedNewlineAndMultibyteRecords_SurviveTheParserIntact_AcrossChunkBoundaries()
    {
        var rows = Walk(MultiChunkSmokeSpec.BuildCsv(), MultiChunkSmokeSpec.ChunkSize).SelectMany(c => c.Rows).ToList();

        MultiChunkSmokeSpec.NewlineRecords.Should().Contain(new[] { 100, 101 }, "straddle del límite del 1er chunk");
        MultiChunkSmokeSpec.NewlineRecords.Should().Contain(new[] { 200, 201 }, "straddle del límite del 2do chunk");
        foreach (var i in MultiChunkSmokeSpec.NewlineRecords)
        {
            var notes = rows[i - 1].Values["Notes"];
            notes.Should().Contain("\n");
            notes.Should().Be(MultiChunkSmokeSpec.ExpectedNotes(i));
        }
        rows.Select((r, idx) => (r, i: idx + 1)).Should().OnlyContain(x => x.r.Values["Notes"] == MultiChunkSmokeSpec.ExpectedNotes(x.i));
        MultiChunkSmokeSpec.ExpectedNotes(1).Should().MatchRegex("[ñé]");
    }

    [Fact]
    public void Csv_RowNumbersAreRecordBased_AndFailedRowsMatchTheBadRecords()
    {
        var rows = Walk(MultiChunkSmokeSpec.BuildCsv(), MultiChunkSmokeSpec.ChunkSize).SelectMany(c => c.Rows).ToList();

        rows.Select(r => r.RowNumber).Should().Equal(Enumerable.Range(2, MultiChunkSmokeSpec.Rows)); // header = 1
        var bad = rows.Where(r => !int.TryParse(r.Values["Children"], out _)).Select(r => r.RowNumber).ToList();
        bad.Should().Equal(MultiChunkSmokeSpec.ExpectedFailedRowNumbers);
        MultiChunkSmokeSpec.ExpectedFailedRowNumbers.Should().Equal(102, 251);
        MultiChunkSmokeSpec.ExpectedSuccess.Should().Be(MultiChunkSmokeSpec.Rows - 2);
    }

    [Fact]
    public void Emails_AreUniqueAndOnTheSmokeDomain_AndGoodRowsAreTheOnesCreated()
    {
        var emails = Enumerable.Range(1, MultiChunkSmokeSpec.Rows).Select(MultiChunkSmokeSpec.Email).ToList();
        emails.Should().OnlyHaveUniqueItems().And.OnlyContain(e => e.EndsWith(MultiChunkSmokeSpec.Domain));
        MultiChunkSmokeSpec.GoodRecords.Should().HaveCount(MultiChunkSmokeSpec.ExpectedSuccess);
        MultiChunkSmokeSpec.GoodRecords.Should().NotContain(MultiChunkSmokeSpec.BadRecords);
    }

    [Fact]
    public void NormalizeNewlines_MakesDataverseCrLfComparableToTheExpectedLf()
    {
        MultiChunkSmokeSpec.NormalizeNewlines("a\r\nb\nc").Should().Be("a\nb\nc");
    }
}
