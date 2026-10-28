// Asserts E2E de TableLabelsSpec.cs: cada tabla de Smelter tiene DisplayName y DisplayCollectionName en español (3082); toda
// columna sanic_smlt_* (salvo las virtuales AttributeOf) y toda opción de sus choices locales tiene label 3082, y las de la
// spec valen exactamente lo declarado. Verifica además que el 1033 de tablas y columnas NO quedó vacío. Solo lee metadata.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class TableLabelsAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[LABELS] Verificando nombres 3082 de tablas, columnas y opciones (TableLabelsSpec.cs) ...");

        var columns = TableLabelsCatalogo.Columns.ToDictionary(c => (c.Table, c.Column), c => c.Es);
        var options = TableLabelsCatalogo.Options.ToDictionary(o => (o.Table, o.Column, o.Value), o => o.Es);
        var checkedColumns = 0;
        var checkedOptions = 0;

        foreach (var spec in TableLabelsCatalogo.Tables)
        {
            if (TableLabelsCatalogo.IsOptional(spec.Table) && !HmacKeyTableProvisioner.TableExists(client))
                continue;

            var entity = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = spec.Table,
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = true,
            })).EntityMetadata;

            Assert(!string.IsNullOrWhiteSpace(LabelEditor.TextFor(entity.DisplayName, Lcids.English)), $"{spec.Table}: perdió el DisplayName 1033");
            Assert(LabelEditor.TextFor(entity.DisplayName, Lcids.Spanish) == spec.DisplayEs,
                $"{spec.Table}: DisplayName 3082 = '{LabelEditor.TextFor(entity.DisplayName, Lcids.Spanish)}', esperado '{spec.DisplayEs}'");
            Assert(LabelEditor.TextFor(entity.DisplayCollectionName, Lcids.Spanish) == spec.CollectionEs,
                $"{spec.Table}: DisplayCollectionName 3082 = '{LabelEditor.TextFor(entity.DisplayCollectionName, Lcids.Spanish)}', esperado '{spec.CollectionEs}'");

            var custom = (entity.Attributes ?? [])
                .Where(a => a.LogicalName.StartsWith("sanic_smlt_", StringComparison.Ordinal))
                // Columnas virtuales ("<bool>name", "<lookup>name"): no son configurables; el label va en la columna real.
                .Where(a => string.IsNullOrEmpty(a.AttributeOf))
                .ToList();
            Assert(custom.Count > 0, $"{spec.Table}: sin columnas sanic_smlt_* (¿metadata incompleta?)");

            foreach (var attribute in custom)
            {
                var es = LabelEditor.TextFor(attribute.DisplayName, Lcids.Spanish);
                Assert(!string.IsNullOrWhiteSpace(LabelEditor.TextFor(attribute.DisplayName, Lcids.English)),
                    $"{spec.Table}.{attribute.LogicalName}: perdió el DisplayName 1033");
                Assert(!string.IsNullOrWhiteSpace(es), $"{spec.Table}.{attribute.LogicalName} no tiene DisplayName en español (3082)");
                if (columns.TryGetValue((spec.Table, attribute.LogicalName), out var expected))
                    Assert(es == expected, $"{spec.Table}.{attribute.LogicalName}: 3082 = '{es}', esperado '{expected}'");
                checkedColumns++;

                foreach (var (value, option) in OptionsOf(attribute))
                {
                    var optionEs = LabelEditor.TextFor(option.Label, Lcids.Spanish);
                    Assert(!string.IsNullOrWhiteSpace(optionEs), $"{spec.Table}.{attribute.LogicalName}: la opción {value} no tiene label 3082");
                    if (options.TryGetValue((spec.Table, attribute.LogicalName, value), out var expectedOption))
                        Assert(optionEs == expectedOption, $"{spec.Table}.{attribute.LogicalName}: opción {value} 3082 = '{optionEs}', esperado '{expectedOption}'");
                    checkedOptions++;
                }
            }
        }

        Console.WriteLine($"[LABELS] OK: {TableLabelsCatalogo.Tables.Length} tablas, {checkedColumns} columnas y {checkedOptions} opciones con label 3082.");
    }

    // Opciones de un choice local (picklist) o las dos de un boolean (1 = verdadera, 0 = falsa).
    private static IEnumerable<(int Value, OptionMetadata Option)> OptionsOf(AttributeMetadata attribute)
    {
        switch (attribute)
        {
            case BooleanAttributeMetadata { OptionSet: { } set }:
                if (set.TrueOption is not null) yield return (1, set.TrueOption);
                if (set.FalseOption is not null) yield return (0, set.FalseOption);
                break;
            case EnumAttributeMetadata { OptionSet: { } picklist }:
                foreach (var o in picklist.Options.Where(o => o.Value is not null))
                    yield return (o.Value!.Value, o);
                break;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[LABELS]: " + message);
    }
}
