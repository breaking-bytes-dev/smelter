using System;
using System.Collections.Generic;
using System.Linq;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Builds a Directed Acyclic Graph (DAG) from RelationshipMappings and returns
    /// TableMappings in topological order (parents before children).
    /// Detects circular dependencies and throws InvalidOperationException.
    /// </summary>
    public static class DependencyResolver
    {
        /// <summary>
        /// Resolve processing order using Kahn's algorithm (BFS topological sort).
        /// </summary>
        /// <param name="tableMappings">All table mappings in the template.</param>
        /// <param name="relationships">All relationship mappings defining parent→child edges.</param>
        /// <returns>Table mappings sorted in dependency order (parents first).</returns>
        /// <exception cref="InvalidOperationException">Thrown when a circular dependency is detected.</exception>
        public static List<TableMappingConfig> Resolve(
            List<TableMappingConfig> tableMappings,
            List<RelationshipMappingConfig> relationships)
        {
            if (tableMappings == null || tableMappings.Count == 0)
                return new List<TableMappingConfig>();

            if (relationships == null || relationships.Count == 0)
                return new List<TableMappingConfig>(tableMappings);

            // Build adjacency list (parent → children) and in-degree map (number of parents per node).
            // A node with in-degree 0 has no dependencies and is safe to process immediately.
            var tableById = tableMappings.ToDictionary(t => t.Id);
            var adjacency = new Dictionary<Guid, List<Guid>>();
            var inDegree = new Dictionary<Guid, int>();

            foreach (var table in tableMappings)
            {
                adjacency[table.Id] = new List<Guid>();
                inDegree[table.Id] = 0;
            }

            foreach (var rel in relationships)
            {
                // Validate both sides exist
                if (!tableById.ContainsKey(rel.ParentTableMappingId) ||
                    !tableById.ContainsKey(rel.ChildTableMappingId))
                    continue;

                // Self-reference check
                if (rel.ParentTableMappingId == rel.ChildTableMappingId)
                {
                    var tableName = tableById[rel.ParentTableMappingId].Name;
                    throw new InvalidOperationException(
                        $"Circular dependency detected: table '{tableName}' references itself.");
                }

                // Edge: parent → child (parent must be processed first)
                adjacency[rel.ParentTableMappingId].Add(rel.ChildTableMappingId);
                inDegree[rel.ChildTableMappingId]++;
            }

            // Kahn's algorithm: BFS from nodes with in-degree 0
            var queue = new Queue<Guid>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value == 0)
                    queue.Enqueue(kvp.Key);
            }

            var sorted = new List<TableMappingConfig>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                sorted.Add(tableById[current]);

                foreach (var neighbor in adjacency[current])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }

            // If not all nodes were visited, there's a cycle
            if (sorted.Count != tableMappings.Count)
            {
                var cycleNodes = tableMappings
                    .Where(t => !sorted.Contains(t))
                    .Select(t => t.Name);
                throw new InvalidOperationException(
                    $"Circular dependency detected involving tables: {string.Join(", ", cycleNodes)}. " +
                    "Check your relationship mappings for cycles.");
            }

            return sorted;
        }
    }
}
