using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
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
            // La dedup column 'k' tiene que ser el Target Column de algún Column Mapping (TemplateValidator);
            // un transform STATIC no lee columna del CSV, así que no agrega nada al chequeo de headers.
            tm.ColumnMappings.Add(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), TargetColumn = "k", DataType = ColumnDataType.String, TransformExpression = "STATIC(\"1\")"
            });
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

            /// <summary>Todo lo que el handler ejecutó vía IOrganizationService.Execute (el reset va como UpdateRequest).</summary>
            public List<OrganizationRequest> Executed = new List<OrganizationRequest>();

            /// <summary>Entities de los UpdateRequest ejecutados (el reset/claim del job).</summary>
            public List<Entity> Resets => Executed.OfType<UpdateRequest>().Select(r => r.Target).ToList();

            /// <summary>Estado actual del job que devuelve Retrieve. Default: status null (legacy: llamada directa a la API).</summary>
            public void JobIs(int? status, string rowVersion = "1")
            {
                Service.Retrieve(PrepareImportHandler.ImportJobTable, Arg.Any<Guid>(), Arg.Any<ColumnSet>())
                    .Returns(ci =>
                    {
                        var e = new Entity(PrepareImportHandler.ImportJobTable, ci.ArgAt<Guid>(1)) { RowVersion = rowVersion };
                        if (status.HasValue) e["sanic_smlt_ij_status"] = new OptionSetValue(status.Value);
                        return e;
                    });
            }

            /// <summary>Hace que el UpdateRequest del claim falle con el fault dado (se registra igual en Executed).</summary>
            public void ExecuteFaults(int errorCode)
            {
                Service.When(x => x.Execute(Arg.Any<OrganizationRequest>())).Do(ci =>
                {
                    Executed.Add(ci.Arg<OrganizationRequest>());
                    throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = errorCode });
                });
            }

            public Mocks()
            {
                JobIs(null);
                Service.Execute(Arg.Do<OrganizationRequest>(r => Executed.Add(r)));
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
            m.Resets.Should().ContainSingle(e =>
                (int)e["sanic_smlt_ij_totalrows"] == 42
                && (int)e["sanic_smlt_ij_processingbatch"] == 0
                && ((OptionSetValue)e["sanic_smlt_ij_status"]).Value == (int)ImportJobStatus.Processing);
        }

        [Fact]
        public void Run_ResetsJob_ClearsStaleEndTime()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };

            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" });
            m.Csv.CountRows(bytes, true, ",").Returns(1);

            m.Build().Run(jobId, Guid.NewGuid());

            m.Resets.Should().ContainSingle(e =>
                e.Contains("sanic_smlt_ij_endtime") && e["sanic_smlt_ij_endtime"] == null);
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
            m.Executed.Should().BeEmpty();
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
            m.Executed.Should().BeEmpty();
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
            m.Executed.Should().BeEmpty();
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
            m.Executed.Should().ContainSingle();
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

            m.Resets.Should().ContainSingle(e =>
                (int)e["sanic_smlt_ij_processingbatch"] == 0
                && (decimal)e[PrepareImportHandler.ByteCursorAttribute] == 13m);
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

            m.Resets.Should().ContainSingle(e =>
                e.Contains(PrepareImportHandler.ByteCursorAttribute) && e[PrepareImportHandler.ByteCursorAttribute] == null);
        }

        // ── Claim atómico (1.0.12): una sola corrida del flow procesa el job ─────────────────────

        private static Mocks ReadyToPrepare(out Guid jobId, int rows = 5)
        {
            var m = new Mocks();
            jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };
            m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name"));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name" });
            m.Csv.CountRows(bytes, true, ",").Returns(rows);
            return m;
        }

        [Fact]
        public void Prepare_QueuedJob_ClaimsItWithRowVersionConcurrency()
        {
            var m = ReadyToPrepare(out var jobId, rows: 7);
            m.JobIs((int)ImportJobStatus.Queued, rowVersion: "123456");

            var result = m.Build().Prepare(jobId, Guid.NewGuid());

            result.Claimed.Should().BeTrue();
            result.TotalRows.Should().Be(7);
            var update = m.Executed.Should().ContainSingle().Which.Should().BeOfType<UpdateRequest>().Subject;
            update.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
            update.Target.Id.Should().Be(jobId);
            update.Target.RowVersion.Should().Be("123456", "el claim usa el RowVersion leído ANTES del trabajo pesado");
            ((OptionSetValue)update.Target["sanic_smlt_ij_status"]).Value.Should().Be((int)ImportJobStatus.Processing);
            ((int)update.Target["sanic_smlt_ij_totalrows"]).Should().Be(7);
        }

        [Fact]
        public void Prepare_JobWithNullStatus_IsClaimable_SoDirectApiCallersKeepWorking()
        {
            // Smelter.Verify (RunJob) y otros callers directos invocan Prepare sobre jobs creados sin status.
            var m = ReadyToPrepare(out var jobId);
            m.JobIs(null);

            var result = m.Build().Prepare(jobId, Guid.NewGuid());

            result.Claimed.Should().BeTrue();
            m.Executed.Should().ContainSingle().Which.Should().BeOfType<UpdateRequest>()
                .Which.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        }

        [Theory]
        [InlineData(ImportJobStatus.Processing)]
        [InlineData(ImportJobStatus.Completed)]
        [InlineData(ImportJobStatus.CompletedWithErrors)]
        [InlineData(ImportJobStatus.Failed)]
        public void Prepare_JobNotQueued_IsNotClaimed_NoResetNoThrowNoDownload(ImportJobStatus status)
        {
            var m = ReadyToPrepare(out var jobId);
            m.JobIs((int)status);

            var result = m.Build().Prepare(jobId, Guid.NewGuid());

            result.Claimed.Should().BeFalse();
            result.TotalRows.Should().Be(0);
            m.Executed.Should().BeEmpty("otra corrida ya es dueña del job: no se resetea nada");
            m.Downloader.DidNotReceive().Download(Arg.Any<EntityReference>(), Arg.Any<string>());
        }

        [Fact]
        public void Prepare_RowVersionMismatch_IsNotClaimed_AndDoesNotThrow()
        {
            var m = ReadyToPrepare(out var jobId);
            m.JobIs((int)ImportJobStatus.Queued);
            m.ExecuteFaults(PrepareImportHandler.ConcurrencyVersionMismatch);

            var result = m.Build().Prepare(jobId, Guid.NewGuid());

            result.Claimed.Should().BeFalse("otra corrida actualizó el job entre la lectura y el claim");
            result.TotalRows.Should().Be(0);
        }

        [Fact]
        public void Prepare_OtherDataverseFault_Propagates()
        {
            var m = ReadyToPrepare(out var jobId);
            m.JobIs((int)ImportJobStatus.Queued);
            m.ExecuteFaults(unchecked((int)0x80040265));

            Action act = () => m.Build().Prepare(jobId, Guid.NewGuid());

            act.Should().Throw<FaultException<OrganizationServiceFault>>();
        }

        [Fact]
        public void Prepare_ValidationFailure_StillThrowsBeforeTheClaim()
        {
            var m = ReadyToPrepare(out var jobId);
            m.JobIs((int)ImportJobStatus.Processing); // aunque ya esté tomado, la validación va primero
            var ctx = ContextWith(true, "name");
            ctx.Template.Status = (int)ImportTemplateStatus.Draft;
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Prepare(jobId, Guid.NewGuid());

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*not Active*");
            m.Executed.Should().BeEmpty();
        }

        [Fact]
        public void Run_NotClaimedJob_ReturnsZeroRows()
        {
            var m = ReadyToPrepare(out var jobId);
            m.JobIs((int)ImportJobStatus.Processing);

            m.Build().Run(jobId, Guid.NewGuid()).Should().Be(0);
        }

        // ── 1.0.10: validaciones de coherencia del template ─────────────────────────────────────

        [Fact]
        public void Run_DedupColumnWithoutColumnMapping_IsRejectedBeforeDownloadOrUpdate()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = ContextWith(true, "name");
            ctx.TableMappings[0].DedupColumns = "k, ghost";
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, Guid.NewGuid());

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*Mapping*ghost*");
            m.Downloader.DidNotReceive().Download(Arg.Any<EntityReference>(), Arg.Any<string>());
            m.Executed.Should().BeEmpty();
        }

        [Fact]
        public void Run_LookupColumnWithoutLookupTable_IsRejected()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = ContextWith(true, "name");
            ctx.TableMappings[0].ColumnMappings.Add(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Owner", SourceColumn = "name", TargetColumn = "owner", DataType = ColumnDataType.Lookup
            });
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, Guid.NewGuid());

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*owner*Lookup Table*");
        }

        [Fact]
        public void Run_ManyToManyRelationship_IsRejectedWithAClearError()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = ContextWith(true, "name");
            var second = new TableMappingConfig { Id = Guid.NewGuid(), Name = "Second", TargetTable = "s", InsertOnly = true };
            ctx.TableMappings.Add(second);
            ctx.RelationshipMappings.Add(new RelationshipMappingConfig
            {
                Id = Guid.NewGuid(), Name = "NM", ParentTableMappingId = ctx.TableMappings[0].Id,
                ChildTableMappingId = second.Id, RelationshipType = 1
            });
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, Guid.NewGuid());

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*NM*ManyToMany is not supported*");
            m.Executed.Should().BeEmpty();
        }

        [Fact]
        public void Run_OneToManyWithParentDedupCountMismatch_IsRejected()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var ctx = ContextWith(true, "name");
            var child = new TableMappingConfig { Id = Guid.NewGuid(), Name = "Child", TargetTable = "c", InsertOnly = true };
            ctx.TableMappings.Add(child);
            ctx.RelationshipMappings.Add(new RelationshipMappingConfig
            {
                Id = Guid.NewGuid(), Name = "R", ParentTableMappingId = ctx.TableMappings[0].Id, ChildTableMappingId = child.Id,
                RelationshipType = 0, LookupColumnOnChild = "x_parent", ParentDedupColumns = "a,b"
            });
            m.Loader.LoadContext(jobId).Returns(ctx);

            Action act = () => m.Build().Run(jobId, Guid.NewGuid());

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*Parent Dedup Columns lists 2*");
        }

        [Fact]
        public void Run_HeaderValidationIgnoresCaseAndSurroundingSpaces()
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = new byte[] { 1 };
            m.Loader.LoadContext(jobId).Returns(ContextWith(true, " Email "));
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
            m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "email" });
            m.Csv.CountRows(bytes, true, ",").Returns(1);

            m.Build().Run(jobId, Guid.NewGuid()).Should().Be(1);
        }

        [Theory]
        [InlineData("TAB")]
        [InlineData("\\t")]
        [InlineData("\t")]
        public void Run_TabDelimiterAlias_ValidatesHeadersOfARealTsv(string delimiter)
        {
            var m = new Mocks();
            var jobId = Guid.NewGuid();
            var bytes = System.Text.Encoding.UTF8.GetBytes("name\tcode\nA\tB\nC\tD");
            var ctx = ContextWith(true, "name", "code");
            ctx.Template.Delimiter = delimiter;
            m.Loader.LoadContext(jobId).Returns(ctx);
            m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);

            var handler = new PrepareImportHandler(m.Loader, m.Downloader, new CsvParserService(), m.Service, m.Access);

            handler.Run(jobId, Guid.NewGuid()).Should().Be(2);
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
            m.Executed.Should().BeEmpty();
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
