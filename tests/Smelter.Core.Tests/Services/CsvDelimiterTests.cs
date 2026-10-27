using System.Text;
using FluentAssertions;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class CsvDelimiterTests
    {
        [Theory]
        [InlineData("\\t", "\t")]
        [InlineData("TAB", "\t")]
        [InlineData("tab", "\t")]
        [InlineData("Tab", "\t")]
        [InlineData(" tab ", "\t")]
        [InlineData("\t", "\t")]
        [InlineData(";", ";")]
        [InlineData("|", "|")]
        [InlineData("||", "||")]
        [InlineData(",", ",")]
        [InlineData(null, ",")]
        [InlineData("", ",")]
        public void Resolve_MapsTabAliasesAndKeepsOthersLiteral(string configured, string expected)
        {
            CsvDelimiter.Resolve(configured).Should().Be(expected);
        }

        [Theory]
        [InlineData("\\t")]
        [InlineData("TAB")]
        [InlineData("\t")]
        public void Parser_ReadsTsvWhenTheTemplateDelimiterIsATabAlias(string configured)
        {
            var sut = new CsvParserService();
            var bytes = Encoding.UTF8.GetBytes("Name\tAge\nJohn\t30\nJane\t25");

            sut.ReadHeaders(bytes, true, configured).Should().Equal("Name", "Age");
            sut.CountRows(bytes, true, configured).Should().Be(2);
            var rows = sut.ReadRange(bytes, true, configured, 0, 10);
            rows[0].Values["Name"].Should().Be("John");
            rows[1].Values["Age"].Should().Be("25");
        }

        [Fact]
        public void Parser_ByteCursorPath_ReadsTsvWithAlias()
        {
            var sut = new CsvParserService();
            var bytes = Encoding.UTF8.GetBytes("Name\tAge\nJohn\t30\nJane\t25");

            var info = sut.ReadHeaderInfo(bytes, true, "TAB");
            info.Headers.Should().Equal("Name", "Age");
            var chunk = sut.ReadChunk(new ByteArrayRangeSource(bytes), info.DataStartOffset, info.Headers, "TAB", 2, 10);
            chunk.Rows.Should().HaveCount(2);
            chunk.Rows[0].Values["Age"].Should().Be("30");
        }
    }
}
