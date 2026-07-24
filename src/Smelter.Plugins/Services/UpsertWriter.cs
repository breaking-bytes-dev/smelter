using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Writes TargetRecords to Dataverse. Replaces BatchProcessor + DedupService:
    /// - When the table mapping has dedup columns (an alternate key), it issues idempotent UpsertRequests
    ///   keyed by those columns — no in-memory dedup, no preload.
    /// - Parent lookups arrive as EntityReference keyed by alternate key (resolved by Dataverse, no GUID).
    /// - Without an alternate key it degrades to Create/Update (NOT idempotent) and traces a warning.
    /// </summary>
    public interface IUpsertWriter
    {
        IList<ProcessingResult> Write(IList<TargetRecord> records, TableMappingConfig tableMapping, bool isDryRun = false);
    }

    /// <inheritdoc />
    public class UpsertWriter : IUpsertWriter
    {
        /// <summary>
        /// Maximum number of records per <c>ExecuteMultipleRequest</c> call.
        /// Dataverse caps ExecuteMultiple at 1000 requests; keeping this at 1000 maximises
        /// throughput while staying within the documented limit.
        /// </summary>
        public const int BatchSize = 1000;

        private readonly IOrganizationService _service;
        private readonly ITracingService _tracing;

        /// <summary>
        /// Initializes a new instance of <see cref="UpsertWriter"/>.
        /// </summary>
        /// <param name="service">Organization service used to execute batched requests (SYSTEM context).</param>
        /// <param name="tracing">Optional tracing service; pass <c>null</c> in unit tests.</param>
        public UpsertWriter(IOrganizationService service, ITracingService tracing = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _tracing = tracing;
        }

        /// <inheritdoc/>
        public IList<ProcessingResult> Write(IList<TargetRecord> records, TableMappingConfig tableMapping, bool isDryRun = false)
        {
            if (records == null || records.Count == 0)
                return new List<ProcessingResult>();

            var keyColumns = tableMapping.GetDedupColumnNames();
            var hasAlternateKey = keyColumns.Length > 0;

            if (!hasAlternateKey)
                _tracing?.Trace($"UpsertWriter: table '{tableMapping.TargetTable}' has no alternate key columns; " +
                                "falling back to Create/Update (NOT idempotent — reprocessing may duplicate).");

            if (isDryRun)
                return Simulate(records, tableMapping.TargetTable);

            var results = new List<ProcessingResult>();
            foreach (var batch in Chunk(records, BatchSize))
            {
                results.AddRange(hasAlternateKey
                    ? ExecuteBatch(batch, tableMapping.TargetTable, BuildUpsert(keyColumns), isUpsert: true)
                    : ExecuteBatch(batch, tableMapping.TargetTable, BuildCreateOrUpdate(), isUpsert: false));
            }
            return results;
        }

        /// <summary>
        /// Builds a request factory for <see cref="UpsertRequest"/>s keyed by the table's alternate
        /// key columns. Alternate-key values are copied from <see cref="TargetRecord.Attributes"/>
        /// into <see cref="Entity.KeyAttributes"/> so Dataverse can match the existing record.
        /// </summary>
        private static Func<TargetRecord, OrganizationRequest> BuildUpsert(string[] keyColumns)
        {
            return record =>
            {
                var entity = new Entity(record.TableLogicalName);
                foreach (var attr in record.Attributes)
                    entity[attr.Key] = attr.Value;

                // Populate KeyAttributes from the already-coerced attribute values so Dataverse
                // performs the match-or-create logic server-side — no pre-query for existing GUIDs.
                foreach (var keyColumn in keyColumns)
                    if (record.Attributes.TryGetValue(keyColumn, out var value) && value != null)
                        entity.KeyAttributes[keyColumn] = value;

                return new UpsertRequest { Target = entity };
            };
        }

        /// <summary>
        /// Builds a request factory that issues <see cref="CreateRequest"/> or
        /// <see cref="UpdateRequest"/> depending on whether <see cref="TargetRecord.IsUpdate"/> is set.
        /// Used as the non-idempotent fallback when the table has no alternate key configured.
        /// </summary>
        private static Func<TargetRecord, OrganizationRequest> BuildCreateOrUpdate()
        {
            return record =>
            {
                var entity = record.IsUpdate
                    ? new Entity(record.TableLogicalName, record.ExistingRecordId.Value)
                    : new Entity(record.TableLogicalName);
                foreach (var attr in record.Attributes)
                    entity[attr.Key] = attr.Value;

                return record.IsUpdate
                    ? (OrganizationRequest)new UpdateRequest { Target = entity }
                    : new CreateRequest { Target = entity };
            };
        }

        /// <summary>
        /// Sends a list of records as a single <c>ExecuteMultipleRequest</c> with
        /// <c>ContinueOnError = true</c> and maps the individual responses back to
        /// <see cref="ProcessingResult"/> entries by request index.
        /// </summary>
        private List<ProcessingResult> ExecuteBatch(List<TargetRecord> batch, string table,
            Func<TargetRecord, OrganizationRequest> build, bool isUpsert)
        {
            var requests = new OrganizationRequestCollection();
            foreach (var record in batch)
                requests.Add(build(record));

            var response = (ExecuteMultipleResponse)_service.Execute(new ExecuteMultipleRequest
            {
                Requests = requests,
                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true }
            });

            var byIndex = response?.Responses?.ToDictionary(r => r.RequestIndex)
                          ?? new Dictionary<int, ExecuteMultipleResponseItem>();

            var results = new List<ProcessingResult>();
            for (var i = 0; i < batch.Count; i++)
            {
                var record = batch[i];
                byIndex.TryGetValue(i, out var item);

                if (item?.Fault != null)
                {
                    results.Add(ProcessingResult.Failure(record.SourceRowNumber, table, item.Fault.Message));
                    continue;
                }

                results.Add(ResolveSuccess(record, table, item, isUpsert));
            }
            return results;
        }

        /// <summary>
        /// Derives a success <see cref="ProcessingResult"/> from the response item. For upserts,
        /// <see cref="UpsertResponse.RecordCreated"/> distinguishes a new create from an update of
        /// an existing record. For plain Create/Update, the outcome is inferred from
        /// <see cref="TargetRecord.IsUpdate"/>.
        /// </summary>
        private static ProcessingResult ResolveSuccess(TargetRecord record, string table,
            ExecuteMultipleResponseItem item, bool isUpsert)
        {
            if (isUpsert)
            {
                var upsert = item?.Response as UpsertResponse;
                var isUpdate = upsert != null && !upsert.RecordCreated;
                var id = upsert?.Target?.Id ?? record.ExistingRecordId ?? Guid.Empty;
                return ProcessingResult.Success(record.SourceRowNumber, table, id, isUpdate);
            }

            if (record.IsUpdate)
                return ProcessingResult.Success(record.SourceRowNumber, table, record.ExistingRecordId.Value, isUpdate: true);

            var created = (item?.Response as CreateResponse)?.id ?? Guid.Empty;
            return ProcessingResult.Success(record.SourceRowNumber, table, created, isUpdate: false);
        }

        /// <summary>
        /// Dry-run path: returns a synthetic success result for every record without touching
        /// Dataverse. New records receive a randomly generated GUID so callers have a non-null ID.
        /// </summary>
        private static List<ProcessingResult> Simulate(IList<TargetRecord> records, string table)
        {
            return records
                .Select(r => ProcessingResult.Success(r.SourceRowNumber, table,
                    r.ExistingRecordId ?? Guid.NewGuid(), r.IsUpdate))
                .ToList();
        }

        /// <summary>Splits a list into consecutive chunks of at most <paramref name="size"/> elements.</summary>
        private static IEnumerable<List<T>> Chunk<T>(IList<T> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
                yield return source.Skip(i).Take(size).ToList();
        }
    }
}
