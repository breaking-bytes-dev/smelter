using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Reads an Import Template and its full mapping tree from Dataverse and builds a portable
    /// <see cref="TemplateExportDto"/>, assigning each table mapping a stable <c>Ref</c> (slug) so that
    /// relationships can be expressed by ref instead of GUID.
    /// </summary>
    public interface ITemplateExporter
    {
        TemplateExportDto Export(Guid templateId);
    }

    /// <inheritdoc />
    public class TemplateExporter : ITemplateExporter
    {
        /// <summary>
        /// Format version written into every exported JSON payload.
        /// Consumers check only the major version; increment the major when making breaking changes
        /// to the DTO structure, increment the minor for additive changes.
        /// </summary>
        public const string FormatVersion = "1.0";

        private readonly IOrganizationService _service;

        /// <summary>
        /// Initializes a new instance of <see cref="TemplateExporter"/>.
        /// </summary>
        /// <param name="service">Organization service used to read template data (SYSTEM context).</param>
        public TemplateExporter(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Reads the Import Template and its full mapping tree from Dataverse and builds a
        /// <see cref="TemplateExportDto"/> suitable for cross-environment transport.
        /// </summary>
        /// <param name="templateId">GUID of the <c>sanic_tbl_smlt_importtemplate</c> record.</param>
        /// <returns>A portable DTO with relationship references expressed as stable slugs (Refs) instead of GUIDs.</returns>
        public TemplateExportDto Export(Guid templateId)
        {
            var t = _service.Retrieve(ImportContextLoader.ImportTemplateTable, templateId, new ColumnSet(true));

            var dto = new TemplateExportDto
            {
                FormatVersion = FormatVersion,
                Template = new TemplateInfoDto
                {
                    Name = t.GetAttributeValue<string>("sanic_smlt_it_name"),
                    SourceType = t.GetAttributeValue<OptionSetValue>("sanic_smlt_it_sourcetype")?.Value ?? 0,
                    HasHeaderRow = t.GetAttributeValue<bool?>("sanic_smlt_it_hasheaderrow") ?? true,
                    Delimiter = t.GetAttributeValue<string>("sanic_smlt_it_delimiter") ?? ",",
                    ChunkSize = t.GetAttributeValue<int?>("sanic_smlt_it_chunksize") ?? 0
                }
            };

            // Build a GUID→Ref map as table mappings are created so relationships can reference
            // the same stable slug without a second pass over the DTO.
            var guidToRef = new Dictionary<Guid, string>();
            var usedRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var tableMappings = _service.RetrieveMultiple(
                ByTemplate(ImportContextLoader.TableMappingTable, "sanic_smlt_tm_importtemplateid", templateId))
                .Entities
                .OrderBy(tm => tm.GetAttributeValue<int?>("sanic_smlt_tm_processingorder") ?? 0)
                .ToList();

            foreach (var tm in tableMappings)
            {
                var name = tm.GetAttributeValue<string>("sanic_smlt_tm_name");
                var refSlug = UniqueRef(name, usedRefs);
                guidToRef[tm.Id] = refSlug;

                dto.TableMappings.Add(new TableMappingDto
                {
                    Ref = refSlug,
                    Name = name,
                    TargetTable = tm.GetAttributeValue<string>("sanic_smlt_tm_targettable"),
                    ProcessingOrder = tm.GetAttributeValue<int?>("sanic_smlt_tm_processingorder") ?? 0,
                    DedupColumns = tm.GetAttributeValue<string>("sanic_smlt_tm_dedupcolumns"),
                    CreateIfNotExists = tm.GetAttributeValue<bool?>("sanic_smlt_tm_createifnotexists") ?? true,
                    UpdateIfExists = tm.GetAttributeValue<bool?>("sanic_smlt_tm_updateifexists") ?? true,
                    InsertOnly = tm.GetAttributeValue<bool?>("sanic_smlt_tm_insertonly") ?? false,
                    ColumnMappings = LoadColumns(tm.Id)
                });
            }

            var rels = _service.RetrieveMultiple(
                ByTemplate(ImportContextLoader.RelationshipMappingTable, "sanic_smlt_rm_importtemplateid", templateId));

            foreach (var rel in rels.Entities)
            {
                var parentId = rel.GetAttributeValue<EntityReference>("sanic_smlt_rm_parenttablemappingid")?.Id ?? Guid.Empty;
                var childId = rel.GetAttributeValue<EntityReference>("sanic_smlt_rm_childtablemappingid")?.Id ?? Guid.Empty;

                dto.RelationshipMappings.Add(new RelationshipMappingDto
                {
                    Name = rel.GetAttributeValue<string>("sanic_smlt_rm_name"),
                    ParentRef = guidToRef.TryGetValue(parentId, out var pr) ? pr : null,
                    ChildRef = guidToRef.TryGetValue(childId, out var cr) ? cr : null,
                    RelationshipType = rel.GetAttributeValue<OptionSetValue>("sanic_smlt_rm_relationshiptype")?.Value ?? 0,
                    LookupColumnOnChild = rel.GetAttributeValue<string>("sanic_smlt_rm_lookupcolumnonchild"),
                    ParentDedupColumns = rel.GetAttributeValue<string>("sanic_smlt_rm_parentdedupcolumns")
                });
            }

            return dto;
        }

        /// <summary>Loads all column mappings for a table mapping and projects them to DTOs.</summary>
        private List<ColumnMappingDto> LoadColumns(Guid tableMappingId)
        {
            var query = ByTemplate(ImportContextLoader.ColumnMappingTable, "sanic_smlt_cm_tablemappingid", tableMappingId);
            return _service.RetrieveMultiple(query).Entities.Select(cm => new ColumnMappingDto
            {
                Name = cm.GetAttributeValue<string>("sanic_smlt_cm_name"),
                SourceColumn = cm.GetAttributeValue<string>("sanic_smlt_cm_sourcecolumn"),
                TargetColumn = cm.GetAttributeValue<string>("sanic_smlt_cm_targetcolumn"),
                DataType = cm.GetAttributeValue<OptionSetValue>("sanic_smlt_cm_datatype")?.Value ?? 0,
                IsRequired = cm.GetAttributeValue<bool?>("sanic_smlt_cm_isrequired") ?? false,
                DefaultValue = cm.GetAttributeValue<string>("sanic_smlt_cm_defaultvalue"),
                LookupTable = cm.GetAttributeValue<string>("sanic_smlt_cm_lookuptable"),
                LookupMatchColumn = cm.GetAttributeValue<string>("sanic_smlt_cm_lookupmatchcolumn"),
                OptionSetMapping = cm.GetAttributeValue<string>("sanic_smlt_cm_optionsetmapping"),
                TransformExpression = cm.GetAttributeValue<string>("sanic_smlt_cm_transformexpression")
            }).ToList();
        }

        /// <summary>Builds a QueryExpression that retrieves all rows of <paramref name="entity"/> where <paramref name="fkColumn"/> equals <paramref name="value"/>.</summary>
        private static QueryExpression ByTemplate(string entity, string fkColumn, Guid value)
        {
            return new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression(fkColumn, ConditionOperator.Equal, value) }
                }
            };
        }

        /// <summary>
        /// Generates a unique, URL-safe slug for a table mapping ref. The base slug is derived
        /// from the mapping name; a numeric suffix is appended when there are name collisions
        /// within the same export (e.g., two mappings both named "Contact" become "tm-contact"
        /// and "tm-contact-2").
        /// </summary>
        private static string UniqueRef(string name, HashSet<string> used)
        {
            var baseSlug = "tm-" + Slug(name);
            var candidate = baseSlug;
            var i = 2;
            while (used.Contains(candidate))
                candidate = baseSlug + "-" + i++;
            used.Add(candidate);
            return candidate;
        }

        /// <summary>
        /// Converts a display name to a lowercase URL-safe slug: non-alphanumeric characters
        /// become hyphens, consecutive hyphens are collapsed, and leading/trailing hyphens are trimmed.
        /// Returns <c>"x"</c> for null/blank/symbol-only inputs.
        /// </summary>
        private static string Slug(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "x";
            var sb = new StringBuilder();
            foreach (var c in s.Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            var slug = Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
            return slug.Length == 0 ? "x" : slug;
        }
    }
}
