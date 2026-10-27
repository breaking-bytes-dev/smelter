using FluentAssertions;
using Microsoft.Xrm.Sdk.Metadata;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de los cambios de schema de la auditoría de campos (1.0.10): atributo junction a eliminar, opciones
/// muertas a quitar de choices, ajustes de required/default y campos a ocultar. Specs puras, sin Dataverse.
/// </summary>
public class SchemaAuditSpecContractTests
{
    [Fact]
    public void IsJunctionTable_IsInTheCleanupCatalog()
    {
        SchemaCleanupCatalogo.Attributes.Should().Contain(a =>
            a.EntityLogicalName == SecuritySpec.Tables.TableMapping && a.AttributeLogicalName == "sanic_smlt_tm_isjunctiontable");
    }

    [Fact]
    public void DeadOptions_AreExactlyTheOnesTheEngineNeverProduces()
    {
        var removed = OptionCleanupCatalogo.Options
            .Select(o => (o.EntityLogicalName, o.AttributeLogicalName, o.Value))
            .ToList();

        removed.Should().BeEquivalentTo(new[]
        {
            (SecuritySpec.Tables.RelationshipMapping, "sanic_smlt_rm_relationshiptype", 1),     // ManyToMany
            (SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_status", 0),                      // Created
            (SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_status", 1),                      // Updated
            (SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_status", 2),                      // Skipped
            (SecuritySpec.Tables.ImportJobLog, "sanic_smlt_ijl_errorcategory", 6),               // Transient
        });
        OptionCleanupCatalogo.Options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Label) && !string.IsNullOrWhiteSpace(o.Reason));
    }

    [Fact]
    public void OptionRemovals_NeverTargetAnAttributeScheduledForDeletion()
    {
        var deleted = SchemaCleanupCatalogo.Attributes.Select(a => a.AttributeLogicalName).ToHashSet();

        OptionCleanupCatalogo.Options.Should().OnlyContain(o => !deleted.Contains(o.AttributeLogicalName));
    }

    [Fact]
    public void SourceType_DefaultsToCsv_AndIsHiddenFromTheTemplateForm()
    {
        var tune = SchemaTuningCatalogo.Attributes.Single(a => a.Attribute == "sanic_smlt_it_sourcetype");

        tune.Entity.Should().Be(SecuritySpec.Tables.ImportTemplate);
        // El required level NO se toca: UpdateAttributeRequest ignora el cambio a None (verificado en Dev) y
        // ApplicationRequired solo rige en formularios, donde el campo está oculto y con default CSV.
        tune.RequiredLevel.Should().BeNull();
        tune.PicklistDefault.Should().Be(0, "0 = CSV, la única opción");
        SchemaTuningCatalogo.HiddenFields.Should().Contain(h =>
            h.Entity == SecuritySpec.Tables.ImportTemplate && h.Field == "sanic_smlt_it_sourcetype" && !h.Visible);
    }

    [Fact]
    public void UpdateIfExists_DefaultsToYes()
    {
        var tune = SchemaTuningCatalogo.Attributes.Single(a => a.Attribute == "sanic_smlt_tm_updateifexists");

        tune.Entity.Should().Be(SecuritySpec.Tables.TableMapping);
        tune.BooleanDefault.Should().BeTrue();
    }

    [Fact]
    public void TuningSpecs_TargetSmelterTables_AndChangeSomething()
    {
        foreach (var t in SchemaTuningCatalogo.Attributes)
        {
            SecuritySpec.Tables.All.Should().Contain(t.Entity);
            (t.RequiredLevel is not null || t.PicklistDefault is not null || t.BooleanDefault is not null || t.IntMin is not null || t.IntMax is not null)
                .Should().BeTrue($"{t.Attribute} debe cambiar algo");
        }
    }

    [Fact]
    public void OptionCleanup_PurgaFilas_SoloEnElImportJobLog()
    {
        // Purgar filas que usan una opción muerta solo vale para datos de diagnóstico descartables; en una tabla de
        // configuración (p.ej. ManyToMany en Relationship Mapping) borraría configuración real.
        OptionCleanupCatalogo.Options.Where(o => o.PurgeRowsInUse)
            .Should().OnlyContain(o => o.EntityLogicalName == SecuritySpec.Tables.ImportJobLog);
        OptionCleanupCatalogo.Options.Single(o => o.AttributeLogicalName == "sanic_smlt_rm_relationshiptype")
            .PurgeRowsInUse.Should().BeFalse();
    }
}
