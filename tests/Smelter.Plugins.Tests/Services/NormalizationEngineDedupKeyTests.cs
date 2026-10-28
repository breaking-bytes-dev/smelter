using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    /// <summary>
    /// Clave de dedup vacía (1.0.16): una fila cuya clave de upsert llega vacía NO se escribe — upsertear sin clave crearía
    /// un registro nuevo en cada corrida (duplicado silencioso). Falla con ValidationError y un mensaje en inglés sin valores.
    /// Los mappings «Solo inserción» no hacen lookup de dedup, así que conservan el comportamiento anterior.
    /// </summary>
    public class NormalizationEngineDedupKeyTests
    {
        private static NormalizationEngine Engine() =>
            new NormalizationEngine(Substitute.For<IOrganizationService>(), Substitute.For<ITracingService>());

        private static SourceRow Row(int n, params (string Key, string Value)[] values)
        {
            var row = new SourceRow { RowNumber = n };
            foreach (var v in values) row.Values[v.Key] = v.Value;
            return row;
        }

        private static ColumnMappingConfig Col(string source, string target, string defaultValue = null) =>
            new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), SourceColumn = source, TargetColumn = target, DataType = ColumnDataType.String,
                DefaultValue = defaultValue
            };

        private static ImportContext Context(string dedup, bool insertOnly, params ColumnMappingConfig[] cols)
        {
            var tm = new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "Contact", TargetTable = "contact", DedupColumns = dedup, InsertOnly = insertOnly
            };
            tm.ColumnMappings.AddRange(cols);
            var ctx = new ImportContext();
            ctx.TableMappings.Add(tm);
            return ctx;
        }

        private static IUpsertWriter RecordingWriter(List<int> writtenRows)
        {
            var writer = Substitute.For<IUpsertWriter>();
            writer.Write(Arg.Any<IList<TargetRecord>>(), Arg.Any<TableMappingConfig>(), Arg.Any<bool>())
                  .Returns(ci =>
                  {
                      var records = ci.Arg<IList<TargetRecord>>();
                      writtenRows.AddRange(records.Select(r => r.SourceRowNumber));
                      return records.Select(r => ProcessingResult.Success(r.SourceRowNumber, "contact", Guid.NewGuid(), false)).ToList();
                  });
            return writer;
        }

        [Fact]
        public void ProcessBatch_EmptyDedupKey_FailsTheRowWithValidationErrorAndDoesNotWriteIt()
        {
            var ctx = Context("emailaddress1", false, Col("Correo", "emailaddress1"), Col("Nombre", "firstname"));
            var written = new List<int>();

            var results = Engine().ProcessBatch(ctx, new List<SourceRow>
            {
                Row(2, ("Correo", ""), ("Nombre", "Ana")),
                Row(3, ("Correo", "b@x.com"), ("Nombre", "Beto")),
            }, RecordingWriter(written));

            var failure = results.Single(r => r.Status == ImportLogStatus.Failed);
            failure.RowNumber.Should().Be(2);
            failure.Category.Should().Be(ErrorCategory.ValidationError);
            failure.ErrorMessage.Should().Be("Dedup key 'emailaddress1' is empty (Source: 'Correo').");
            written.Should().Equal(3);
        }

        [Fact]
        public void ProcessBatch_MissingCsvCellForDedupKey_AlsoFails()
        {
            var ctx = Context("emailaddress1", false, Col("Correo", "emailaddress1"), Col("Nombre", "firstname"));

            var results = Engine().ProcessBatch(ctx,
                new List<SourceRow> { Row(2, ("Nombre", "Ana")) }, RecordingWriter(new List<int>()));

            results.Single().Category.Should().Be(ErrorCategory.ValidationError);
        }

        [Fact]
        public void ProcessBatch_CompositeKeyWithOneEmptyColumn_NamesTheEmptyColumn()
        {
            var ctx = Context("a, b", false, Col("A", "a"), Col("B", "b"));

            var results = Engine().ProcessBatch(ctx,
                new List<SourceRow> { Row(2, ("A", "1"), ("B", "")) }, RecordingWriter(new List<int>()));

            results.Single().ErrorMessage.Should().Be("Dedup key 'b' is empty (Source: 'B').");
        }

        [Fact]
        public void ProcessBatch_DefaultValueSatisfiesTheDedupKey()
        {
            var ctx = Context("emailaddress1", false, Col("Correo", "emailaddress1", defaultValue: "none@x.com"));
            var written = new List<int>();

            Engine().ProcessBatch(ctx, new List<SourceRow> { Row(2, ("Correo", "")) }, RecordingWriter(written));

            written.Should().Equal(2);
        }

        [Fact]
        public void ProcessBatch_InsertOnlyMapping_KeepsWritingRowsWithEmptyKey()
        {
            var ctx = Context("emailaddress1", true, Col("Correo", "emailaddress1"), Col("Nombre", "firstname"));
            var written = new List<int>();

            var results = Engine().ProcessBatch(ctx,
                new List<SourceRow> { Row(2, ("Correo", ""), ("Nombre", "Ana")) }, RecordingWriter(written));

            results.Should().OnlyContain(r => r.Status != ImportLogStatus.Failed);
            written.Should().Equal(2);
        }
    }
}
