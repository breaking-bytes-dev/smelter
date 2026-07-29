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
            public IAccessChecker Access = Substitute.For<IAccessChecker>();

            public Mocks()
            {
                // Default: access granted, so pre-existing tests (unrelated to Fase 5) don't need
                // to know about the access check at all.
                Access.HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>()).Returns(true);
            }

            public PrepareImportHandler Build() => new PrepareImportHandler(Loader, Downloader, Csv, Service, Access);
        }

        [Fact]
        public void Run_CountsRowsValidatesHeadersAndResetsJob()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name", "code"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name", "code" });
            m.Csv.CountRows(bytes, true, ",").Returns(42);

            var total = m.Build().Run(jobId, userId);

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
            var userId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name", "code"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" }); // falta "code"

            Action act = () => m.Build().Run(jobId, userId);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*code*");
            m.Service.DidNotReceive().Update(Arg.Any<Entity>());
        }

        // ── 1.3.0.0: gate de estado del template — solo Active es ejecutable ─────────────────────

        [Theory]
        [InlineData(ImportTemplateStatus.Draft)]
        [InlineData(ImportTemplateStatus.Archived)]
        public void Run_TemplateNotActive_ThrowsBeforeAccessCheckDownloadOrUpdate(ImportTemplateStatus status)
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var ctx = ContextWith(true, "name");
            ctx.Template.Status = (int)status;
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, userId);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*not Active*");
            m.Access.DidNotReceive().HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>());
            m.Downloader.DidNotReceive().Download(Arg.Any<EntityReference>(), Arg.Any<string>());
            m.Service.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void Run_ActiveTemplate_PassesTheStatusGate()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            var ctx = ContextWith(true, "name");
            ctx.Template.Status = (int)ImportTemplateStatus.Active;
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" });
            m.Csv.CountRows(bytes, true, ",").Returns(3);

            m.Build().Run(jobId, Guid.NewGuid()).Should().Be(3);
        }

        // ── Fase 5: validación de acceso al Template (smelter-access-control) ─────────────────────

        [Fact]
        public void Run_InitiatingUserWithoutReadAccessOnTemplate_ThrowsAndDoesNotDownloadOrUpdate()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var templateId = Guid.NewGuid();

            var ctx = ContextWith(true, "name", "code");
            ctx.Template.Id = templateId;
            m.Loader.LoadContext(jobId).Returns(ctx);
            // Overrides the Mocks() default (true) for this specific templateId/user pair.
            m.Access.HasReadAccess(userId, Arg.Any<EntityReference>()).Returns(false);

            Action act = () => m.Build().Run(jobId, userId);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*Read access*");
            m.Access.Received(1).HasReadAccess(userId, Arg.Is<EntityReference>(r =>
                r.LogicalName == ImportContextLoader.ImportTemplateTable && r.Id == templateId));
            m.Downloader.DidNotReceive().Download(Arg.Any<EntityReference>(), Arg.Any<string>());
            m.Service.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void Run_InitiatingUserWithReadAccessOnTemplate_CompletesNormally()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var templateId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            var ctx = ContextWith(true, "name", "code");
            ctx.Template.Id = templateId;
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Access.HasReadAccess(userId, Arg.Any<EntityReference>()).Returns(true);
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name", "code" });
            m.Csv.CountRows(bytes, true, ",").Returns(7);

            var total = m.Build().Run(jobId, userId);

            total.Should().Be(7);
            m.Access.Received(1).HasReadAccess(userId, Arg.Is<EntityReference>(r =>
                r.LogicalName == ImportContextLoader.ImportTemplateTable && r.Id == templateId));
            m.Service.Received(1).Update(Arg.Any<Entity>());
        }
    }
}
