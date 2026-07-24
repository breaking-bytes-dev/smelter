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
| `sanic_smlt_tm_dedupcolumns` | String(500) | No | Nombres lógicos de columnas destino usados como dedup key, separados por coma. Vacío = siempre crear. |
| `sanic_smlt_tm_isjunctiontable` | Boolean | No | `true` para tablas intersect (N:M) |
| `sanic_smlt_tm_createifnotexists` | Boolean | Sí | Crear registro si no hay match de dedup (default: Sí) |
| `sanic_smlt_tm_updateifexists` | Boolean | Sí | Actualizar registro si hay match de dedup (default: No) |

> ⚠️ **`sanic_smlt_tm_isjunctiontable` — no implementado en v2.** El campo se persiste y se exporta/importa
> con el template, pero ningún componente del motor de procesamiento lo lee. No tiene efecto en el
> comportamiento del import. Es un placeholder para el soporte de relaciones N:M, que no está implementado.
> Ver [Relationship Mapping](relationship-mapping.md) para más detalles sobre la limitación ManyToMany.

> ⚠️ **`sanic_smlt_tm_createifnotexists` / `sanic_smlt_tm_updateifexists` — no usados en v2.** El
> `UpsertWriter` siempre ejecuta `UpsertRequest` por alternate key (o degrada a Create/Update si no hay
> `DedupColumns`), independientemente del valor de estos flags. No se leen en ninguna ruta de código.
> Son herencia del diseño v1 y no tienen efecto en ninguna combinación de valores.

> ⚠️ **`sanic_smlt_tm_dedupcolumns` vacío — comportamiento degradado.** Cuando este campo está vacío,
> el `UpsertWriter` no puede construir un `UpsertRequest` idempotente y cae al fallback `Create` (sin
> deduplicación). Consecuencias: (a) reprocesar el mismo CSV **duplica registros**; se traza un warning en
> el log. (b) una tabla sin `DedupColumns` **no puede ser referenciada como padre** en un Relationship
> Mapping: el motor no puede construir el `KeyAttributes` del lookup y registra "cannot resolve parent
> lookup" — el campo FK queda `null` en los registros hijo.

> **Nota**: el campo `sanic_smlt_tm_sourcesheet` (hoja de Excel) fue eliminado en v2. Smelter
> solo procesa CSV. No existe ni se usa.

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
