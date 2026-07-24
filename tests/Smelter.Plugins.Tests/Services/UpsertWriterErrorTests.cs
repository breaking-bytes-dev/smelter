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

        private static ExecuteMultipleResponse FaultResponse(string message)
        {
            var items = new ExecuteMultipleResponseItemCollection
            {
                new ExecuteMultipleResponseItem
                {
                    RequestIndex = 0,
                    Fault = new OrganizationServiceFault { Message = message }
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
    }
}
