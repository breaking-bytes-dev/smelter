using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Plugins.Tests.Services
{
    public class DependencyResolverTests
    {
        /// <summary>
        /// Helper to create a TableMappingConfig with an Id and name.
        /// </summary>
        private static TableMappingConfig MakeTable(string name, Guid? id = null)
        {
            return new TableMappingConfig
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                TargetTable = name.ToLowerInvariant()
            };
        }

        private static RelationshipMappingConfig MakeRelationship(
            TableMappingConfig parent, TableMappingConfig child,
            int relType = 0, string lookupColumn = null)
        {
            return new RelationshipMappingConfig
            {
                Id = Guid.NewGuid(),
                ParentTableMappingId = parent.Id,
                ChildTableMappingId = child.Id,
                RelationshipType = relType,
                LookupColumnOnChild = lookupColumn ?? $"{parent.Name.ToLowerInvariant()}id"
            };
        }

        [Fact]
        public void Resolve_NoRelationships_ReturnsTablesInOriginalOrder()
        {
            var t1 = MakeTable("Account");
            var t2 = MakeTable("Contact");
            var tables = new List<TableMappingConfig> { t1, t2 };

            var result = DependencyResolver.Resolve(tables, new List<RelationshipMappingConfig>());

            result.Should().HaveCount(2);
        }

        [Fact]
        public void Resolve_SingleParentChild_ParentComesFirst()
        {
            var parent = MakeTable("Account");
            var child = MakeTable("Contact");
            var rel = MakeRelationship(parent, child);

            var result = DependencyResolver.Resolve(
                new List<TableMappingConfig> { child, parent },
                new List<RelationshipMappingConfig> { rel });

            result.IndexOf(parent).Should().BeLessThan(result.IndexOf(child),
                "parent Account must be processed before child Contact");
        }

        [Fact]
        public void Resolve_ThreeLevelHierarchy_CorrectOrder()
        {
            // Country → State → City
            var country = MakeTable("Country");
            var state = MakeTable("State");
            var city = MakeTable("City");

            var rel1 = MakeRelationship(country, state);
            var rel2 = MakeRelationship(state, city);

            // Provide in reverse order to test sorting
            var result = DependencyResolver.Resolve(
                new List<TableMappingConfig> { city, state, country },
                new List<RelationshipMappingConfig> { rel1, rel2 });

            result.IndexOf(country).Should().BeLessThan(result.IndexOf(state));
            result.IndexOf(state).Should().BeLessThan(result.IndexOf(city));
        }

        [Fact]
        public void Resolve_DiamondDependency_AllParentsBeforeChild()
        {
            //   A    B
            //    \  /
            //     C
            var a = MakeTable("A");
            var b = MakeTable("B");
            var c = MakeTable("C");

            var rel1 = MakeRelationship(a, c);
            var rel2 = MakeRelationship(b, c);

            var result = DependencyResolver.Resolve(
                new List<TableMappingConfig> { c, b, a },
                new List<RelationshipMappingConfig> { rel1, rel2 });

            result.IndexOf(a).Should().BeLessThan(result.IndexOf(c));
            result.IndexOf(b).Should().BeLessThan(result.IndexOf(c));
        }

        [Fact]
        public void Resolve_CircularDependency_ThrowsInvalidOperation()
        {
            var a = MakeTable("A");
            var b = MakeTable("B");

            var rel1 = MakeRelationship(a, b);
            var rel2 = MakeRelationship(b, a);

            Action act = () => DependencyResolver.Resolve(
                new List<TableMappingConfig> { a, b },
                new List<RelationshipMappingConfig> { rel1, rel2 });

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*circular*");
        }

        [Fact]
        public void Resolve_SelfReference_ThrowsInvalidOperation()
        {
            var a = MakeTable("A");
            var rel = MakeRelationship(a, a);

            Action act = () => DependencyResolver.Resolve(
                new List<TableMappingConfig> { a },
                new List<RelationshipMappingConfig> { rel });

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*circular*");
        }

        [Fact]
        public void Resolve_MultipleRoots_AllProcessed()
        {
            // A → C, B → D (two independent trees)
            var a = MakeTable("A");
            var b = MakeTable("B");
            var c = MakeTable("C");
            var d = MakeTable("D");

            var rel1 = MakeRelationship(a, c);
            var rel2 = MakeRelationship(b, d);

            var result = DependencyResolver.Resolve(
                new List<TableMappingConfig> { d, c, b, a },
                new List<RelationshipMappingConfig> { rel1, rel2 });

            result.Should().HaveCount(4);
            result.IndexOf(a).Should().BeLessThan(result.IndexOf(c));
            result.IndexOf(b).Should().BeLessThan(result.IndexOf(d));
        }

        [Fact]
        public void Resolve_SingleTable_ReturnsSingleTable()
        {
            var a = MakeTable("Account");

            var result = DependencyResolver.Resolve(
                new List<TableMappingConfig> { a },
                new List<RelationshipMappingConfig>());

            result.Should().ContainSingle().Which.Should().Be(a);
        }

        [Fact]
        public void Resolve_EmptyInput_ReturnsEmpty()
        {
            var result = DependencyResolver.Resolve(
                new List<TableMappingConfig>(),
                new List<RelationshipMappingConfig>());

            result.Should().BeEmpty();
        }
    }
}
