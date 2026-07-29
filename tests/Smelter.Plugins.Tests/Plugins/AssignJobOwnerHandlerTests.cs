using System;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Smelter.Plugins;
using Xunit;

namespace Smelter.Plugins.Tests.Plugins
{
    public class AssignJobOwnerHandlerTests
    {
        private readonly IOrganizationService _service = Substitute.For<IOrganizationService>();

        private AssignJobOwnerHandler Build() => new AssignJobOwnerHandler(_service);

        private static Entity JobTargetWithTemplate(Guid templateId) =>
            new Entity(AssignJobOwnerHandler.ImportJobTable)
            {
                [AssignJobOwnerHandler.TemplateLookupAttribute] =
                    new EntityReference(AssignJobOwnerHandler.ImportTemplateTable, templateId)
            };

        private void TemplateOwnedBy(Guid templateId, EntityReference owner)
        {
            var template = new Entity(AssignJobOwnerHandler.ImportTemplateTable, templateId)
            {
                [AssignJobOwnerHandler.OwnerAttribute] = owner
            };
            _service.Retrieve(AssignJobOwnerHandler.ImportTemplateTable, templateId, Arg.Any<ColumnSet>())
                .Returns(template);
        }

        [Fact]
        public void Run_TemplateOwnedByTeam_SetsJobOwnerToThatTeam()
        {
            var templateId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            TemplateOwnedBy(templateId, new EntityReference("team", teamId));
            var target = JobTargetWithTemplate(templateId);

            Build().Run(target);

            var owner = target.GetAttributeValue<EntityReference>(AssignJobOwnerHandler.OwnerAttribute);
            owner.Should().NotBeNull();
            owner.LogicalName.Should().Be("team");
            owner.Id.Should().Be(teamId);
        }

        [Fact]
        public void Run_TemplateOwnedByUser_DoesNotTouchOwner()
        {
            var templateId = Guid.NewGuid();
            TemplateOwnedBy(templateId, new EntityReference("systemuser", Guid.NewGuid()));
            var target = JobTargetWithTemplate(templateId);

            Build().Run(target);

            target.Contains(AssignJobOwnerHandler.OwnerAttribute).Should().BeFalse(
                "con template owned por un user, el owner del job queda en el default de la plataforma (el creador)");
        }

        [Fact]
        public void Run_TemplateOwnedByTeam_OverridesExplicitOwnerInTarget()
        {
            var templateId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            TemplateOwnedBy(templateId, new EntityReference("team", teamId));
            var target = JobTargetWithTemplate(templateId);
            target[AssignJobOwnerHandler.OwnerAttribute] = new EntityReference("systemuser", Guid.NewGuid());

            Build().Run(target);

            var owner = target.GetAttributeValue<EntityReference>(AssignJobOwnerHandler.OwnerAttribute);
            owner.LogicalName.Should().Be("team",
                "la regla es determinista: template owned por team ⇒ job owned por ese team, gane a cualquier owner explícito");
            owner.Id.Should().Be(teamId);
        }

        [Fact]
        public void Run_NoTemplateReference_DoesNotRetrieveNorSetOwner()
        {
            var target = new Entity(AssignJobOwnerHandler.ImportJobTable);

            Build().Run(target);

            _service.DidNotReceive().Retrieve(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<ColumnSet>());
            target.Contains(AssignJobOwnerHandler.OwnerAttribute).Should().BeFalse();
        }

        [Fact]
        public void Run_TemplateWithoutOwnerInMetadata_DoesNotSetOwner()
        {
            // Defensivo: un Retrieve que no trae ownerid (no debería pasar con ColumnSet("ownerid"),
            // pero un null no puede tirar abajo el Create del job).
            var templateId = Guid.NewGuid();
            _service.Retrieve(AssignJobOwnerHandler.ImportTemplateTable, templateId, Arg.Any<ColumnSet>())
                .Returns(new Entity(AssignJobOwnerHandler.ImportTemplateTable, templateId));
            var target = JobTargetWithTemplate(templateId);

            Build().Run(target);

            target.Contains(AssignJobOwnerHandler.OwnerAttribute).Should().BeFalse();
        }

        [Fact]
        public void Run_RetrievesOnlyTheOwnerColumnOfTheTemplate()
        {
            var templateId = Guid.NewGuid();
            TemplateOwnedBy(templateId, new EntityReference("team", Guid.NewGuid()));

            Build().Run(JobTargetWithTemplate(templateId));

            _service.Received(1).Retrieve(
                AssignJobOwnerHandler.ImportTemplateTable,
                templateId,
                Arg.Is<ColumnSet>(c => c.Columns.Count == 1 && c.Columns[0] == AssignJobOwnerHandler.OwnerAttribute));
        }

        [Fact]
        public void Run_NullTarget_Throws()
        {
            Action act = () => Build().Run(null);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
