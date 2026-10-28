using System.Text;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;

namespace Smelter.Contract.Tests;

/// <summary>
/// Simula LOCALMENTE lo que PrepareImport valida antes de tocar datos (gate de dedup, TemplateValidator, headers del CSV,
/// conteo de filas) para que un template/CSV de los pasos [E2E-n] de Smelter.Verify con una configuración incoherente se
/// descubra en un test y no en la corrida contra Dev. No reemplaza al E2E: solo evita gastar una corrida en errores de armado.
/// </summary>
internal static class E2eSpecTestKit
{
    internal static ImportContext ToContext(TemplateExportDto dto)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var ctx = new ImportContext
        {
            Template = new ImportTemplateConfig
            {
                Name = dto.Template.Name,
                HasHeaderRow = dto.Template.HasHeaderRow,
                Delimiter = dto.Template.Delimiter,
                ChunkSize = dto.Template.ChunkSize,
                Status = dto.Template.Status ?? (int)ImportTemplateStatus.Active,
            },
        };

        foreach (var tm in dto.TableMappings.OrderBy(t => t.ProcessingOrder)) // el loader ordena por ProcessingOrder
        {
            var id = Guid.NewGuid();
            ids[tm.Ref] = id;
            ctx.TableMappings.Add(new TableMappingConfig
            {
                Id = id, Name = tm.Name, TargetTable = tm.TargetTable, ProcessingOrder = tm.ProcessingOrder,
                DedupColumns = tm.DedupColumns, CreateIfNotExists = tm.CreateIfNotExists,
                UpdateIfExists = tm.UpdateIfExists, InsertOnly = tm.InsertOnly,
                ColumnMappings = tm.ColumnMappings.Select(c => new ColumnMappingConfig
                {
                    Name = c.Name, SourceColumn = c.SourceColumn, TargetColumn = c.TargetColumn,
                    DataType = (ColumnDataType)c.DataType, IsRequired = c.IsRequired, DefaultValue = c.DefaultValue,
                    LookupTable = c.LookupTable, LookupMatchColumn = c.LookupMatchColumn,
                    OptionSetMapping = c.OptionSetMapping, TransformExpression = c.TransformExpression,
                }).ToList(),
            });
        }

        foreach (var rel in dto.RelationshipMappings)
            ctx.RelationshipMappings.Add(new RelationshipMappingConfig
            {
                Name = rel.Name, ParentTableMappingId = ids[rel.ParentRef], ChildTableMappingId = ids[rel.ChildRef],
                RelationshipType = rel.RelationshipType, LookupColumnOnChild = rel.LookupColumnOnChild,
                ParentDedupColumns = rel.ParentDedupColumns,
            });
        return ctx;
    }

    /// <summary>Problemas que PrepareImport reportaría (vacío = el template + CSV pasan Prepare). Excluye el gate de Active.</summary>
    internal static List<string> PrepareProblems(TemplateExportDto dto, string csv)
    {
        var problems = new List<string>();
        var ctx = ToContext(dto);

        foreach (var tm in ctx.TableMappings.Where(t => !t.InsertOnly && t.GetDedupColumnNames().Length == 0))
            problems.Add($"Table Mapping '{tm.Name}' has no dedup columns");
        problems.AddRange(TemplateValidator.Validate(ctx));

        var bytes = new UTF8Encoding(false).GetBytes(csv);
        var reader = new CsvParserService();
        if (ctx.Template.HasHeaderRow)
        {
            var headers = new HashSet<string>(reader.ReadHeaders(bytes, true, ctx.Template.Delimiter), StringComparer.OrdinalIgnoreCase);
            foreach (var missing in ctx.TableMappings.SelectMany(t => t.ColumnMappings)
                         .Where(c => string.IsNullOrWhiteSpace(c.TransformExpression) && !string.IsNullOrEmpty(c.SourceColumn))
                         .Select(c => c.SourceColumn.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(sc => !headers.Contains(sc)))
                problems.Add($"CSV is missing mapped source column '{missing}'");
        }
        return problems;
    }

    internal static int CountRows(TemplateExportDto dto, string csv) =>
        new CsvParserService().CountRows(new UTF8Encoding(false).GetBytes(csv), dto.Template.HasHeaderRow, dto.Template.Delimiter);

    internal static List<SourceRow> ReadRows(TemplateExportDto dto, string csv) =>
        new CsvParserService().ReadRange(new UTF8Encoding(false).GetBytes(csv), dto.Template.HasHeaderRow, dto.Template.Delimiter, 0, int.MaxValue).ToList();
}
