using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class TemplateSerializerTests
    {
        private readonly ITemplateSerializer _sut = new TemplateSerializer();

        private static TemplateExportDto SampleDto()
        {
            return new TemplateExportDto
            {
                FormatVersion = "1.0",
                Template = new TemplateInfoDto
                {
                    Name = "Cobranza", SourceType = 0, HasHeaderRow = true, Delimiter = ",", ChunkSize = 1000
                },
                TableMappings = new List<TableMappingDto>
                {
                    new TableMappingDto
                    {
                        Ref = "tm-cliente", Name = "Cliente", TargetTable = "sanic_cliente",
                        ProcessingOrder = 1, DedupColumns = "sanic_cif", CreateIfNotExists = true, UpdateIfExists = true,
                        ColumnMappings = new List<ColumnMappingDto>
                        {
                            new ColumnMappingDto { Name = "Nombre", SourceColumn = "Cliente",
                                TargetColumn = "sanic_nombre", DataType = 0 }
                        }
                    },
                    new TableMappingDto
                    {
                        Ref = "tm-cuenta", Name = "Cuenta", TargetTable = "sanic_cuentamora",
                        ProcessingOrder = 2, DedupColumns = "sanic_numerocuenta", CreateIfNotExists = true,
                        ColumnMappings = new List<ColumnMappingDto>()
                    }
                },
                RelationshipMappings = new List<RelationshipMappingDto>
                {
                    new RelationshipMappingDto
                    {
                        ParentRef = "tm-cliente", ChildRef = "tm-cuenta", RelationshipType = 0,
                        LookupColumnOnChild = "sanic_clienteid", ParentDedupColumns = "CIF"
                    }
                }
            };
        }

        [Fact]
        public void Roundtrip_PreservesTheWholeTree()
        {
            var original = SampleDto();

            var json = _sut.Serialize(original);
            var result = _sut.Deserialize(json);

            result.Should().BeEquivalentTo(original);
        }

        [Fact]
        public void Serialize_DoesNotEmitGuids_OnlyRefs()
        {
            var json = _sut.Serialize(SampleDto());

            json.Should().Contain("tm-cliente").And.Contain("tm-cuenta");
            json.Should().Contain("\"ParentRef\"").And.Contain("\"ChildRef\"");
        }

        [Fact]
        public void Deserialize_MalformedJson_Throws()
        {
            System.Action act = () => _sut.Deserialize("{ this is not json");
            act.Should().Throw<InvalidPluginExecutionException>();
        }

        [Fact]
        public void Deserialize_FutureFormatVersion_Throws()
        {
            var dto = SampleDto();
            dto.FormatVersion = "2.0";
            var json = _sut.Serialize(dto);

            System.Action act = () => _sut.Deserialize(json);
            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*2.0*");
        }

        [Fact]
        public void Deserialize_RelationshipWithUnknownParentRef_Throws()
        {
            var dto = SampleDto();
            dto.RelationshipMappings[0].ParentRef = "tm-no-existe";
            var json = _sut.Serialize(dto);

            System.Action act = () => _sut.Deserialize(json);
            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("*tm-no-existe*");
        }
    }
}
