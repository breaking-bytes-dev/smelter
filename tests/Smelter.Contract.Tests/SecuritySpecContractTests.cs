using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO ExecutePrivilegeName (ApiSpec) ↔ convención real de nombres de privilegio de Dataverse
/// (prv{Tipo}{tabla}, ver SecuritySpec) — sin llamar Dataverse. También encodea, como test de
/// datos, los Requirements de matriz de smelter-access-control (Configurador CRUD Org, Operador
/// sin Write en Template) para que cualquier cambio accidental a la matriz quede en rojo acá,
/// antes de llegar a Smelter.Verify (Fase 7) contra un ambiente real.
/// </summary>
public class SecuritySpecContractTests
{
    public static TheoryData<string> ApiUniqueNames()
    {
        var data = new TheoryData<string>();
        foreach (var api in Catalogo.Apis)
            data.Add(api.UniqueName);
        return data;
    }

    [Theory]
    [MemberData(nameof(ApiUniqueNames))]
    public void Api_ExecutePrivilegeName_MatchesPrvTipoTablaFormat(string uniqueName)
    {
        var api = Catalogo.Apis.Single(a => a.UniqueName == uniqueName);

        api.ExecutePrivilegeName.Should().MatchRegex(
            "^prv(Create|Read|Write|Delete|Append|AppendTo)sanic_tbl_smlt_[a-z]+$",
            $"'{uniqueName}'.ExecutePrivilegeName debe seguir la convención real prv{{Tipo}}{{tabla}} de Dataverse");
    }

    [Theory]
    [InlineData("sanic_capi_smelter_exporttemplate")]
    [InlineData("sanic_capi_smelter_importtemplate")]
    public void ExportImportTemplate_Apis_RequireWriteOnImportTemplate(string uniqueName)
    {
        var api = Catalogo.Apis.Single(a => a.UniqueName == uniqueName);

        api.ExecutePrivilegeName.Should().Be(
            SecuritySpec.PrivilegeName(PrivilegeType.Write, SecuritySpec.Tables.ImportTemplate));
    }

    [Theory]
    [InlineData("anic_capi_smelter_processchunk")]
    [InlineData("sanic_capi_smelter_prepareimport")]
    public void PrepareImportAndProcessChunk_Apis_RequireCreateOnImportJob(string uniqueName)
    {
        var api = Catalogo.Apis.Single(a => a.UniqueName == uniqueName);

        api.ExecutePrivilegeName.Should().Be(
            SecuritySpec.PrivilegeName(PrivilegeType.Create, SecuritySpec.Tables.ImportJob));
    }

    [Fact]
    public void PrivilegeName_FormatsAsPrvTipoTabla()
    {
        SecuritySpec.PrivilegeName(PrivilegeType.Create, SecuritySpec.Tables.ImportJob)
            .Should().Be("prvCreatesanic_tbl_smlt_importjob");
        SecuritySpec.PrivilegeName(PrivilegeType.Write, SecuritySpec.Tables.ImportTemplate)
            .Should().Be("prvWritesanic_tbl_smlt_importtemplate");
        SecuritySpec.PrivilegeName(PrivilegeType.AppendTo, SecuritySpec.Tables.ImportTemplate)
            .Should().Be("prvAppendTosanic_tbl_smlt_importtemplate");
    }

    [Fact]
    public void Configurador_HasFullCrudAppendAppendToAtOrgDepthOnAllSixTables()
    {
        SecuritySpec.Configurador.Privileges.Should().HaveCount(SecuritySpec.Tables.All.Length * 6);
        SecuritySpec.Configurador.Privileges.Should().OnlyContain(p => p.Depth == PrivilegeDepth.Global);

        foreach (var table in SecuritySpec.Tables.All)
        {
            foreach (var type in new[]
                     {
                         PrivilegeType.Create, PrivilegeType.Read, PrivilegeType.Write,
                         PrivilegeType.Delete, PrivilegeType.Append, PrivilegeType.AppendTo,
                     })
            {
                SecuritySpec.Configurador.Privileges.Should().Contain(p => p.Table == table && p.Type == type);
            }
        }
    }

    [Fact]
    public void Operador_HasNoCreateWriteOrDeleteOnImportTemplate()
    {
        SecuritySpec.Operador.Privileges.Should().NotContain(p =>
            p.Table == SecuritySpec.Tables.ImportTemplate
            && (p.Type == PrivilegeType.Create || p.Type == PrivilegeType.Write || p.Type == PrivilegeType.Delete));

        SecuritySpec.Operador.Privileges.Should().Contain(p =>
            p.Table == SecuritySpec.Tables.ImportTemplate && p.Type == PrivilegeType.Read && p.Depth == PrivilegeDepth.Basic);
    }

    [Fact]
    public void Operador_HasNoCreateWriteOrDeleteOnMappingTables()
    {
        foreach (var table in new[] { SecuritySpec.Tables.TableMapping, SecuritySpec.Tables.ColumnMapping, SecuritySpec.Tables.RelationshipMapping })
        {
            SecuritySpec.Operador.Privileges.Should().NotContain(p =>
                p.Table == table && (p.Type == PrivilegeType.Create || p.Type == PrivilegeType.Write || p.Type == PrivilegeType.Delete));
            SecuritySpec.Operador.Privileges.Should().Contain(p =>
                p.Table == table && p.Type == PrivilegeType.Read && p.Depth == PrivilegeDepth.Global);
        }
    }

    [Fact]
    public void Operador_CanCreateImportJobAtOrgDepthAndReadWriteAtUserDepth()
    {
        SecuritySpec.Operador.Privileges.Should().Contain(p =>
            p.Table == SecuritySpec.Tables.ImportJob && p.Type == PrivilegeType.Create && p.Depth == PrivilegeDepth.Global);
        SecuritySpec.Operador.Privileges.Should().Contain(p =>
            p.Table == SecuritySpec.Tables.ImportJob && p.Type == PrivilegeType.Read && p.Depth == PrivilegeDepth.Basic);
        SecuritySpec.Operador.Privileges.Should().Contain(p =>
            p.Table == SecuritySpec.Tables.ImportJob && p.Type == PrivilegeType.Write && p.Depth == PrivilegeDepth.Basic);
        SecuritySpec.Operador.Privileges.Should().Contain(p =>
            p.Table == SecuritySpec.Tables.ImportJobLog && p.Type == PrivilegeType.Read && p.Depth == PrivilegeDepth.Basic);
    }

    [Fact]
    public void Views_ThreeViewsDefined_WithDistinctFetchXmlAndImportJobEntity()
    {
        SecuritySpec.Views.Should().HaveCount(3);
        SecuritySpec.Views.Select(v => v.Name).Should().BeEquivalentTo("Jobs Activos", "Completados con Errores", "Todos los Jobs");
        SecuritySpec.Views.Select(v => v.FetchXml).Distinct().Should().HaveCount(3);
        SecuritySpec.Views.Should().OnlyContain(v => v.FetchXml.Contains($"<entity name=\"{SecuritySpec.Tables.ImportJob}\">"));
    }

    [Fact]
    public void ActiveJobsView_FiltersQueuedAndProcessing_NotCompletedNorFailed()
    {
        var fetch = SecuritySpec.ActiveJobsView.FetchXml;
        fetch.Should().Contain("<condition attribute=\"sanic_smlt_ij_status\" operator=\"eq\" value=\"0\" />");
        fetch.Should().Contain("<condition attribute=\"sanic_smlt_ij_status\" operator=\"eq\" value=\"1\" />");
        fetch.Should().NotContain("<condition attribute=\"sanic_smlt_ij_failedcount\"");
    }

    [Fact]
    public void CompletedWithErrorsView_FiltersOnFailedCountGreaterThanZero()
    {
        SecuritySpec.CompletedWithErrorsView.FetchXml.Should().Contain(
            "<condition attribute=\"sanic_smlt_ij_failedcount\" operator=\"gt\" value=\"0\" />");
    }

    [Fact]
    public void AllJobsView_HasNoFilter()
    {
        SecuritySpec.AllJobsView.FetchXml.Should().NotContain("<filter");
    }
}
