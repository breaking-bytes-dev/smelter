using System.Text;
using FluentAssertions;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class CsvParserServiceTests
    {
        private readonly CsvParserService _sut = new CsvParserService();

        private static byte[] ToBytes(string csv) => Encoding.UTF8.GetBytes(csv);

        private static FileParseOptions DefaultOptions(string delimiter = ",") =>
            new FileParseOptions { Delimiter = delimiter };

        [Fact]
        public void Parse_WithHeaderRow_ReturnsRowsKeyedByHeader()
        {
            var csv = "Name,Age,City\nJohn,30,NYC\nJane,25,LA";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions());

            result.Should().HaveCount(2);
            result[0].Values["Name"].Should().Be("John");
            result[0].Values["Age"].Should().Be("30");
            result[0].Values["City"].Should().Be("NYC");
            result[1].Values["Name"].Should().Be("Jane");
        }

        [Fact]
        public void Parse_WithHeaderRow_SetsCorrectRowNumbers()
        {
            var csv = "Name,Age\nJohn,30\nJane,25\nBob,40";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions());

            result[0].RowNumber.Should().Be(2, "first data row is row 2 (header is row 1)");
            result[1].RowNumber.Should().Be(3);
            result[2].RowNumber.Should().Be(4);
        }

        [Fact]
        public void Parse_WithoutHeaderRow_UsesColumnIndexAsKey()
        {
            var csv = "John,30,NYC\nJane,25,LA";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: false, DefaultOptions());

            result.Should().HaveCount(2);
            result[0].Values["Column0"].Should().Be("John");
            result[0].Values["Column1"].Should().Be("30");
            result[0].Values["Column2"].Should().Be("NYC");
            result[0].RowNumber.Should().Be(1);
        }

        [Fact]
        public void Parse_WithSemicolonDelimiter_ParsesCorrectly()
        {
            var csv = "Name;Age;City\nJohn;30;NYC";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions(";"));

            result.Should().HaveCount(1);
            result[0].Values["Name"].Should().Be("John");
            result[0].Values["Age"].Should().Be("30");
        }

        [Fact]
        public void Parse_WithTabDelimiter_ParsesCorrectly()
        {
            var csv = "Name\tAge\nJohn\t30";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions("\t"));

            result[0].Values["Name"].Should().Be("John");
            result[0].Values["Age"].Should().Be("30");
        }

        [Fact]
        public void Parse_WithQuotedFields_HandlesCommasInValues()
        {
            var csv = "Name,Address\nJohn,\"123 Main St, Apt 4\"";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions());

            result[0].Values["Address"].Should().Be("123 Main St, Apt 4");
        }

        [Fact]
        public void Parse_WithEmptyFields_ReturnsEmptyStrings()
        {
            var csv = "Name,Age,City\nJohn,,NYC";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions());

            result[0].Values["Age"].Should().BeEmpty();
        }

        [Fact]
        public void Parse_EmptyFile_ReturnsEmptyList()
        {
            var csv = "";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions());

            result.Should().BeEmpty();
        }

        [Fact]
        public void Parse_HeaderOnly_ReturnsEmptyList()
        {
            var csv = "Name,Age,City";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions());

            result.Should().BeEmpty();
        }

        [Fact]
        public void Parse_WithWhitespaceInHeaders_TrimsHeaders()
        {
            var csv = " Name , Age , City \nJohn,30,NYC";
            var result = _sut.Parse(ToBytes(csv), hasHeaderRow: true, DefaultOptions());

            result[0].Values.Should().ContainKey("Name");
            result[0].Values.Should().ContainKey("Age");
            result[0].Values.Should().ContainKey("City");
        }

        [Fact]
        public void Parse_WithBom_ParsesCorrectly()
        {
            // UTF-8 BOM: EF BB BF
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var csv = Encoding.UTF8.GetBytes("Name,Age\nJohn,30");
            var withBom = new byte[bom.Length + csv.Length];
            bom.CopyTo(withBom, 0);
            csv.CopyTo(withBom, bom.Length);

            var result = _sut.Parse(withBom, hasHeaderRow: true, DefaultOptions());

            result[0].Values.Should().ContainKey("Name");
        }
    }
}
