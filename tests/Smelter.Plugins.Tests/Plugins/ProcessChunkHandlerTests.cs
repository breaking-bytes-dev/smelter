using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using NSubstitute;
using Smelter.Plugins;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Plugins
{
    public class ProcessChunkHandlerTests
    {
        private static SourceRow Row(int n) => new SourceRow { RowNumber = n };

        private static ImportContext Ctx(int offset, int chunkSize, int initialFailed = 0) => new ImportContext
        {
            ImportJobId = Guid.NewGuid(),
            InitialProcessingBatch = offset,
            InitialFailedCount = initialFailed,
            Template = new ImportTemplateConfig { HasHeaderRow = true, Delimiter = ",", ChunkSize = chunkSize }
        };

        private sealed class Mocks
        {
            public IImportContextLoader Loader = Substitute.For<IImportContextLoader>();
            public IFileDownloader Downloader = Substitute.For<IFileDownloader>();
            public ICsvReader Csv = Substitute.For<ICsvReader>();
            public INormalizationEngine Engine = Substitute.For<INormalizationEngine>();
            public IUpsertWriter Writer = Substitute.For<IUpsertWriter>();
            public IOrganizationService Service = Substitute.For<IOrganizationService>();
            public ITracingService Tracing = Substitute.For<ITracingService>();
            public readonly byte[] Bytes = { 1 };

            public Mocks() => Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(Bytes);

            public IStaleLogPurger Purger = Substitute.For<IStaleLogPurger>();

            public ProcessChunkHandler Build() =>
                new ProcessChunkHandler(Loader, Downloader, Csv, Engine, Writer, Service, Tracing);

            public ProcessChunkHandler BuildWithPurger() =>
                new ProcessChunkHandler(Loader, Downloader, Csv, Engine, Writer, Service, Tracing, Purger);
        }

        private static int Status(Entity e) => ((OptionSetValue)e["sanic_smlt_ij_status"]).Value;
        private static int Offset(Entity e) => (int)e["sanic_smlt_ij_processingbatch"];

        private static IList<ProcessingResult> AllOk(IReadOnlyList<SourceRow> rows) =>
            rows.Select(r => ProcessingResult.Success(r.RowNumber, "t", Guid.NewGuid(), false)).ToList();

        [Theory]
        [InlineData(ImportTemplateStatus.Draft)]
        [InlineData(ImportTemplateStatus.Archived)]
        public void Run_TemplateNotActive_ThrowsBeforeDownloadOrAnyWrite(ImportTemplateStatus status)
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            ctx.Template.Status = (int)status;
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, 0);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*not Active*");
            m.Downloader.DidNotReceiveWithAnyArgs().Download(null, null);
            m.Engine.DidNotReceive().ProcessBatch(Arg.Any<ImportContext>(), Arg.Any<IList<SourceRow>>(), Arg.Any<IUpsertWriter>());
            m.Service.DidNotReceiveWithAnyArgs().Update(null);
        }

        [Theory]
        [InlineData(ImportJobStatus.Queued)]
        [InlineData(ImportJobStatus.Completed)]
        [InlineData(ImportJobStatus.CompletedWithErrors)]
        [InlineData(ImportJobStatus.Failed)]
        public void Run_JobNotProcessing_ThrowsBeforeDownloadOrAnyWrite(ImportJobStatus status)
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            ctx.JobStatus = (int)status;
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, 0);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*not in Processing state*");
            m.Downloader.DidNotReceiveWithAnyArgs().Download(null, null);
            m.Engine.DidNotReceive().ProcessBatch(Arg.Any<ImportContext>(), Arg.Any<IList<SourceRow>>(), Arg.Any<IUpsertWriter>());
            m.Service.DidNotReceiveWithAnyArgs().Update(null);
        }

        [Fact]
        public void Run_FullChunk_AdvancesOffsetByRowsAndReportsHasMore()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            var rows = Enumerable.Range(1, 1000).Select(Row).ToList();

            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 0, 1000).Returns(rows);
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(AllOk(rows));

            var result = m.Build().Run(jobId, chunkSizeOverride: 0);

            result.HasMore.Should().BeTrue();
            m.Service.Received(1).Update(Arg.Is<Entity>(e =>
                Offset(e) == 1000 && Status(e) == (int)ImportJobStatus.Processing));
        }

        [Fact]
        public void Run_PartialChunk_MarksCompleted()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 2000, chunkSize: 1000);
            var rows = Enumerable.Range(1, 400).Select(Row).ToList();

            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 2000, 1000).Returns(rows);
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(AllOk(rows));

            var result = m.Build().Run(jobId, 0);

            result.HasMore.Should().BeFalse();
            m.Service.Received(1).Update(Arg.Is<Entity>(e =>
                Offset(e) == 2400 && Status(e) == (int)ImportJobStatus.Completed));
        }

        [Fact]
        public void Run_NoRows_MarksCompletedWithoutProcessing()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            m.Loader.LoadContext(jobId).Returns(Ctx(offset: 9999, chunkSize: 1000));
            m.Csv.ReadRange(m.Bytes, true, ",", 9999, 1000).Returns(new List<SourceRow>());

            var result = m.Build().Run(jobId, 0);

            result.HasMore.Should().BeFalse();
            m.Engine.DidNotReceive().ProcessBatch(Arg.Any<ImportContext>(), Arg.Any<IList<SourceRow>>(), Arg.Any<IUpsertWriter>());
            m.Service.Received(1).Update(Arg.Is<Entity>(e => Status(e) == (int)ImportJobStatus.Completed));
        }

        [Fact]
        public void Run_UsesChunkSizeFromTemplate()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 4000);
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 0, 4000).Returns(new List<SourceRow>());

            m.Build().Run(jobId, chunkSizeOverride: 0);

            m.Csv.Received().ReadRange(m.Bytes, true, ",", 0, 4000);
        }

        [Fact]
        public void Run_LogsOnlyFailedRows_AsIdempotentUpsertWithCategory()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            var rows = Enumerable.Range(1, 3).Select(Row).ToList();

            ExecuteMultipleRequest captured = null;
            m.Service.Execute(Arg.Do<OrganizationRequest>(r => captured = r as ExecuteMultipleRequest))
                     .Returns(_ => new ExecuteMultipleResponse());

            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 0, 1000).Returns(rows);
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(new List<ProcessingResult>
            {
                ProcessingResult.Success(1, "t", Guid.NewGuid(), false),
                ProcessingResult.Failure(2, "t", "bad", ErrorCategory.TypeError),
                ProcessingResult.Success(3, "t", Guid.NewGuid(), false)
            });

            m.Build().Run(jobId, 0);

            captured.Should().NotBeNull();
            var upserts = captured.Requests.OfType<UpsertRequest>().ToList();
            upserts.Should().ContainSingle("only the 1 failed row is logged");
            var log = (Entity)upserts[0].Target;
            log.KeyAttributes.Should().ContainKey("sanic_smlt_ijl_rownumber");
            log.KeyAttributes["sanic_smlt_ijl_rownumber"].Should().Be(2);
            ((OptionSetValue)log["sanic_smlt_ijl_errorcategory"]).Value.Should().Be((int)ErrorCategory.TypeError);

            // Regresión 1.2.1.0: la key usaba sanic_smlt_ijl_id (nombre primario AUTONUMBER, nvarchar)
            // con un EntityReference → "Incorrect type of attribute value" en CADA upsert y el log
            // quedaba vacío. La key correcta referencia el FK real del job.
            log.KeyAttributes.Should().NotContainKey("sanic_smlt_ijl_id",
                "sanic_smlt_ijl_id es el autonumber del nombre primario, no el lookup al job");
            log.KeyAttributes.Should().ContainKey(ProcessChunkHandler.LogJobLookupAttribute);
            var keyRef = log.KeyAttributes[ProcessChunkHandler.LogJobLookupAttribute].Should()
                .BeOfType<EntityReference>().Subject;
            keyRef.LogicalName.Should().Be(ProcessChunkHandler.ImportJobTable);
            keyRef.Id.Should().Be(jobId);

            // El lookup required también viaja como atributo (el create no depende de la copia de KeyAttributes).
            var attrRef = log.GetAttributeValue<EntityReference>(ProcessChunkHandler.LogJobLookupAttribute);
            attrRef.Should().NotBeNull();
            attrRef.Id.Should().Be(jobId);
        }

        // ── Cursor de bytes: rango en vez de descarga completa ───────────────────────────────────

        private static ImportContext CursorCtx(int offset, long cursor, int chunkSize)
        {
            var ctx = Ctx(offset, chunkSize);
            ctx.InitialByteCursor = cursor;
            return ctx;
        }

        private sealed class FakeRangedFile : IRangedFile
        {
            public long Length => 1000;
            public byte[] Read(long offset, int count) => new byte[0];
        }

        [Fact]
        public void Run_WithByteCursor_DownloadsOnlyTheRangeAndPersistsTheNextCursor()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var file = new FakeRangedFile();
            var headers = new List<string> { "name" };
            var ctx = CursorCtx(offset: 1000, cursor: 5000, chunkSize: 1000);
            var rows = Enumerable.Range(1001, 1000).Select(Row).ToList();

            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Downloader.OpenRanged(Arg.Any<EntityReference>(), "sanic_smlt_ij_sourcefile").Returns(file);
            m.Csv.ReadHeaderInfo(file, true, ",").Returns(new CsvHeaderInfo { Supported = true, Headers = headers, DataStartOffset = 10 });
            // RowNumber de la primera fila = fila física 2 (cabecera = 1) + offset de filas ya consumidas.
            m.Csv.ReadChunk(file, 5000, headers, ",", 1002, 1000).Returns(new CsvChunk { Rows = rows, NextOffset = 9000 });
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(AllOk(rows));

            var result = m.Build().Run(jobId, 0);

            result.HasMore.Should().BeTrue();
            m.Downloader.DidNotReceiveWithAnyArgs().Download(null, null);
            m.Service.Received(1).Update(Arg.Is<Entity>(e =>
                Offset(e) == 2000 && (decimal)e["sanic_smlt_ij_bytecursor"] == 9000m));
        }

        [Fact]
        public void Run_WithByteCursor_PartialChunk_CompletesAndKeepsTheCursor()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var file = new FakeRangedFile();
            var headers = new List<string> { "name" };
            var ctx = CursorCtx(offset: 0, cursor: 10, chunkSize: 1000);
            var rows = Enumerable.Range(2, 400).Select(Row).ToList();

            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Downloader.OpenRanged(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(file);
            m.Csv.ReadHeaderInfo(file, true, ",").Returns(new CsvHeaderInfo { Supported = true, Headers = headers, DataStartOffset = 10 });
            m.Csv.ReadChunk(file, 10, headers, ",", 2, 1000).Returns(new CsvChunk { Rows = rows, NextOffset = 777 });
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(AllOk(rows));

            var result = m.Build().Run(jobId, 0);

            result.HasMore.Should().BeFalse();
            m.Service.Received(1).Update(Arg.Is<Entity>(e =>
                Status(e) == (int)ImportJobStatus.Completed && (decimal)e["sanic_smlt_ij_bytecursor"] == 777m));
        }

        [Fact]
        public void Run_WithByteCursor_NoRows_CompletesWithoutProcessing()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var file = new FakeRangedFile();
            var headers = new List<string> { "name" };
            m.Loader.LoadContext(jobId).Returns(CursorCtx(offset: 7, cursor: 999, chunkSize: 1000));
            m.Downloader.OpenRanged(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(file);
            m.Csv.ReadHeaderInfo(file, true, ",").Returns(new CsvHeaderInfo { Supported = true, Headers = headers });
            m.Csv.ReadChunk(file, 999, headers, ",", 9, 1000).Returns(new CsvChunk { Rows = new List<SourceRow>(), NextOffset = 999 });

            m.Build().Run(jobId, 0).HasMore.Should().BeFalse();

            m.Engine.DidNotReceive().ProcessBatch(Arg.Any<ImportContext>(), Arg.Any<IList<SourceRow>>(), Arg.Any<IUpsertWriter>());
            m.Service.Received(1).Update(Arg.Is<Entity>(e => Status(e) == (int)ImportJobStatus.Completed));
        }

        [Fact]
        public void Run_WithoutByteCursor_FallsBackToFullDownloadAndSkip_AndLeavesCursorUntouched()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            var rows = Enumerable.Range(1, 1000).Select(Row).ToList();
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 0, 1000).Returns(rows);
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(AllOk(rows));

            m.Build().Run(jobId, 0);

            m.Downloader.DidNotReceiveWithAnyArgs().OpenRanged(null, null);
            m.Service.Received(1).Update(Arg.Is<Entity>(e => !e.Contains("sanic_smlt_ij_bytecursor")));
        }

        // ── Gate de idempotencia también en ProcessChunk ─────────────────────────────────────────

        [Fact]
        public void Run_MappingWithoutDedupColumnsAndNotInsertOnly_ThrowsBeforeDownloadOrAnyWrite()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            ctx.TableMappings.Add(new TableMappingConfig { Name = "Facturas", TargetTable = "invoice" });
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, 0);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*has no dedup columns*");
            m.Downloader.DidNotReceiveWithAnyArgs().Download(null, null);
            m.Downloader.DidNotReceiveWithAnyArgs().OpenRanged(null, null);
            m.Service.DidNotReceiveWithAnyArgs().Update(null);
        }

        [Fact]
        public void Run_InsertOnlyMappingWithoutDedupColumns_PassesTheGate()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            ctx.TableMappings.Add(new TableMappingConfig { Name = "Facturas", TargetTable = "invoice", InsertOnly = true });
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 0, 1000).Returns(new List<SourceRow>());

            Action act = () => m.Build().Run(jobId, 0);

            act.Should().NotThrow();
        }

        // ── continuación del borrado de logs de la corrida anterior (1.0.16) ───────────────────────

        [Fact]
        public void Run_WithPurger_DeletesLeftoverLogsOfThePreviousRun_BeforeWritingThisChunksLogs()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var startedAt = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            ctx.JobStartTime = startedAt;
            var rows = Enumerable.Range(1, 3).Select(Row).ToList();
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 0, 1000).Returns(rows);
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(AllOk(rows));

            m.BuildWithPurger().Run(jobId, 0);

            Received.InOrder(() =>
            {
                m.Purger.Purge(jobId, startedAt);
                m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer);
            });
        }

        [Fact]
        public void Run_WithPurgerButNoStartTime_DoesNotPurge()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000); // job preparado por una versión anterior: sin starttime
            var rows = Enumerable.Range(1, 3).Select(Row).ToList();
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Csv.ReadRange(m.Bytes, true, ",", 0, 1000).Returns(rows);
            m.Engine.ProcessBatch(ctx, Arg.Any<IList<SourceRow>>(), m.Writer).Returns(AllOk(rows));

            m.BuildWithPurger().Run(jobId, 0);

            m.Purger.DidNotReceiveWithAnyArgs().Purge(Guid.Empty, default(DateTime));
        }

        [Fact]
        public void Run_JobNotProcessing_DoesNotPurge()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = Ctx(offset: 0, chunkSize: 1000);
            ctx.JobStartTime = DateTime.UtcNow;
            ctx.JobStatus = (int)ImportJobStatus.Completed;
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.BuildWithPurger().Run(jobId, 0);

            act.Should().Throw<InvalidPluginExecutionException>();
            m.Purger.DidNotReceiveWithAnyArgs().Purge(Guid.Empty, default(DateTime));
        }
    }
}
