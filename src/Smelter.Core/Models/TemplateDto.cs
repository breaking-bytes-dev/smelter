using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Portable representation of an Import Template and its full mapping tree, for export/import
    /// between environments. Relationships reference table mappings by a stable <c>Ref</c> (slug),
    /// NOT by GUID — GUIDs don't survive across environments.
    /// </summary>
    [DataContract]
    public class TemplateExportDto
    {
        [DataMember(Order = 1)] public string FormatVersion { get; set; }
        [DataMember(Order = 2)] public TemplateInfoDto Template { get; set; }
        [DataMember(Order = 3)] public List<TableMappingDto> TableMappings { get; set; } = new List<TableMappingDto>();
        [DataMember(Order = 4)] public List<RelationshipMappingDto> RelationshipMappings { get; set; } = new List<RelationshipMappingDto>();
    }

    /// <summary>Import Template header fields (no mapping tree — those are in the sibling lists).</summary>
    [DataContract]
    public class TemplateInfoDto
    {
        /// <summary>Display name of the template, used as the match key on import.</summary>
        [DataMember(Order = 1)] public string Name { get; set; }

        /// <summary>Source file type: 0 = CSV, 1 = Excel.</summary>
        [DataMember(Order = 2)] public int SourceType { get; set; }

        /// <summary>Whether the first row of the source file contains column headers.</summary>
        [DataMember(Order = 3)] public bool HasHeaderRow { get; set; }

        /// <summary>CSV delimiter character (e.g., <c>","</c> or <c>";"</c>).</summary>
        [DataMember(Order = 4)] public string Delimiter { get; set; }

        /// <summary>Number of source rows to process per <c>sanic_ProcessChunk</c> invocation. 0 = use system default.</summary>
        [DataMember(Order = 5)] public int ChunkSize { get; set; }
    }

    /// <summary>Portable representation of a Table Mapping and its child column mappings.</summary>
    [DataContract]
    public class TableMappingDto
    {
        /// <summary>Stable reference (slug) that replaces the GUID as the link target for relationships.</summary>
        [DataMember(Order = 1)] public string Ref { get; set; }

        /// <summary>Display name of the table mapping.</summary>
        [DataMember(Order = 2)] public string Name { get; set; }

        /// <summary>Logical name of the target Dataverse table (e.g., <c>"account"</c>).</summary>
        [DataMember(Order = 3)] public string TargetTable { get; set; }

        /// <summary>Processing order used as a hint for DAG resolution. Lower values are processed first.</summary>
        [DataMember(Order = 4)] public int ProcessingOrder { get; set; }

        /// <summary>Comma-separated target column logical names that form the alternate key for upsert deduplication.</summary>
        [DataMember(Order = 5)] public string DedupColumns { get; set; }

        // Order=6 fue IsJunctionTable (feature N:M nunca implementada, campo eliminado en 1.3.0.0).
        // DataContractJsonSerializer ignora el member al deserializar JSONs viejos que lo traigan.

        /// <summary>When <c>true</c>, a new Dataverse record is created if no existing record matches the alternate key.</summary>
        [DataMember(Order = 7)] public bool CreateIfNotExists { get; set; }

        /// <summary>When <c>true</c>, an existing record matched by alternate key is updated.</summary>
        [DataMember(Order = 8)] public bool UpdateIfExists { get; set; }

        /// <summary>Column mappings belonging to this table mapping.</summary>
        [DataMember(Order = 9)] public List<ColumnMappingDto> ColumnMappings { get; set; } = new List<ColumnMappingDto>();
    }

    /// <summary>Portable representation of a Column Mapping.</summary>
    [DataContract]
    public class ColumnMappingDto
    {
        /// <summary>Display name of the column mapping.</summary>
        [DataMember(Order = 1)] public string Name { get; set; }

        /// <summary>Source column header name. Null when value is derived from <see cref="TransformExpression"/>.</summary>
        [DataMember(Order = 2)] public string SourceColumn { get; set; }

        /// <summary>Target Dataverse column logical name.</summary>
        [DataMember(Order = 3)] public string TargetColumn { get; set; }

        /// <summary>Target data type as an integer matching the <c>sanic_smlt_cm_datatype</c> OptionSet values.</summary>
        [DataMember(Order = 4)] public int DataType { get; set; }

        /// <summary>When <c>true</c>, an empty value after default substitution causes the row to fail.</summary>
        [DataMember(Order = 5)] public bool IsRequired { get; set; }

        /// <summary>Fallback value used when the source cell is blank.</summary>
        [DataMember(Order = 6)] public string DefaultValue { get; set; }

        /// <summary>For Lookup columns: logical name of the target Dataverse table.</summary>
        [DataMember(Order = 7)] public string LookupTable { get; set; }

        /// <summary>For Lookup columns: alternate-key column to match in the lookup table when the value is not a GUID.</summary>
        [DataMember(Order = 8)] public string LookupMatchColumn { get; set; }

        /// <summary>For OptionSet columns: JSON mapping from source string values to OptionSet integer values (e.g., <c>{"Active":1,"Inactive":0}</c>).</summary>
        [DataMember(Order = 9)] public string OptionSetMapping { get; set; }

        /// <summary>DSL transform expression. When set, takes priority over the raw source column value.</summary>
        [DataMember(Order = 10)] public string TransformExpression { get; set; }
    }

    /// <summary>Portable representation of a Relationship Mapping.</summary>
    [DataContract]
    public class RelationshipMappingDto
    {
        /// <summary>Display name of the relationship mapping.</summary>
        [DataMember(Order = 1)] public string Name { get; set; }

        /// <summary>Ref of the parent table mapping (matches a <see cref="TableMappingDto.Ref"/>).</summary>
        [DataMember(Order = 2)] public string ParentRef { get; set; }

        /// <summary>Ref of the child table mapping (matches a <see cref="TableMappingDto.Ref"/>).</summary>
        [DataMember(Order = 3)] public string ChildRef { get; set; }

        /// <summary>Relationship type: 0 = OneToMany, 1 = ManyToMany.</summary>
        [DataMember(Order = 4)] public int RelationshipType { get; set; }

        /// <summary>For 1:N relationships: logical name of the lookup column on the child entity.</summary>
        [DataMember(Order = 5)] public string LookupColumnOnChild { get; set; }

        /// <summary>Comma-separated source column names used to identify the parent record for lookup resolution.</summary>
        [DataMember(Order = 6)] public string ParentDedupColumns { get; set; }
    }
}
