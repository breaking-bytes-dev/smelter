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
    public class ImportContextLoaderTests
    {
        private const string RelTable = "sanic_tbl_smlt_relationshipmapping";

        private static Entity RelEntity(Guid parentTm, Guid childTm, int type = 0, string lookup = "parentid")
        {
            var e = new Entity(RelTable, Guid.NewGuid());
            e["sanic_smlt_rm_name"] = "rel";
            e["sanic_smlt_rm_parenttablemappingid"] = new EntityReference("sanic_tbl_smlt_tablemapping", parentTm);
            e["sanic_smlt_rm_childtablemappingid"] = new EntityReference("sanic_tbl_smlt_tablemapping", childTm);
            e["sanic_smlt_rm_relationshiptype"] = new OptionSetValue(type);
            e["sanic_smlt_rm_lookupcolumnonchild"] = lookup;
            e["sanic_smlt_rm_parentdedupcolumns"] = "code";
            return e;
        }

        [Fact]
        public void LoadRelationshipMappings_MapsEntitiesToConfigs()
        {
            var service = Substitute.For<IOrganizationService>();
            var parentId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            var ec = new EntityCollection();
            ec.Entities.Add(RelEntity(parentId, childId, 0, "sanic_countryid"));
            service.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(ec);

            var loader = new ImportContextLoader(service);
            var rels = loader.LoadRelationshipMappings(Guid.NewGuid());

            rels.Should().ContainSingle();
            rels[0].ParentTableMappingId.Should().Be(parentId);
            rels[0].ChildTableMappingId.Should().Be(childId);
            rels[0].LookupColumnOnChild.Should().Be("sanic_countryid");
            rels[0].ParentDedupColumns.Should().Be("code");
            rels[0].IsOneToMany.Should().BeTrue();
        }

        [Fact]
        public void LoadRelationshipMappings_FiltersByTemplateId()
        {
            var service = Substitute.For<IOrganizationService>();
            QueryExpression captured = null;
            var templateId = Guid.NewGuid();
            service.RetrieveMultiple(Arg.Do<QueryExpression>(q => captured = q)).Returns(new EntityCollection());

            new ImportContextLoader(service).LoadRelationshipMappings(templateId);

            captured.EntityName.Should().Be(RelTable);
            captured.Criteria.Conditions.Should().Contain(c =>
                c.AttributeName == "sanic_smlt_rm_importtemplateid"
                && c.Operator == ConditionOperator.Equal
                && c.Values.Contains(templateId));
        }

        [Fact]
        public void LoadRelationshipMappings_NoneConfigured_ReturnsEmpty()
        {
            var service = Substitute.For<IOrganizationService>();
            service.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(new EntityCollection());

            new ImportContextLoader(service).LoadRelationshipMappings(Guid.NewGuid())
                .Should().BeEmpty();
        }

        /// <summary>
        /// Regression guard for bug #1: relationships were never loaded, so the resolver never
        /// received edges and the DAG was inert. This proves loaded relationships now reach the resolver.
        /// </summary>
        [Fact]
        public void LoadedRelationships_FedToResolver_OrderParentBeforeChild()
        {
            var parent = new TableMappingConfig { Id = Guid.NewGuid(), Name = "Country", TargetTable = "country" };
            var child = new TableMappingConfig { Id = Guid.NewGuid(), Name = "City", TargetTable = "city" };

            var service = Substitute.For<IOrganizationService>();
            var ec = new EntityCollection();
            ec.Entities.Add(RelEntity(parent.Id, child.Id, 0, "sanic_countryid"));
            service.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(ec);

            var rels = new ImportContextLoader(service).LoadRelationshipMappings(Guid.NewGuid());
            var ordered = DependencyResolver.Resolve(new List<TableMappingConfig> { child, parent }, rels);

            ordered.IndexOf(parent).Should().BeLessThan(ordered.IndexOf(child),
                "loaded relationships must drive the topological order (bug #1 fixed)");
        }

        [Theory]
        [InlineData(1, (int)ImportJobStatus.Processing)]
        [InlineData(2, (int)ImportJobStatus.Completed)]
        [InlineData(null, (int)ImportJobStatus.Queued)] // sin estado → fail-closed para ProcessChunk
        public void LoadContext_LoadsJobStatus(int? raw, int expected)
        {
            var service = Substitute.For<IOrganizationService>();
            var jobId = Guid.NewGuid();
            var templateId = Guid.NewGuid();
            var job = new Entity("sanic_tbl_smlt_importjob", jobId);
            job["sanic_smlt_ij_importtemplateid"] = new EntityReference("sanic_tbl_smlt_importtemplate", templateId);
            if (raw.HasValue) job["sanic_smlt_ij_status"] = new OptionSetValue(raw.Value);
            service.Retrieve("sanic_tbl_smlt_importjob", jobId, Arg.Any<ColumnSet>()).Returns(job);
            service.Retrieve("sanic_tbl_smlt_importtemplate", templateId, Arg.Any<ColumnSet>())
                .Returns(new Entity("sanic_tbl_smlt_importtemplate", templateId));
            service.RetrieveMultiple(Arg.Any<QueryBase>()).Returns(new EntityCollection());

            var context = new ImportContextLoader(service).LoadContext(jobId);

            context.JobStatus.Should().Be(expected);
        }

        /// <summary>
        /// Regression: LoadTemplateConfig recibe un TEMPLATE id y va directo al template.
        /// NO debe hacer Retrieve de importjob con ese id (causa de "importjob ... Does Not Exist").
        /// </summary>
        [Fact]
        public void LoadTemplateConfig_RetrievesTemplateDirectly_DoesNotProbeImportJob()
        {
            var service = Substitute.For<IOrganizationService>();
            var templateId = Guid.NewGuid();
            var template = new Entity("sanic_tbl_smlt_importtemplate", templateId);
            template["sanic_smlt_it_name"] = "T";
            service.Retrieve("sanic_tbl_smlt_importtemplate", templateId, Arg.Any<ColumnSet>()).Returns(template);

            var config = new ImportContextLoader(service).LoadTemplateConfig(templateId);

            config.Id.Should().Be(templateId);
            service.DidNotReceive().Retrieve("sanic_tbl_smlt_importjob", templateId, Arg.Any<ColumnSet>());
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(123456789)]
        public void LoadContext_LoadsByteCursor_NullWhenJobWasPreparedByAnOlderVersion(int? raw)
        {
            var service = Substitute.For<IOrganizationService>();
            var jobId = Guid.NewGuid();
            var templateId = Guid.NewGuid();
            var job = new Entity("sanic_tbl_smlt_importjob", jobId);
            job["sanic_smlt_ij_importtemplateid"] = new EntityReference("sanic_tbl_smlt_importtemplate", templateId);
            if (raw.HasValue) job["sanic_smlt_ij_bytecursor"] = (decimal)raw.Value;
            service.Retrieve("sanic_tbl_smlt_importjob", jobId, Arg.Any<ColumnSet>()).Returns(job);
            service.Retrieve("sanic_tbl_smlt_importtemplate", templateId, Arg.Any<ColumnSet>())
                .Returns(new Entity("sanic_tbl_smlt_importtemplate", templateId));
            service.RetrieveMultiple(Arg.Any<QueryBase>()).Returns(new EntityCollection());

            var context = new ImportContextLoader(service).LoadContext(jobId);

            context.InitialByteCursor.Should().Be(raw.HasValue ? (long?)raw.Value : null);
            service.Received().Retrieve("sanic_tbl_smlt_importjob", jobId,
                Arg.Is<ColumnSet>(c => c.Columns.Contains("sanic_smlt_ij_bytecursor")));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(null, false)] // registros previos a 1.0.8: sin valor => false
        public void LoadContext_LoadsInsertOnlyFlagOfEachTableMapping(bool? raw, bool expected)
        {
            var service = Substitute.For<IOrganizationService>();
            var jobId = Guid.NewGuid();
            var templateId = Guid.NewGuid();
            var job = new Entity("sanic_tbl_smlt_importjob", jobId);
            job["sanic_smlt_ij_importtemplateid"] = new EntityReference("sanic_tbl_smlt_importtemplate", templateId);
            service.Retrieve("sanic_tbl_smlt_importjob", jobId, Arg.Any<ColumnSet>()).Returns(job);
            service.Retrieve("sanic_tbl_smlt_importtemplate", templateId, Arg.Any<ColumnSet>())
                .Returns(new Entity("sanic_tbl_smlt_importtemplate", templateId));
            var tm = new Entity("sanic_tbl_smlt_tablemapping", Guid.NewGuid());
            tm["sanic_smlt_tm_name"] = "Facturas";
            if (raw.HasValue) tm["sanic_smlt_tm_insertonly"] = raw.Value;
            service.RetrieveMultiple(Arg.Any<QueryBase>()).Returns(ci =>
                ((QueryExpression)ci.Arg<QueryBase>()).EntityName == "sanic_tbl_smlt_tablemapping"
                    ? new EntityCollection(new List<Entity> { tm })
                    : new EntityCollection());

            var context = new ImportContextLoader(service).LoadContext(jobId);

            context.TableMappings.Should().ContainSingle().Which.InsertOnly.Should().Be(expected);
        }
    }
}
