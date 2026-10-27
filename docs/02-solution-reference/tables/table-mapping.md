[← Volver al índice de documentación](../../README.md)

# Table Mapping — `sanic_tbl_smlt_tablemapping`

Define qué tabla Dataverse recibirá datos de cada fila del CSV. Un template puede tener múltiples
Table Mappings para normalizar una fuente plana en varias tablas relacionadas.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Hijo de | [Import Template](import-template.md) | N:1 | `sanic_smlt_tm_importtemplateid` |
| Padre de | [Column Mapping](column-mapping.md) | 1:N | `sanic_smlt_cm_tablemappingid` |
| Referenciado por | [Relationship Mapping](relationship-mapping.md) | N:1 | `sanic_smlt_rm_parenttablemappingid` / `sanic_smlt_rm_childtablemappingid` |

## Columnas

| Columna | Tipo | Requerido | Descripción |
|---------|------|-----------|-------------|
| `sanic_smlt_tm_tablemappingid` | GUID (PK) | Auto | Clave primaria |
| `sanic_smlt_tm_importtemplateid` | Lookup | Sí | FK → Import Template |
| `sanic_smlt_tm_name` | String(200) | Sí | Nombre descriptivo |
| `sanic_smlt_tm_targettable` | String(100) | Sí | Logical name de la tabla Dataverse destino (ej. `account`) |
| `sanic_smlt_tm_processingorder` | Integer | Sí | Orden de procesamiento DAG — padres primero (número menor = primero) |
| `sanic_smlt_tm_dedupcolumns` | String(500) | No* | Nombres lógicos de columnas destino usados como dedup key, separados por coma (se recortan espacios). Cada una debe ser el **Target Column de un Column Mapping** de este mismo Table Mapping. *Obligatorio salvo "Solo inserción": `PrepareImport` rechaza el template si falta. |
| `sanic_smlt_tm_createifnotexists` | Boolean | Sí | Si el registro **no existe** (sin match de dedup): Sí = Create, No = fila `Skipped` (default: Sí) |
| `sanic_smlt_tm_updateifexists` | Boolean | Sí | Si el registro **existe** (match de dedup): Sí = Update, No = fila `Skipped`. Default del schema: **Sí** desde 1.0.10 (antes No, en contradicción con estos docs; los mappings existentes conservan su valor) |

| `sanic_smlt_tm_insertonly` | Boolean | No | **Solo inserción (permite duplicados)**, desde 1.0.8. Sí = el mapping se escribe siempre con `Create`, sin alternate key: reprocesar el mismo CSV **puede duplicar** registros y los flags Create/Update If Exists no aplican. Es el opt-in explícito para un mapping sin Dedup Columns (sin él, `PrepareImport` rechaza el template). Default: No. Va en el form justo después de Update If Exists. |

> **`sanic_smlt_tm_isjunctiontable` fue eliminado (1.0.10)** del form y del schema por SDK
> (`SchemaCleanupSpec`): era un placeholder de N:M que ningún componente leía. Un JSON de export viejo que
> lo traiga sigue importando (se ignora). Ver [Relationship Mapping](relationship-mapping.md).

> **`sanic_smlt_tm_createifnotexists` / `sanic_smlt_tm_updateifexists` — implementados en 1.3.0.0.**
> El `UpsertWriter` honra ambos flags cuando la tabla tiene `DedupColumns` (alternate key):
>
> | ¿El registro existe? | Flag relevante | Resultado |
> |----------------------|----------------|-----------|
> | Sí | `UpdateIfExists` = Sí | Update |
> | Sí | `UpdateIfExists` = No | `Skipped` — no se escribe; cuenta en `skippedcount` |
> | No | `CreateIfNotExists` = Sí | Create |
> | No | `CreateIfNotExists` = No | `Skipped` — no se escribe; cuenta en `skippedcount` |
>
> Con **ambos flags en Sí (default)** se mantiene el `UpsertRequest` de un solo round-trip.
> **Sin `DedupColumns` los flags se ignoran** — no hay concepto de "existe" (ver el bloque siguiente).
> Las filas `Skipped` se cuentan en el job pero **no se loguean** en el Import Job Log.

> ⚠️ **`sanic_smlt_tm_dedupcolumns` vacío — comportamiento degradado.** Cuando este campo está vacío,
> el `UpsertWriter` no puede construir un `UpsertRequest` idempotente y cae al fallback `Create` (sin
> deduplicación). Consecuencias: (a) reprocesar el mismo CSV **duplica registros**; se traza un warning en
> el log. (b) una tabla sin `DedupColumns` **no puede ser referenciada como padre** en un Relationship
> Mapping: el motor no puede construir el `KeyAttributes` del lookup y registra "cannot resolve parent
> lookup" — el campo FK queda `null` en los registros hijo.

> **Nota**: el campo `sanic_smlt_tm_sourcesheet` (hoja de Excel) dejó de usarse en v2 y fue
> **eliminado del schema en 1.3.0.0**. Smelter solo procesa CSV.

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
