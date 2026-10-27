using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Serializes/deserializes the portable template tree to/from JSON, and validates it on import.
    /// Uses DataContractJsonSerializer because System.Web.Extensions (JavaScriptSerializer) is NOT on the
    /// Dataverse sandbox whitelist.
    /// </summary>
    public interface ITemplateSerializer
    {
        string Serialize(TemplateExportDto dto);
        /// <summary>Parse + validate. Throws TemplateFormatException on malformed/unsupported/invalid.</summary>
        TemplateExportDto Deserialize(string json);
    }

    /// <inheritdoc />
    public class TemplateSerializer : ITemplateSerializer
    {
        /// <summary>
        /// Highest major format version this serializer can deserialize. A payload whose major
        /// version exceeds this value is rejected to prevent silent data loss when new required
        /// fields are added in future versions.
        /// </summary>
        public const string SupportedFormatVersion = "1.1";

        /// <summary>
        /// Serializes a <see cref="TemplateExportDto"/> to a UTF-8 JSON string using
        /// <see cref="DataContractJsonSerializer"/>.
        /// </summary>
        /// <param name="dto">The populated export DTO to serialize.</param>
        /// <returns>JSON string suitable for storage or transmission.</returns>
        public string Serialize(TemplateExportDto dto)
        {
            var serializer = new DataContractJsonSerializer(typeof(TemplateExportDto));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, dto);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public TemplateExportDto Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new TemplateFormatException("Template JSON is empty.");

            TemplateExportDto dto;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(TemplateExportDto));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    dto = (TemplateExportDto)serializer.ReadObject(ms);
                }
            }
            catch (Exception ex)
            {
                throw new TemplateFormatException("Template JSON is malformed: " + ex.Message);
            }

            Validate(dto);
            return dto;
        }

        /// <summary>
        /// Validates the deserialized DTO for structural integrity: checks that the template
        /// node is present, the format version is supported, and that every relationship
        /// references a known table mapping Ref. Throws <see cref="TemplateFormatException"/>
        /// on any violation so the error surfaces cleanly in the Power Automate run history.
        /// </summary>
        private static void Validate(TemplateExportDto dto)
        {
            if (dto == null || dto.Template == null)
                throw new TemplateFormatException("Template JSON is invalid (missing template).");

            if (ParseMajor(dto.FormatVersion) > ParseMajor(SupportedFormatVersion))
                throw new TemplateFormatException(
                    $"Unsupported formatVersion '{dto.FormatVersion}'. Expected {SupportedFormatVersion} or lower.");

            var status = dto.Template.Status;
            if (status.HasValue && !Enum.IsDefined(typeof(ImportTemplateStatus), status.Value))
                throw new TemplateFormatException(
                    $"Template JSON is invalid: status {status.Value} is not a valid template status (0=Draft, 1=Active, 2=Archived).");

            var refs = new HashSet<string>(
                (dto.TableMappings ?? new List<TableMappingDto>())
                    .Select(t => t.Ref).Where(r => !string.IsNullOrEmpty(r)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var rel in dto.RelationshipMappings ?? new List<RelationshipMappingDto>())
            {
                // ManyToMany se quitó del choice de Relationship Type: importarlo crearía un valor inexistente.
                if (rel.RelationshipType != (int)RelationshipType.OneToMany)
                    throw new TemplateFormatException(
                        $"Relationship '{rel.Name}' is ManyToMany, which is not supported; only OneToMany is.");
                if (!refs.Contains(rel.ParentRef))
                    throw new TemplateFormatException(
                        $"Relationship references unknown parentRef '{rel.ParentRef}'.");
                if (!refs.Contains(rel.ChildRef))
                    throw new TemplateFormatException(
                        $"Relationship references unknown childRef '{rel.ChildRef}'.");
            }
        }

        /// <summary>
        /// Extracts the major version number from a semver-style version string (e.g., "1.0" → 1).
        /// Returns <c>0</c> for null, empty, or unparseable strings so version checks degrade safely.
        /// </summary>
        private static int ParseMajor(string version)
        {
            if (string.IsNullOrEmpty(version)) return 0;
            return int.TryParse(version.Split('.')[0], out var major) ? major : 0;
        }
    }
}
