using System.Linq;
using System.Text;
using FluentAssertions;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class CsvReaderTests
    {
        private readonly ICsvReader _sut = new CsvParserService();

        private static byte[] Csv(string content) => Encoding.UTF8.GetBytes(content);

        [Fact]
        public void ReadRange_WithHeader_ReturnsRequestedSliceWithPhysicalRowNumbers()
        {
            var csv = Csv("name,code\nA,1\nB,2\nC,3\nD,4\n"); // 4 data rows (físicas 2..5)

            var rows = _sut.ReadRange(csv, hasHeaderRow: true, delimiter: ",", skip: 1, take: 2);

            rows.Should().HaveCount(2);
            rows[0].Values["name"].Should().Be("B");
            rows[0].RowNumber.Should().Be(3); // header=1, A=2, B=3
            rows[1].Values["name"].Should().Be("C");
            rows[1].RowNumber.Should().Be(4);
        }

        [Fact]
        public void ReadRange_TakeLimitsResult()
        {
            var csv = Csv("name\nA\nB\nC\nD\n");

            var rows = _sut.ReadRange(csv, hasHeaderRow: true, delimiter: ",", skip: 0, take: 2);

            rows.Should().HaveCount(2);
            rows.Select(r => r.Values["name"]).Should().ContainInOrder("A", "B");
        }

        [Fact]
        public void ReadRange_SkipBeyondEnd_ReturnsEmpty()
        {
            var csv = Csv("name\nA\nB\n");

            var rows = _sut.ReadRange(csv, hasHeaderRow: true, delimiter: ",", skip: 10, take: 5);

            rows.Should().BeEmpty();
        }

        [Fact]
        public void ReadRange_NoHeader_UsesColumnKeysAndRowNumberFromOne()
        {
            var csv = Csv("A,1\nB,2\n");

            var rows = _sut.ReadRange(csv, hasHeaderRow: false, delimiter: ",", skip: 0, take: 1);

            rows.Should().ContainSingle();
            rows[0].Values["Column0"].Should().Be("A");
            rows[0].Values["Column1"].Should().Be("1");
            rows[0].RowNumber.Should().Be(1);
        }

        [Fact]
        public void CountRows_WithHeader_CountsDataRowsOnly()
        {
            _sut.CountRows(Csv("name\nA\nB\nC\n"), hasHeaderRow: true, delimiter: ",")
                .Should().Be(3);
        }

        [Fact]
        public void CountRows_NoHeader_CountsAllRows()
        {
            _sut.CountRows(Csv("A\nB\n"), hasHeaderRow: false, delimiter: ",")
                .Should().Be(2);
        }

        [Fact]
        public void CountRows_Empty_ReturnsZero()
        {
            _sut.CountRows(new byte[0], hasHeaderRow: true, delimiter: ",").Should().Be(0);
        }

        [Fact]
        public void ReadHeaders_WithHeader_ReturnsTrimmedColumnNames()
        {
            var headers = _sut.ReadHeaders(Csv("name, code ,email\nA,1,x\n"), hasHeaderRow: true, delimiter: ",");

            headers.Should().ContainInOrder("name", "code", "email");
        }

        [Fact]
        public void ReadHeaders_NoHeader_ReturnsColumnKeys()
        {
            var headers = _sut.ReadHeaders(Csv("A,1\n"), hasHeaderRow: false, delimiter: ",");

            headers.Should().ContainInOrder("Column0", "Column1");
        }
    }
}
