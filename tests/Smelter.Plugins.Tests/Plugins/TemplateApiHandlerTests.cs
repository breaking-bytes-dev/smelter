using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using Smelter.Plugins;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Plugins
{
    public class TemplateApiHandlerTests
    {
        [Fact]
        public void ExportHandler_SerializesTheExportedTree()
        {
            var exporter = Substitute.For<ITemplateExporter>();
            var serializer = Substitute.For<ITemplateSerializer>();
            var id = Guid.NewGuid();
            var dto = new TemplateExportDto();
            exporter.Export(id).Returns(dto);
            serializer.Serialize(dto).Returns("{\"ok\":1}");

            var json = new ExportTemplateHandler(exporter, serializer).Run(id);

            json.Should().Be("{\"ok\":1}");
            exporter.Received(1).Export(id);
            serializer.Received(1).Serialize(dto);
        }

        [Fact]
        public void ImportHandler_DeserializesThenImports_ReturnsIdAndWarnings()
        {
            var serializer = Substitute.For<ITemplateSerializer>();
            var importer = Substitute.For<ITemplateImporter>();
            var dto = new TemplateExportDto();
            var newId = Guid.NewGuid();
            serializer.Deserialize("{json}").Returns(dto);
            importer.Import(dto).Returns(new ImportResult { TemplateId = newId, Warnings = new List<string> { "w1", "w2" } });

            var result = new ImportTemplateHandler(serializer, importer).Run("{json}");

            result.TemplateId.Should().Be(newId);
            result.Warnings.Should().Contain("w1").And.Contain("w2");
            serializer.Received(1).Deserialize("{json}");
            importer.Received(1).Import(dto);
        }
    }
}
