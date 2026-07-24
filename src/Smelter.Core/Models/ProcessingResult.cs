using System;

namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Result of processing a single source row for a specific target table.
    /// Used for logging to ImportJobLog and aggregating counters on ImportJob.
    /// </summary>
    public class ProcessingResult
    {
        /// <summary>
        /// Source file row number.
        /// </summary>
        public int RowNumber { get; set; }

        /// <summary>
        /// Target table logical name.
        /// </summary>
        public string TargetTable { get; set; }

        /// <summary>
        /// Outcome of the operation.
        /// </summary>
        public ImportLogStatus Status { get; set; }

        /// <summary>
        /// GUID of the created or updated record (null on failure/skip).
        /// </summary>
        public Guid? RecordId { get; set; }

        /// <summary>
        /// Error message (null on success).
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Origin/stage of the failure (null on success/skip). Used for diagnostics in Import Job Log.
        /// </summary>
        public ErrorCategory? Category { get; set; }

        /// <summary>
        /// Whether this result represents a successful operation.
        /// </summary>
        public bool IsSuccess => Status == ImportLogStatus.Created || Status == ImportLogStatus.Updated;

        /// <summary>
        /// Creates a success result. Sets <see cref="Status"/> to <see cref="ImportLogStatus.Created"/>
        /// or <see cref="ImportLogStatus.Updated"/> based on <paramref name="isUpdate"/>.
        /// </summary>
        public static ProcessingResult Success(int rowNumber, string targetTable, Guid recordId, bool isUpdate)
        {
            return new ProcessingResult
            {
                RowNumber = rowNumber,
                TargetTable = targetTable,
                Status = isUpdate ? ImportLogStatus.Updated : ImportLogStatus.Created,
                RecordId = recordId
            };
        }

        /// <summary>
        /// Creates a skipped result for a row where dedup found an existing record and
        /// <c>UpdateIfExists</c> is <c>false</c>. The existing record is untouched.
        /// </summary>
        public static ProcessingResult Skipped(int rowNumber, string targetTable, Guid existingId)
        {
            return new ProcessingResult
            {
                RowNumber = rowNumber,
                TargetTable = targetTable,
                Status = ImportLogStatus.Skipped,
                RecordId = existingId
            };
        }

        /// <summary>
        /// Creates a failure result. Defaults to <see cref="ErrorCategory.TargetData"/> when no
        /// explicit category is supplied (i.e., the error came from Dataverse, not the build phase).
        /// </summary>
        public static ProcessingResult Failure(int rowNumber, string targetTable, string errorMessage,
            ErrorCategory category = ErrorCategory.TargetData)
        {
            return new ProcessingResult
            {
                RowNumber = rowNumber,
                TargetTable = targetTable,
                Status = ImportLogStatus.Failed,
                ErrorMessage = errorMessage,
                Category = category
            };
        }
    }
}
