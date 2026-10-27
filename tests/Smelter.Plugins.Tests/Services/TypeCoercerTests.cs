using System;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class TypeCoercerTests
    {
        private readonly TypeCoercer _sut = new TypeCoercer();

        [Theory]
        [InlineData(ColumnDataType.Integer, "SECRET-4111")]
        [InlineData(ColumnDataType.Decimal, "SECRET-4111")]
        [InlineData(ColumnDataType.DateTime, "SECRET-4111")]
        [InlineData(ColumnDataType.Boolean, "SECRET-4111")]
        [InlineData(ColumnDataType.Money, "SECRET-4111")]
        [InlineData(ColumnDataType.Guid, "SECRET-4111")]
        [InlineData(ColumnDataType.OptionSet, "SECRET-4111")]
        public void Coerce_Invalid_MessageNamesTypeAndLengthButNeverTheValue(ColumnDataType type, string value)
        {
            Action act = () => _sut.Coerce(value, type);

            var ex = act.Should().Throw<FormatException>().Which;
            ex.Message.Should().Contain(type == ColumnDataType.Guid ? "GUID" : type.ToString());
            ex.Message.Should().Contain("(11 chars)");
            ex.Message.Should().NotContain("SECRET").And.NotContain("4111");
        }

        // --- String ---

        [Fact]
        public void Coerce_String_ReturnsAsIs()
        {
            _sut.Coerce("Hello", ColumnDataType.String)
                .Should().Be("Hello");
        }

        [Fact]
        public void Coerce_String_Null_ReturnsNull()
        {
            _sut.Coerce(null, ColumnDataType.String)
                .Should().BeNull();
        }

        [Fact]
        public void Coerce_String_TrimsSurroundingWhitespace()
        {
            // #13: String must trim edges like every other type (CSV trims, but Excel/defaults didn't).
            _sut.Coerce("  Hello  ", ColumnDataType.String)
                .Should().Be("Hello");
        }

        // --- Integer ---

        [Fact]
        public void Coerce_Integer_ValidNumber_ReturnsInt()
        {
            _sut.Coerce("42", ColumnDataType.Integer)
                .Should().Be(42);
        }

        [Fact]
        public void Coerce_Integer_WithWhitespace_Parses()
        {
            _sut.Coerce(" 42 ", ColumnDataType.Integer)
                .Should().Be(42);
        }

        [Fact]
        public void Coerce_Integer_Invalid_Throws()
        {
            Action act = () => _sut.Coerce("abc", ColumnDataType.Integer);
            act.Should().Throw<FormatException>();
        }

        [Fact]
        public void Coerce_Integer_Empty_ReturnsNull()
        {
            _sut.Coerce("", ColumnDataType.Integer)
                .Should().BeNull();
        }

        // --- Decimal ---

        [Fact]
        public void Coerce_Decimal_ValidNumber_ReturnsDecimal()
        {
            _sut.Coerce("3.14", ColumnDataType.Decimal)
                .Should().Be(3.14m);
        }

        [Fact]
        public void Coerce_Decimal_NegativeNumber_ReturnsDecimal()
        {
            _sut.Coerce("-99.5", ColumnDataType.Decimal)
                .Should().Be(-99.5m);
        }

        [Fact]
        public void Coerce_Decimal_Empty_ReturnsNull()
        {
            _sut.Coerce("", ColumnDataType.Decimal)
                .Should().BeNull();
        }

        // --- DateTime ---

        [Fact]
        public void Coerce_DateTime_IsoFormat_Parses()
        {
            var result = _sut.Coerce("2024-01-15", ColumnDataType.DateTime);
            result.Should().BeOfType<DateTime>();
            ((DateTime)result).Day.Should().Be(15);
            ((DateTime)result).Month.Should().Be(1);
            ((DateTime)result).Year.Should().Be(2024);
        }

        [Fact]
        public void Coerce_DateTime_WithTime_Parses()
        {
            var result = _sut.Coerce("2024-01-15T10:30:00", ColumnDataType.DateTime);
            result.Should().BeOfType<DateTime>();
            ((DateTime)result).Hour.Should().Be(10);
        }

        [Fact]
        public void Coerce_DateTime_Empty_ReturnsNull()
        {
            _sut.Coerce("", ColumnDataType.DateTime)
                .Should().BeNull();
        }

        [Fact]
        public void Coerce_DateTime_PlausibleExcelSerial_StillParses()
        {
            // 44941 ≈ 2023-01-15 as an Excel OADate — a legitimate serial date must still convert.
            var result = _sut.Coerce("44941", ColumnDataType.DateTime);
            result.Should().BeOfType<DateTime>();
            ((DateTime)result).Year.Should().BeInRange(2022, 2024);
        }

        [Fact]
        public void Coerce_DateTime_ImplausibleNumber_ThrowsInsteadOfAbsurdDate()
        {
            // #12: 200000 as OADate is year ~2447 — a stray ID/code must FAIL, not become an absurd date.
            Action act = () => _sut.Coerce("200000", ColumnDataType.DateTime);
            act.Should().Throw<FormatException>();
        }

        // --- Boolean ---

        [Theory]
        [InlineData("true", true)]
        [InlineData("True", true)]
        [InlineData("TRUE", true)]
        [InlineData("1", true)]
        [InlineData("yes", true)]
        [InlineData("Yes", true)]
        [InlineData("false", false)]
        [InlineData("False", false)]
        [InlineData("0", false)]
        [InlineData("no", false)]
        [InlineData("No", false)]
        public void Coerce_Boolean_KnownValues_ReturnsCorrect(string input, bool expected)
        {
            _sut.Coerce(input, ColumnDataType.Boolean)
                .Should().Be(expected);
        }

        [Fact]
        public void Coerce_Boolean_Empty_ReturnsNull()
        {
            _sut.Coerce("", ColumnDataType.Boolean)
                .Should().BeNull();
        }

        [Fact]
        public void Coerce_Boolean_Invalid_Throws()
        {
            Action act = () => _sut.Coerce("maybe", ColumnDataType.Boolean);
            act.Should().Throw<FormatException>();
        }

        // --- Money ---

        [Fact]
        public void Coerce_Money_ValidNumber_ReturnsMoney()
        {
            var result = _sut.Coerce("100.50", ColumnDataType.Money);
            result.Should().BeOfType<Money>();
            ((Money)result).Value.Should().Be(100.50m);
        }

        [Fact]
        public void Coerce_Money_Empty_ReturnsNull()
        {
            _sut.Coerce("", ColumnDataType.Money)
                .Should().BeNull();
        }

        // --- GUID ---

        [Fact]
        public void Coerce_Guid_ValidGuid_ReturnsGuid()
        {
            var guid = Guid.NewGuid();
            _sut.Coerce(guid.ToString(), ColumnDataType.Guid)
                .Should().Be(guid);
        }

        [Fact]
        public void Coerce_Guid_Empty_ReturnsNull()
        {
            _sut.Coerce("", ColumnDataType.Guid)
                .Should().BeNull();
        }

        [Fact]
        public void Coerce_Guid_Invalid_Throws()
        {
            Action act = () => _sut.Coerce("not-a-guid", ColumnDataType.Guid);
            act.Should().Throw<FormatException>();
        }

        // --- Null/Empty handling ---

        [Fact]
        public void Coerce_Null_AnyType_ReturnsNull()
        {
            _sut.Coerce(null, ColumnDataType.Integer).Should().BeNull();
            _sut.Coerce(null, ColumnDataType.Decimal).Should().BeNull();
            _sut.Coerce(null, ColumnDataType.DateTime).Should().BeNull();
            _sut.Coerce(null, ColumnDataType.Boolean).Should().BeNull();
        }
    }
}
