using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de la tabla de keys HMAC (<see cref="HmacKeyTableSpec"/>): la convención de
/// nombres del repo y, sobre todo, las garantías de seguridad que la hacen aceptable en DEV
/// (ningún rol de Smelter la toca, no vive en SecuritySpec.Tables); el schema viaja en la solución.
/// Datos puros — sin llamar Dataverse.
/// </summary>
public class HmacKeyTableSpecContractTests
{
    [Fact]
    public void Names_FollowTheSmelterConvention()
    {
        HmacKeyTableSpec.Table.Should().Be("sanic_tbl_smlt_hmackey");
        HmacKeyTableSpec.NameColumn.Should().Be("sanic_smlt_hk_name");
        HmacKeyTableSpec.ValueColumn.Should().Be("sanic_smlt_hk_value");

        HmacKeyTableSpec.Table.Should().MatchRegex("^sanic_tbl_smlt_[a-z]+$");
        HmacKeyTableSpec.NameColumn.Should().MatchRegex("^sanic_smlt_hk_[a-z]+$");
        HmacKeyTableSpec.ValueColumn.Should().MatchRegex("^sanic_smlt_hk_[a-z]+$");
    }

    [Fact]
    public void ValueColumn_IsNotAuditedNorValidForAdvancedFind()
    {
        HmacKeyTableSpec.ValueColumnAudited.Should().BeFalse("el valor de la key nunca debe quedar en el audit log");
        HmacKeyTableSpec.ValueColumnValidForAdvancedFind.Should().BeFalse("la key no debe poder consultarse por Advanced Find");
        HmacKeyTableSpec.TableAudited.Should().BeTrue("los cambios (quién creó/rotó/borró una key) sí se auditan");
    }

    [Fact]
    public void ValueColumn_IsSecured_ColumnSecurityAsDefenseInDepth()
    {
        // Seguridad de columna: el form muestra ****, y un rol con acceso a la tabla por error igual no lee
        // el valor. NO frena a System Administrator (limite documentado) y NO hay field security profile.
        HmacKeyTableSpec.ValueColumnSecured.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(null, true)]
    [InlineData(true, false)]
    public void NeedsSecuring_IsTrueUnlessAlreadySecured(bool? current, bool expected)
    {
        HmacKeyTableProvisioner.NeedsSecuring(current).Should().Be(expected);
    }

    [Fact]
    public void Table_IsNotPartOfTheSixSmelterTablesNorOfAnyRole()
    {
        SecuritySpec.Tables.All.Should().NotContain(HmacKeyTableSpec.Table);

        foreach (var role in SecuritySpec.Roles)
            role.Privileges.Should().NotContain(p => p.Table == HmacKeyTableSpec.Table,
                $"el rol '{role.Name}' no debe tener privilegios sobre la tabla de keys: solo SYSTEM / System Administrator la leen");
    }

    [Fact]
    public void Table_IsPartOfTheSmelterSolution_SoItsSchemaTravelsToTestAndProd()
    {
        // El SCHEMA viaja con la solución managed; el VALOR de la key se carga a mano en cada entorno.
        HmacKeyTableSpec.SolutionUniqueName.Should().Be(Catalogo.SolutionName);
        HmacKeyTableSpec.SolutionUniqueName.Should().Be("smlt_core_smelter");
        HmacKeyTableSpec.EntityComponentType.Should().Be(1);
    }

    [Fact]
    public void TableDescription_NoLongerClaimsDevOnly()
    {
        HmacKeyTableSpec.TableDescription.Should().NotContain("DEV ONLY");
    }

    [Fact]
    public void NeedsSolutionAdd_IsTrueOnlyWhenEntityIsNotYetAComponent()
    {
        var entityId = Guid.NewGuid();
        HmacKeyTableProvisioner.NeedsSolutionAdd(entityId, new[] { Guid.NewGuid() }).Should().BeTrue();
        HmacKeyTableProvisioner.NeedsSolutionAdd(entityId, Array.Empty<Guid>()).Should().BeTrue();
        HmacKeyTableProvisioner.NeedsSolutionAdd(entityId, new[] { Guid.NewGuid(), entityId }).Should().BeFalse();
    }
}
