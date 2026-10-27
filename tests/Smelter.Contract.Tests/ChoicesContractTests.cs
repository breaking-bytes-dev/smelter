using FluentAssertions;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO enums del código ↔ optionsets de la solución (metadata versionada en solutions/unpacked/).
/// El VALOR numérico es lo funcional (el plugin escribe new OptionSetValue((int)enum)): se compara
/// estricto. El label se compara normalizado (sin espacios, case-insensitive): "GUID" ≈ "Guid".
/// Si alguien cambia un valor en Dev (llega por solution-sync) o un enum en el código → rojo.
/// Las opciones que <see cref="OptionCleanupCatalogo"/> manda a eliminar (SDK, SchemaCleanupProvisioner) se
/// descuentan del lado de la metadata: así el contrato vale antes y después de que solution-sync traiga el cambio.
/// </summary>
public class ChoicesContractTests
{
    private static Dictionary<int, string> PersistedOptions(string entityFolder, string optionSetName)
    {
        var column = optionSetName[(entityFolder.Length + 1)..];
        var pendingRemoval = OptionCleanupCatalogo.Options
            .Where(o => o.EntityLogicalName == entityFolder && o.AttributeLogicalName == column)
            .Select(o => o.Value)
            .ToHashSet();

        return SolutionMetadata.OptionSet(entityFolder, optionSetName)
            .Where(kv => !pendingRemoval.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));
    }

    private static void AssertEnumMatchesOptionSet<TEnum>(string entityFolder, string optionSetName)
        where TEnum : struct, Enum
    {
        var expected = Enum.GetValues<TEnum>()
            .ToDictionary(e => Convert.ToInt32(e), e => Normalize(e.ToString()));

        PersistedOptions(entityFolder, optionSetName).Should().BeEquivalentTo(expected,
            $"el optionset '{optionSetName}' de la solución debe reflejar exactamente el enum {typeof(TEnum).Name} del código");
    }

    private static string Normalize(string label) => label.Replace(" ", "").ToLowerInvariant();

    [Fact]
    public void ImportJobStatus_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<ImportJobStatus>(
            "sanic_tbl_smlt_importjob", "sanic_tbl_smlt_importjob_sanic_smlt_ij_status");

    [Fact]
    public void ImportLogStatus_OptionSetOnlyHasFailed_TheOnlyStatusThatIsPersisted()
    {
        // El log solo registra fallos: Created/Updated/Skipped viven en el enum (contadores del job) pero no se
        // persisten, así que el choice se redujo a Failed (3).
        var persisted = PersistedOptions("sanic_tbl_smlt_importjoblog", "sanic_tbl_smlt_importjoblog_sanic_smlt_ijl_status");

        persisted.Should().BeEquivalentTo(new Dictionary<int, string> { [(int)ImportLogStatus.Failed] = "failed" });
    }

    [Fact]
    public void RelationshipType_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<RelationshipType>(
            "sanic_tbl_smlt_relationshipmapping", "sanic_tbl_smlt_relationshipmapping_sanic_smlt_rm_relationshiptype");

    [Fact]
    public void ErrorCategory_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<ErrorCategory>(
            "sanic_tbl_smlt_importjoblog", "sanic_tbl_smlt_importjoblog_sanic_smlt_ijl_errorcategory");

    [Fact]
    public void ColumnDataType_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<ColumnDataType>(
            "sanic_tbl_smlt_columnmapping", "sanic_tbl_smlt_columnmapping_sanic_smlt_cm_datatype");
}
