using System;
using System.Collections.Generic;
using FluentAssertions;
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
            act.Should().Throw<TemplateFormatException>();
        }

        [Fact]
        public void Deserialize_FutureFormatVersion_Throws()
        {
            var dto = SampleDto();
            dto.FormatVersion = "2.0";
            var json = _sut.Serialize(dto);

            System.Action act = () => _sut.Deserialize(json);
            act.Should().Throw<TemplateFormatException>().WithMessage("*2.0*");
        }

        [Fact]
        public void Deserialize_RelationshipWithUnknownParentRef_Throws()
        {
            var dto = SampleDto();
            dto.RelationshipMappings[0].ParentRef = "tm-no-existe";
            var json = _sut.Serialize(dto);

            System.Action act = () => _sut.Deserialize(json);
            act.Should().Throw<TemplateFormatException>().WithMessage("*tm-no-existe*");
        }

        [Fact]
        public void Roundtrip_PreservesInsertOnly()
        {
            var dto = SampleDto();
            dto.TableMappings[1].InsertOnly = true;

            var result = _sut.Deserialize(_sut.Serialize(dto));

            result.TableMappings[0].InsertOnly.Should().BeFalse();
            result.TableMappings[1].InsertOnly.Should().BeTrue();
        }

        [Fact]
        public void Deserialize_JsonWithoutInsertOnly_ImportsAsFalse()
        {
            // JSON exportado por una versión anterior: no trae el campo.
            const string oldJson = "{\"FormatVersion\":\"1.0\",\"Template\":{\"Name\":\"T\",\"SourceType\":0,\"HasHeaderRow\":true,\"Delimiter\":\",\",\"ChunkSize\":0}," +
                "\"TableMappings\":[{\"Ref\":\"a\",\"Name\":\"A\",\"TargetTable\":\"x\",\"ProcessingOrder\":1,\"DedupColumns\":\"k\",\"CreateIfNotExists\":true,\"UpdateIfExists\":true,\"ColumnMappings\":[]}]," +
                "\"RelationshipMappings\":[]}";

            var result = _sut.Deserialize(oldJson);

            result.TableMappings.Should().ContainSingle().Which.InsertOnly.Should().BeFalse();
        }

        [Fact]
        public void Deserialize_JsonWithRemovedIsJunctionTableMember_StillImports()
        {
            // JSON exportado antes de 1.3.0.0: trae IsJunctionTable (campo eliminado). Se ignora.
            const string oldJson = "{\"FormatVersion\":\"1.0\",\"Template\":{\"Name\":\"T\",\"SourceType\":0,\"HasHeaderRow\":true,\"Delimiter\":\",\",\"ChunkSize\":0}," +
                "\"TableMappings\":[{\"Ref\":\"a\",\"Name\":\"A\",\"TargetTable\":\"x\",\"ProcessingOrder\":1,\"DedupColumns\":\"k\",\"CreateIfNotExists\":true,\"UpdateIfExists\":true,\"IsJunctionTable\":true,\"ColumnMappings\":[]}]," +
                "\"RelationshipMappings\":[]}";

            var result = _sut.Deserialize(oldJson);

            result.TableMappings.Should().ContainSingle().Which.Name.Should().Be("A");
        }

        [Fact]
        public void Deserialize_ManyToManyRelationship_IsRejectedWithAClearError()
        {
            const string json = "{\"FormatVersion\":\"1.0\",\"Template\":{\"Name\":\"T\",\"SourceType\":0,\"HasHeaderRow\":true,\"Delimiter\":\",\",\"ChunkSize\":0}," +
                "\"TableMappings\":[{\"Ref\":\"a\",\"Name\":\"A\",\"TargetTable\":\"x\",\"ColumnMappings\":[]},{\"Ref\":\"b\",\"Name\":\"B\",\"TargetTable\":\"y\",\"ColumnMappings\":[]}]," +
                "\"RelationshipMappings\":[{\"Name\":\"NM\",\"ParentRef\":\"a\",\"ChildRef\":\"b\",\"RelationshipType\":1}]}";

            Action act = () => _sut.Deserialize(json);

            act.Should().Throw<TemplateFormatException>().WithMessage("*NM*ManyToMany*not supported*");
        }

        [Fact]
        public void Roundtrip_PreservesDescriptionAndStatus()
        {
            var dto = SampleDto();
            dto.Template.Description = "Carga mensual de cobranza";
            dto.Template.Status = (int)ImportTemplateStatus.Draft;

            var result = _sut.Deserialize(_sut.Serialize(dto));

            result.Template.Description.Should().Be("Carga mensual de cobranza");
            result.Template.Status.Should().Be((int)ImportTemplateStatus.Draft);
        }

        [Fact]
        public void Deserialize_JsonWithoutDescriptionNorStatus_LeavesThemNull()
        {
            // JSON 1.0 (anterior al bump 1.1): sin el campo => null, distinto de "Draft" (0) o "" ; el importer usa el null
            // para decidir el default (Active en create, sin tocar en update).
            const string oldJson = "{\"FormatVersion\":\"1.0\",\"Template\":{\"Name\":\"T\",\"SourceType\":0,\"HasHeaderRow\":true,\"Delimiter\":\",\",\"ChunkSize\":0}," +
                "\"TableMappings\":[],\"RelationshipMappings\":[]}";

            var result = _sut.Deserialize(oldJson);

            result.Template.Description.Should().BeNull();
            result.Template.Status.Should().BeNull();
        }

        [Fact]
        public void Deserialize_StatusOutsideTheChoice_IsRejected()
        {
            const string json = "{\"FormatVersion\":\"1.1\",\"Template\":{\"Name\":\"T\",\"SourceType\":0,\"HasHeaderRow\":true,\"Delimiter\":\",\",\"ChunkSize\":0,\"Status\":9}," +
                "\"TableMappings\":[],\"RelationshipMappings\":[]}";

            Action act = () => _sut.Deserialize(json);

            act.Should().Throw<TemplateFormatException>().WithMessage("*status*9*");
        }

        [Fact]
        public void Deserialize_MinorVersion1_1_IsAccepted()
        {
            var dto = SampleDto();
            dto.FormatVersion = "1.1";

            Action act = () => _sut.Deserialize(_sut.Serialize(dto));

            act.Should().NotThrow();
        }
    }
}
