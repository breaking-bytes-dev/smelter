using System;
using System.Collections.Generic;

namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Hydrated configuration context loaded from the Import Template and its related mapping tables.
    /// This is the single source of truth for the processing pipeline — all config is loaded once
    /// at the start of processing and passed through every service.
    /// </summary>
    public class ImportContext
    {
        /// <summary>
        /// The Import Job ID being processed.
        /// </summary>
        public Guid ImportJobId { get; set; }

        /// <summary>
        /// If true, the job will simulate the execution without committing to Dataverse.
        /// </summary>
        public bool IsDryRun { get; set; }

        /// <summary>
        /// Estado del job (<see cref="ImportJobStatus"/>) al cargar el contexto. ProcessChunk solo corre
        /// sobre <see cref="ImportJobStatus.Processing"/> (lo deja PrepareImport). Default <c>Processing</c>
        /// para contextos armados a mano; el loader lo completa con el valor real (null → Queued, fail-closed).
        /// </summary>
        public int JobStatus { get; set; } = (int)ImportJobStatus.Processing;

        /// <summary>Cumulative processed-row count loaded from the job record at the start of this invocation.</summary>
        public int InitialProcessedRows { get; set; }

        /// <summary>Cumulative success count loaded from the job record at the start of this invocation.</summary>
        public int InitialSuccessCount { get; set; }

        /// <summary>Cumulative failure count loaded from the job record at the start of this invocation.</summary>
        public int InitialFailedCount { get; set; }

        /// <summary>Cumulative skip count loaded from the job record at the start of this invocation.</summary>
        public int InitialSkippedCount { get; set; }

        /// <summary>
        /// The CSV row offset (number of data rows already consumed) loaded from the job record.
        /// This is the <c>skip</c> value passed to <see cref="ICsvReader.ReadRange"/> for this chunk.
        /// </summary>
        public int InitialProcessingBatch { get; set; }

        /// <summary>
        /// The Import Template configuration.
        /// </summary>
        public ImportTemplateConfig Template { get; set; }

        /// <summary>
        /// All table mappings for this template, keyed by TableMapping ID.
        /// </summary>
        public List<TableMappingConfig> TableMappings { get; set; } = new List<TableMappingConfig>();

        /// <summary>
        /// All relationship mappings for this template.
        /// </summary>
        public List<RelationshipMappingConfig> RelationshipMappings { get; set; } = new List<RelationshipMappingConfig>();
    }

    /// <summary>
    /// Hydrated Import Template configuration.
    /// </summary>
    public class ImportTemplateConfig
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// 0 = CSV, 1 = Excel
        /// </summary>
        public int SourceType { get; set; }

        public bool HasHeaderRow { get; set; } = true;

        /// <summary>
        /// CSV delimiter character. Default: ","
        /// </summary>
        public string Delimiter { get; set; } = ",";

        /// <summary>
        /// Rows processed per ProcessChunk invocation. 0 = use the system default.
        /// Calibrated per template to the total cost-per-row (all target tables).
        /// </summary>
        public int ChunkSize { get; set; }

        /// <summary>
        /// Estado del template (<see cref="ImportTemplateStatus"/>). Solo <see cref="ImportTemplateStatus.Active"/>
        /// es ejecutable — PrepareImportHandler rechaza Draft/Archived (1.3.0.0). Default Active para
        /// no bloquear registros legacy sin el atributo poblado.
        /// </summary>
        public int Status { get; set; } = (int)ImportTemplateStatus.Active;
    }

    /// <summary>
    /// Hydrated Table Mapping configuration with its child column mappings.
    /// </summary>
    public class TableMappingConfig
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Logical name of the target Dataverse table (e.g., "account").
        /// </summary>
        public string TargetTable { get; set; }

        /// <summary>
        /// Processing order for DAG resolution. Lower numbers processed first.
        /// </summary>
        public int ProcessingOrder { get; set; }

        /// <summary>
        /// Comma-separated target column logical names used for deduplication.
        /// Null/empty means no dedup (always create).
        /// </summary>
        public string DedupColumns { get; set; }

        /// <summary>When <c>true</c>, a new record is created if the alternate-key match finds no existing row.</summary>
        public bool CreateIfNotExists { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, an existing record matched by alternate key is updated. When <c>false</c>,
        /// matched records are skipped (<see cref="ImportLogStatus.Skipped"/>). Default <c>true</c> —
        /// el comportamiento histórico del motor (siempre actualizaba) para registros sin el flag.
        /// </summary>
        public bool UpdateIfExists { get; set; } = true;

        /// <summary>
        /// Column mappings belonging to this table mapping.
        /// </summary>
        public List<ColumnMappingConfig> ColumnMappings { get; set; } = new List<ColumnMappingConfig>();

        /// <summary>
        /// Parsed dedup column names (split from DedupColumns).
        /// </summary>
        public string[] GetDedupColumnNames()
        {
            if (string.IsNullOrWhiteSpace(DedupColumns))
                return Array.Empty<string>();

            return DedupColumns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    /// <summary>
    /// Hydrated Column Mapping configuration.
    /// </summary>
    public class ColumnMappingConfig
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Source column name from CSV/Excel header.
        /// Can be null when using STATIC() transform.
        /// </summary>
        public string SourceColumn { get; set; }

        /// <summary>
        /// Target Dataverse column logical name.
        /// </summary>
        public string TargetColumn { get; set; }

        /// <summary>
        /// Target data type for coercion.
        /// </summary>
        public ColumnDataType DataType { get; set; }

        /// <summary>When <c>true</c>, an empty value after default substitution causes the row to fail with <see cref="ErrorCategory.ValidationError"/>.</summary>
        public bool IsRequired { get; set; }

        /// <summary>Fallback value used when the source cell is blank. Applied before the required-field check.</summary>
        public string DefaultValue { get; set; }

        /// <summary>
        /// For Lookup type: target entity logical name.
        /// </summary>
        public string LookupTable { get; set; }

        /// <summary>
        /// For Lookup type: column to match against in the lookup table.
        /// </summary>
        public string LookupMatchColumn { get; set; }

        /// <summary>
        /// For OptionSet type: JSON mapping from source values to OptionSet int values.
        /// Format: {"source_value": 123456}
        /// </summary>
        public string OptionSetMapping { get; set; }

        /// <summary>
        /// Transform DSL expression. When set, takes priority over raw source column value.
        /// </summary>
        public string TransformExpression { get; set; }
    }

    /// <summary>
    /// Hydrated Relationship Mapping configuration.
    /// </summary>
    public class RelationshipMappingConfig
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Table Mapping ID of the parent table.
        /// </summary>
        public Guid ParentTableMappingId { get; set; }

        /// <summary>
        /// Table Mapping ID of the child table.
        /// </summary>
        public Guid ChildTableMappingId { get; set; }

        /// <summary>
        /// 0 = OneToMany, 1 = ManyToMany
        /// </summary>
        public int RelationshipType { get; set; }

        /// <summary>
        /// For 1:N: lookup column logical name on the child entity.
        /// </summary>
        public string LookupColumnOnChild { get; set; }

        /// <summary>
        /// Comma-separated source column names that identify the parent record.
        /// Used to look up the parent's GUID in the dedup cache.
        /// </summary>
        public string ParentDedupColumns { get; set; }

        /// <summary>
        /// <c>true</c> when <see cref="RelationshipType"/> is 0 (one-to-many). Los mappings N:M
        /// (valor 1) no están soportados: el engine los filtra por esta propiedad.
        /// </summary>
        public bool IsOneToMany => RelationshipType == 0;
    }
}
