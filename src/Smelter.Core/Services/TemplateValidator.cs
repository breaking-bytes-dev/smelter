using System;
using System.Collections.Generic;
using System.Linq;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Validaciones de coherencia de la configuración de un template que Dataverse no puede expresar (son reglas
    /// entre registros). Pura (sin Dataverse): PrepareImport la corre antes de bajar el archivo y convierte los
    /// errores en un único mensaje. Los mensajes nombran mappings y columnas — nunca valores de filas.
    /// </summary>
    public static class TemplateValidator
    {
        /// <summary>Devuelve un mensaje por problema encontrado; vacío = configuración coherente.</summary>
        public static IReadOnlyList<string> Validate(ImportContext context)
        {
            var errors = new List<string>();
            if (context == null) return errors;

            foreach (var tm in context.TableMappings)
            {
                if (!tm.InsertOnly)
                {
                    var targets = new HashSet<string>(
                        tm.ColumnMappings.Where(c => !string.IsNullOrWhiteSpace(c.TargetColumn)).Select(c => c.TargetColumn.Trim()),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var dedup in tm.GetDedupColumnNames().Where(d => !targets.Contains(d)))
                        errors.Add($"Table Mapping '{tm.Name}' ({tm.TargetTable}): dedup column '{dedup}' is not the " +
                                   "Target Column of any of its Column Mappings.");
                }

                foreach (var cm in tm.ColumnMappings.Where(c => c.DataType == ColumnDataType.Lookup
                                                                && string.IsNullOrWhiteSpace(c.LookupTable)))
                    errors.Add($"Column Mapping '{cm.Name}' (target '{cm.TargetColumn}') in Table Mapping '{tm.Name}': " +
                               "Data Type Lookup requires a Lookup Table.");
            }

            foreach (var rel in context.RelationshipMappings)
                ValidateRelationship(context, rel, errors);

            return errors;
        }

        private static void ValidateRelationship(ImportContext context, RelationshipMappingConfig rel, List<string> errors)
        {
            if (!rel.IsOneToMany)
            {
                errors.Add($"Relationship Mapping '{rel.Name}': ManyToMany is not supported; use OneToMany.");
                return;
            }

            if (string.IsNullOrWhiteSpace(rel.LookupColumnOnChild))
                errors.Add($"Relationship Mapping '{rel.Name}': OneToMany requires 'Lookup column on child'.");

            var parent = context.TableMappings.FirstOrDefault(t => t.Id == rel.ParentTableMappingId);
            if (parent == null)
            {
                errors.Add($"Relationship Mapping '{rel.Name}': the parent Table Mapping does not belong to this template.");
                return;
            }
            if (!context.TableMappings.Any(t => t.Id == rel.ChildTableMappingId))
                errors.Add($"Relationship Mapping '{rel.Name}': the child Table Mapping does not belong to this template.");

            var parentKeys = parent.GetDedupColumnNames();
            if (parentKeys.Length == 0)
            {
                errors.Add($"Relationship Mapping '{rel.Name}': parent Table Mapping '{parent.Name}' has no dedup columns, " +
                           "so the child cannot reference it by alternate key.");
                return;
            }

            var listed = SplitList(rel.ParentDedupColumns).Length;
            if (listed != parentKeys.Length)
                errors.Add($"Relationship Mapping '{rel.Name}': Parent Dedup Columns lists {listed} column(s) but parent " +
                           $"Table Mapping '{parent.Name}' has {parentKeys.Length} dedup column(s); they must match one to one.");
        }

        /// <summary>Lista separada por comas → nombres sin espacios y sin vacíos.</summary>
        public static string[] SplitList(string csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? Array.Empty<string>()
                : csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
    }
}
