namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Origin/stage of a row-level failure, for diagnostics in Import Job Log.
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary>Malformed source row / missing columns in the row.</summary>
        SourceError = 0,

        /// <summary>TransformEngine (DSL) failed: unknown function, syntax error.</summary>
        TransformError = 1,

        /// <summary>TypeCoercer could not convert the value to the target type.</summary>
        TypeError = 2,

        /// <summary>Required field empty / business validation.</summary>
        ValidationError = 3,

        /// <summary>A lookup (parent or external) could not be resolved.</summary>
        LookupUnresolved = 4,

        /// <summary>Dataverse rejected the write for data reasons (constraint, privilege, duplicate).</summary>
        TargetData = 5,

        /// <summary>Transient Dataverse fault (SQL deadlock/timeout/lock). Reintentable. Diferido en v1.</summary>
        Transient = 6
    }
}
