namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Result of processing a single row for a target table (internal to the pipeline). Only <see cref="Failed"/>
    /// is persisted: the Import Job Log table records failures only, so since 1.0.10 the
    /// <c>sanic_smlt_ijl_status</c> choice has just <c>Failed (3)</c>; Created/Updated/Skipped feed the job counters.
    /// </summary>
    public enum ImportLogStatus
    {
        /// <summary>A new Dataverse record was created for this row.</summary>
        Created = 0,

        /// <summary>An existing Dataverse record was updated (matched by alternate key).</summary>
        Updated = 1,

        /// <summary>A matching record was found but left unchanged (<c>UpdateIfExists</c> is <c>false</c>).</summary>
        Skipped = 2,

        /// <summary>Processing of this row failed; an Import Job Log record is written with the error details.</summary>
        Failed = 3
    }
}
