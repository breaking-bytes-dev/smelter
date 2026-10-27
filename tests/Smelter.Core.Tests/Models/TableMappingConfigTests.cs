using FluentAssertions;
using Smelter.Plugins.Models;
using Xunit;

namespace Smelter.Core.Tests.Models
{
    public class TableMappingConfigTests
    {
        [Theory]
        [InlineData("a, b", new[] { "a", "b" })]
        [InlineData(" a ,b ,  c", new[] { "a", "b", "c" })]
        [InlineData("a,,b, ,", new[] { "a", "b" })]
        [InlineData("", new string[0])]
        [InlineData(null, new string[0])]
        [InlineData("  ,  ", new string[0])]
        public void GetDedupColumnNames_TrimsEachNameAndIgnoresEmpties(string dedup, string[] expected)
        {
            var tm = new TableMappingConfig { DedupColumns = dedup };

            tm.GetDedupColumnNames().Should().Equal(expected);
        }
    }
}
