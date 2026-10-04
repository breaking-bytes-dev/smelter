using System;
using System.Collections.Generic;
using FluentAssertions;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class TransformEngineTests
    {
        private readonly TransformEngine _sut = new TransformEngine();

        [Theory]
        [InlineData("UPPER(name)", " JOHN ")]
        [InlineData("LOWER(name)", " john ")]
        [InlineData("TRIM(name)", "John")] // " John " -> "John"
        [InlineData("STATIC(\"Hello\")", "Hello")]
        public void Evaluate_SimpleFunctions(string expression, string expected)
        {
            var row = new Dictionary<string, string>
            {
                { "name", " John " }
            };

            var result = _sut.Evaluate(expression, row);

            result.Should().Be(expected);
        }

        [Fact]
        public void Evaluate_Concat_MultipleArgs()
        {
            var row = new Dictionary<string, string>
            {
                { "first", "John" },
                { "last", "Doe" }
            };

            var result = _sut.Evaluate("CONCAT(first, \" \", last)", row);

            result.Should().Be("John Doe");
        }

        [Fact]
        public void Evaluate_NestedFunctions()
        {
            var row = new Dictionary<string, string>
            {
                { "first", " john " },
                { "last", "doe" }
            };

            // UPPER(TRIM(first)) -> "JOHN"
            // CONCAT("JOHN", "-", "doe") -> "JOHN-doe"
            var result = _sut.Evaluate("CONCAT(UPPER(TRIM(first)), \"-\", last)", row);

            result.Should().Be("JOHN-doe");
        }

        [Fact]
        public void Evaluate_Left_Right()
        {
            var row = new Dictionary<string, string>
            {
                { "code", "ABCDEF" }
            };

            _sut.Evaluate("LEFT(code, 3)", row).Should().Be("ABC");
            _sut.Evaluate("RIGHT(code, 2)", row).Should().Be("EF");
        }

        [Fact]
        public void Evaluate_DateParse()
        {
            var row = new Dictionary<string, string>
            {
                { "fecha", "31/12/2023" }
            };

            var result = _sut.Evaluate("DATEPARSE(fecha, \"dd/MM/yyyy\")", row);
            
            // Returns an ISO formatted string or a DateTime string
            // We should standardise to what TypeCoercer expects, e.g., ISO8601
            result.Should().Be("2023-12-31T00:00:00.0000000");
        }

        [Fact]
        public void Evaluate_Replace()
        {
            var row = new Dictionary<string, string>
            {
                { "phone", "555-123-456" }
            };

            var result = _sut.Evaluate("REPLACE(phone, \"-\", \"\")", row);

            result.Should().Be("555123456");
        }

        [Fact]
        public void Evaluate_Split()
        {
            var row = new Dictionary<string, string>
            {
                { "name", "John Smith Doe" }
            };

            _sut.Evaluate("SPLIT(name, \" \", 0)", row).Should().Be("John");
            _sut.Evaluate("SPLIT(name, \" \", 1)", row).Should().Be("Smith");
            _sut.Evaluate("SPLIT(name, \" \", 2)", row).Should().Be("Doe");
        }

        [Fact]
        public void Evaluate_If()
        {
            var row = new Dictionary<string, string>
            {
                { "status", "Active" }
            };

            _sut.Evaluate("IF(status, \"Active\", \"1\", \"0\")", row).Should().Be("1");
            _sut.Evaluate("IF(status, \"Inactive\", \"1\", \"0\")", row).Should().Be("0");
        }

        [Fact]
        public void Evaluate_Coalesce()
        {
            var row = new Dictionary<string, string>
            {
                { "mobile", "" },
                { "phone", "123" }
            };

            _sut.Evaluate("COALESCE(mobile, phone)", row).Should().Be("123");
        }

        [Fact]
        public void Evaluate_Map()
        {
            var row = new Dictionary<string, string>
            {
                { "state", "NY" }
            };

            var mapJson = "{\"NY\":\"New York\",\"CA\":\"California\"}";
            _sut.Evaluate($"MAP(state, \"{mapJson.Replace("\"", "\"\"")}\")", row).Should().Be("New York");
        }

        [Fact]
        public void Evaluate_InvalidFunction_Throws()
        {
            var row = new Dictionary<string, string>();
            Action act = () => _sut.Evaluate("UNKNOWN(field)", row);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void Evaluate_SyntaxError_Throws()
        {
            var row = new Dictionary<string, string>();
            Action act = () => _sut.Evaluate("UPPER(field", row);

            act.Should().Throw<InvalidOperationException>().WithMessage("*Expected*");
        }

        #region HMAC

        // Vector conocido: key = bytes 0x00..0x1f (32 bytes), HMAC-SHA256("4111111111111111").
        // Calculado con Python hmac/hashlib (paridad con paso3_backfill.py de Cobranza Móvil).
        private const string Pan = "4111111111111111";
        private const string PanHmac = "0622241201382a45912fb22828b3f7db5153cf2072722a73ded22623ea79abc9";

        private static byte[] SequentialKey(int length = 32)
        {
            var key = new byte[length];
            for (int i = 0; i < length; i++) key[i] = (byte)i;
            return key;
        }

        /// <summary>Provider en memoria; registra los nombres de key pedidos.</summary>
        private sealed class FakeKeyProvider : IHmacKeyProvider
        {
            private readonly Dictionary<string, byte[]> _keys;
            public List<string> Requested { get; } = new List<string>();

            public FakeKeyProvider(string name, byte[] key)
            {
                _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                if (name != null) _keys[name] = key;
            }

            public byte[] GetKey(string keyName)
            {
                Requested.Add(keyName);
                _keys.TryGetValue(keyName, out var key);
                return key;
            }
        }

        private static TransformEngine EngineWithKey(string name = "k", byte[] key = null) =>
            new TransformEngine(new FakeKeyProvider(name, key ?? SequentialKey()));

        [Fact]
        public void Hmac_KnownVector_ReturnsLowercaseHex()
        {
            var result = EngineWithKey().Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>());

            result.Should().Be(PanHmac);
            result.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        }

        [Fact]
        public void Hmac_TrimsInput()
        {
            var row = new Dictionary<string, string> { { "pan", " " + Pan + " " } };

            EngineWithKey().Evaluate("HMAC(pan, \"k\")", row).Should().Be(PanHmac);
        }

        [Fact]
        public void Hmac_EmptyInput_ReturnsEmptyWithoutTouchingTheKey()
        {
            var provider = new FakeKeyProvider("k", SequentialKey());
            var sut = new TransformEngine(provider);

            sut.Evaluate("HMAC(\"\", \"k\")", new Dictionary<string, string>()).Should().Be("");
            sut.Evaluate("HMAC(pan, \"k\")", new Dictionary<string, string> { { "pan", "   " } }).Should().Be("");
            sut.Evaluate("HMAC(missingcolumn, \"k\")", new Dictionary<string, string>()).Should().Be("");

            provider.Requested.Should().BeEmpty();
        }

        [Fact]
        public void Hmac_EmptyInput_WithoutProvider_ReturnsEmpty()
        {
            new TransformEngine().Evaluate("HMAC(\"\", \"k\")", new Dictionary<string, string>()).Should().Be("");
        }

        [Fact]
        public void Hmac_LowercaseFunctionName_Works()
        {
            var row = new Dictionary<string, string> { { "Col", Pan } };

            EngineWithKey().Evaluate("hmac(Col, \"k\")", row).Should().Be(PanHmac);
        }

        [Fact]
        public void Hmac_ComposedWithColumnReference_Works()
        {
            var row = new Dictionary<string, string> { { "pan", " " + Pan } };

            // Compuesta: el valor sale de otra función; el nombre de la key es literal.
            EngineWithKey().Evaluate("HMAC(TRIM(pan), \"k\")", row).Should().Be(PanHmac);
            // Y como argumento de otra función.
            EngineWithKey().Evaluate("UPPER(HMAC(pan, \"k\"))", row).Should().Be(PanHmac.ToUpperInvariant());
        }

        [Fact]
        public void Hmac_RequestsTheKeyByTheNameArgument()
        {
            var provider = new FakeKeyProvider("cm-pan", SequentialKey());

            new TransformEngine(provider).Evaluate($"HMAC(\"{Pan}\", \"cm-pan\")", new Dictionary<string, string>())
                .Should().Be(PanHmac);

            provider.Requested.Should().Equal("cm-pan");
        }

        [Fact]
        public void Hmac_NoProvider_Throws()
        {
            Action act = () => new TransformEngine().Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>());

            act.Should().Throw<InvalidOperationException>().WithMessage("*no key provider*");
        }

        [Fact]
        public void Hmac_KeyShorterThan32Bytes_Throws()
        {
            Action act = () => EngineWithKey(key: SequentialKey(31))
                .Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>());

            act.Should().Throw<InvalidOperationException>().WithMessage("*'k'*32 bytes*");
        }

        [Fact]
        public void Hmac_KeyLongerThan32Bytes_IsAccepted()
        {
            var result = EngineWithKey(key: SequentialKey(64))
                .Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>());

            result.Should().HaveLength(64);
        }

        [Fact]
        public void Hmac_KeyNotFound_Throws()
        {
            Action act = () => EngineWithKey("other")
                .Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>());

            act.Should().Throw<InvalidOperationException>().WithMessage("*'k'*missing*");
        }

        [Theory]
        [InlineData("HMAC(\"4111111111111111\")")]
        [InlineData("HMAC(\"4111111111111111\", \"k\", \"extra\")")]
        public void Hmac_WrongArgumentCount_Throws(string expression)
        {
            Action act = () => EngineWithKey().Evaluate(expression, new Dictionary<string, string>());

            act.Should().Throw<InvalidOperationException>().WithMessage("HMAC expects*");
        }

        [Fact]
        public void Hmac_KeyNameFromColumn_ThrowsWithoutAskingTheProviderNorEchoingTheValue()
        {
            // HMAC(x, NumeroCuenta) pondría el PAN en el mensaje de error y en la consulta de la clave.
            var provider = new FakeKeyProvider("k", SequentialKey());
            var row = new Dictionary<string, string> { ["NumeroCuenta"] = Pan };

            Action act = () => new TransformEngine(provider).Evaluate("HMAC(NumeroCuenta, NumeroCuenta)", row);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("HMAC: the key name must be a string literal*")
                .Which.Message.Should().NotContain(Pan);
            provider.Requested.Should().BeEmpty();
        }

        [Theory]
        [InlineData("4111111111111111")]
        [InlineData("cm pan")]
        [InlineData("")]
        public void Hmac_KeyNameThatIsNotALabel_ThrowsWithoutEchoingIt(string keyName)
        {
            var provider = new FakeKeyProvider(keyName, SequentialKey());

            Action act = () => new TransformEngine(provider)
                .Evaluate($"HMAC(\"{Pan}\", \"{keyName}\")", new Dictionary<string, string>());

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("HMAC: the key name must be a string literal*")
                .Which.Message.Should().NotContain(Pan);
            provider.Requested.Should().BeEmpty();
        }

        [Fact]
        public void Hmac_ErrorMessagesNeverContainTheInputValueNorTheKey()
        {
            var keyHex = BitConverter.ToString(SequentialKey(31)).Replace("-", "").ToLowerInvariant();
            var missing = EngineWithKey("other");

            var actions = new List<Action>
            {
                () => new TransformEngine().Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>()),
                () => EngineWithKey(key: SequentialKey(31)).Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>()),
                () => missing.Evaluate($"HMAC(\"{Pan}\", \"k\")", new Dictionary<string, string>()),
                () => EngineWithKey().Evaluate($"HMAC(\"{Pan}\")", new Dictionary<string, string>()),
                () => EngineWithKey().Evaluate($"HMAC(\"{Pan}\", \"k\", \"x\")", new Dictionary<string, string>()),
            };

            foreach (var action in actions)
            {
                var ex = Assert.Throws<InvalidOperationException>(action);
                ex.ToString().Should().NotContain(Pan);
                ex.ToString().Should().NotContain(keyHex);
            }
        }

        #endregion
    }
}
