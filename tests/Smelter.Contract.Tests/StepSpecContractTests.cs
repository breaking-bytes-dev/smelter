using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de StepSpec (message steps) — sin llamar Dataverse. Encodea, como test de datos, el
/// Requirement "Ownership del Import Job heredado del team del template" (smelter-access-control):
/// el step de AssignJobOwner debe ser sync Pre-operation sobre el Create de Import Job — cualquier
/// cambio accidental (async, post-op, otra tabla) queda en rojo acá antes de llegar a
/// Smelter.Verify contra un ambiente real.
/// </summary>
public class StepSpecContractTests
{
    [Fact]
    public void AssignJobOwner_Step_IsSyncPreOperationOnCreateOfImportJob()
    {
        var step = StepCatalogo.Steps.Single(s => s.PluginTypeName == "Smelter.Plugins.AssignJobOwnerPlugin");

        step.Message.Should().Be("Create",
            "el owner se hereda al NACER el job — pre-op de Create, sin AssignRequest posterior");
        step.PrimaryEntity.Should().Be(SecuritySpec.Tables.ImportJob);
        step.Stage.Should().Be(StepStage.PreOperation,
            "en Pre-operation basta mutar el Target; en Post-operation habría que hacer un Assign extra");
        step.Mode.Should().Be(StepMode.Synchronous,
            "async dejaría una ventana en la que el job existe owned por el creador y el team no lo ve");
    }

    [Fact]
    public void Steps_PluginTypeNames_LiveInSmelterPluginsNamespace()
    {
        StepCatalogo.Steps.Should().OnlyContain(s => s.PluginTypeName.StartsWith("Smelter.Plugins."));
    }

    [Fact]
    public void CreateSteps_HaveNoFilteringAttributes()
    {
        StepCatalogo.Steps.Where(s => s.Message == "Create").Should().OnlyContain(
            s => string.IsNullOrEmpty(s.FilteringAttributes),
            "filteringattributes solo aplica a Update — en Create es ruido que confunde al lector");
    }

    [Fact]
    public void Steps_HaveUniqueNamesAndPositiveRank()
    {
        StepCatalogo.Steps.Select(s => s.Name).Distinct().Should().HaveCount(StepCatalogo.Steps.Length);
        StepCatalogo.Steps.Should().OnlyContain(s => s.Rank >= 1);
    }
}
