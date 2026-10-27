[← Volver al índice de documentación](../../README.md)

# Import Template — `sanic_tbl_smlt_importtemplate`

Entidad raíz de configuración de Smelter. Cada template define el formato del archivo fuente CSV y actúa
como padre de todos los mappings de configuración.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Padre de | [Table Mapping](table-mapping.md) | 1:N | `sanic_smlt_tm_importtemplateid` |
| Padre de | [Relationship Mapping](relationship-mapping.md) | 1:N | `sanic_smlt_rm_importtemplateid` |
| Padre de | [Import Job](import-job.md) | 1:N | `sanic_smlt_ij_importtemplateid` |

La relación con Import Job tiene **Cascade Share = All** y **Cascade Reparent = All**, de modo que los
jobs creados bajo un template compartido heredan el acceso vía POA (Principal Object Access) sin código
adicional.

## Ownership

La tabla es user/team-owned, y el **owner del template define el owner de sus Import Jobs**: si el
template es owned por un **team**, todo Import Job creado contra él nace owned por ese mismo team
(`AssignJobOwnerPlugin`, step sync pre-operation del Create del job) — así todos los miembros del team
ven y operan los jobs de cualquier otro miembro. La práctica operativa recomendada es **asignar cada
template al team de operadores que lo usa** (ver [Seguridad y
roles](../../04-admin-guide/security-and-roles.md), guía operativa de teams).

## Columnas

| Columna | Tipo | Requerido | Descripción |
|---------|------|-----------|-------------|
| `sanic_smlt_it_importtemplateid` | GUID (PK) | Auto | Clave primaria |
| `sanic_smlt_it_name` | String(200) | Sí | Nombre del template |
| `sanic_smlt_it_description` | Multiline | No | Descripción libre |
| `sanic_smlt_it_sourcetype` | OptionSet | No | Tipo de archivo fuente. Solo existe CSV; desde 1.0.10 no es requerido, nace en CSV y está oculto en el form (el motor no lo lee) |
| `sanic_smlt_it_hasheaderrow` | Boolean | Sí | ¿Primera fila es encabezado? (default: Sí) |
| `sanic_smlt_it_delimiter` | String(5) | No | Separador de columnas CSV (default: `,`). Para tabulador (TSV) escribir `\t` o `TAB` (sin distinguir mayúsculas) o pegar el tab real; cualquier otro valor se usa literal |
| `sanic_smlt_it_chunksize` | Whole Number | No | Filas por invocación de `ProcessChunk`. 0 = usar el default del sistema (1000). La columna acepta 0–10000 (validado en el form desde 1.0.11); el motor acota a 100–10000. Calibrar según el costo total de upserts por fila. |
| `sanic_smlt_it_status` | OptionSet | Sí | Estado del template. Desde 1.3.0.0 **gatea la ejecución**: solo `Active (1)` puede correr imports |

## Descripciones de columnas y export/import

Desde 1.0.11 **todas** las columnas custom de las 6 tablas tienen Description (tooltip en la MDA), provisionada por
SDK (`ColumnDescriptionsSpec`). El export/import de templates (formato **1.1**, compatible hacia atrás con 1.0)
incluye ahora `Description` y `Status`: al **crear**, el template toma el `Status` del JSON (si el JSON no lo trae,
nace `Active`); al **actualizar** uno existente, se aplica el `Status`/`Description` del JSON solo si vienen; un JSON
1.0 deja ambos como están. Un `Status` fuera de 0/1/2 se rechaza.

## OptionSets

### `sanic_smlt_it_sourcetype`

| Valor | Label |
|-------|-------|
| 0 | CSV |

> Solo CSV está soportado en v2. El valor Excel (1) existía en versiones anteriores y fue eliminado del motor.

### `sanic_smlt_it_status`

| Valor | Label |
|-------|-------|
| 0 | Draft |
| 1 | Active |
| 2 | Archived |

Desde 1.3.0.0 el status **gatea la ejecución de imports**: `PrepareImport` rechaza los templates en
`Draft` o `Archived` con el error _"Import Template ... is not Active"_ — solo un template `Active`
puede correr imports. Los templates importados vía `sanic_capi_smelter_importtemplate` nacen con el `Status` del
JSON (formato 1.1) o `Active` si el JSON no lo trae; cuando el import actualiza un template existente, el status
solo cambia si el JSON lo especifica.

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
