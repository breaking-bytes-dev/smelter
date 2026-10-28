using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using NSubstitute;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class UpsertWriterErrorTests
    {
        private static TableMappingConfig Mapping(string table, string dedup) =>
            new TableMappingConfig { Id = Guid.NewGuid(), TargetTable = table, DedupColumns = dedup };

        private static ExecuteMultipleResponse FaultResponse(string message, int errorCode = 0)
        {
            var items = new ExecuteMultipleResponseItemCollection
            {
                new ExecuteMultipleResponseItem
                {
                    RequestIndex = 0,
                    Fault = new OrganizationServiceFault { Message = message, ErrorCode = errorCode }
                }
            };
            var response = new ExecuteMultipleResponse();
            response["Responses"] = items;
            response["IsFaulted"] = true;
            return response;
        }

        [Fact]
        public void Write_DataverseFault_IsClassifiedAsTargetData()
        {
            // v1 (decisión 9): los faults de escritura del plugin se clasifican TargetData.
            // El Transient interno (SQL deadlock/timeout) está diferido — no se detecta ni re-lanza aún.
            var service = Substitute.For<IOrganizationService>();
            service.Execute(Arg.Any<OrganizationRequest>()).Returns(_ => FaultResponse("constraint violation"));

            var record = new TargetRecord { TableLogicalName = "city", SourceRowNumber = 2 };
            record.Attributes["sanic_name"] = "X";

            var results = new UpsertWriter(service)
                .Write(new List<TargetRecord> { record }, Mapping("city", "sanic_name"), false);

            results.Should().ContainSingle();
            results[0].Status.Should().Be(ImportLogStatus.Failed);
            results[0].Category.Should().Be(ErrorCategory.TargetData);
        }

        [Theory]
        [InlineData(true)]  // upsert (con alternate key)
        [InlineData(false)] // create/update (sin key)
        public void Write_DataverseFault_LogsErrorCodeButNeverTheFaultMessage(bool withDedup)
        {
            // El fault de Dataverse suele traer el valor de la clave duplicada (PII): no puede llegar al log.
            var service = Substitute.For<IOrganizationService>();
            service.Execute(Arg.Any<OrganizationRequest>()).Returns(_ =>
                FaultResponse("A record with the same key already exists: 4111111111111111", unchecked((int)0x80040237)));

            var record = new TargetRecord { TableLogicalName = "city", SourceRowNumber = 2 };
            record.Attributes["sanic_name"] = "4111111111111111";

            var results = new UpsertWriter(service)
                .Write(new List<TargetRecord> { record }, Mapping("city", withDedup ? "sanic_name" : null), false);

            results.Should().ContainSingle();
            results[0].ErrorMessage.Should().Contain("80040237");
            results[0].ErrorMessage.Should().NotContain("4111").And.NotContain("already exists");
        }
    }
}
