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
| `HMAC` | `HMAC(val, "nombreKey")` | HMAC-SHA256 en hex minúsculas (64 chars) con la key registrada bajo ese nombre — ver [HMAC](#hmac) | `HMAC(CardNumber, "cm-pan")` |

## HMAC

```
HMAC(valor, "nombreKey")
```

Calcula **HMAC-SHA256** del `valor` con la key registrada bajo `nombreKey` y devuelve **64 caracteres
hexadecimales en minúsculas**. Pensada para derivar un identificador buscable de un dato sensible (ej.
el PAN de una tarjeta) sin guardar el dato en claro: el mismo valor y la misma key dan siempre el mismo
hash, así que sirve como columna de lookup/alternate key.

- **Trim**: el valor se hashea **sin espacios** al inicio y al final (`" 4111111111111111 "` y
  `"4111111111111111"` dan el mismo hash). El resto del valor se hashea tal cual, en UTF-8.
- **Vacío → vacío**: si el valor (ya sin espacios) es vacío, el resultado es `""` y ni siquiera se
  resuelve la key.
- **Exactamente 2 argumentos**: el valor (columna, literal o expresión anidada) y el nombre de la key
  como literal de texto. Cualquier otra cantidad falla con `HMAC expects (value, "keyName").`
- **El nombre de la key es siempre un literal con forma de etiqueta**: empieza con letra y sigue con letras,
  dígitos, `-` o `_` (hasta 50). Una columna o una expresión en ese lugar (`HMAC(Pan, Pan)`) falla **antes** de
  evaluar, sin repetir el valor: si no, el dato sensible terminaría en el log del job y en la consulta de la key.
- **Composición**: `HMAC(TRIM(Pan), "cm-pan")`, `UPPER(HMAC(Pan, "cm-pan"))`, etc.

### Fallas

La fila falla con categoría `TransformError` (el resto del chunk sigue) cuando:

| Causa | Mensaje |
|-------|---------|
| No hay key provider configurado en el motor | `HMAC: no key provider configured.` |
| La key no existe, o tiene menos de 32 bytes | `HMAC: key 'nombreKey' missing or shorter than 32 bytes.` |
| El valor guardado de la key no es hex válido  | `HMAC: key 'nombreKey' is not valid hex.` |
| Hay más de una key activa con ese nombre  | `HMAC: more than one active key named 'nombreKey'.` |
| Cantidad de argumentos distinta de 2 | `HMAC expects (value, "keyName").` |
| El nombre de la key no es un literal con forma de etiqueta | `HMAC: the key name must be a string literal label (a letter, then letters, digits, '-' or '_').` |

Los mensajes **nunca incluyen el valor de entrada** (puede ser un PAN) **ni la key**, y el plugin no los
traza: en el Import Job Log y en el Plugin Trace Log solo aparece el *nombre* de la key.

### De dónde sale la key

> ⚠️ **El segundo argumento es una etiqueta, NO la key.** Nunca escribas el material de la key en la
> expresión (ni en el template, ni en el CSV): la expresión se guarda en Dataverse, se exporta en el JSON
> del template y se ve en el Import Job. El motor le pide los bytes al `IHmacKeyProvider` por ese nombre.

| Provider | Cómo se carga la key |
|----------|----------------------|
| `DataverseHmacKeyProvider` — lee la tabla `sanic_tbl_smlt_hmackey` (nombre = `sanic_smlt_hk_name`, key en hex = `sanic_smlt_hk_value`) con el usuario SYSTEM del plugin | **A mano, en cada entorno**: un System Administrator crea la fila (64 hex = 32 bytes). Nunca por CI, repo ni GitHub secret. Procedimiento completo en [Clave HMAC](../04-admin-guide/security-and-roles.md#clave-hmac) |

La tabla `sanic_tbl_smlt_hmackey` **es parte de la solución** `smlt_core_smelter`: su *schema* viaja a
Test/Prod con el managed. El *valor* de la key no viaja nunca. La tabla no está en ningún rol de Smelter,
no audita la columna del valor y no es válida para Advanced Find.

**Riesgo aceptado**: cualquier System Administrator del entorno puede leer la key, y las copias/backups del
entorno la arrastran (ver la tabla de riesgos en la guía de seguridad). **Endurecimiento futuro posible**:
un `IHmacKeyProvider` basado en managed identity + Azure Key Vault; como la key sería la misma, los hashes
serían idénticos y la migración no rompe nada. Hoy no es una opción directa porque un plugin de Custom API
no puede recibir *secure configuration* y los *environment variables* secret respaldados por Key Vault no
llegan a los plugins.

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
