[← Volver al índice de documentación](../../README.md)

# Relationship Mapping — `sanic_tbl_smlt_relationshipmapping`

Define cómo se relacionan las tablas destino entre sí. Usado por el `DependencyResolver` para
construir el DAG de procesamiento (orden padre→hijo) y por la resolución de lookups durante el upsert.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Hijo de | [Import Template](import-template.md) | N:1 | `sanic_smlt_rm_importtemplateid` |
| Referencia | [Table Mapping](table-mapping.md) (padre) | N:1 | `sanic_smlt_rm_parenttablemappingid` |
| Referencia | [Table Mapping](table-mapping.md) (hijo) | N:1 | `sanic_smlt_rm_childtablemappingid` |

## Columnas

| Columna | Tipo | Requerido | Descripción |
|---------|------|-----------|-------------|
| `sanic_smlt_rm_relationshipmappingid` | GUID (PK) | Auto | Clave primaria |
| `sanic_smlt_rm_importtemplateid` | Lookup | Sí | FK → Import Template |
| `sanic_smlt_rm_name` | String(200) | Sí | Nombre descriptivo |
| `sanic_smlt_rm_parenttablemappingid` | Lookup | Sí | FK → Table Mapping (tabla padre en el DAG) |
| `sanic_smlt_rm_childtablemappingid` | Lookup | Sí | FK → Table Mapping (tabla hija en el DAG) |
| `sanic_smlt_rm_relationshiptype` | OptionSet | Sí | Tipo de relación |
| `sanic_smlt_rm_lookupcolumnonchild` | String(100) | Sí | Logical name de la columna lookup en la tabla hija. `PrepareImport` rechaza el template si falta |
| `sanic_smlt_rm_parentdedupcolumns` | String(500) | Sí | Columnas del CSV (separadas por coma) que identifican al registro padre, emparejadas **por posición** con las `Dedup Columns` del padre. Deben ser tantas como dedup columns tiene el padre (`PrepareImport` lo valida). El valor se calcula con el **mismo transform / default / tipo** que el Table Mapping padre aplica a esa columna destino (p. ej. un padre keyed por `HMAC(Pan, "k")` resuelve para sus hijos). |

## Form (desde 1.0.11)

El form muestra **Import Template** (debajo del nombre) y los lookups de Parent/Child Table Mapping se **filtran por ese
template** (related-records filtering: solo se ofrecen mappings del mismo template). Elegí primero el template.
Column Mapping no lleva este filtro: pertenece a un único Table Mapping.

## OptionSet — `sanic_smlt_rm_relationshiptype`

| Valor | Label | Descripción |
|-------|-------|-------------|
| 0 | OneToMany | El hijo tiene un lookup al padre |

> **`ManyToMany` ya no existe (1.0.10).** N:M nunca se implementó: el motor ignoraba el mapping en silencio.
> La opción se eliminó del choice por SDK (el deploy se niega si alguna fila la usa), `PrepareImport` y el
> import de templates rechazan un valor distinto de `OneToMany` con un error claro, y
> `sanic_smlt_rm_intersecttable` ya se había eliminado en 1.3.0.0.
> Para una tabla intersect, mapear la tabla join como un `TableMapping` adicional con sus propias
> `DedupColumns` y lookups hacia ambas tablas padre.

## Sin relationship mappings — comportamiento del motor

Cuando un template no tiene ningún Relationship Mapping configurado, el `DependencyResolver` no construye
un DAG y devuelve las tablas en el **orden original** (sin ordenación topológica). Los lookups padre-hijo
no se resuelven y los campos FK permanecen `null`. Cada tabla se procesa de forma independiente.

Este comportamiento es correcto y esperado para dos casos de uso:
- **Una sola tabla**: no hay relaciones que resolver.
- **Tablas no relacionadas entre sí**: catálogos independientes que no se referencian mutuamente.

Si el template sí tiene relaciones entre tablas pero no se configuran los Relationship Mappings, los
campos lookup de los registros hijo quedarán vacíos sin error visible — el import completa sin FKs.

## Escenarios de uso

Todos los relationship mappings son OneToMany (1:N). Ejemplos representativos:

| Escenario | Padre | Hijo | Lookup en hijo |
|-----------|-------|------|----------------|
| Geografía multinivel | `Country` → `State` | `State` apunta a `Country` por código | `sanic_countryid` |
| Geografía multinivel | `State` → `City` | `City` apunta a `State` por código | `sanic_stateid` |
| Cliente y cuenta | `Cliente` → `Cuenta` | `Cuenta` apunta al cliente por CIF | `sanic_clienteid` |
| Catálogos → Cuenta | `Producto` / `Agente` / `Fiador` → `Cuenta` | `Cuenta` referencia varios catálogos | lookup correspondiente |
| Cuenta y snapshot diario | `Cuenta` → `CargaDiaria` | `CargaDiaria` apunta a la cuenta | `sanic_cuentaid` |

En todos los casos, `Parent Dedup Columns` contiene los nombres de las **columnas del CSV** que
identifican al registro padre, y `Dedup Columns` del Table Mapping padre contiene los nombres
**lógicos de Dataverse** que componen la alternate key.

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
