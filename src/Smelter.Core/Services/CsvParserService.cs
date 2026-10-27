using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Streaming CSV reader: reads a range of data rows by offset/skip without materializing the whole
    /// file, and counts rows cheaply. This is what lets ProcessChunk parse only its slice and PrepareImport
    /// count rows, with no staging table.
    /// </summary>
    public interface ICsvReader
    {
        /// <summary>Count data rows (excludes the header row when present).</summary>
        int CountRows(byte[] fileBytes, bool hasHeaderRow, string delimiter);

        /// <summary>Read the column names (header row, or generated Column0..N when no header).</summary>
        IReadOnlyList<string> ReadHeaders(byte[] fileBytes, bool hasHeaderRow, string delimiter);

        /// <summary>
        /// Read <paramref name="take"/> data rows starting after skipping <paramref name="skip"/> data rows.
        /// Skipped rows are advanced but NOT materialized. RowNumber is the physical row (header = 1).
        /// </summary>
        IReadOnlyList<SourceRow> ReadRange(byte[] fileBytes, bool hasHeaderRow, string delimiter, int skip, int take);

        /// <summary>Cabecera + byte del primer dato de un archivo completo; valida que se pueda recorrer por bytes (UTF-8 válido).</summary>
        CsvHeaderInfo ReadHeaderInfo(byte[] wholeFile, bool hasHeaderRow, string delimiter);

        /// <summary>Cabecera + byte del primer dato leyendo solo el prefijo necesario por rangos.</summary>
        CsvHeaderInfo ReadHeaderInfo(IRangeSource source, bool hasHeaderRow, string delimiter);

        /// <summary>
        /// Lee hasta <paramref name="take"/> registros COMPLETOS empezando en el byte <paramref name="cursor"/>,
        /// bajando solo los bloques necesarios. <paramref name="firstRowNumber"/> es el RowNumber del primer registro.
        /// </summary>
        CsvChunk ReadChunk(IRangeSource source, long cursor, IReadOnlyList<string> headers, string delimiter,
            int firstRowNumber, int take);
    }

    /// <summary>
    /// Parses CSV files using CsvHelper. Handles custom delimiters, quoted fields, BOM, and header modes.
    /// </summary>
    public class CsvParserService : IFileParser, ICsvReader
    {
        /// <summary>Bytes por bloque al recorrer por rangos (4 MB = el tamaño recomendado de DownloadBlock).</summary>
        public const int DefaultBlockSize = 4 * 1024 * 1024;

        private readonly int _blockSize;

        public CsvParserService() : this(DefaultBlockSize) { }

        public CsvParserService(int blockSize)
        {
            _blockSize = blockSize > 0 ? blockSize : DefaultBlockSize;
        }

        /// <summary>
        /// Convenience wrapper over <see cref="ReadRange"/> that reads all rows from the beginning.
        /// Satisfies <see cref="IFileParser"/> for callers that want the complete file at once.
        /// </summary>
        /// <param name="fileBytes">Raw file content as bytes.</param>
        /// <param name="hasHeaderRow">Whether the first row contains column headers.</param>
        /// <param name="options">Parse options; only <see cref="FileParseOptions.Delimiter"/> is used for CSV.</param>
        /// <returns>All data rows from the file.</returns>
        public List<SourceRow> Parse(byte[] fileBytes, bool hasHeaderRow, FileParseOptions options)
        {
            var delimiter = string.IsNullOrEmpty(options?.Delimiter) ? "," : options.Delimiter;
            return ReadRange(fileBytes, hasHeaderRow, delimiter, 0, int.MaxValue).ToList();
        }

        // ── Cursor de bytes (O(N)) ─────────────────────────────────────────────────────────────
        // ProcessChunk deja de bajar y re-parsear el archivo entero en cada chunk: guarda el BYTE del
        // próximo registro y baja solo el rango que necesita. Las posiciones se calculan sobre el texto
        // decodificado (Parser.CharCount) re-codificado a UTF-8: CsvConfiguration.CountBytes cuenta mal los
        // pares subrogados (😀 = 6 bytes en vez de 4), así que NO se usa. Regla de completitud: un registro
        // está completo si hay otro después o si se llegó al EOF; el último registro de un segmento que NO
        // llega al EOF se descarta (puede estar cortado en medio de un campo entrecomillado o de un CRLF).

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private sealed class ParsedRecord
        {
            public string[] Fields;
            /// <summary>Bytes UTF-8 desde el inicio del segmento hasta el final de este registro (incluye su terminador).</summary>
            public long EndByte;
        }

        /// <inheritdoc/>
        public CsvHeaderInfo ReadHeaderInfo(byte[] wholeFile, bool hasHeaderRow, string delimiter)
        {
            if (wholeFile == null || wholeFile.Length == 0)
                return new CsvHeaderInfo { Supported = true };

            // Los offsets en bytes solo son confiables con UTF-8 válido: un byte inválido se decodifica a U+FFFD
            // (3 bytes al re-codificar) y desalinea el cursor. Archivo no-UTF-8 => camino legacy (sin cursor).
            try { new UTF8Encoding(false, true).GetCharCount(wholeFile); }
            catch (ArgumentException) { return new CsvHeaderInfo { Supported = false }; }

            return ReadHeaderInfo(new ByteArrayRangeSource(wholeFile), hasHeaderRow, delimiter);
        }

        /// <inheritdoc/>
        public CsvHeaderInfo ReadHeaderInfo(IRangeSource source, bool hasHeaderRow, string delimiter)
        {
            if (source == null || source.Length == 0)
                return new CsvHeaderInfo { Supported = true };

            var head = source.Read(0, (int)Math.Min(4, source.Length));
            if (IsUtf16Or32Bom(head))
                return new CsvHeaderInfo { Supported = false };

            var bom = head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;

            var first = ReadRecords(source, bom, delimiter, 1);
            if (first.Count == 0)
                return new CsvHeaderInfo { Supported = true, DataStartOffset = bom };

            if (hasHeaderRow)
                return new CsvHeaderInfo
                {
                    Supported = true,
                    Headers = first[0].Fields.Select(h => h.Trim()).ToList(),
                    DataStartOffset = bom + first[0].EndByte
                };

            return new CsvHeaderInfo
            {
                Supported = true,
                Headers = Enumerable.Range(0, first[0].Fields.Length).Select(i => $"Column{i}").ToList(),
                DataStartOffset = bom
            };
        }

        /// <inheritdoc/>
        public CsvChunk ReadChunk(IRangeSource source, long cursor, IReadOnlyList<string> headers, string delimiter,
            int firstRowNumber, int take)
        {
            var rows = new List<SourceRow>();
            if (source == null || take <= 0 || cursor >= source.Length)
                return new CsvChunk { Rows = rows, NextOffset = cursor };

            var records = ReadRecords(source, cursor, delimiter, take);
            for (var n = 0; n < records.Count; n++)
            {
                var row = new SourceRow { RowNumber = firstRowNumber + n };
                var fields = records[n].Fields;
                for (var i = 0; i < headers.Count; i++)
                    row.Values[headers[i]] = i < fields.Length ? fields[i] ?? "" : "";
                rows.Add(row);
            }

            return new CsvChunk
            {
                Rows = rows,
                NextOffset = cursor + (records.Count > 0 ? records[records.Count - 1].EndByte : 0)
            };
        }

        private static bool IsUtf16Or32Bom(byte[] h) =>
            h.Length >= 2 && ((h[0] == 0xFF && h[1] == 0xFE) || (h[0] == 0xFE && h[1] == 0xFF))
            || h.Length >= 4 && h[0] == 0x00 && h[1] == 0x00 && h[2] == 0xFE && h[3] == 0xFF;

        /// <summary>
        /// Devuelve hasta <paramref name="take"/> registros COMPLETOS desde <paramref name="cursor"/>, bajando
        /// bloques de <c>_blockSize</c> hasta tener <c>take + 1</c> registros (el +1 prueba que el anterior está
        /// completo) o llegar al EOF.
        /// </summary>
        private List<ParsedRecord> ReadRecords(IRangeSource source, long cursor, string delimiter, int take)
        {
            var buffer = new MemoryStream();
            var fetchedEnd = cursor;
            List<ParsedRecord> parsed;
            while (true)
            {
                var block = source.Read(fetchedEnd, (int)Math.Min(_blockSize, source.Length - fetchedEnd));
                buffer.Write(block, 0, block.Length);
                fetchedEnd += block.Length;
                var atEof = fetchedEnd >= source.Length || block.Length == 0;

                parsed = ParseRecords(buffer.ToArray(), delimiter, take + 1);
                if (atEof || parsed.Count > take)
                    break;
            }

            return parsed.Count > take ? parsed.GetRange(0, take) : parsed;
        }

        private static List<ParsedRecord> ParseRecords(byte[] segment, string delimiter, int maxRecords)
        {
            var records = new List<ParsedRecord>();
            // GetString no consume BOM; el segmento nunca lo trae (el cursor arranca después).
            var chars = Utf8NoBom.GetString(segment).ToCharArray();
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
                Delimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,
                BadDataFound = null
            };

            using (var csv = new CsvReader(new StringReader(new string(chars)), config))
            {
                long bytes = 0;
                long previousChars = 0;
                while (records.Count < maxRecords && csv.Read())
                {
                    var fields = new string[csv.Parser.Count];
                    for (var i = 0; i < fields.Length; i++)
                        fields[i] = csv.GetField(i) ?? "";

                    var charCount = csv.Parser.CharCount;
                    bytes += Utf8NoBom.GetByteCount(chars, (int)previousChars, (int)(charCount - previousChars));
                    previousChars = charCount;
                    records.Add(new ParsedRecord { Fields = fields, EndByte = bytes });
                }
            }

            return records;
        }

        /// <inheritdoc/>
        public int CountRows(byte[] fileBytes, bool hasHeaderRow, string delimiter)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return 0;

            using (var csv = OpenReader(fileBytes, hasHeaderRow, delimiter))
            {
                if (hasHeaderRow)
                {
                    if (!csv.Read()) return 0;
                    csv.ReadHeader();
                }

                var count = 0;
                while (csv.Read()) count++;
                return count;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> ReadHeaders(byte[] fileBytes, bool hasHeaderRow, string delimiter)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return new List<string>();

            using (var csv = OpenReader(fileBytes, hasHeaderRow, delimiter))
            {
                if (!csv.Read()) return new List<string>();

                if (hasHeaderRow)
                {
                    csv.ReadHeader();
                    return csv.HeaderRecord.Select(h => h.Trim()).ToList();
                }

                return Enumerable.Range(0, csv.Parser.Count).Select(i => $"Column{i}").ToList();
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<SourceRow> ReadRange(byte[] fileBytes, bool hasHeaderRow, string delimiter, int skip, int take)
        {
            var rows = new List<SourceRow>();
            if (fileBytes == null || fileBytes.Length == 0 || take <= 0)
                return rows;

            using (var csv = OpenReader(fileBytes, hasHeaderRow, delimiter))
            {
                string[] headers = null;

                // When there is a header row it occupies physical row 1, so data rows start at row 2.
                // When there is no header row, data starts at row 1 and column keys are generated as
                // "Column0", "Column1", … derived from the first data row's field count.
                var baseRowNumber = hasHeaderRow ? 2 : 1;

                if (hasHeaderRow)
                {
                    if (!csv.Read()) return rows;
                    csv.ReadHeader();
                    headers = csv.HeaderRecord.Select(h => h.Trim()).ToArray();
                }

                var dataIndex = 0; // 0-based index into data rows (header excluded)
                while (csv.Read())
                {
                    // No-header: derive column keys from the first data row's field count.
                    if (headers == null)
                        headers = Enumerable.Range(0, csv.Parser.Count).Select(i => $"Column{i}").ToArray();

                    // Stream past the rows that belong to previous chunks without materializing them —
                    // this is the core streaming pattern that avoids loading the whole file per chunk.
                    if (dataIndex >= skip)
                    {
                        var row = new SourceRow { RowNumber = baseRowNumber + dataIndex };
                        for (var i = 0; i < headers.Length; i++)
                            row.Values[headers[i]] = i < csv.Parser.Count ? csv.GetField(i) ?? "" : "";
                        rows.Add(row);

                        if (rows.Count >= take) break;
                    }

                    dataIndex++;
                }
            }

            return rows;
        }

        /// <summary>
        /// Creates and configures a <see cref="CsvReader"/> over the given bytes.
        /// <para>
        /// Noteworthy configuration choices:
        /// <list type="bullet">
        ///   <item><description><c>MissingFieldFound = null</c> — silently returns empty string for absent columns instead of throwing.</description></item>
        ///   <item><description><c>BadDataFound = null</c> — swallows malformed rows; the caller receives an empty field rather than an exception.</description></item>
        ///   <item><description><c>detectEncodingFromByteOrderMarks: true</c> — handles UTF-8 BOM files exported from Excel.</description></item>
        /// </list>
        /// </para>
        /// The returned reader owns the underlying <see cref="MemoryStream"/> and <see cref="StreamReader"/>;
        /// dispose the reader to release them.
        /// </summary>
        private static CsvReader OpenReader(byte[] fileBytes, bool hasHeaderRow, string delimiter)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = hasHeaderRow,
                Delimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,
                BadDataFound = null
            };

            var stream = new MemoryStream(fileBytes);
            var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            // CsvReader disposes the underlying reader/stream when leaveOpen defaults to false.
            return new CsvReader(reader, config);
        }
    }
}
