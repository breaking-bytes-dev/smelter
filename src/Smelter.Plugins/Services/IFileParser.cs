using System.Collections.Generic;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Contract for file parsers. Implementations handle specific formats (CSV, Excel).
    /// Each parser reads a source file and returns a flat list of SourceRows with raw string values.
    /// </summary>
    public interface IFileParser
    {
        /// <summary>
        /// Parse the source file bytes into a list of SourceRows.
        /// </summary>
        /// <param name="fileBytes">Raw file content.</param>
        /// <param name="hasHeaderRow">Whether the first row contains headers.</param>
        /// <param name="options">Format-specific options (delimiter for CSV, sheet name for Excel).</param>
        /// <returns>List of parsed rows with raw string values keyed by header name.</returns>
        List<SourceRow> Parse(byte[] fileBytes, bool hasHeaderRow, FileParseOptions options);
    }

    /// <summary>
    /// Format-specific parsing options.
    /// </summary>
    public class FileParseOptions
    {
        /// <summary>
        /// CSV delimiter character. Default: ","
        /// </summary>
        public string Delimiter { get; set; } = ",";
    }
}
