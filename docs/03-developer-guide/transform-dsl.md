[← Volver al índice de documentación](../README.md)

# Smelter — Expresiones de Transformación (DSL)

Las expresiones de transformación son un mini DSL almacenado en la columna
`sanic_smlt_cm_transformexpression`. Permiten manipular los valores de origen antes de escribirlos
en Dataverse.

## Sintaxis

Las expresiones usan una sintaxis de llamada a función: `FUNCION(arg1, arg2, ...)`. Los argumentos
pueden ser:
- **Referencias a columnas**: nombres sin comillas que referencian columnas de origen (ej. `FirstName`)
- **Literales de texto**: entre comillas dobles (ej. `" "`)
- **Literales numéricos**: números planos (ej. `3`, `0`)
- **Expresiones anidadas**: funciones como argumentos de otras funciones (ej. `UPPER(TRIM(Name))`)

## Funciones disponibles

| Función | Firma | Descripción | Ejemplo |
|---------|-------|-------------|---------|
| `CONCAT` | `CONCAT(val1, val2, ...)` | Concatena los valores | `CONCAT(FirstName, " ", LastName)` |
| `UPPER` | `UPPER(val)` | Convierte a mayúsculas | `UPPER(Country)` |
| `LOWER` | `LOWER(val)` | Convierte a minúsculas | `LOWER(Email)` |
| `TRIM` | `TRIM(val)` | Elimina espacios al inicio y al final | `TRIM(Name)` |
| `LEFT` | `LEFT(val, n)` | Toma los primeros N caracteres | `LEFT(Code, 3)` |
| `RIGHT` | `RIGHT(val, n)` | Toma los últimos N caracteres | `RIGHT(Phone, 10)` |
| `DATEPARSE` | `DATEPARSE(val, formato)` | Parsea una fecha con formato personalizado → ISO 8601 | `DATEPARSE(Fecha, "dd/MM/yyyy")` |
| `REPLACE` | `REPLACE(val, buscar, reemplazo)` | Reemplaza una subcadena | `REPLACE(Phone, "-", "")` |
| `SPLIT` | `SPLIT(val, delimitador, indice)` | Divide y toma la parte N (índice base 0) | `SPLIT(FullName, " ", 0)` |
| `IF` | `IF(val, igual_a, entonces, sino)` | Valor condicional (compara igualdad `val == igual_a`) | `IF(Status, "Active", 1, 0)` |
| `MAP` | `MAP(clave, jsonMap)` | Traduce un valor mediante un mapa JSON | `MAP(Region, "{""N"":1,""S"":2}")` |
| `COALESCE` | `COALESCE(val1, val2, ...)` | Primer valor no nulo / no vacío | `COALESCE(Mobile, Phone)` |
| `STATIC` | `STATIC(valor)` | Valor fijo (ignora la columna de origen) | `STATIC("Imported")` |

## Composición

Las expresiones pueden anidarse para construir transformaciones complejas:

```
UPPER(TRIM(CONCAT(FirstName, " ", LastName)))
```

Esto: concatena nombre + apellido → elimina espacios → convierte a mayúsculas.

## Notas

- Cuando se configura una `transformexpression`, tiene prioridad sobre el valor crudo de `sourcecolumn`
- `STATIC()` no requiere que se configure `sourcecolumn`
- Las referencias a columnas distinguen mayúsculas y minúsculas; deben coincidir exactamente con los
  encabezados del archivo de origen
- `MAP` usa formato JSON — dentro de los campos de texto de Dataverse, escapá las comillas dobles
  como `""`
- El formato de `DATEPARSE` sigue las cadenas de formato de `DateTime.ParseExact` de .NET
- Las funciones son insensibles a mayúsculas/minúsculas: `concat`, `CONCAT` y `Concat` son equivalentes

## Volver a

- [Data Model](data-model.md) | [Architecture](architecture.md)
