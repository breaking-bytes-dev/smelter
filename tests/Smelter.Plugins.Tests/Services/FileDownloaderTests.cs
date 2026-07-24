using System;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class FileDownloaderTests
    {
        [Fact]
        public void Download_AssemblesBlocksIntoFullByteArray()
        {
            var service = Substitute.For<IOrganizationService>();

            var initResponse = new OrganizationResponse();
            initResponse["FileContinuationToken"] = "token";
            initResponse["FileSizeInBytes"] = (long)5;
            service.Execute(Arg.Is<OrganizationRequest>(r => r.RequestName == "InitializeFileBlocksDownload"))
                   .Returns(initResponse);

            var blockResponse = new OrganizationResponse();
            blockResponse["Data"] = new byte[] { 10, 20, 30, 40, 50 };
            service.Execute(Arg.Is<OrganizationRequest>(r => r.RequestName == "DownloadBlock"))
                   .Returns(blockResponse);

            var bytes = new FileDownloader(service).Download(
                new EntityReference("sanic_tbl_smlt_importjob", Guid.NewGuid()), "sanic_smlt_ij_sourcefile");

            bytes.Should().Equal(new byte[] { 10, 20, 30, 40, 50 });
        }
    }
}
