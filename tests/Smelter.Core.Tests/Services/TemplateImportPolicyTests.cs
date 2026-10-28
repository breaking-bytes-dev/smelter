using FluentAssertions;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class TemplateImportPolicyTests
    {
        [Fact]
        public void ResolveStatus_Create_WithoutStatusInJson_DefaultsToActive()
        {
            TemplateImportPolicy.ResolveStatus(null, isCreate: true).Should().Be((int)ImportTemplateStatus.Active);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ResolveStatus_Create_WithStatusInJson_UsesIt(int status)
        {
            TemplateImportPolicy.ResolveStatus(status, isCreate: true).Should().Be(status);
        }

        [Fact]
        public void ResolveStatus_Update_WithoutStatusInJson_LeavesTheCurrentOneUntouched()
        {
            TemplateImportPolicy.ResolveStatus(null, isCreate: false).Should().BeNull();
        }

        [Fact]
        public void ResolveStatus_Update_WithStatusInJson_AppliesIt()
        {
            TemplateImportPolicy.ResolveStatus((int)ImportTemplateStatus.Archived, isCreate: false)
                .Should().Be((int)ImportTemplateStatus.Archived);
        }
    }
}
