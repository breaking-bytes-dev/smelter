using FluentAssertions;
using Smelter.Plugins.Models;
using Xunit;

namespace Smelter.Core.Tests.Models
{
    public class SourceRowTests
    {
        [Fact]
        public void Values_DefaultsToCaseInsensitiveKeys()
        {
            var row = new SourceRow();
            row.Values["Email"] = "a@b.c";

            row.Values.TryGetValue("email", out var value).Should().BeTrue();
            value.Should().Be("a@b.c");
            row.Values.ContainsKey("EMAIL").Should().BeTrue();
        }
    }
}
