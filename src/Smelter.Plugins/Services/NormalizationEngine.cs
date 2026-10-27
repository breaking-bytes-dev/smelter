using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Converts a batch of source rows into Dataverse records and writes them via the supplied
    /// <see cref="IUpsertWriter"/>. Processing order follows the dependency DAG so that parent
    /// tables are written before child tables, enabling same-chunk parent lookup resolution.
    /// </summary>
    public interface INormalizationEngine
    {
        /// <summary>
        /// Processes a batch of source rows against all table mappings in the context and writes
        /// the resulting records to Dataverse.
        /// </summary>
        /// <param name="context">The full import configuration for this job.</param>
        /// <param name="rows">Source rows to process in this chunk.</param>
        /// <param name="writer">Write target for the built <see cref="TargetRecord"/>s.</param>
        /// <returns>One <see cref="ProcessingResult"/> per (row, table) pair processed.</returns>
        IList<ProcessingResult> ProcessBatch(ImportContext context, IList<SourceRow> rows, IUpsertWriter writer);
    }

    /// <summary>
    /// Builds Dataverse records from source rows for a single chunk and writes them via an
    /// <see cref="IUpsertWriter"/>. Tables are processed in DAG order so a parent's alternate-key
    /// lookup resolves for children in the same chunk. Stateless across invocations — no timeout,
    /// no auto-continue, no in-memory dedup (idempotency comes from upsert by alternate key).
    /// </summary>
    public class NormalizationEngine : INormalizationEngine
    {
        private readonly ITracingService _tracingService;
        private readonly TypeCoercer _typeCoercer;
        private readonly TransformEngine _transformEngine;

        /// <summary>
        /// Initializes a new instance of <see cref="NormalizationEngine"/>.
        /// </summary>
        /// <param name="service">
        /// Organization service — accepted for signature stability and future use, but the engine
        /// itself does not issue Dataverse calls; writing is delegated to the <see cref="IUpsertWriter"/>.
        /// </param>
        /// <param name="tracingService">Optional tracing service; pass <c>null</c> in unit tests.</param>
        public NormalizationEngine(IOrganizationService service, ITracingService tracingService)
            : this(service, tracingService, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="NormalizationEngine"/> with a key source for the
        /// <c>HMAC</c> transform function.
        /// </summary>
        /// <param name="service">See the two-argument constructor.</param>
        /// <param name="tracingService">Optional tracing service; pass <c>null</c> in unit tests.</param>
        /// <param name="hmacKeyProvider">
        /// Resolves HMAC key bytes by name; <c>null</c> leaves <c>HMAC</c> unavailable (any template
        /// that uses it fails per row with a clear message).
        /// </param>
        public NormalizationEngine(IOrganizationService service, ITracingService tracingService, IHmacKeyProvider hmacKeyProvider)
        {
            // 'service' is accepted for signature stability; ProcessBatch writes via IUpsertWriter.
            if (service == null) throw new ArgumentNullException(nameof(service));
            _tracingService = tracingService;
            _typeCoercer = new TypeCoercer();
            _transformEngine = new TransformEngine(hmacKeyProvider);
        }

        public IList<ProcessingResult> ProcessBatch(ImportContext context, IList<SourceRow> rows, IUpsertWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            var results = new List<ProcessingResult>();
            var ordered = DependencyResolver.Resolve(context.TableMappings, context.RelationshipMappings);

            foreach (var tableMapping in ordered)
            {
                var built = BuildBatchRecords(context, tableMapping, rows);

                results.AddRange(built.Skipped);
                if (built.Records.Count > 0)
                    results.AddRange(writer.Write(built.Records, tableMapping, context.IsDryRun));
            }

            return results;
        }

        private class BatchBuildResult
        {
            public List<TargetRecord> Records { get; } = new List<TargetRecord>();
            public List<ProcessingResult> Skipped { get; } = new List<ProcessingResult>();
        }

        private BatchBuildResult BuildBatchRecords(ImportContext context, TableMappingConfig tableMapping, IList<SourceRow> rows)
        {
            var result = new BatchBuildResult();
            var parentRelations = context.RelationshipMappings
                .Where(r => r.ChildTableMappingId == tableMapping.Id && r.IsOneToMany)
                .ToList();

            foreach (var sourceRow in rows)
            {
                try
                {
                    var record = new TargetRecord
                    {
                        TableLogicalName = tableMapping.TargetTable,
                        SourceRowNumber = sourceRow.RowNumber
                    };

                    if (!ApplyColumnMappings(tableMapping, sourceRow, record, result))
                        continue; // required field missing — already logged as failure

                    ApplyParentLookups(context, parentRelations, sourceRow, record);
                    result.Records.Add(record);
                }
                catch (Exception ex)
                {
                    result.Skipped.Add(ProcessingResult.Failure(sourceRow.RowNumber, tableMapping.TargetTable,
                        ex.Message, ClassifyBuildException(ex)));
                }
            }

            return result;
        }

        /// <summary>
        /// Applies all column mappings from the table mapping configuration to the source row,
        /// populating <paramref name="record"/> with coerced Dataverse attribute values.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the record is valid and should be written; <c>false</c> when a required
        /// field is empty (a failure result is added to <paramref name="result"/> before returning).
        /// </returns>
        private bool ApplyColumnMappings(TableMappingConfig tableMapping, SourceRow sourceRow,
            TargetRecord record, BatchBuildResult result)
        {
            foreach (var colMapping in tableMapping.ColumnMappings)
            {
                var rawValue = GetRawValue(sourceRow, colMapping);

                // Fix #6: apply the configured default BEFORE the required-field check so that a
                // default value satisfies a required mapping when the source cell is blank.
                if (string.IsNullOrEmpty(rawValue) && !string.IsNullOrEmpty(colMapping.DefaultValue))
                    rawValue = colMapping.DefaultValue;

                if (colMapping.IsRequired && string.IsNullOrEmpty(rawValue))
                {
                    result.Skipped.Add(ProcessingResult.Failure(sourceRow.RowNumber, tableMapping.TargetTable,
                        $"Required field '{colMapping.TargetColumn}' is empty (Source: '{colMapping.SourceColumn}').",
                        ErrorCategory.ValidationError));
                    return false;
                }

                if (string.IsNullOrEmpty(rawValue)) continue;

                if (colMapping.DataType == ColumnDataType.OptionSet && !string.IsNullOrWhiteSpace(colMapping.OptionSetMapping))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        colMapping.OptionSetMapping,
                        $"\"{System.Text.RegularExpressions.Regex.Escape(rawValue)}\"\\s*:\\s*([0-9]+)");
                    if (match.Success) rawValue = match.Groups[1].Value;
                }

                object coerced;
                try
                {
                    coerced = _typeCoercer.Coerce(rawValue, colMapping.DataType);
                }
                catch (FormatException ex)
                {
                    // Mismo tipo de excepción (la clasificación TypeError de ClassifyBuildException
                    // depende de FormatException), pero con la COLUMNA en el mensaje: el TypeCoercer
                    // solo conoce el valor (que NO va en el mensaje: puede ser PII) y sin decir dónde no le sirve de
                    // nada al operador que tiene 40 columnas mapeadas (1.2.1.0).
                    throw new FormatException(
                        $"Column '{colMapping.TargetColumn}' (source '{colMapping.SourceColumn}'): {ex.Message}", ex);
                }
                if (coerced == null) continue;

                if (colMapping.DataType == ColumnDataType.Lookup && !string.IsNullOrEmpty(colMapping.LookupTable))
                {
                    if (coerced is Guid guidVal)
                        record.Attributes[colMapping.TargetColumn] = new EntityReference(colMapping.LookupTable, guidVal);
                    else if (Guid.TryParse(coerced.ToString(), out var parsedGuid))
                        record.Attributes[colMapping.TargetColumn] = new EntityReference(colMapping.LookupTable, parsedGuid);
                    else if (!string.IsNullOrEmpty(colMapping.LookupMatchColumn))
                        // #7 fix: resolve an external lookup by business key (alternate key), no GUID needed.
                        record.Attributes[colMapping.TargetColumn] =
                            new EntityReference(colMapping.LookupTable, colMapping.LookupMatchColumn, coerced.ToString());
                    else
                    {
                        result.Skipped.Add(ProcessingResult.Failure(sourceRow.RowNumber, tableMapping.TargetTable,
                            $"Lookup '{colMapping.TargetColumn}' value is not a GUID and no " +
                            "LookupMatchColumn is configured.", ErrorCategory.LookupUnresolved));
                        return false;
                    }
                }
                else
                {
                    record.Attributes[colMapping.TargetColumn] = coerced;
                }
            }

            return true;
        }

        /// <summary>
        /// Maps the exception type thrown during record build to the appropriate
        /// <see cref="ErrorCategory"/> for diagnostics in the Import Job Log.
        /// <list type="bullet">
        ///   <item><description><see cref="FormatException"/> → <see cref="ErrorCategory.TypeError"/> (thrown by <see cref="TypeCoercer"/>).</description></item>
        ///   <item><description><see cref="InvalidOperationException"/> → <see cref="ErrorCategory.TransformError"/> (thrown by <see cref="TransformEngine"/>).</description></item>
        ///   <item><description>All other exceptions → <see cref="ErrorCategory.SourceError"/> (malformed row or unexpected condition).</description></item>
        /// </list>
        /// </summary>
        private static ErrorCategory ClassifyBuildException(Exception ex)
        {
            if (ex is FormatException) return ErrorCategory.TypeError;            // TypeCoercer
            if (ex is InvalidOperationException) return ErrorCategory.TransformError; // TransformEngine
            return ErrorCategory.SourceError;
        }

        /// <summary>
        /// Resolves parent record references for one-to-many relationships and injects them as
        /// alternate-key <see cref="EntityReference"/>s into the child record's attributes.
        /// <para>
        /// Dataverse resolves the alternate-key reference at write time — no GUID lookup is
        /// needed here. The parent only needs to exist by the time <see cref="IUpsertWriter.Write"/>
        /// is called, which is guaranteed because tables are processed in DAG order.
        /// </para>
        /// </summary>
        private void ApplyParentLookups(ImportContext context, List<RelationshipMappingConfig> parentRelations,
            SourceRow sourceRow, TargetRecord record)
        {
            foreach (var rel in parentRelations)
            {
                var parent = context.TableMappings.FirstOrDefault(t => t.Id == rel.ParentTableMappingId);
                if (parent == null) continue;

                var parentKeyColumns = parent.GetDedupColumnNames();          // target alternate-key columns
                var sourceColumns = string.IsNullOrEmpty(rel.ParentDedupColumns)
                    ? Array.Empty<string>()
                    : rel.ParentDedupColumns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                if (parentKeyColumns.Length == 0 || sourceColumns.Length == 0)
                {
                    _tracingService?.Trace($"Row {sourceRow.RowNumber}: cannot resolve parent lookup " +
                        $"'{rel.LookupColumnOnChild}' — parent has no alternate key or no source columns mapped.");
                    continue;
                }

                var keyAttributes = new KeyAttributeCollection();
                for (var i = 0; i < parentKeyColumns.Length && i < sourceColumns.Length; i++)
                {
                    sourceRow.Values.TryGetValue(sourceColumns[i].Trim(), out var value);
                    keyAttributes[parentKeyColumns[i]] = value;
                }

                record.Attributes[rel.LookupColumnOnChild] = new EntityReference(parent.TargetTable, keyAttributes);
            }
        }

        /// <summary>
        /// Retrieves the raw string value for a column mapping, applying the transform expression
        /// when one is configured. Priority: transform expression &gt; direct source column lookup.
        /// Returns <c>null</c> when neither is available (e.g., a STATIC-only column with no expression).
        /// </summary>
        private string GetRawValue(SourceRow sourceRow, ColumnMappingConfig colMapping)
        {
            if (!string.IsNullOrWhiteSpace(colMapping.TransformExpression))
                return _transformEngine.Evaluate(colMapping.TransformExpression, sourceRow.Values);

            if (string.IsNullOrEmpty(colMapping.SourceColumn))
                return null;

            sourceRow.Values.TryGetValue(colMapping.SourceColumn, out var value);
            return value;
        }
    }
}
