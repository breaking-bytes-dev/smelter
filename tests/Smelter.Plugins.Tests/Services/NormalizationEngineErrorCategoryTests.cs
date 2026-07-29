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
    public class NormalizationEngineErrorCategoryTests
    {
        private static SourceRow Row(int n, params (string Key, string Value)[] values)
        {
            var row = new SourceRow { RowNumber = n };
            foreach (var v in values) row.Values[v.Key] = v.Value;
            return row;
        }

        private static NormalizationEngine Engine() =>
            new NormalizationEngine(Substitute.For<IOrganizationService>(), Substitute.For<ITracingService>());

        // Writer that just echoes success for whatever it's given (failures come from the build phase).
        private static IUpsertWriter PassthroughWriter()
        {
            var writer = Substitute.For<IUpsertWriter>();
            writer.Write(Arg.Any<IList<TargetRecord>>(), Arg.Any<TableMappingConfig>(), Arg.Any<bool>())
                  .Returns(ci => ci.Arg<IList<TargetRecord>>()
                      .Select(r => ProcessingResult.Success(r.SourceRowNumber, "t", Guid.NewGuid(), false))
                      .ToList());
            return writer;
        }

        private static ImportContext ContextWith(params ColumnMappingConfig[] columns)
        {
            var table = new TableMappingConfig
            {
                Id = Guid.NewGuid(), Name = "T", TargetTable = "t", DedupColumns = "sanic_key"
            };
            table.ColumnMappings.Add(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), SourceColumn = "key", TargetColumn = "sanic_key", DataType = ColumnDataType.String
            });
            foreach (var c in columns) table.ColumnMappings.Add(c);

            var context = new ImportContext { ImportJobId = Guid.NewGuid() };
            context.TableMappings.Add(table);
            return context;
        }

        private static ProcessingResult OnlyFailure(IList<ProcessingResult> results) =>
            results.Single(r => r.Status == ImportLogStatus.Failed);

        [Fact]
        public void ProcessBatch_TypeCoercionFailure_IsTypeError()
        {
            var context = ContextWith(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), SourceColumn = "n", TargetColumn = "sanic_n", DataType = ColumnDataType.Integer
            });

            var results = Engine().ProcessBatch(context,
                new List<SourceRow> { Row(2, ("key", "K1"), ("n", "abc")) }, PassthroughWriter());

            OnlyFailure(results).Category.Should().Be(ErrorCategory.TypeError);
        }

        [Fact]
        public void ProcessBatch_TypeCoercionFailure_MessageNamesTheColumn()
        {
            // 1.2.1.0: el mensaje del TypeCoercer trae el valor pero no la columna — el engine debe
            // agregar target y source column, que es lo que el operador necesita para corregir el CSV.
            var context = ContextWith(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), SourceColumn = "n", TargetColumn = "sanic_n", DataType = ColumnDataType.Integer
            });

            var results = Engine().ProcessBatch(context,
                new List<SourceRow> { Row(2, ("key", "K1"), ("n", "abc")) }, PassthroughWriter());

            var failure = OnlyFailure(results);
            failure.ErrorMessage.Should().Contain("'sanic_n'", "debe nombrar la columna destino");
            failure.ErrorMessage.Should().Contain("'n'", "debe nombrar la columna fuente del CSV");
            failure.ErrorMessage.Should().Contain("'abc'", "debe conservar el valor original del TypeCoercer");
        }

        [Fact]
        public void ProcessBatch_TransformFailure_IsTransformError()
        {
            var context = ContextWith(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), TargetColumn = "sanic_n", DataType = ColumnDataType.String,
                TransformExpression = "NOPE(key)" // unknown function
            });

            var results = Engine().ProcessBatch(context,
                new List<SourceRow> { Row(2, ("key", "K1")) }, PassthroughWriter());

            OnlyFailure(results).Category.Should().Be(ErrorCategory.TransformError);
        }

        [Fact]
        public void ProcessBatch_RequiredEmpty_IsValidationError()
        {
            var context = ContextWith(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), SourceColumn = "n", TargetColumn = "sanic_n",
                DataType = ColumnDataType.String, IsRequired = true
            });

            var results = Engine().ProcessBatch(context,
                new List<SourceRow> { Row(2, ("key", "K1"), ("n", "")) }, PassthroughWriter());

            OnlyFailure(results).Category.Should().Be(ErrorCategory.ValidationError);
        }

        [Fact]
        public void ProcessBatch_NonGuidLookupWithoutMatchColumn_IsLookupUnresolved()
        {
            var context = ContextWith(new ColumnMappingConfig
            {
                Id = Guid.NewGuid(), SourceColumn = "c", TargetColumn = "sanic_l",
                DataType = ColumnDataType.Lookup, LookupTable = "account", LookupMatchColumn = null
            });

            var results = Engine().ProcessBatch(context,
                new List<SourceRow> { Row(2, ("key", "K1"), ("c", "NOT-A-GUID")) }, PassthroughWriter());

            OnlyFailure(results).Category.Should().Be(ErrorCategory.LookupUnresolved);
        }
    }
}
