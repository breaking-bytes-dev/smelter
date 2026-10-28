using FluentAssertions;
using Smelter.DevAccess;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>Smelter.DevAccess: lógica pura (rol por BU, diffs) + contrato de la spec contra Smelter.Deploy.</summary>
public class DevAccessTests
{
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Child = Guid.NewGuid();

    private static RoleRef R(string name, Guid bu) => new(Guid.NewGuid(), name, bu);

    [Fact]
    public void PickRoleForBu_PicksTheCopyOfTheUsersBu()
    {
        var rootCopy = R("Smelter Operador", Root);
        var childCopy = R("Smelter Operador", Child);
        var other = R("Smelter Configurador", Child);
        var roles = new[] { rootCopy, childCopy, other };

        AccessPlanner.PickRoleForBu(roles, "Smelter Operador", Child).Should().Be(childCopy);
        AccessPlanner.PickRoleForBu(roles, "Smelter Operador", Root).Should().Be(rootCopy);
    }

    [Fact]
    public void PickRoleForBu_NoCopyInBu_ReturnsNull() =>
        AccessPlanner.PickRoleForBu([R("Smelter Operador", Root)], "Smelter Operador", Child).Should().BeNull();

    [Fact]
    public void DiffRoles_Missing_AddsDesired()
    {
        var desired = R("Smelter Operador", Child);
        var d = AccessPlanner.DiffRoles(desired, []);
        d.ToAdd.Should().ContainSingle().Which.Should().Be(desired);
        d.ToRemove.Should().BeEmpty();
    }

    [Fact]
    public void DiffRoles_AlreadyCorrect_IsNoOp()
    {
        var desired = R("Smelter Operador", Child);
        var d = AccessPlanner.DiffRoles(desired, [desired]);
        d.ToAdd.Should().BeEmpty();
        d.ToRemove.Should().BeEmpty();
    }

    [Fact]
    public void DiffRoles_WrongCopyOrWrongRole_IsRemovedAndDesiredAdded()
    {
        var desired = R("Smelter Operador", Child);
        var wrongBu = R("Smelter Operador", Root);
        var wrongRole = R("Smelter Configurador", Child);
        var d = AccessPlanner.DiffRoles(desired, [wrongBu, wrongRole]);
        d.ToAdd.Should().ContainSingle().Which.Should().Be(desired);
        d.ToRemove.Should().BeEquivalentTo([wrongBu, wrongRole]);
    }

    [Fact]
    public void DiffRoles_NegativeControl_RemovesEverySmelterRole()
    {
        var a = R("Smelter Operador", Child);
        var b = R("Smelter Configurador", Child);
        var d = AccessPlanner.DiffRoles(null, [a, b]);
        d.ToAdd.Should().BeEmpty();
        d.ToRemove.Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public void DiffMembership_NotMember_Adds() =>
        AccessPlanner.DiffMembership("A", []).Should().BeEquivalentTo(new MembershipChanges(["A"], []));

    [Fact]
    public void DiffMembership_AlreadyInDesired_IsNoOp() =>
        AccessPlanner.DiffMembership("A", ["A"]).Should().BeEquivalentTo(new MembershipChanges([], []));

    [Fact]
    public void DiffMembership_InOtherQaTeam_MovesIt() =>
        AccessPlanner.DiffMembership("A", ["B"]).Should().BeEquivalentTo(new MembershipChanges(["A"], ["B"]));

    [Fact]
    public void DiffMembership_NoTeamDesired_RemovesFromAll() =>
        AccessPlanner.DiffMembership(null, ["A", "B"]).Should().BeEquivalentTo(new MembershipChanges([], ["A", "B"]));

    [Theory]
    [InlineData("dev", true)]
    [InlineData(" DEV ", true)]
    [InlineData("test", false)]
    [InlineData("prod", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void CheckEnvironment_OnlyDev(string? env, bool allowed) =>
        (AccessPlanner.CheckEnvironment(env) is null).Should().Be(allowed);

    [Theory]
    [InlineData(null)]
    [InlineData("staging")]
    public void CheckEnvironment_AgreesWithDeployGuards(string? env) =>
        (AccessPlanner.CheckEnvironment(env) is null).Should().Be(DeployGuards.CheckEnvironment(env) is null);

    // ── Contrato de la spec ────────────────────────────────────────────────────────────────

    [Fact]
    public void Spec_RoleNamesAndApiMatchDeploySource()
    {
        DevAccessSpec.ConfiguradorRoleName.Should().Be(SecuritySpec.ConfiguradorRoleName);
        DevAccessSpec.OperadorRoleName.Should().Be(SecuritySpec.OperadorRoleName);
        Catalogo.Apis.Select(a => a.UniqueName).Should().Contain(DevAccessSpec.ImportTemplateApi);
    }

    [Fact]
    public void Spec_FourUsers_OneConfiguradorTwoOperadoresOneNegativeControl()
    {
        DevAccessSpec.Users.Should().HaveCount(4);
        DevAccessSpec.Users.Select(u => u.UserId).Should().OnlyHaveUniqueItems();
        DevAccessSpec.Users.Count(u => u.RoleName == SecuritySpec.ConfiguradorRoleName).Should().Be(1);
        DevAccessSpec.Users.Count(u => u.RoleName == SecuritySpec.OperadorRoleName).Should().Be(2);
        DevAccessSpec.Users.Single(u => u.RoleName is null).TeamName.Should().BeNull();
    }

    [Fact]
    public void Spec_TeamsAndTemplatesAreConsistent()
    {
        DevAccessSpec.Users.Where(u => u.TeamName is not null).Select(u => u.TeamName!)
            .Should().OnlyContain(t => DevAccessSpec.QaTeamNames.Contains(t));
        DevAccessSpec.Templates.Select(t => t.OwnerTeamName).Should().BeEquivalentTo(DevAccessSpec.QaTeamNames);
        DevAccessSpec.Templates.Select(t => t.Name).Should().OnlyContain(n => n.StartsWith("QA ·"));
    }
}
