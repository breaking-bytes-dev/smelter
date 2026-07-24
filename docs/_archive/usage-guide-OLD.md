[← Volver al índice de documentación](../README.md)

# Smelter — Usage Guide

> This guide will be expanded as features are implemented.

## Quick Start

1. **Create an Import Template** — define source type (CSV/Excel), delimiter, header row setting
2. **Add Table Mappings** — specify which Dataverse tables will receive data
3. **Add Column Mappings** — map source columns to target columns with types and transforms
4. **Add Relationship Mappings** — define how tables relate (1:N, N:M)
5. **Upload File & Launch** — create an Import Job, upload the file, set status to Queued
6. **Monitor Progress** — watch the Import Job record for real-time counters

## Scenarios

### Simple Import (Single Table)

One CSV → one Dataverse table, direct column mapping.

### Normalized Import (Multiple Tables)

One CSV with denormalized data → multiple related Dataverse tables with deduplication.

### Multi-Sheet Excel

Each sheet maps to a different table (or multiple tables per sheet).

### Hierarchical Lookups

Country → State → City chain, processed in correct dependency order.

---

*Detailed walkthroughs with examples coming in Phase 2+.*
