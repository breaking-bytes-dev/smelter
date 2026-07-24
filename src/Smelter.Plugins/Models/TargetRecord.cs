using System;
using System.Collections.Generic;

namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Represents a record ready to be created or updated in Dataverse.
    /// Contains the target table, coerced attribute values, and source context for logging.
    /// </summary>
    public class TargetRecord
    {
        /// <summary>
        /// Logical name of the target Dataverse table.
        /// </summary>
        public string TableLogicalName { get; set; }

        /// <summary>
        /// Coerced attribute values keyed by target column logical name.
        /// Values are already converted to the correct CRM types (string, int, EntityReference, etc.).
        /// </summary>
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Source row number (for error reporting and logging).
        /// </summary>
        public int SourceRowNumber { get; set; }

        /// <summary>
        /// If this is an update (dedup match found), the existing record's ID.
        /// Null for new creates.
        /// </summary>
        public Guid? ExistingRecordId { get; set; }

        /// <summary>
        /// Whether this record should be created (false) or updated (true).
        /// </summary>
        public bool IsUpdate => ExistingRecordId.HasValue;
    }
}
