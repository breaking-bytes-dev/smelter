using FluentAssertions;
using Smelter.Plugins.Models;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO enums del código ↔ optionsets de la solución (metadata versionada en solutions/unpacked/).
/// El VALOR numérico es lo funcional (el plugin escribe new OptionSetValue((int)enum)): se compara
/// estricto. El label se compara normalizado (sin espacios, case-insensitive): "GUID" ≈ "Guid".
/// Si alguien cambia un valor en Dev (llega por solution-sync) o un enum en el código → rojo.
/// </summary>
public class ChoicesContractTests
{
    private static void AssertEnumMatchesOptionSet<TEnum>(string entityFolder, string optionSetName)
        where TEnum : struct, Enum
    {
        var expected = Enum.GetValues<TEnum>()
            .ToDictionary(e => Convert.ToInt32(e), e => Normalize(e.ToString()));

        var actual = SolutionMetadata.OptionSet(entityFolder, optionSetName)
            .ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));

        actual.Should().BeEquivalentTo(expected,
            $"el optionset '{optionSetName}' de la solución debe reflejar exactamente el enum {typeof(TEnum).Name} del código");
    }

    private static string Normalize(string label) => label.Replace(" ", "").ToLowerInvariant();

    [Fact]
    public void ImportJobStatus_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<ImportJobStatus>(
            "sanic_tbl_smlt_importjob", "sanic_tbl_smlt_importjob_sanic_smlt_ij_status");

    [Fact]
    public void ImportLogStatus_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<ImportLogStatus>(
            "sanic_tbl_smlt_importjoblog", "sanic_tbl_smlt_importjoblog_sanic_smlt_ijl_status");

    [Fact]
    public void ErrorCategory_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<ErrorCategory>(
            "sanic_tbl_smlt_importjoblog", "sanic_tbl_smlt_importjoblog_sanic_smlt_ijl_errorcategory");

    [Fact]
    public void ColumnDataType_MatchesSolutionOptionSet() =>
        AssertEnumMatchesOptionSet<ColumnDataType>(
            "sanic_tbl_smlt_columnmapping", "sanic_tbl_smlt_columnmapping_sanic_smlt_cm_datatype");
}
