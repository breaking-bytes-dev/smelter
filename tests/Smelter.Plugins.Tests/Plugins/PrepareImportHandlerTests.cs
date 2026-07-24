using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using Smelter.Plugins;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Plugins
{
    public class PrepareImportHandlerTests
    {
        private static ImportContext ContextWith(bool hasHeader, params string[] sourceColumns)
        {
            var ctx = new ImportContext
            {
                Template = new ImportTemplateConfig { HasHeaderRow = hasHeader, Delimiter = "," }
            };
            var tm = new TableMappingConfig { Id = Guid.NewGuid(), TargetTable = "t" };
            foreach (var sc in sourceColumns)
                tm.ColumnMappings.Add(new ColumnMappingConfig
                {
                    Id = Guid.NewGuid(), SourceColumn = sc, TargetColumn = "x_" + sc, DataType = ColumnDataType.String
                });
            ctx.TableMappings.Add(tm);
            return ctx;
        }

        private sealed class Mocks
        {
            public IImportContextLoader Loader = Substitute.For<IImportContextLoader>();
            public IFileDownloader Downloader = Substitute.For<IFileDownloader>();
            public ICsvReader Csv = Substitute.For<ICsvReader>();
            public IOrganizationService Service = Substitute.For<IOrganizationService>();

            public PrepareImportHandler Build() => new PrepareImportHandler(Loader, Downloader, Csv, Service);
        }

        [Fact]
        public void Run_CountsRowsValidatesHeadersAndResetsJob()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name", "code"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name", "code" });
            m.Csv.CountRows(bytes, true, ",").Returns(42);

            var total = m.Build().Run(jobId);

            total.Should().Be(42);
            m.Service.Received(1).Update(Arg.Is<Entity>(e =>
                (int)e["sanic_smlt_ij_totalrows"] == 42
                && (int)e["sanic_smlt_ij_processingbatch"] == 0
                && ((OptionSetValue)e["sanic_smlt_ij_status"]).Value == (int)ImportJobStatus.Processing));
        }

        [Fact]
        public void Run_MissingMappedHeader_ThrowsAndDoesNotStart()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name", "code"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" }); // falta "code"

            Action act = () => m.Build().Run(jobId);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*code*");
            m.Service.DidNotReceive().Update(Arg.Any<Entity>());
        }
    }
}
