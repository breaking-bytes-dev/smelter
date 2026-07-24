using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    }

    /// <summary>
    /// Parses CSV files using CsvHelper. Handles custom delimiters, quoted fields, BOM, and header modes.
    /// </summary>
    public class CsvParserService : IFileParser, ICsvReader
    {
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
