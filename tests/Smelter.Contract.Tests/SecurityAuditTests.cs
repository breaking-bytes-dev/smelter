using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Regla del [SEC] de Verify: un rol de Smelter tiene EXACTAMENTE lo que declara SecuritySpec (+ la lista explícita de
/// <see cref="RoleSpec.AllowedExtras"/>, vacía por defecto). Cualquier privilegio más, sobre una tabla de Smelter o sobre cualquier otra
/// cosa de la plataforma, es una violación: el deploy lo quita y el verify falla si sigue ahí.
/// </summary>
public class SecurityAuditTests
{
    private static readonly RoleSpec Role = new(
        "Smelter Test",
        [
            new PrivilegeSpec(SecuritySpec.Tables.ImportJob, PrivilegeType.Create, PrivilegeDepth.Global),
            new PrivilegeSpec(SecuritySpec.Tables.ImportJob, PrivilegeType.Read, PrivilegeDepth.Basic),
        ],
        [new PrivilegeSpec(SecuritySpec.Tables.ImportJobLog, PrivilegeType.Create, PrivilegeDepth.Global)]);

    private static List<(string Name, PrivilegeDepth Depth)> Exact() =>
    [
        ("prvCreatesanic_tbl_smlt_importjob", PrivilegeDepth.Global),
        ("prvReadsanic_tbl_smlt_importjob", PrivilegeDepth.Basic),
    ];

    [Fact]
    public void ExactlyTheSpec_HasNoViolations()
    {
        SecurityAudit.Violations(Role, Exact()).Should().BeEmpty();
        SecurityAudit.Unexpected(Role, Exact()).Should().BeEmpty();
    }

    [Fact]
    public void MissingPrivilege_IsReported()
    {
        var actual = Exact().Where(p => !p.Name.StartsWith("prvRead")).ToList();

        SecurityAudit.Violations(Role, actual).Should().ContainSingle().Which.Should().Contain("falta").And.Contain("prvReadsanic_tbl_smlt_importjob");
    }

    [Fact]
    public void WrongDepth_IsReported()
    {
        var actual = Exact();
        actual[1] = ("prvReadsanic_tbl_smlt_importjob", PrivilegeDepth.Global);

        SecurityAudit.Violations(Role, actual).Should().ContainSingle().Which.Should().Contain("prvReadsanic_tbl_smlt_importjob").And.Contain("Global");
    }

    [Fact]
    public void ExtraPrivilegeOnASmelterTable_IsAViolationAndUnexpected()
    {
        var actual = Exact();
        actual.Add(("prvAppendsanic_tbl_smlt_importjoblog", PrivilegeDepth.Basic));

        SecurityAudit.Violations(Role, actual).Should().ContainSingle().Which.Should().Contain("prvAppendsanic_tbl_smlt_importjoblog").And.Contain("tabla de Smelter");
        SecurityAudit.Unexpected(Role, actual).Should().Equal("prvAppendsanic_tbl_smlt_importjoblog");
    }

    [Theory]
    [InlineData("prvAssignsanic_tbl_smlt_importjob")]
    [InlineData("prvSharesanic_tbl_smlt_importtemplate")]
    [InlineData("prvReadsanic_tbl_smlt_hmackey")]
    public void AssignShareAndTheHmacKeyTable_AreNotInTheSpecEither(string name)
    {
        var actual = Exact();
        actual.Add((name, PrivilegeDepth.Basic));

        SecurityAudit.Unexpected(Role, actual).Should().Equal(name);
    }

    [Theory]
    [InlineData("prvReadSharePointData")]
    [InlineData("prvReadPluginAssembly")]
    [InlineData("prvReadSdkMessage")]
    public void ExtraPlatformPrivilege_IsAViolationUnlessExplicitlyAllowed(string name)
    {
        var actual = Exact();
        actual.Add((name, PrivilegeDepth.Global));

        SecurityAudit.Violations(Role, actual).Should().ContainSingle().Which.Should().Contain(name).And.Contain("fuera de la spec");
        SecurityAudit.Unexpected(Role, actual).Should().Equal(name);

        var allowing = Role with { AllowedExtraPrivileges = [name] };
        SecurityAudit.Violations(allowing, actual).Should().BeEmpty();
        SecurityAudit.Unexpected(allowing, actual).Should().BeEmpty();
    }

    [Fact]
    public void DeniedPrivilegePresent_IsReportedAndRemovedEvenThoughItIsAlsoOnASmelterTable()
    {
        var actual = Exact();
        actual.Add(("prvCreatesanic_tbl_smlt_importjoblog", PrivilegeDepth.Global));

        SecurityAudit.Violations(Role, actual).Should().Contain(v => v.Contains("prohibido") && v.Contains("prvCreatesanic_tbl_smlt_importjoblog"));
        SecurityAudit.Unexpected(Role, actual).Should().Equal("prvCreatesanic_tbl_smlt_importjoblog");
    }

    [Fact]
    public void Unexpected_NeverIncludesWhatTheSpecDeclares_SoAdjustingDepthIsLeftToAddPrivileges()
    {
        var actual = Exact();
        actual[1] = ("prvReadsanic_tbl_smlt_importjob", PrivilegeDepth.Global);

        SecurityAudit.Unexpected(Role, actual).Should().BeEmpty();
    }

    // ── la spec real ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Operador_CanAppendImportJob_BecauseSettingTheTemplateLookupOnANewJobNeedsAppendOnTheJobAndAppendToOnTheTemplate()
    {
        SecuritySpec.Operador.Privileges.Should().Contain(p =>
            p.Table == SecuritySpec.Tables.ImportJob && p.Type == PrivilegeType.Append && p.Depth == PrivilegeDepth.Basic);
        SecuritySpec.Operador.Privileges.Should().Contain(p =>
            p.Table == SecuritySpec.Tables.ImportTemplate && p.Type == PrivilegeType.AppendTo && p.Depth == PrivilegeDepth.Basic);
    }

    [Fact]
    public void Operador_NeverGetsAppendOrWriteOnTheLogsNorAppendToOnTheJob_NobodyButTheEngineTouchesThem()
    {
        SecuritySpec.Operador.Privileges.Should().NotContain(p =>
            p.Table == SecuritySpec.Tables.ImportJobLog && p.Type != PrivilegeType.Read);
        SecuritySpec.Operador.Privileges.Should().NotContain(p =>
            p.Table == SecuritySpec.Tables.ImportJob && p.Type == PrivilegeType.AppendTo);
    }

    [Fact]
    public void BothRealRoles_AllowNoPlatformExtras_TheBaselineComesFromTheRolesTheOrganizationAssigns()
    {
        SecuritySpec.Configurador.AllowedExtraPrivileges.Should().BeNullOrEmpty();
        SecuritySpec.Operador.AllowedExtraPrivileges.Should().BeNullOrEmpty();
    }
}
