namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Maps to the OptionSet values on sanic_smlt_cm_datatype.
    /// Defines the target Dataverse column data type for a column mapping.
    /// </summary>
    public enum ColumnDataType
    {
        /// <summary>Plain text. Mapped to a Dataverse <c>string</c> attribute.</summary>
        String = 0,

        /// <summary>Whole number. Mapped to a Dataverse <c>int</c> attribute.</summary>
        Integer = 1,

        /// <summary>Fixed-point number. Mapped to a Dataverse <c>decimal</c> attribute.</summary>
        Decimal = 2,

        /// <summary>Date/time value. Supports ISO 8601 strings and Excel OADate serial numbers.</summary>
        DateTime = 3,

        /// <summary>Boolean. Accepts <c>true/false</c>, <c>1/0</c>, <c>yes/no</c> (case-insensitive).</summary>
        Boolean = 4,

        /// <summary>EntityReference. NormalizationEngine builds the reference from a GUID or an alternate key.</summary>
        Lookup = 5,

        /// <summary>OptionSet (picklist). Accepts an integer value or a source-to-integer mapping JSON.</summary>
        OptionSet = 6,

        /// <summary>Currency. Mapped to a Dataverse <c>Money</c> attribute.</summary>
        Money = 7,

        /// <summary>GUID. Mapped to a Dataverse <c>uniqueidentifier</c> attribute.</summary>
        Guid = 8
    }
}
