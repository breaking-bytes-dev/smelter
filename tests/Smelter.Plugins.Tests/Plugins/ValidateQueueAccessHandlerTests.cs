using System;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Smelter.Plugins;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Plugins
{
    public class ValidateQueueAccessHandlerTests
    {
        private const string Status = "sanic_smlt_ij_status";

        private readonly IOrganizationService _service = Substitute.For<IOrganizationService>();
        private readonly IAccessChecker _access = Substitute.For<IAccessChecker>();
        private readonly Guid _user = Guid.NewGuid();
        private readonly Guid _templateId = Guid.NewGuid();
        private readonly Guid _jobId = Guid.NewGuid();

        private ValidateQueueAccessHandler Build() => new ValidateQueueAccessHandler(_service, _access);

        private EntityReference TemplateRef() =>
            new EntityReference(AssignJobOwnerHandler.ImportTemplateTable, _templateId);

        private Entity Target(int? status, bool withTemplate, bool withId)
        {
            var e = withId
                ? new Entity(AssignJobOwnerHandler.ImportJobTable, _jobId)
                : new Entity(AssignJobOwnerHandler.ImportJobTable);
            if (status.HasValue) e[Status] = new OptionSetValue(status.Value);
            if (withTemplate) e[AssignJobOwnerHandler.TemplateLookupAttribute] = TemplateRef();
            return e;
        }

        private void JobInDbHasTemplate()
        {
            _service.Retrieve(AssignJobOwnerHandler.ImportJobTable, _jobId, Arg.Any<ColumnSet>())
                .Returns(new Entity(AssignJobOwnerHandler.ImportJobTable, _jobId)
                {
                    [AssignJobOwnerHandler.TemplateLookupAttribute] = TemplateRef()
                });
        }

        [Fact]
        public void Update_QueuedAndUserCanReadTemplate_Allows()
        {
            JobInDbHasTemplate();
            _access.HasReadAccess(_user, Arg.Is<EntityReference>(r => r.Id == _templateId)).Returns(true);

            Action act = () => Build().Run("Update", Target(0, false, true), _user);

            act.Should().NotThrow();
        }

        [Fact]
        public void Update_QueuedAndUserCannotReadTemplate_Throws()
        {
            JobInDbHasTemplate();
            _access.HasReadAccess(_user, Arg.Any<EntityReference>()).Returns(false);

            Action act = () => Build().Run("Update", Target(0, false, true), _user);

            act.Should().Throw<InvalidPluginExecutionException>()
                .WithMessage("No tenés acceso al Import Template de este job: no se puede poner en cola.");
        }

        [Fact]
        public void Update_ChecksTheInitiatingUser_NotAnyoneElse()
        {
            JobInDbHasTemplate();
            _access.HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>()).Returns(false);
            _access.HasReadAccess(_user, Arg.Any<EntityReference>()).Returns(true);

            Action act = () => Build().Run("Update", Target(0, false, true), _user);

            act.Should().NotThrow();
            _access.Received(1).HasReadAccess(_user, Arg.Any<EntityReference>());
        }

        [Theory]
        [InlineData(1)] // Processing — lo que setea el flow/PrepareImport
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)] // Failed — lo que setea el handler del flow
        public void Update_StatusOtherThanQueued_DoesNothing(int status)
        {
            _access.HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>()).Returns(false);

            Action act = () => Build().Run("Update", Target(status, false, true), _user);

            act.Should().NotThrow();
            _access.DidNotReceive().HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>());
            _service.DidNotReceive().Retrieve(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<ColumnSet>());
        }

        [Fact]
        public void Update_StatusNotInTarget_DoesNothing()
        {
            Action act = () => Build().Run("Update", Target(null, false, true), _user);

            act.Should().NotThrow();
            _access.DidNotReceive().HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>());
        }

        [Fact]
        public void Update_TemplateInTarget_UsesItForTheAccessCheck_StillRetrievesStatus()
        {
            JobInDbWithStatus(null);
            _access.HasReadAccess(_user, Arg.Any<EntityReference>()).Returns(false);

            Action act = () => Build().Run("Update", Target(0, true, true), _user);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("No tenés acceso*");
            _service.Received(1).Retrieve(AssignJobOwnerHandler.ImportJobTable, _jobId,
                Arg.Is<ColumnSet>(c => c.Columns.Contains(Status)));
        }

        private void JobInDbWithStatus(int? status)
        {
            var job = new Entity(AssignJobOwnerHandler.ImportJobTable, _jobId)
            {
                [AssignJobOwnerHandler.TemplateLookupAttribute] = TemplateRef()
            };
            if (status.HasValue) job[Status] = new OptionSetValue(status.Value);
            _service.Retrieve(AssignJobOwnerHandler.ImportJobTable, _jobId, Arg.Any<ColumnSet>()).Returns(job);
        }

        private const string ProcessingMessage =
            "El Import Job se está procesando: no se puede volver a poner en cola hasta que termine.";

        [Theory]
        [InlineData(false)]
        [InlineData(true)] // template en el Target: igual hay que leer el status persistido
        public void Update_QueuedWhileJobIsProcessing_ThrowsBeforeTheAccessCheck(bool templateInTarget)
        {
            JobInDbWithStatus(1);
            _access.HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>()).Returns(true);

            Action act = () => Build().Run("Update", Target(0, templateInTarget, true), _user);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage(ProcessingMessage);
            _access.DidNotReceive().HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>());
        }

        [Theory]
        [InlineData(0)] // ya en cola: idempotente, el claim lo resuelve
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(null)]
        public void Update_QueuedWhileJobIsNotProcessing_Allows(int? persisted)
        {
            JobInDbWithStatus(persisted);
            _access.HasReadAccess(_user, Arg.Any<EntityReference>()).Returns(true);

            Action act = () => Build().Run("Update", Target(0, false, true), _user);

            act.Should().NotThrow();
        }

        [Fact]
        public void Create_QueuedIgnoresPersistedStatus_DoesNotRetrieve()
        {
            _access.HasReadAccess(_user, Arg.Any<EntityReference>()).Returns(true);

            Action act = () => Build().Run("Create", Target(0, true, false), _user);

            act.Should().NotThrow();
            _service.DidNotReceive().Retrieve(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<ColumnSet>());
        }

        [Fact]
        public void Update_JobHasNoTemplate_DoesNothing()
        {
            _service.Retrieve(AssignJobOwnerHandler.ImportJobTable, _jobId, Arg.Any<ColumnSet>())
                .Returns(new Entity(AssignJobOwnerHandler.ImportJobTable, _jobId));

            Action act = () => Build().Run("Update", Target(0, false, true), _user);

            act.Should().NotThrow();
            _access.DidNotReceive().HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>());
        }

        [Fact]
        public void Create_QueuedAndUserCannotReadTemplate_Throws()
        {
            _access.HasReadAccess(_user, Arg.Any<EntityReference>()).Returns(false);

            Action act = () => Build().Run("Create", Target(0, true, false), _user);

            act.Should().Throw<InvalidPluginExecutionException>().WithMessage("No tenés acceso*");
            _service.DidNotReceive().Retrieve(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<ColumnSet>());
        }

        [Fact]
        public void Create_QueuedAndUserCanReadTemplate_Allows()
        {
            _access.HasReadAccess(_user, Arg.Is<EntityReference>(r => r.Id == _templateId)).Returns(true);

            Action act = () => Build().Run("Create", Target(0, true, false), _user);

            act.Should().NotThrow();
        }

        [Fact]
        public void Create_WithoutStatus_DoesNothing()
        {
            _access.HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>()).Returns(false);

            Action act = () => Build().Run("Create", Target(null, true, false), _user);

            act.Should().NotThrow();
            _access.DidNotReceive().HasReadAccess(Arg.Any<Guid>(), Arg.Any<EntityReference>());
        }

        [Fact]
        public void Run_NullTarget_Throws()
        {
            Action act = () => Build().Run("Update", null, _user);
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
