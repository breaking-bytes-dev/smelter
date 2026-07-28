using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// CONTRATO <see cref="SecuritySpec.TemplateToJobRelationshipSchemaName"/> ↔ metadata REAL de la
/// relación (<c>solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml</c>) — si
/// alguien renombra la relación en Dev sin actualizar la constante (o viceversa), lo que hoy falla
/// recién en runtime (<c>SecurityProvisioner.SetCascade</c> / <c>SecurityAssertions.AssertCascade</c>,
/// "relación no encontrada" contra Dataverse) queda agarrado acá, sin llamar Dataverse.
/// </summary>
public class RelationshipSchemaContractTests
{
    private static readonly string RelationshipsXmlPath = Path.Combine(
        SolutionMetadata.RepoRoot, "solutions", "unpacked", "Other", "Relationships", "sanic_tbl_smlt_importtemplate.xml");

    [Fact]
    public void TemplateToJobRelationshipSchemaName_ExistsInSolutionMetadata()
    {
        File.Exists(RelationshipsXmlPath).Should().BeTrue($"metadata no encontrada: {RelationshipsXmlPath}");

        var doc = XDocument.Load(RelationshipsXmlPath);
        var relationship = doc.Descendants("EntityRelationship")
            .FirstOrDefault(e => (string?)e.Attribute("Name") == SecuritySpec.TemplateToJobRelationshipSchemaName);

        relationship.Should().NotBeNull(
            $"SecuritySpec.TemplateToJobRelationshipSchemaName ('{SecuritySpec.TemplateToJobRelationshipSchemaName}') " +
            "debe existir como <EntityRelationship Name=\"...\"> real en la metadata de la solución");
    }

    [Fact]
    public void TemplateToJobRelationshipSchemaName_IsOneToManyFromImportJobToImportTemplate()
    {
        var doc = XDocument.Load(RelationshipsXmlPath);
        var relationship = doc.Descendants("EntityRelationship")
            .Single(e => (string?)e.Attribute("Name") == SecuritySpec.TemplateToJobRelationshipSchemaName);

        ((string?)relationship.Element("EntityRelationshipType")).Should().Be("OneToMany",
            "SecurityProvisioner.SetCascade castea RelationshipMetadata a OneToManyRelationshipMetadata — " +
            "si la relación real no fuera 1:N, ese cast fallaría recién contra Dev");
        ((string?)relationship.Element("ReferencingEntityName")).Should().Be(SecuritySpec.Tables.ImportJob);
        ((string?)relationship.Element("ReferencedEntityName")).Should().Be(SecuritySpec.Tables.ImportTemplate);
    }
}
