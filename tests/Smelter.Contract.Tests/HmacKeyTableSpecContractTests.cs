using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO de la tabla DEV-only de keys HMAC (<see cref="HmacKeyTableSpec"/>): la convención de
/// nombres del repo y, sobre todo, las garantías de seguridad que la hacen aceptable en DEV
/// (ningún rol de Smelter la toca, no vive en SecuritySpec.Tables, no viaja en la solución).
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
    public void Table_IsNotPartOfTheSixSmelterTablesNorOfAnyRole()
    {
        SecuritySpec.Tables.All.Should().NotContain(HmacKeyTableSpec.Table);

        foreach (var role in SecuritySpec.Roles)
            role.Privileges.Should().NotContain(p => p.Table == HmacKeyTableSpec.Table,
                $"el rol '{role.Name}' no debe tener privilegios sobre la tabla de keys: solo SYSTEM / System Administrator la leen");
    }

    [Fact]
    public void Table_IsNotShippedInTheSmelterSolution()
    {
        // DEV-only: se crea sin SolutionUniqueName (Default solution) para que NO viaje a PROD
        // con el export de smlt_core_smelter. PROD usa managed identity + Key Vault.
        HmacKeyTableSpec.SolutionUniqueName.Should().BeNull();
    }
}
