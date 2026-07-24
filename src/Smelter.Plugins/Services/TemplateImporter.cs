using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>Outcome of an import: the (created or updated) template id and any non-fatal warnings.</summary>
    public class ImportResult
    {
        /// <summary>GUID of the <c>sanic_tbl_smlt_importtemplate</c> record that was created or updated.</summary>
        public Guid TemplateId { get; set; }

        /// <summary>
        /// Non-fatal warnings accumulated during import (e.g., target tables that don't exist in this environment).
        /// An empty list means the import completed cleanly.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Recreates a template tree from a <see cref="TemplateExportDto"/> in this environment. Matches by name
    /// (existing → replace mappings; new → create), and remaps each relationship's parentRef/childRef to the
    /// freshly created Table Mapping GUIDs of THIS environment.
    /// </summary>
    public interface ITemplateImporter
    {
        ImportResult Import(TemplateExportDto dto);
    }

    /// <inheritdoc />
    public class TemplateImporter : ITemplateImporter
    {
        private readonly IOrganizationService _service;
        private readonly Func<string, bool> _entityExists;

        /// <summary>
        /// Initializes a new instance of <see cref="TemplateImporter"/>.
        /// </summary>
        /// <param name="service">Organization service used to create/update/delete records (SYSTEM context).</param>
        /// <param name="entityExists">
        /// Optional override for the entity-existence check, used in unit tests to avoid
        /// <c>RetrieveEntityRequest</c> calls against a live org. Defaults to a
        /// <c>RetrieveEntityRequest</c>-based probe when <c>null</c>.
        /// </param>
        public TemplateImporter(IOrganizationService service, Func<string, bool> entityExists = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _entityExists = entityExists ?? DefaultEntityExists;
        }

        /// <summary>
        /// Recreates the full template tree described by <paramref name="dto"/> in this Dataverse environment.
        /// <para>
        /// If a template with the same name already exists it is updated in-place and all its
        /// existing mappings are deleted before the new ones are created — a full replace strategy
        /// that avoids orphaned or duplicate mapping records.
        /// </para>
        /// <para>
        /// Relationship <c>ParentRef</c>/<c>ChildRef</c> slugs are remapped to the freshly-created
        /// Table Mapping GUIDs of THIS environment — the GUIDs from the source environment are
        /// never written.
        /// </para>
        /// </summary>
        /// <param name="dto">Validated DTO produced by <see cref="TemplateSerializer.Deserialize"/>.</param>
        /// <returns>An <see cref="ImportResult"/> with the template GUID and any non-fatal warnings.</returns>
        public ImportResult Import(TemplateExportDto dto)
        {
            var result = new ImportResult();

            var templateId = FindTemplateByName(dto.Template.Name);
            var template = new Entity(ImportContextLoader.ImportTemplateTable)
            {
                ["sanic_smlt_it_name"] = dto.Template.Name,
                ["sanic_smlt_it_sourcetype"] = new OptionSetValue(dto.Template.SourceType),
                ["sanic_smlt_it_hasheaderrow"] = dto.Template.HasHeaderRow,
                ["sanic_smlt_it_delimiter"] = dto.Template.Delimiter,
                ["sanic_smlt_it_chunksize"] = dto.Template.ChunkSize
            };

            if (templateId == Guid.Empty)
            {
                templateId = _service.Create(template);
            }
            else
            {
                template.Id = templateId;
                _service.Update(template);
                DeleteExistingMappings(templateId);
            }
            result.TemplateId = templateId;

            // Map each DTO Ref (slug from the source env) to the new GUID created in THIS environment.
            // Relationships use these GUIDs to link parent/child table mappings correctly.
            var refToGuid = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            // Track which target tables have already been checked for existence so we don't issue
            // a RetrieveEntityRequest for the same logical name more than once per import.
            var checkedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tm in dto.TableMappings ?? new List<TableMappingDto>())
            {
                if (!string.IsNullOrEmpty(tm.TargetTable) && checkedTables.Add(tm.TargetTable) && !_entityExists(tm.TargetTable))
                    result.Warnings.Add($"Target table '{tm.TargetTable}' does not exist in this environment. " +
                        "Create the table and its alternate key before importing data.");

                var tmEntity = new Entity(ImportContextLoader.TableMappingTable)
                {
                    ["sanic_smlt_tm_importtemplateid"] = new EntityReference(ImportContextLoader.ImportTemplateTable, templateId),
                    ["sanic_smlt_tm_name"] = tm.Name,
                    ["sanic_smlt_tm_targettable"] = tm.TargetTable,
                    ["sanic_smlt_tm_processingorder"] = tm.ProcessingOrder,
                    ["sanic_smlt_tm_dedupcolumns"] = tm.DedupColumns,
                    ["sanic_smlt_tm_isjunctiontable"] = tm.IsJunctionTable,
                    ["sanic_smlt_tm_createifnotexists"] = tm.CreateIfNotExists,
                    ["sanic_smlt_tm_updateifexists"] = tm.UpdateIfExists
                };
                var tmId = _service.Create(tmEntity);
                if (!string.IsNullOrEmpty(tm.Ref)) refToGuid[tm.Ref] = tmId;

                foreach (var cm in tm.ColumnMappings ?? new List<ColumnMappingDto>())
                {
                    _service.Create(new Entity(ImportContextLoader.ColumnMappingTable)
                    {
                        ["sanic_smlt_cm_tablemappingid"] = new EntityReference(ImportContextLoader.TableMappingTable, tmId),
                        ["sanic_smlt_cm_name"] = cm.Name,
                        ["sanic_smlt_cm_sourcecolumn"] = cm.SourceColumn,
                        ["sanic_smlt_cm_targetcolumn"] = cm.TargetColumn,
                        ["sanic_smlt_cm_datatype"] = new OptionSetValue(cm.DataType),
                        ["sanic_smlt_cm_isrequired"] = cm.IsRequired,
                        ["sanic_smlt_cm_defaultvalue"] = cm.DefaultValue,
                        ["sanic_smlt_cm_lookuptable"] = cm.LookupTable,
                        ["sanic_smlt_cm_lookupmatchcolumn"] = cm.LookupMatchColumn,
                        ["sanic_smlt_cm_optionsetmapping"] = cm.OptionSetMapping,
                        ["sanic_smlt_cm_transformexpression"] = cm.TransformExpression
                    });
                }
            }

            foreach (var rel in dto.RelationshipMappings ?? new List<RelationshipMappingDto>())
            {
                var relEntity = new Entity(ImportContextLoader.RelationshipMappingTable)
                {
                    ["sanic_smlt_rm_importtemplateid"] = new EntityReference(ImportContextLoader.ImportTemplateTable, templateId),
                    ["sanic_smlt_rm_name"] = rel.Name,
                    ["sanic_smlt_rm_relationshiptype"] = new OptionSetValue(rel.RelationshipType),
                    ["sanic_smlt_rm_lookupcolumnonchild"] = rel.LookupColumnOnChild,
                    ["sanic_smlt_rm_parentdedupcolumns"] = rel.ParentDedupColumns
                };
                if (refToGuid.TryGetValue(rel.ParentRef ?? "", out var pGuid))
                    relEntity["sanic_smlt_rm_parenttablemappingid"] = new EntityReference(ImportContextLoader.TableMappingTable, pGuid);
                if (refToGuid.TryGetValue(rel.ChildRef ?? "", out var cGuid))
                    relEntity["sanic_smlt_rm_childtablemappingid"] = new EntityReference(ImportContextLoader.TableMappingTable, cGuid);

                _service.Create(relEntity);
            }

            return result;
        }

        /// <summary>
        /// Looks up an existing Import Template by name. Returns <see cref="Guid.Empty"/> when no
        /// match is found, which signals the caller to create a new record rather than update.
        /// </summary>
        private Guid FindTemplateByName(string name)
        {
            var query = new QueryExpression(ImportContextLoader.ImportTemplateTable)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("sanic_smlt_it_name", ConditionOperator.Equal, name) }
                }
            };
            return _service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id ?? Guid.Empty;
        }

        /// <summary>
        /// Deletes all mapping records owned by an existing template before re-importing them.
        /// Deletion order matters due to FK constraints: relationships are removed first (they
        /// reference table mappings), then column mappings (they reference table mappings), and
        /// finally the table mappings themselves.
        /// </summary>
        private void DeleteExistingMappings(Guid templateId)
        {
            // Relationships first (they reference table mappings), then column mappings, then table mappings.
            foreach (var rel in RetrieveByFk(ImportContextLoader.RelationshipMappingTable, "sanic_smlt_rm_importtemplateid", templateId))
                _service.Delete(ImportContextLoader.RelationshipMappingTable, rel.Id);

            foreach (var tm in RetrieveByFk(ImportContextLoader.TableMappingTable, "sanic_smlt_tm_importtemplateid", templateId))
            {
                foreach (var cm in RetrieveByFk(ImportContextLoader.ColumnMappingTable, "sanic_smlt_cm_tablemappingid", tm.Id))
                    _service.Delete(ImportContextLoader.ColumnMappingTable, cm.Id);
                _service.Delete(ImportContextLoader.TableMappingTable, tm.Id);
            }
        }

        /// <summary>Retrieves all records of <paramref name="entity"/> where <paramref name="fkColumn"/> equals <paramref name="value"/>, returning only the record ID (no extra columns needed for deletion).</summary>
        private List<Entity> RetrieveByFk(string entity, string fkColumn, Guid value)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression(fkColumn, ConditionOperator.Equal, value) }
                }
            };
            return _service.RetrieveMultiple(query).Entities.ToList();
        }

        /// <summary>
        /// Checks whether a Dataverse table (entity) exists in the current environment by issuing
        /// a <c>RetrieveEntityRequest</c>. Returns <c>false</c> on any exception (e.g., table not
        /// found), so the caller can emit a warning instead of letting the import fail.
        /// </summary>
        private bool DefaultEntityExists(string logicalName)
        {
            if (string.IsNullOrEmpty(logicalName)) return false;
            try
            {
                _service.Execute(new RetrieveEntityRequest
                {
                    LogicalName = logicalName,
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = false
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
