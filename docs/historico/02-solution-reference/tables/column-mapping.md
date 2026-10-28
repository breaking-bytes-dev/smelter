[← Volver al índice de documentación](../../README.md)

# Column Mapping — `sanic_tbl_smlt_columnmapping`

Mapea una columna individual del CSV a una columna Dataverse destino, con información de tipo, valor
default y expresiones de transform.

## Relaciones

| Dirección | Tabla relacionada | Tipo | Columna FK |
|-----------|------------------|------|------------|
| Hijo de | [Table Mapping](table-mapping.md) | N:1 | `sanic_smlt_cm_tablemappingid` |

## Columnas

| Columna | Tipo | Requerido | Descripción |
|---------|------|-----------|-------------|
| `sanic_smlt_cm_columnmappingid` | GUID (PK) | Auto | Clave primaria |
| `sanic_smlt_cm_tablemappingid` | Lookup | Sí | FK → Table Mapping |
| `sanic_smlt_cm_name` | String(200) | Sí | Nombre descriptivo |
| `sanic_smlt_cm_sourcecolumn` | String(200) | Condicional | Nombre de columna en el CSV; **no distingue mayúsculas** ni espacios alrededor (`Email` lee la columna `email`). Opcional cuando se usa transform `STATIC()`. |
| `sanic_smlt_cm_targetcolumn` | String(100) | Sí | Logical name de la columna Dataverse destino |
| `sanic_smlt_cm_datatype` | OptionSet | Sí | Tipo de dato destino |
| `sanic_smlt_cm_isrequired` | Boolean | No | Fallar la fila si el valor de origen está vacío |
| `sanic_smlt_cm_defaultvalue` | String(500) | No | Valor por defecto cuando el origen está vacío |
| `sanic_smlt_cm_lookuptable` | String(100) | Condicional | Para tipo Lookup: logical name de la tabla a la que apunta. **Obligatorio con Data Type = Lookup** (`PrepareImport` rechaza el template si falta) |
| `sanic_smlt_cm_lookupmatchcolumn` | String(100) | Condicional | Para tipo Lookup: columna de la tabla lookup contra la que hacer match |
| `sanic_smlt_cm_optionsetmapping` | Multiline | Condicional | Para tipo OptionSet: JSON `{"valor_origen": 123456}` |
| `sanic_smlt_cm_transformexpression` | String(1000) | No | Expresión DSL de transform. Tiene prioridad sobre el valor crudo del origen. Ver [Transform DSL](../../03-developer-guide/transform-dsl.md). |

## OptionSet — `sanic_smlt_cm_datatype`

| Valor | Label | Notas |
|-------|-------|-------|
| 0 | String | Default |
| 1 | Integer | Número entero |
| 2 | Decimal | Flotante / decimal |
| 3 | DateTime | Parseable vía formato o transform DATEPARSE |
| 4 | Boolean | `true`/`false`, `1`/`0`, `yes`/`no` |
| 5 | Lookup | Requiere `lookuptable` + `lookupmatchcolumn` |
| 6 | OptionSet | Requiere `optionsetmapping` JSON |
| 7 | Money | Decimal con semántica de moneda |
| 8 | Guid | Identificador único |

## Volver a

- [Data Model](../data-model.md)
- [Architecture](../../01-overview/architecture.md)
