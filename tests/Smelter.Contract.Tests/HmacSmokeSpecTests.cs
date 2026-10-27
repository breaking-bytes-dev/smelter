using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>Piezas puras del smoke E2E de HMAC (HmacSmokeSpec): vector conocido, key aleatoria, regex del DSL.</summary>
public class HmacSmokeSpecTests
{
    [Fact]
    public void ComputeHex_MatchesRfc4231TestCase1()
    {
        // RFC 4231 caso 1 (key 20 x 0x0b, "Hi There"); el engine exige >= 32 bytes, pero el cálculo es el mismo.
        var keyHex = string.Concat(Enumerable.Repeat("0b", 20));
        HmacSmokeSpec.ComputeHex(keyHex, "Hi There")
            .Should().Be("b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7");
    }

    [Fact]
    public void NewKeyHex_Is32RandomBytesAsLowercaseHex_AndDiffersEachCall()
    {
        var a = HmacSmokeSpec.NewKeyHex();
        var b = HmacSmokeSpec.NewKeyHex();

        a.Should().MatchRegex("^[0-9a-f]{64}$");
        a.Should().NotBe(b);
    }

    [Fact]
    public void KeyLabel_IsAValidDslKeyNameAndOutsideRealKeyNames()
    {
        HmacSmokeSpec.KeyLabel.Should().Be("smoke-verify");
        HmacSmokeSpec.KeyLabel.Should().MatchRegex("^[A-Za-z][A-Za-z0-9_-]{0,49}$");
        HmacSmokeSpec.TransformExpression("Email").Should().Be("HMAC(Email, \"smoke-verify\")");
    }

    [Fact]
    public void ComputeHex_RejectsNonHexKeys()
    {
        var act = () => HmacSmokeSpec.ComputeHex("zz", "x");
        act.Should().Throw<FormatException>();
    }
}
