using System.Collections.Generic;

namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Represents a single parsed row from the source CSV/Excel file.
    /// Column values are stored as raw strings keyed by header name.
    /// </summary>
    public class SourceRow
    {
        /// <summary>
        /// 1-based row number from the source file (for error reporting).
        /// </summary>
        public int RowNumber { get; set; }

        /// <summary>
        /// Raw column values keyed by header name.
        /// Keys are case-sensitive and match the source file headers exactly.
        /// </summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }
}
