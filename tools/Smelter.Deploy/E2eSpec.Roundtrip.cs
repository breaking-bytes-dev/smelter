// [E2E-10] Roundtrip Export -> Import. FullDto() lleva TODOS los campos del DTO con un valor distinto del default en algún lado
// (un test por reflexión lo exige: un campo nuevo del DTO que no se agregue acá rompe un test), y E2eDtoComparer compara el
// resultado del export contra lo importado ignorando solo lo que NO es contenido: los Ref (el exporter los regenera), el orden
// de las listas y null-vs-vacío (Dataverse guarda "" como null).

using Smelter.Plugins.Models;

namespace Smelter.Deploy;

internal static partial class E2eSpec
{
    internal static class Roundtrip
    {
        internal const string Key = "roundtrip";

        internal static TemplateExportDto FullDto()
        {
            var parent = ContactMapping("tm-rt-parent", "Parent (roundtrip)", 2, "emailaddress1", create: false, update: true,
                Col("emailaddress1", "Column0", required: true, defaultValue: "none@smelter-e2e.test"),
                Col("numberofchildren", "Column1", ColumnDataType.Integer),
                Col("exchangerate", "Column8", ColumnDataType.Decimal),
                Col("birthdate", "Column2", ColumnDataType.DateTime),
                Col("donotemail", "Column3", ColumnDataType.Boolean, defaultValue: "false"),
                Col("parentcustomerid", "Column4", ColumnDataType.Lookup, lookupTable: "contact", lookupMatch: "emailaddress1"),
                Col("gendercode", "Column5", ColumnDataType.OptionSet, optionSetMapping: "{\"M\":1,\"F\":2}"),
                Col("creditlimit", "Column6", ColumnDataType.Money),
                Col("contactid", "Column7", ColumnDataType.Guid),
                Col("description", null, transform: "CONCAT(Column0, \"-\", Column1)"));

            var child = ContactMapping("tm-rt-child", "Child (roundtrip, insert only)", 1, dedup: null, create: true, update: false,
                Col("lastname", "Column0", required: true, defaultValue: "Child"),
                Col("firstname", "Column1"));
            child.InsertOnly = true;

            return Template(Key, header: false, delimiter: ";", chunkSize: 250, ImportTemplateStatus.Archived,
                [parent, child],
                [
                    new RelationshipMappingDto
                    {
                        Name = "Parent -> Child (roundtrip)", ParentRef = parent.Ref, ChildRef = child.Ref,
                        RelationshipType = (int)RelationshipType.OneToMany, LookupColumnOnChild = "parentcustomerid",
                        ParentDedupColumns = "Column0",
                    },
                ],
                description: "Template de prueba del roundtrip Export -> Import (Smelter.Verify). Se borra al terminar.");
        }
    }
}

/// <summary>Diferencias de CONTENIDO entre dos templates exportados (lista vacía = idénticos). Cada línea dice qué y dónde.</summary>
internal static class E2eDtoComparer
{
    internal static IReadOnlyList<string> Diff(TemplateExportDto expected, TemplateExportDto actual)
    {
        var diffs = new List<string>();

        void Check(string where, object? e, object? a)
        {
            var en = Norm(e);
            var an = Norm(a);
            if (en != an) diffs.Add($"{where}: esperado '{en}', obtenido '{an}'");
        }

        var et = expected.Template;
        var at = actual.Template;
        Check("Template.Name", et.Name, at.Name);
        Check("Template.SourceType", et.SourceType, at.SourceType);
        Check("Template.HasHeaderRow", et.HasHeaderRow, at.HasHeaderRow);
        Check("Template.Delimiter", et.Delimiter, at.Delimiter);
        Check("Template.ChunkSize", et.ChunkSize, at.ChunkSize);
        Check("Template.Description", et.Description, at.Description);
        Check("Template.Status", et.Status, at.Status);

        var expTms = ByName(expected.TableMappings, t => t.Name, "TableMapping", diffs, "esperado");
        var actTms = ByName(actual.TableMappings, t => t.Name, "TableMapping", diffs, "obtenido");

        foreach (var name in expTms.Keys.Where(n => !actTms.ContainsKey(n)))
            diffs.Add($"TableMapping '{name}': falta en lo obtenido");
        foreach (var name in actTms.Keys.Where(n => !expTms.ContainsKey(n)))
            diffs.Add($"TableMapping '{name}': sobra en lo obtenido");

        foreach (var (name, e) in expTms.Where(kv => actTms.ContainsKey(kv.Key)))
        {
            var a = actTms[name];
            var w = $"TableMapping '{name}'";
            Check($"{w}.TargetTable", e.TargetTable, a.TargetTable);
            Check($"{w}.ProcessingOrder", e.ProcessingOrder, a.ProcessingOrder);
            Check($"{w}.DedupColumns", e.DedupColumns, a.DedupColumns);
            Check($"{w}.CreateIfNotExists", e.CreateIfNotExists, a.CreateIfNotExists);
            Check($"{w}.UpdateIfExists", e.UpdateIfExists, a.UpdateIfExists);
            Check($"{w}.InsertOnly", e.InsertOnly, a.InsertOnly);

            var eCms = ByName(e.ColumnMappings, c => c.Name, $"{w} ColumnMapping", diffs, "esperado");
            var aCms = ByName(a.ColumnMappings, c => c.Name, $"{w} ColumnMapping", diffs, "obtenido");
            foreach (var cn in eCms.Keys.Where(n => !aCms.ContainsKey(n)))
                diffs.Add($"{w} ColumnMapping '{cn}': falta en lo obtenido");
            foreach (var cn in aCms.Keys.Where(n => !eCms.ContainsKey(n)))
                diffs.Add($"{w} ColumnMapping '{cn}': sobra en lo obtenido");

            foreach (var (cn, ce) in eCms.Where(kv => aCms.ContainsKey(kv.Key)))
            {
                var ca = aCms[cn];
                var cw = $"{w} ColumnMapping '{cn}'";
                Check($"{cw}.SourceColumn", ce.SourceColumn, ca.SourceColumn);
                Check($"{cw}.TargetColumn", ce.TargetColumn, ca.TargetColumn);
                Check($"{cw}.DataType", ce.DataType, ca.DataType);
                Check($"{cw}.IsRequired", ce.IsRequired, ca.IsRequired);
                Check($"{cw}.DefaultValue", ce.DefaultValue, ca.DefaultValue);
                Check($"{cw}.LookupTable", ce.LookupTable, ca.LookupTable);
                Check($"{cw}.LookupMatchColumn", ce.LookupMatchColumn, ca.LookupMatchColumn);
                Check($"{cw}.OptionSetMapping", ce.OptionSetMapping, ca.OptionSetMapping);
                Check($"{cw}.TransformExpression", ce.TransformExpression, ca.TransformExpression);
            }
        }

        // Los Ref no se comparan: se traducen al NOMBRE del table mapping de cada lado.
        string NameOf(TemplateExportDto dto, string? reference) =>
            dto.TableMappings.FirstOrDefault(t => string.Equals(t.Ref, reference, StringComparison.OrdinalIgnoreCase))?.Name ?? $"<ref sin resolver '{reference}'>";

        var eRels = ByName(expected.RelationshipMappings, r => r.Name, "Relationship", diffs, "esperado");
        var aRels = ByName(actual.RelationshipMappings, r => r.Name, "Relationship", diffs, "obtenido");
        foreach (var rn in eRels.Keys.Where(n => !aRels.ContainsKey(n)))
            diffs.Add($"Relationship '{rn}': falta en lo obtenido");
        foreach (var rn in aRels.Keys.Where(n => !eRels.ContainsKey(n)))
            diffs.Add($"Relationship '{rn}': sobra en lo obtenido");
        foreach (var (rn, re) in eRels.Where(kv => aRels.ContainsKey(kv.Key)))
        {
            var ra = aRels[rn];
            var rw = $"Relationship '{rn}'";
            Check($"{rw}.Parent", NameOf(expected, re.ParentRef), NameOf(actual, ra.ParentRef));
            Check($"{rw}.Child", NameOf(expected, re.ChildRef), NameOf(actual, ra.ChildRef));
            Check($"{rw}.RelationshipType", re.RelationshipType, ra.RelationshipType);
            Check($"{rw}.LookupColumnOnChild", re.LookupColumnOnChild, ra.LookupColumnOnChild);
            Check($"{rw}.ParentDedupColumns", re.ParentDedupColumns, ra.ParentDedupColumns);
        }

        return diffs;
    }

    private static string Norm(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static Dictionary<string, T> ByName<T>(IEnumerable<T> items, Func<T, string?> name, string what, List<string> diffs, string side)
    {
        var dict = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var n = name(item) ?? "";
            if (!dict.TryAdd(n, item)) diffs.Add($"{what} '{n}': nombre duplicado en lo {side} (la comparación es por nombre)");
        }
        return dict;
    }
}
