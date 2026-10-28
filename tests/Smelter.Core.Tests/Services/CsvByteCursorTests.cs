using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    /// <summary>
    /// Cursor de bytes (O(N) en vez de O(N²)): cada chunk baja solo el rango que necesita y solo consume
    /// registros COMPLETOS. Las posiciones son BYTES UTF-8 (no chars), con el BOM fuera de la cuenta.
    /// </summary>
    public class CsvByteCursorTests
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        private static byte[] B(string s) => Utf8.GetBytes(s);
        private static CsvParserService Sut(int blockSize = 4 * 1024 * 1024) => new CsvParserService(blockSize);
        private static IRangeSource Src(byte[] b) => new ByteArrayRangeSource(b);

        private static byte[] WithBom(byte[] b) => Bom.Concat(b).ToArray();

        /// <summary>Recorre todo el archivo por cursor, chunk a chunk, como lo hace ProcessChunk.</summary>
        private static (List<SourceRow> rows, List<long> cursors) Walk(byte[] file, bool hasHeader, string delimiter, int k, int blockSize)
        {
            var sut = Sut(blockSize);
            var src = Src(file);
            var info = sut.ReadHeaderInfo(src, hasHeader, delimiter);
            info.Supported.Should().BeTrue();
            var cursor = info.DataStartOffset;
            var rows = new List<SourceRow>();
            var cursors = new List<long> { cursor };
            var baseRow = hasHeader ? 2 : 1;
            for (var guard = 0; guard < 100000; guard++)
            {
                var chunk = sut.ReadChunk(src, cursor, info.Headers, delimiter, baseRow + rows.Count, k);
                if (chunk.Rows.Count == 0) break;
                chunk.Rows.Count.Should().BeLessOrEqualTo(k);
                chunk.NextOffset.Should().BeGreaterThan(cursor);
                rows.AddRange(chunk.Rows);
                cursor = chunk.NextOffset;
                cursors.Add(cursor);
            }
            return (rows, cursors);
        }

        // ── Cabecera ────────────────────────────────────────────────────────────

        [Fact]
        public void HeaderInfo_Lf_DataStartsAfterHeaderLine()
        {
            var info = Sut().ReadHeaderInfo(B("name,code\nA,1\n"), true, ",");
            info.Supported.Should().BeTrue();
            info.Headers.Should().Equal("name", "code");
            info.DataStartOffset.Should().Be(10); // "name,code\n"
        }

        [Fact]
        public void HeaderInfo_Crlf_DataStartsAfterCrLf()
        {
            var info = Sut().ReadHeaderInfo(B("name,code\r\nA,1\r\n"), true, ",");
            info.DataStartOffset.Should().Be(11);
        }

        [Fact]
        public void HeaderInfo_Bom_IsCountedInOffsetsButNotInHeaderName()
        {
            var info = Sut().ReadHeaderInfo(WithBom(B("name,code\nA,1\n")), true, ",");
            info.Supported.Should().BeTrue();
            info.Headers[0].Should().Be("name", "el BOM no es parte del nombre de la primera columna");
            info.DataStartOffset.Should().Be(13); // 3 (BOM) + 10
        }

        [Fact]
        public void HeaderInfo_NoHeader_GeneratesColumnKeysAndStartsAtZero()
        {
            var info = Sut().ReadHeaderInfo(B("A,1\nB,2\n"), false, ",");
            info.Headers.Should().Equal("Column0", "Column1");
            info.DataStartOffset.Should().Be(0);
        }

        [Fact]
        public void HeaderInfo_NoHeaderWithBom_StartsAfterBom()
        {
            Sut().ReadHeaderInfo(WithBom(B("A,1\n")), false, ",").DataStartOffset.Should().Be(3);
        }

        [Fact]
        public void HeaderInfo_QuotedHeaderWithNewline_SpansLines()
        {
            var info = Sut().ReadHeaderInfo(B("\"a\nb\",c\nX,1\n"), true, ",");
            info.Headers[1].Should().Be("c");
            info.DataStartOffset.Should().Be(B("\"a\nb\",c\n").Length);
        }

        [Fact]
        public void HeaderInfo_HeaderOnlyWithoutNewline_DataStartsAtEnd()
        {
            var info = Sut().ReadHeaderInfo(B("name,code"), true, ",");
            info.DataStartOffset.Should().Be(9);
        }

        [Fact]
        public void HeaderInfo_EmptyFile_IsSupportedWithNoHeaders()
        {
            var info = Sut().ReadHeaderInfo(new byte[0], true, ",");
            info.Supported.Should().BeTrue();
            info.Headers.Should().BeEmpty();
            info.DataStartOffset.Should().Be(0);
        }

        [Theory]
        [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00 })]             // UTF-16 LE
        [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x61 })]             // UTF-16 BE
        [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x61, 0, 0, 0 })] // UTF-32 LE
        public void HeaderInfo_NonUtf8Bom_IsNotSupported_SoCallerFallsBackToLegacy(byte[] file)
        {
            Sut().ReadHeaderInfo(file, true, ",").Supported.Should().BeFalse();
        }

        [Fact]
        public void HeaderInfo_InvalidUtf8_IsNotSupported()
        {
            // Latin-1 'á' (0xE1) suelto: los offsets en bytes no son confiables → legacy.
            var file = new byte[] { (byte)'a', (byte)',', (byte)'b', (byte)'\n', 0xE1, (byte)',', (byte)'1', (byte)'\n' };
            Sut().ReadHeaderInfo(file, true, ",").Supported.Should().BeFalse();
        }

        [Fact]
        public void HeaderInfo_ByRange_ExtendsThePrefixUntilTheHeaderIsComplete()
        {
            var file = B("a_rather_long_column_name,another_long_column_name,third\nA,1,2\n");
            var info = Sut(blockSize: 7).ReadHeaderInfo(Src(file), true, ",");
            info.Headers.Should().Equal("a_rather_long_column_name", "another_long_column_name", "third");
            info.DataStartOffset.Should().Be(B("a_rather_long_column_name,another_long_column_name,third\n").Length);
        }

        // ── Chunks ──────────────────────────────────────────────────────────────

        [Fact]
        public void ReadChunk_Lf_ReturnsRequestedRecordsAndCursorOfTheNextOne()
        {
            var file = B("name\nA\nB\nC\nD\n");
            var sut = Sut();
            var info = sut.ReadHeaderInfo(file, true, ",");

            var chunk = sut.ReadChunk(Src(file), info.DataStartOffset, info.Headers, ",", firstRowNumber: 2, take: 2);

            chunk.Rows.Select(r => r.Values["name"]).Should().Equal("A", "B");
            chunk.Rows.Select(r => r.RowNumber).Should().Equal(2, 3);
            chunk.NextOffset.Should().Be(B("name\nA\nB\n").Length);
        }

        [Fact]
        public void ReadChunk_ResumesFromTheCursorWithoutRereadingEarlierRows()
        {
            var file = B("name\nA\nB\nC\nD\n");
            var sut = Sut();
            var info = sut.ReadHeaderInfo(file, true, ",");
            var cursor = B("name\nA\nB\n").Length;

            var chunk = sut.ReadChunk(Src(file), cursor, info.Headers, ",", firstRowNumber: 4, take: 5);

            chunk.Rows.Select(r => r.Values["name"]).Should().Equal("C", "D");
            chunk.Rows.Select(r => r.RowNumber).Should().Equal(4, 5);
            chunk.NextOffset.Should().Be(file.Length);
        }

        [Fact]
        public void ReadChunk_Crlf_CursorLandsAfterTheFullTerminator()
        {
            var file = B("name\r\nA\r\nB\r\nC\r\n");
            var sut = Sut(blockSize: 5);
            var info = sut.ReadHeaderInfo(Src(file), true, ",");

            var chunk = sut.ReadChunk(Src(file), info.DataStartOffset, info.Headers, ",", 2, 1);

            chunk.Rows.Should().ContainSingle().Which.Values["name"].Should().Be("A");
            chunk.NextOffset.Should().Be(B("name\r\nA\r\n").Length);
        }

        [Fact]
        public void ReadChunk_QuotedFieldsWithEmbeddedNewlinesAndDelimiters_AreOneRecord()
        {
            var file = B("a,b\n\"line1\nline2, still\",x\n\"q\"\"uote\",y\nlast,z\n");
            var (rows, _) = Walk(file, true, ",", k: 1, blockSize: 4);

            rows.Select(r => r.Values["a"]).Should().Equal("line1\nline2, still", "q\"uote", "last");
            rows.Select(r => r.Values["b"]).Should().Equal("x", "y", "z");
            rows.Select(r => r.RowNumber).Should().Equal(2, 3, 4);
        }

        [Fact]
        public void ReadChunk_MultibyteUtf8_OffsetsAreBytesNotChars()
        {
            // á/ñ = 2 bytes, 😀 = 4 bytes (par subrogado: CsvHelper.CountBytes lo cuenta como 6).
            var file = B("n\ná,ñ\n😀\nok\n");
            var sut = Sut();
            var info = sut.ReadHeaderInfo(file, true, ";");

            var chunk = sut.ReadChunk(Src(file), info.DataStartOffset, info.Headers, ";", 2, 2);

            chunk.Rows.Select(r => r.Values["n"]).Should().Equal("á,ñ", "😀");
            chunk.NextOffset.Should().Be(B("n\ná,ñ\n😀\n").Length);
            chunk.NextOffset.Should().Be(2 + 6 + 5); // sanity: bytes (header + "á,ñ\n" + "😀\n"), no chars
        }

        [Fact]
        public void ReadChunk_LastLineWithoutNewline_IsReturnedAndCursorReachesEof()
        {
            var file = B("n\nA\nB");
            var (rows, cursors) = Walk(file, true, ",", k: 10, blockSize: 3);

            rows.Select(r => r.Values["n"]).Should().Equal("A", "B");
            cursors.Last().Should().Be(file.Length);
        }

        [Fact]
        public void ReadChunk_BoundaryExactlyOnRecordEnd_DoesNotLoseOrDuplicate()
        {
            var file = B("n\nAA\nBB\nCC\n"); // registros de 3 bytes
            foreach (var block in new[] { 3, 6 })
            {
                var (rows, _) = Walk(file, true, ",", k: 1, blockSize: block);
                rows.Select(r => r.Values["n"]).Should().Equal("AA", "BB", "CC");
            }
        }

        [Fact]
        public void ReadChunk_BlockBoundaryInsideAMultibyteCharacter_IsHandled()
        {
            var file = B("n\naé😀b\nñ\n");
            for (var block = 1; block <= 9; block++)
            {
                var (rows, _) = Walk(file, true, ",", k: 1, blockSize: block);
                rows.Select(r => r.Values["n"]).Should().Equal("aé😀b", "ñ");
            }
        }

        [Fact]
        public void ReadChunk_TrailingBlankLines_YieldNoPhantomRecord()
        {
            var file = B("n\nA\nB\n\n\n");
            var (rows, cursors) = Walk(file, true, ",", k: 1, blockSize: 4);

            rows.Select(r => r.Values["n"]).Should().Equal("A", "B");
            cursors.Last().Should().BeLessOrEqualTo(file.Length);
        }

        [Fact]
        public void ReadChunk_CursorAtOrPastEof_ReturnsEmpty()
        {
            var file = B("n\nA\n");
            var sut = Sut();
            var info = sut.ReadHeaderInfo(file, true, ",");

            var chunk = sut.ReadChunk(Src(file), file.Length, info.Headers, ",", 3, 5);

            chunk.Rows.Should().BeEmpty();
            chunk.NextOffset.Should().Be(file.Length);
        }

        [Fact]
        public void ReadChunk_Bom_FirstChunkStartsAtDataAndRowsMatch()
        {
            var file = WithBom(B("n,m\nA,1\nB,2\n"));
            var (rows, cursors) = Walk(file, true, ",", k: 1, blockSize: 4);

            rows.Select(r => r.Values["n"]).Should().Equal("A", "B");
            cursors.First().Should().Be(3 + 4);
            cursors.Last().Should().Be(file.Length);
        }

        [Fact]
        public void ReadChunk_MissingTrailingFields_AreEmptyStringsLikeReadRange()
        {
            var file = B("a,b,c\nx\n");
            var (rows, _) = Walk(file, true, ",", k: 5, blockSize: 4);
            rows[0].Values["b"].Should().Be("");
            rows[0].Values["c"].Should().Be("");
        }

        // ── Propiedad: chunks por cursor == ReadAll ──────────────────────────────

        private static string Generate(Random rnd, int rows, string nl, string delim, bool trailingNewline)
        {
            string[] pieces = { "plain", "á", "ñandú", "😀", "x y", "a\"b", "", "comma,inside", "semi;inside", "multi\nline", "cr\r\nlf", "end" };
            var sb = new StringBuilder();
            sb.Append("id").Append(delim).Append("txt").Append(delim).Append("other").Append(nl);
            for (var i = 0; i < rows; i++)
            {
                sb.Append(i).Append(delim);
                for (var f = 0; f < 2; f++)
                {
                    var v = pieces[rnd.Next(pieces.Length)];
                    var needsQuote = v.IndexOfAny(new[] { '"', '\n', '\r' }) >= 0 || v.Contains(delim) || v.Length == 0 && rnd.Next(2) == 0;
                    sb.Append(needsQuote ? "\"" + v.Replace("\"", "\"\"") + "\"" : v);
                    if (f == 0) sb.Append(delim);
                }
                if (i < rows - 1 || trailingNewline) sb.Append(nl);
            }
            return sb.ToString();
        }

        [Theory]
        [InlineData(",", "\n", false, true)]
        [InlineData(",", "\r\n", false, false)]
        [InlineData(";", "\n", true, true)]
        [InlineData("\t", "\r\n", true, false)]
        public void Property_ChunkWalkEqualsReadAll_ForManyChunkAndBlockSizes(string delim, string nl, bool bom, bool trailingNewline)
        {
            var rnd = new Random(1234);
            var text = Generate(rnd, rows: 60, nl, delim, trailingNewline);
            var file = bom ? WithBom(B(text)) : B(text);
            var expected = new CsvParserService().Parse(file, true, new FileParseOptions { Delimiter = delim });
            expected.Count.Should().Be(60);

            foreach (var k in new[] { 1, 2, 3, 7, 59, 60, 100 })
            foreach (var block in new[] { 1, 7, 13, 64, 1000, 4 * 1024 * 1024 })
            {
                var (rows, cursors) = Walk(file, true, delim, k, block);

                rows.Count.Should().Be(expected.Count, $"k={k} block={block}");
                for (var i = 0; i < rows.Count; i++)
                {
                    rows[i].RowNumber.Should().Be(expected[i].RowNumber, $"k={k} block={block} i={i}");
                    rows[i].Values.Should().Equal(expected[i].Values, $"k={k} block={block} i={i}");
                }
                cursors.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
                cursors.Last().Should().BeLessOrEqualTo(file.Length);
            }
        }

        [Fact]
        public void Property_NoHeader_ChunkWalkEqualsReadAll()
        {
            var rnd = new Random(99);
            var text = Generate(rnd, 40, "\n", ",", true);
            var file = B(text);
            var expected = new CsvParserService().Parse(file, false, new FileParseOptions { Delimiter = "," });

            foreach (var k in new[] { 1, 4, 41 })
            foreach (var block in new[] { 5, 64 })
            {
                var (rows, _) = Walk(file, false, ",", k, block);
                rows.Count.Should().Be(expected.Count);
                rows.Select(r => r.RowNumber).Should().Equal(expected.Select(r => r.RowNumber));
                for (var i = 0; i < rows.Count; i++)
                    rows[i].Values.Should().Equal(expected[i].Values);
            }
        }
    }
}
