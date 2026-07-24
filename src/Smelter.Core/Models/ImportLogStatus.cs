namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Maps to the OptionSet values on sanic_smlt_ijl_status.
    /// Result of processing a single row for a target table.
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
