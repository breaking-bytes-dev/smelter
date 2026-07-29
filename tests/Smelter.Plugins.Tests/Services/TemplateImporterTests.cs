using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class TemplateImporterTests
    {
        private const string TemplateTable = "sanic_tbl_smlt_importtemplate";
        private const string TmTable = "sanic_tbl_smlt_tablemapping";
        private const string CmTable = "sanic_tbl_smlt_columnmapping";
        private const string RelTable = "sanic_tbl_smlt_relationshipmapping";

        private static TemplateExportDto Dto()
        {
            return new TemplateExportDto
            {
                FormatVersion = "1.0",
                Template = new TemplateInfoDto { Name = "Cobranza", SourceType = 0, HasHeaderRow = true, Delimiter = ",", ChunkSize = 1000 },
                TableMappings = new List<TableMappingDto>
                {
                    new TableMappingDto { Ref = "tm-cliente", Name = "Cliente", TargetTable = "sanic_cliente", ProcessingOrder = 1,
                        DedupColumns = "sanic_cif", CreateIfNotExists = true,
                        ColumnMappings = new List<ColumnMappingDto> { new ColumnMappingDto { SourceColumn = "Cliente", TargetColumn = "sanic_nombre", DataType = 0 } } },
                    new TableMappingDto { Ref = "tm-cuenta", Name = "Cuenta", TargetTable = "sanic_cuentamora", ProcessingOrder = 2,
                        DedupColumns = "sanic_numerocuenta", CreateIfNotExists = true, ColumnMappings = new List<ColumnMappingDto>() }
                },
                RelationshipMappings = new List<RelationshipMappingDto>
                {
                    new RelationshipMappingDto { ParentRef = "tm-cliente", ChildRef = "tm-cuenta", RelationshipType = 0, LookupColumnOnChild = "sanic_clienteid" }
                }
            };
        }

        /// <summary>Mock: captures Creates (assigns Id), dispatches RetrieveMultiple by entity name.</summary>
        private static IOrganizationService Service(out List<Entity> created,
            EntityCollection templateByName = null, EntityCollection oldTms = null,
            EntityCollection oldRels = null, EntityCollection oldCms = null)
        {
            var captured = new List<Entity>();
            created = captured;
            var service = Substitute.For<IOrganizationService>();
            service.Create(Arg.Do<Entity>(e => captured.Add(e))).Returns(ci =>
            {
                var e = ci.Arg<Entity>();
                if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
                return e.Id;
            });
            service.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(ci =>
            {
                var q = ci.Arg<QueryExpression>();
                if (q.EntityName == TemplateTable) return templateByName ?? new EntityCollection();
                if (q.EntityName == RelTable) return oldRels ?? new EntityCollection();
                if (q.EntityName == TmTable) return oldTms ?? new EntityCollection();
                if (q.EntityName == CmTable) return oldCms ?? new EntityCollection();
                return new EntityCollection();
            });
            return service;
        }

        private static Entity Created(List<Entity> created, string table, Func<Entity, bool> pred = null) =>
            created.First(e => e.LogicalName == table && (pred == null || pred(e)));

        [Fact]
        public void Import_CleanEnvironment_CreatesTemplateTableColumnAndRelationship()
        {
            var service = Service(out var created);

            var result = new TemplateImporter(service, _ => true).Import(Dto());

            created.Count(e => e.LogicalName == TemplateTable).Should().Be(1);
            created.Count(e => e.LogicalName == TmTable).Should().Be(2);
            created.Count(e => e.LogicalName == CmTable).Should().Be(1);
            created.Count(e => e.LogicalName == RelTable).Should().Be(1);
            result.TemplateId.Should().NotBe(Guid.Empty);
        }

        [Fact]
        public void Import_NewTemplate_IsCreatedActive()
        {
            // 1.3.0.0: PrepareImport solo ejecuta templates Active — un template importado debe
            // nacer ejecutable, no depender del default de la columna en la plataforma.
            var service = Service(out var created);

            new TemplateImporter(service, _ => true).Import(Dto());

            var status = Created(created, TemplateTable).GetAttributeValue<OptionSetValue>("sanic_smlt_it_status");
            status.Should().NotBeNull();
            status.Value.Should().Be((int)ImportTemplateStatus.Active);
        }

        [Fact]
        public void Import_RemapsRefsToNewlyCreatedTableMappingGuids()
        {
            var service = Service(out var created);

            new TemplateImporter(service, _ => true).Import(Dto());

            var clienteTm = Created(created, TmTable, e => e.GetAttributeValue<string>("sanic_smlt_tm_targettable") == "sanic_cliente");
            var cuentaTm = Created(created, TmTable, e => e.GetAttributeValue<string>("sanic_smlt_tm_targettable") == "sanic_cuentamora");
            var rel = Created(created, RelTable);

            rel.GetAttributeValue<EntityReference>("sanic_smlt_rm_parenttablemappingid").Id.Should().Be(clienteTm.Id);
            rel.GetAttributeValue<EntityReference>("sanic_smlt_rm_childtablemappingid").Id.Should().Be(cuentaTm.Id);
        }

        [Fact]
        public void Import_ExistingTemplateByName_UpdatesAndDeletesOldMappings()
        {
            var existingId = Guid.NewGuid();
            var existing = new EntityCollection(new List<Entity> { new Entity(TemplateTable, existingId) });
            var oldTm = new Entity(TmTable, Guid.NewGuid());
            var service = Service(out _, templateByName: existing,
                oldTms: new EntityCollection(new List<Entity> { oldTm }),
                oldRels: new EntityCollection(new List<Entity> { new Entity(RelTable, Guid.NewGuid()) }));

            var result = new TemplateImporter(service, _ => true).Import(Dto());

            result.TemplateId.Should().Be(existingId);
            service.Received().Update(Arg.Is<Entity>(e => e.LogicalName == TemplateTable && e.Id == existingId));
            service.Received().Delete(TmTable, oldTm.Id);
        }

        [Fact]
        public void Import_MissingTargetTable_AddsWarningAndDoesNotAbort()
        {
            var service = Service(out var created);

            // sanic_cuentamora "does not exist" in destination
            var result = new TemplateImporter(service, name => name != "sanic_cuentamora").Import(Dto());

            result.Warnings.Should().ContainSingle(w => w.Contains("sanic_cuentamora"));
            created.Count(e => e.LogicalName == TmTable).Should().Be(2); // still imported the whole template
        }
    }
}
