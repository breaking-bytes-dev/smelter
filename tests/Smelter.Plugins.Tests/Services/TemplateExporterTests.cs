using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class TemplateExporterTests
    {
        private const string TemplateTable = "sanic_tbl_smlt_importtemplate";
        private const string TmTable = "sanic_tbl_smlt_tablemapping";
        private const string CmTable = "sanic_tbl_smlt_columnmapping";
        private const string RelTable = "sanic_tbl_smlt_relationshipmapping";

        private static Entity Template(Guid id, string name)
        {
            var e = new Entity(TemplateTable, id);
            e["sanic_smlt_it_name"] = name;
            e["sanic_smlt_it_sourcetype"] = new OptionSetValue(0);
            e["sanic_smlt_it_hasheaderrow"] = true;
            e["sanic_smlt_it_delimiter"] = ",";
            e["sanic_smlt_it_chunksize"] = 1000;
            return e;
        }

        private static Entity Tm(Guid id, string name, string target, int order)
        {
            var e = new Entity(TmTable, id);
            e["sanic_smlt_tm_name"] = name;
            e["sanic_smlt_tm_targettable"] = target;
            e["sanic_smlt_tm_processingorder"] = order;
            e["sanic_smlt_tm_dedupcolumns"] = "sanic_key";
            e["sanic_smlt_tm_createifnotexists"] = true;
            return e;
        }

        private static Entity Rel(Guid parentTm, Guid childTm)
        {
            var e = new Entity(RelTable, Guid.NewGuid());
            e["sanic_smlt_rm_parenttablemappingid"] = new EntityReference(TmTable, parentTm);
            e["sanic_smlt_rm_childtablemappingid"] = new EntityReference(TmTable, childTm);
            e["sanic_smlt_rm_relationshiptype"] = new OptionSetValue(0);
            e["sanic_smlt_rm_lookupcolumnonchild"] = "sanic_parentid";
            e["sanic_smlt_rm_parentdedupcolumns"] = "Key";
            return e;
        }

        private static Guid ConditionGuid(QueryExpression q, string attr)
        {
            var c = q.Criteria.Conditions.FirstOrDefault(x => x.AttributeName == attr);
            return c != null ? (Guid)c.Values[0] : Guid.Empty;
        }

        /// <summary>Wires a mock that dispatches RetrieveMultiple by entity name (and tablemappingid for columns).</summary>
        private static IOrganizationService Service(Guid templateId, List<Entity> tms,
            Dictionary<Guid, List<Entity>> columnsByTm, List<Entity> rels, string templateName = "Cobranza")
        {
            var service = Substitute.For<IOrganizationService>();
            service.Retrieve(TemplateTable, templateId, Arg.Any<ColumnSet>()).Returns(Template(templateId, templateName));
            service.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(ci =>
            {
                var q = ci.Arg<QueryExpression>();
                if (q.EntityName == TmTable) return new EntityCollection(tms);
                if (q.EntityName == RelTable) return new EntityCollection(rels);
                if (q.EntityName == CmTable)
                {
                    var tmId = ConditionGuid(q, "sanic_smlt_cm_tablemappingid");
                    return new EntityCollection(columnsByTm.TryGetValue(tmId, out var cols) ? cols : new List<Entity>());
                }
                return new EntityCollection();
            });
            return service;
        }

        [Fact]
        public void Export_BuildsTreeWithTemplateTablesColumnsAndRelationships()
        {
            var templateId = Guid.NewGuid();
            var parent = Tm(Guid.NewGuid(), "Cliente", "sanic_cliente", 1);
            var child = Tm(Guid.NewGuid(), "Cuenta", "sanic_cuentamora", 2);
            var cm = new Entity(CmTable, Guid.NewGuid());
            cm["sanic_smlt_cm_sourcecolumn"] = "Cliente";
            cm["sanic_smlt_cm_targetcolumn"] = "sanic_nombre";
            cm["sanic_smlt_cm_datatype"] = new OptionSetValue(0);

            var service = Service(templateId,
                new List<Entity> { parent, child },
                new Dictionary<Guid, List<Entity>> { { parent.Id, new List<Entity> { cm } }, { child.Id, new List<Entity>() } },
                new List<Entity> { Rel(parent.Id, child.Id) });

            var dto = new TemplateExporter(service).Export(templateId);

            dto.FormatVersion.Should().Be("1.0");
            dto.Template.Name.Should().Be("Cobranza");
            dto.TableMappings.Should().HaveCount(2);
            dto.TableMappings.First(t => t.TargetTable == "sanic_cliente")
                .ColumnMappings.Single().TargetColumn.Should().Be("sanic_nombre");
            dto.RelationshipMappings.Should().ContainSingle();
        }

        [Fact]
        public void Export_GeneratesUniqueRefs_WhenNamesCollide()
        {
            var templateId = Guid.NewGuid();
            var a = Tm(Guid.NewGuid(), "Dirección", "sanic_direccion", 1);
            var b = Tm(Guid.NewGuid(), "Dirección", "sanic_direccion", 2);

            var service = Service(templateId, new List<Entity> { a, b },
                new Dictionary<Guid, List<Entity>>(), new List<Entity>());

            var dto = new TemplateExporter(service).Export(templateId);

            var refs = dto.TableMappings.Select(t => t.Ref).ToList();
            refs.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void Export_RelationshipsReferenceRefs_NotGuids()
        {
            var templateId = Guid.NewGuid();
            var parent = Tm(Guid.NewGuid(), "Cliente", "sanic_cliente", 1);
            var child = Tm(Guid.NewGuid(), "Cuenta", "sanic_cuentamora", 2);

            var service = Service(templateId, new List<Entity> { parent, child },
                new Dictionary<Guid, List<Entity>>(), new List<Entity> { Rel(parent.Id, child.Id) });

            var dto = new TemplateExporter(service).Export(templateId);

            var rel = dto.RelationshipMappings.Single();
            var parentRef = dto.TableMappings.First(t => t.TargetTable == "sanic_cliente").Ref;
            var childRef = dto.TableMappings.First(t => t.TargetTable == "sanic_cuentamora").Ref;
            rel.ParentRef.Should().Be(parentRef);
            rel.ChildRef.Should().Be(childRef);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(null, false)]
        public void Export_CarriesInsertOnly_AndTreatsMissingAsFalse(bool? raw, bool expected)
        {
            var templateId = Guid.NewGuid();
            var tm = Tm(Guid.NewGuid(), "Facturas", "invoice", 1);
            if (raw.HasValue) tm["sanic_smlt_tm_insertonly"] = raw.Value;
            var service = Service(templateId, new List<Entity> { tm },
                new Dictionary<Guid, List<Entity>>(), new List<Entity>());

            var dto = new TemplateExporter(service).Export(templateId);

            dto.TableMappings.Single().InsertOnly.Should().Be(expected);
        }
    }
}
