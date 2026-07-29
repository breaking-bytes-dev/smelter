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

            public ProcessChunkHandler Build() =>
                new ProcessChunkHandler(Loader, Downloader, Csv, Engine, Writer, Service, Tracing);
        }

        private static int Status(Entity e) => ((OptionSetValue)e["sanic_smlt_ij_status"]).Value;
        private static int Offset(Entity e) => (int)e["sanic_smlt_ij_processingbatch"];

        private static IList<ProcessingResult> AllOk(IReadOnlyList<SourceRow> rows) =>
            rows.Select(r => ProcessingResult.Success(r.RowNumber, "t", Guid.NewGuid(), false)).ToList();

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
    }
}
