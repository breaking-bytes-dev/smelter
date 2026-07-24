namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Maps to the OptionSet values on sanic_smlt_ij_status.
    /// Tracks the lifecycle of an import job.
    /// </summary>
    public enum ImportJobStatus
    {
        /// <summary>Job has been created and is waiting for the Cloud Flow to invoke <c>sanic_PrepareImport</c>.</summary>
        Queued = 0,

        /// <summary>PrepareImport has run successfully and ProcessChunk invocations are in progress.</summary>
        Processing = 1,

        /// <summary>All rows were processed with no failures.</summary>
        Completed = 2,

        /// <summary>All rows were attempted but one or more rows failed. Check Import Job Log for details.</summary>
        CompletedWithErrors = 3,

        /// <summary>The import pipeline itself failed (e.g., unhandled plugin exception). Distinct from row-level failures.</summary>
        Failed = 4
    }
}
