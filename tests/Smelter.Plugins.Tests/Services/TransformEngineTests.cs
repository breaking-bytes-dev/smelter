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
    }
}
