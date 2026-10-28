using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Loads template configuration from Dataverse into config objects.
    /// Crucially loads the Relationship Mappings that the old ImportProcessorPlugin never queried
    /// (bug #1) — without them the dependency DAG and parent→child lookups were inert.
    /// </summary>
    public interface IImportContextLoader
    {
        /// <summary>Full processing context for a job (template, table/column/relationship mappings, progress).</summary>
        ImportContext LoadContext(Guid importJobId);

        /// <summary>Just the template config — enough for PrepareImport to parse the file.</summary>
        ImportTemplateConfig LoadTemplateConfig(Guid templateId);

        /// <summary>All relationship mappings for a template (empty when none).</summary>
        List<RelationshipMappingConfig> LoadRelationshipMappings(Guid templateId);
    }

    /// <inheritdoc />
    public class ImportContextLoader : IImportContextLoader
    {
        /// <summary>Logical name of the Import Job table.</summary>
        public const string ImportJobTable = "sanic_tbl_smlt_importjob";

        /// <summary>Logical name of the Import Template table.</summary>
        public const string ImportTemplateTable = "sanic_tbl_smlt_importtemplate";

        /// <summary>Logical name of the Table Mapping table.</summary>
        public const string TableMappingTable = "sanic_tbl_smlt_tablemapping";

        /// <summary>Logical name of the Column Mapping table.</summary>
        public const string ColumnMappingTable = "sanic_tbl_smlt_columnmapping";

        /// <summary>Logical name of the Relationship Mapping table.</summary>
        public const string RelationshipMappingTable = "sanic_tbl_smlt_relationshipmapping";

        /// <summary>FK column on Relationship Mapping that points back to the Import Template.</summary>
        public const string TemplateIdColumn = "sanic_smlt_rm_importtemplateid";

        private readonly IOrganizationService _service;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportContextLoader"/>.
        /// </summary>
        /// <param name="service">Organization service used to retrieve configuration records (SYSTEM context).</param>
        public ImportContextLoader(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public ImportContext LoadContext(Guid importJobId)
        {
            var job = _service.Retrieve(ImportJobTable, importJobId, new ColumnSet(
                "sanic_smlt_ij_importtemplateid", "sanic_smlt_ij_isdryrun",
                "sanic_smlt_ij_processedrows", "sanic_smlt_ij_successcount",
                "sanic_smlt_ij_failedcount", "sanic_smlt_ij_skippedcount",
                "sanic_smlt_ij_processingbatch", "sanic_smlt_ij_status", "sanic_smlt_ij_bytecursor",
                "sanic_smlt_ij_starttime"));

            var templateRef = job.GetAttributeValue<EntityReference>("sanic_smlt_ij_importtemplateid");
            if (templateRef == null)
                throw new InvalidPluginExecutionException("Import Job has no template reference.");

            var context = new ImportContext
            {
                ImportJobId = importJobId,
                IsDryRun = job.GetAttributeValue<bool?>("sanic_smlt_ij_isdryrun") ?? false,
                InitialProcessedRows = job.GetAttributeValue<int?>("sanic_smlt_ij_processedrows") ?? 0,
                InitialSuccessCount = job.GetAttributeValue<int?>("sanic_smlt_ij_successcount") ?? 0,
                InitialFailedCount = job.GetAttributeValue<int?>("sanic_smlt_ij_failedcount") ?? 0,
                InitialSkippedCount = job.GetAttributeValue<int?>("sanic_smlt_ij_skippedcount") ?? 0,
                InitialProcessingBatch = job.GetAttributeValue<int?>("sanic_smlt_ij_processingbatch") ?? 0,
                InitialByteCursor = ToLong(job.GetAttributeValue<decimal?>("sanic_smlt_ij_bytecursor")),
                JobStartTime = job.GetAttributeValue<DateTime?>("sanic_smlt_ij_starttime"),
                JobStatus = job.GetAttributeValue<OptionSetValue>("sanic_smlt_ij_status")?.Value
                    ?? (int)ImportJobStatus.Queued,
                Template = LoadTemplateConfig(templateRef.Id)
            };

            context.TableMappings.AddRange(LoadTableMappings(templateRef.Id));
            context.RelationshipMappings.AddRange(LoadRelationshipMappings(templateRef.Id));
            return context;
        }

        // La columna es Decimal(0): un entero de hasta 10^11 (los whole number de Dataverse topan en 2^31).
        private static long? ToLong(decimal? value) => value.HasValue ? (long?)decimal.ToInt64(value.Value) : null;

        /// <inheritdoc/>
        public ImportTemplateConfig LoadTemplateConfig(Guid templateId)
        {
            // Receives a TEMPLATE id (LoadContext resolves it from the job). No job probe here:
            // probing importjob with a template id was the cause of "importjob ... Does Not Exist".
            var template = _service.Retrieve(ImportTemplateTable, templateId, new ColumnSet(true));
            return new ImportTemplateConfig
            {
                Id = template.Id,
                Name = template.GetAttributeValue<string>("sanic_smlt_it_name"),
                SourceType = template.GetAttributeValue<OptionSetValue>("sanic_smlt_it_sourcetype")?.Value ?? 0,
                HasHeaderRow = template.GetAttributeValue<bool?>("sanic_smlt_it_hasheaderrow") ?? true,
                Delimiter = template.GetAttributeValue<string>("sanic_smlt_it_delimiter") ?? ",",
                ChunkSize = template.GetAttributeValue<int?>("sanic_smlt_it_chunksize") ?? 0,
                // Default Active si el atributo viene sin poblar (registros legacy): el gate de
                // PrepareImport no debe bloquear templates anteriores a 1.3.0.0.
                Status = template.GetAttributeValue<OptionSetValue>("sanic_smlt_it_status")?.Value
                    ?? (int)ImportTemplateStatus.Active
            };
        }

        /// <inheritdoc/>
        public List<RelationshipMappingConfig> LoadRelationshipMappings(Guid templateId)
        {
            var query = new QueryExpression(RelationshipMappingTable)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression(TemplateIdColumn, ConditionOperator.Equal, templateId) }
                }
            };

            return _service.RetrieveMultiple(query).Entities.Select(MapRelationship).ToList();
        }

        /// <summary>
        /// Loads all table mappings for a template ordered by <c>ProcessingOrder</c> ascending.
        /// Column mappings are loaded eagerly for each table mapping to avoid N+1 round-trips by
        /// batching via one query per table.
        /// </summary>
        private List<TableMappingConfig> LoadTableMappings(Guid templateId)
        {
            var query = new QueryExpression(TableMappingTable)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("sanic_smlt_tm_importtemplateid", ConditionOperator.Equal, templateId) }
                },
                Orders = { new OrderExpression("sanic_smlt_tm_processingorder", OrderType.Ascending) }
            };

            var mappings = new List<TableMappingConfig>();
            foreach (var tm in _service.RetrieveMultiple(query).Entities)
            {
                var config = new TableMappingConfig
                {
                    Id = tm.Id,
                    Name = tm.GetAttributeValue<string>("sanic_smlt_tm_name"),
                    TargetTable = tm.GetAttributeValue<string>("sanic_smlt_tm_targettable"),
                    ProcessingOrder = tm.GetAttributeValue<int?>("sanic_smlt_tm_processingorder") ?? 0,
                    DedupColumns = tm.GetAttributeValue<string>("sanic_smlt_tm_dedupcolumns"),
                    CreateIfNotExists = tm.GetAttributeValue<bool?>("sanic_smlt_tm_createifnotexists") ?? true,
                    // Fallback true = default del schema desde 1.0.10 (antes No) y comportamiento histórico (el
                    // motor siempre actualizaba) para registros legacy sin el atributo poblado.
                    UpdateIfExists = tm.GetAttributeValue<bool?>("sanic_smlt_tm_updateifexists") ?? true,
                    // Sin valor (registros previos al flag) = false: el gate de idempotencia aplica.
                    InsertOnly = tm.GetAttributeValue<bool?>("sanic_smlt_tm_insertonly") ?? false
                };
                config.ColumnMappings.AddRange(LoadColumnMappings(tm.Id));
                mappings.Add(config);
            }
            return mappings;
        }

        /// <summary>Loads all column mappings belonging to a specific table mapping record.</summary>
        private List<ColumnMappingConfig> LoadColumnMappings(Guid tableMappingId)
        {
            var query = new QueryExpression(ColumnMappingTable)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("sanic_smlt_cm_tablemappingid", ConditionOperator.Equal, tableMappingId) }
                }
            };

            return _service.RetrieveMultiple(query).Entities.Select(cm => new ColumnMappingConfig
            {
                Id = cm.Id,
                Name = cm.GetAttributeValue<string>("sanic_smlt_cm_name"),
                SourceColumn = cm.GetAttributeValue<string>("sanic_smlt_cm_sourcecolumn"),
                TargetColumn = cm.GetAttributeValue<string>("sanic_smlt_cm_targetcolumn"),
                DataType = (ColumnDataType)(cm.GetAttributeValue<OptionSetValue>("sanic_smlt_cm_datatype")?.Value ?? 0),
                IsRequired = cm.GetAttributeValue<bool?>("sanic_smlt_cm_isrequired") ?? false,
                DefaultValue = cm.GetAttributeValue<string>("sanic_smlt_cm_defaultvalue"),
                LookupTable = cm.GetAttributeValue<string>("sanic_smlt_cm_lookuptable"),
                LookupMatchColumn = cm.GetAttributeValue<string>("sanic_smlt_cm_lookupmatchcolumn"),
                OptionSetMapping = cm.GetAttributeValue<string>("sanic_smlt_cm_optionsetmapping"),
                TransformExpression = cm.GetAttributeValue<string>("sanic_smlt_cm_transformexpression")
            }).ToList();
        }

        /// <summary>Maps a raw <see cref="Entity"/> from the Relationship Mapping table to a typed config object.</summary>
        private static RelationshipMappingConfig MapRelationship(Entity e)
        {
            return new RelationshipMappingConfig
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("sanic_smlt_rm_name"),
                ParentTableMappingId = e.GetAttributeValue<EntityReference>("sanic_smlt_rm_parenttablemappingid")?.Id ?? Guid.Empty,
                ChildTableMappingId = e.GetAttributeValue<EntityReference>("sanic_smlt_rm_childtablemappingid")?.Id ?? Guid.Empty,
                RelationshipType = e.GetAttributeValue<OptionSetValue>("sanic_smlt_rm_relationshiptype")?.Value ?? 0,
                LookupColumnOnChild = e.GetAttributeValue<string>("sanic_smlt_rm_lookupcolumnonchild"),
                ParentDedupColumns = e.GetAttributeValue<string>("sanic_smlt_rm_parentdedupcolumns")
            };
        }
    }
}
