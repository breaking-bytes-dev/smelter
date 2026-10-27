using System;
using System.Linq;
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    /// <summary>
    /// Tests <see cref="DataverseHmacKeyProvider"/> (DEV-only). Mismo patrón que
    /// <c>ImportContextLoaderTests</c>: se mockea <see cref="IOrganizationService.RetrieveMultiple"/>
    /// y se asertea la forma de la query y el resultado — sin llamadas reales a Dataverse.
    /// </summary>
    public class DataverseHmacKeyProviderTests
    {
        // 32 bytes 0x00..0x1f en hex.
        private const string KeyHex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";

        private static byte[] ExpectedKey() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        private static Entity KeyEntity(string value)
        {
            var e = new Entity(DataverseHmacKeyProvider.Table, Guid.NewGuid());
            e[DataverseHmacKeyProvider.NameColumn] = "cm-pan";
            e[DataverseHmacKeyProvider.ValueColumn] = value;
            return e;
        }

        private static EntityCollection Rows(params Entity[] entities)
        {
            var ec = new EntityCollection();
            foreach (var e in entities) ec.Entities.Add(e);
            return ec;
        }

        private static IOrganizationService ServiceReturning(EntityCollection rows)
        {
            var service = Substitute.For<IOrganizationService>();
            service.RetrieveMultiple(Arg.Any<QueryBase>()).Returns(rows);
            return service;
        }

        [Fact]
        public void GetKey_Found_ReturnsDecodedBytes()
        {
            var provider = new DataverseHmacKeyProvider(ServiceReturning(Rows(KeyEntity(KeyHex))));

            provider.GetKey("cm-pan").Should().Equal(ExpectedKey());
        }

        [Fact]
        public void GetKey_UppercaseHexAndSurroundingWhitespace_IsAccepted()
        {
            var provider = new DataverseHmacKeyProvider(
                ServiceReturning(Rows(KeyEntity("  " + KeyHex.ToUpperInvariant() + "\n"))));

            provider.GetKey("cm-pan").Should().Equal(ExpectedKey());
        }

        [Fact]
        public void GetKey_NotFound_ReturnsNull()
        {
            var provider = new DataverseHmacKeyProvider(ServiceReturning(Rows()));

            provider.GetKey("cm-pan").Should().BeNull();
        }

        [Fact]
        public void GetKey_BlankValue_ReturnsNull()
        {
            var provider = new DataverseHmacKeyProvider(ServiceReturning(Rows(KeyEntity("   "))));

            provider.GetKey("cm-pan").Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void GetKey_BlankName_ReturnsNullWithoutQuerying(string keyName)
        {
            var service = ServiceReturning(Rows(KeyEntity(KeyHex)));

            new DataverseHmacKeyProvider(service).GetKey(keyName).Should().BeNull();

            service.DidNotReceiveWithAnyArgs().RetrieveMultiple(null);
        }

        [Theory]
        [InlineData("zz0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")] // no-hex
        [InlineData("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1")]  // largo impar
        [InlineData("0x000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e")] // prefijo 0x
        public void GetKey_InvalidHex_ThrowsWithoutLeakingTheValue(string value)
        {
            var provider = new DataverseHmacKeyProvider(ServiceReturning(Rows(KeyEntity(value))));

            Action act = () => provider.GetKey("cm-pan");

            var ex = act.Should().Throw<InvalidOperationException>().Which;
            ex.Message.Should().Contain("cm-pan").And.Contain("hex");
            ex.ToString().Should().NotContain(value.Trim());
        }

        [Fact]
        public void GetKey_DuplicateNames_ThrowsInsteadOfPickingOne()
        {
            var provider = new DataverseHmacKeyProvider(
                ServiceReturning(Rows(KeyEntity(KeyHex), KeyEntity(KeyHex))));

            Action act = () => provider.GetKey("cm-pan");

            act.Should().Throw<InvalidOperationException>().WithMessage("*cm-pan*");
        }

        [Fact]
        public void GetKey_CachesPerInstance_QueriesOnce()
        {
            var service = ServiceReturning(Rows(KeyEntity(KeyHex)));
            var provider = new DataverseHmacKeyProvider(service);

            var first = provider.GetKey("cm-pan");
            var second = provider.GetKey("cm-pan");

            second.Should().BeSameAs(first);
            service.Received(1).RetrieveMultiple(Arg.Any<QueryBase>());
        }

        private static IOrganizationService FaultingService()
        {
            var service = Substitute.For<IOrganizationService>();
            service.RetrieveMultiple(Arg.Any<QueryBase>()).Returns(_ =>
                throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { Message = "Entity 'sanic_tbl_smlt_hmackey' does not exist" }));
            return service;
        }

        [Fact]
        public void GetKey_MissingTableFault_IsWrappedAsInvalidOperationWithoutFaultMessage()
        {
            var provider = new DataverseHmacKeyProvider(FaultingService());

            Action act = () => provider.GetKey("cm-pan");

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("HMAC: key store not available in this environment ('cm-pan').")
                .Which.ToString().Should().NotContain("does not exist");
        }

        [Fact]
        public void GetKey_Failure_IsCachedPerKeyName_QueriesOnce()
        {
            var service = FaultingService();
            var provider = new DataverseHmacKeyProvider(service);

            Action act = () => provider.GetKey("cm-pan");
            act.Should().Throw<InvalidOperationException>();
            act.Should().Throw<InvalidOperationException>();
            act.Should().Throw<InvalidOperationException>();

            service.Received(1).RetrieveMultiple(Arg.Any<QueryBase>());
        }

        [Fact]
        public void GetKey_Failure_OnOneName_DoesNotPoisonAnotherName()
        {
            var service = Substitute.For<IOrganizationService>();
            service.RetrieveMultiple(Arg.Any<QueryBase>()).Returns(
                _ => throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault()),
                _ => Rows(KeyEntity(KeyHex)));
            var provider = new DataverseHmacKeyProvider(service);

            Action bad = () => provider.GetKey("a");
            bad.Should().Throw<InvalidOperationException>();

            provider.GetKey("b").Should().Equal(ExpectedKey());
        }

        [Fact]
        public void GetKey_CachesNotFoundToo()
        {
            var service = ServiceReturning(Rows());
            var provider = new DataverseHmacKeyProvider(service);

            provider.GetKey("cm-pan").Should().BeNull();
            provider.GetKey("cm-pan").Should().BeNull();

            service.Received(1).RetrieveMultiple(Arg.Any<QueryBase>());
        }

        [Fact]
        public void GetKey_DifferentNames_QueryEachOnce()
        {
            var service = ServiceReturning(Rows(KeyEntity(KeyHex)));
            var provider = new DataverseHmacKeyProvider(service);

            provider.GetKey("a");
            provider.GetKey("b");
            provider.GetKey("a");

            service.Received(2).RetrieveMultiple(Arg.Any<QueryBase>());
        }

        [Fact]
        public void GetKey_QueryShape_FiltersByNameAndActiveAndFetchesOnlyTheValueColumn()
        {
            QueryExpression captured = null;
            var service = Substitute.For<IOrganizationService>();
            service.RetrieveMultiple(Arg.Do<QueryBase>(q => captured = (QueryExpression)q)).Returns(Rows());

            new DataverseHmacKeyProvider(service).GetKey("cm-pan");

            captured.EntityName.Should().Be("sanic_tbl_smlt_hmackey");
            captured.ColumnSet.AllColumns.Should().BeFalse();
            captured.ColumnSet.Columns.Should().Equal("sanic_smlt_hk_value");
            captured.Criteria.Conditions.Should().Contain(c =>
                c.AttributeName == "sanic_smlt_hk_name"
                && c.Operator == ConditionOperator.Equal
                && c.Values.Contains("cm-pan"));
            captured.Criteria.Conditions.Should().Contain(c =>
                c.AttributeName == "statecode" && c.Operator == ConditionOperator.Equal && c.Values.Contains(0));
        }

        [Fact]
        public void Constructor_NullService_Throws()
        {
            Action act = () => new DataverseHmacKeyProvider(null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Integration_WithTransformEngine_ProducesTheKnownVector()
        {
            var provider = new DataverseHmacKeyProvider(ServiceReturning(Rows(KeyEntity(KeyHex))));
            var engine = new TransformEngine(provider);

            var result = engine.Evaluate("HMAC(\"4111111111111111\", \"cm-pan\")",
                new System.Collections.Generic.Dictionary<string, string>());

            result.Should().Be("0622241201382a45912fb22828b3f7db5153cf2072722a73ded22623ea79abc9");
        }
    }
}
