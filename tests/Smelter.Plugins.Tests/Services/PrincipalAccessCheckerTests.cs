using System;
using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    /// <summary>
    /// Tests <see cref="PrincipalAccessChecker"/>, the real <see cref="IAccessChecker"/> implementation
    /// backed by <c>RetrievePrincipalAccessRequest</c>. Same pattern as <c>FileDownloaderTests</c>:
    /// mock <see cref="IOrganizationService.Execute"/>, assert the typed request shape and the
    /// boolean derived from the response's <c>AccessRights</c> flags — no live Dataverse call.
    /// </summary>
    public class PrincipalAccessCheckerTests
    {
        [Fact]
        public void HasReadAccess_ResponseIncludesReadAccessFlag_ReturnsTrue()
        {
            var service = Substitute.For<IOrganizationService>();
            var principalId = Guid.NewGuid();
            var target = new EntityReference("sanic_tbl_smlt_importtemplate", Guid.NewGuid());

            var response = new RetrievePrincipalAccessResponse();
            response.Results["AccessRights"] = AccessRights.ReadAccess | AccessRights.WriteAccess;

            service.Execute(Arg.Is<OrganizationRequest>(r =>
                    (r as RetrievePrincipalAccessRequest) != null
                    && ((RetrievePrincipalAccessRequest)r).Principal.LogicalName == "systemuser"
                    && ((RetrievePrincipalAccessRequest)r).Principal.Id == principalId
                    && ((RetrievePrincipalAccessRequest)r).Target == target))
                .Returns(response);

            var result = new PrincipalAccessChecker(service).HasReadAccess(principalId, target);

            result.Should().BeTrue();
        }

        [Fact]
        public void HasReadAccess_ResponseWithoutReadAccessFlag_ReturnsFalse()
        {
            var service = Substitute.For<IOrganizationService>();
            var principalId = Guid.NewGuid();
            var target = new EntityReference("sanic_tbl_smlt_importtemplate", Guid.NewGuid());

            var response = new RetrievePrincipalAccessResponse();
            response.Results["AccessRights"] = AccessRights.WriteAccess | AccessRights.AssignAccess;

            service.Execute(Arg.Any<OrganizationRequest>()).Returns(response);

            var result = new PrincipalAccessChecker(service).HasReadAccess(principalId, target);

            result.Should().BeFalse();
        }

        [Fact]
        public void Constructor_NullService_Throws()
        {
            Action act = () => new PrincipalAccessChecker(null);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
