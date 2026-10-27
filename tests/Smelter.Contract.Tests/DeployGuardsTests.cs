using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>Guardas de Smelter.Deploy: solo corre con SMELTER_ENV=dev; lo destructivo es opt-in.</summary>
public class DeployGuardsTests
{
    [Theory]
    [InlineData("dev")]
    [InlineData("DEV")]
    [InlineData(" Dev ")] // la implementación hace Trim + OrdinalIgnoreCase
    public void CheckEnvironment_Dev_IsAllowed(string env) =>
        DeployGuards.CheckEnvironment(env).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("prod")]
    [InlineData("test")]
    [InlineData("development")]
    public void CheckEnvironment_NotDev_ReturnsRefusalMessage(string? env)
    {
        var msg = DeployGuards.CheckEnvironment(env);

        msg.Should().NotBeNull();
        msg.Should().Contain(DeployGuards.EnvVarName);
    }

    [Fact]
    public void CheckEnvironment_Null_MentionsUndefined() =>
        DeployGuards.CheckEnvironment(null).Should().Contain("<no definida>");

    [Fact]
    public void IsDestructiveAllowed_WithArg_IsTrueEvenIfEnvMissing() =>
        DeployGuards.IsDestructiveAllowed(new[] { "--other", "--allow-destructive" }, null).Should().BeTrue();

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    public void IsDestructiveAllowed_EnvTrue_IsTrue(string env) =>
        DeployGuards.IsDestructiveAllowed(Array.Empty<string>(), env).Should().BeTrue();

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData(null)] // variable no definida
    public void IsDestructiveAllowed_NoArgAndEnvNotTrue_IsFalse(string? env) =>
        DeployGuards.IsDestructiveAllowed(Array.Empty<string>(), env).Should().BeFalse();

    [Fact]
    public void IsDestructiveAllowed_EnvFalseButArgPresent_IsTrue() =>
        DeployGuards.IsDestructiveAllowed(new[] { DeployGuards.AllowDestructiveArg }, "false").Should().BeTrue();
}
