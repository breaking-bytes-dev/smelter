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
            var tm = new TableMappingConfig { Id = Guid.NewGuid(), Name = "Mapping", TargetTable = "t", DedupColumns = "k" };
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

        // ── Cursor de bytes: Prepare lo deja en el primer byte de datos (después de BOM + cabecera) ──

        [Fact]
        public void Run_Utf8File_ResetsByteCursorToTheFirstDataByte()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" });
            m.Csv.CountRows(bytes, true, ",").Returns(3);
            m.Csv.ReadHeaderInfo(bytes, true, ",").Returns(new CsvHeaderInfo
            {
                Supported = true, Headers = new List<string> { "name" }, DataStartOffset = 13
            });

            m.Build().Run(jobId, Guid.NewGuid());

            m.Service.Received(1).Update(Arg.Is<Entity>(e =>
                (int)e["sanic_smlt_ij_processingbatch"] == 0
                && (decimal)e[PrepareImportHandler.ByteCursorAttribute] == 13m));
        }

        [Fact]
        public void Run_FileThatCannotBeWalkedByBytes_LeavesTheByteCursorNull_SoProcessChunkFallsBackToLegacy()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" });
            m.Csv.CountRows(bytes, true, ",").Returns(3);
            m.Csv.ReadHeaderInfo(bytes, true, ",").Returns(new CsvHeaderInfo { Supported = false });

            m.Build().Run(jobId, Guid.NewGuid());

            m.Service.Received(1).Update(Arg.Is<Entity>(e =>
                e.Contains(PrepareImportHandler.ByteCursorAttribute) && e[PrepareImportHandler.ByteCursorAttribute] == null));
        }

        // ── Gate de idempotencia: todo Table Mapping necesita dedup columns, salvo "Solo inserción" ──

        private const string DedupMessage =
            "Table Mapping 'Facturas' (invoice) has no dedup columns: Smelter requires an alternate key so " +
            "reprocessing never duplicates. Set dedup columns or enable 'Insert only (allows duplicates)'.";

        private static ImportContext ContextWithoutDedup(bool insertOnly, string dedup = null)
        {
            var ctx = ContextWith(true, "name");
            ctx.TableMappings.Add(new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Facturas", TargetTable = "invoice",
                DedupColumns = dedup, InsertOnly = insertOnly
            });
            return ctx;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ,  ")]
        public void Run_MappingWithoutDedupColumnsAndNotInsertOnly_IsRejectedBeforeDownloadOrUpdate(string dedup)
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            m.Loader.LoadContext(jobId).Returns(ContextWithoutDedup(insertOnly: false, dedup));

            Action act = () => m.Build().Run(jobId, Guid.NewGuid());

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage(DedupMessage);
            m.Downloader.DidNotReceive().Download(Arg.Any<EntityReference>(), Arg.Any<string>());
            m.Service.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void Run_MappingWithoutDedupColumnsButInsertOnly_PassesTheGate()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };
            m.Loader.LoadContext(jobId).Returns(ContextWithoutDedup(insertOnly: true));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" });
            m.Csv.CountRows(bytes, true, ",").Returns(2);

            m.Build().Run(jobId, Guid.NewGuid()).Should().Be(2);
        }

        [Fact]
        public void ValidateDedupGate_AllMappingsKeyedOrInsertOnly_DoesNotThrow()
        {
            var mappings = new List<TableMappingConfig>
            {
                new TableMappingConfig { Name = "A", TargetTable = "a", DedupColumns = "k1,k2" },
                new TableMappingConfig { Name = "B", TargetTable = "b", InsertOnly = true }
            };

            Action act = () => PrepareImportHandler.ValidateDedupGate(mappings);

            act.Should().NotThrow();
        }
    }
}
