[← Volver al índice de documentación](../README.md)

# Guía paso a paso — Configurar un Import Template

> Esta guía cubre la configuración completa de un Import Template en Smelter: desde el registro raíz hasta
> los Relationship Mappings que resuelven los lookups padre-hijo por valor de negocio.
>
> **Requisito previo:** las tablas destino ya deben existir en Dataverse **y sus alternate keys deben estar
> `Active`** antes de ejecutar cualquier import. Ver [Alternate Keys](./alternate-keys.md).

---

## Estructura de configuración

Un template se compone de cuatro niveles anidados:

```
Import Template
└── Table Mapping (1 por tabla destino, o 1 por "variante" de tabla)
    └── Column Mapping (1 por columna a mapear)
Import Template
└── Relationship Mapping (1 por relación padre-hijo entre Table Mappings)
```

Los Relationship Mappings viven directamente bajo el template (no bajo un Table Mapping) porque expresan
relaciones entre Table Mappings, no dentro de uno.

---

## PASO 1 — Crear el Import Template

En la MDA de Smelter, navegá a **Import Templates** → **Nuevo**.

| Campo | Descripción | Ejemplo |
|-------|-------------|---------|
| **Name** | Nombre identificador del template | `Cobranza Diaria` |
| **Description** | Descripción opcional | `CSV denormalizado de cobranza` |
| **Source Type** | Tipo de archivo fuente | `CSV` (valor 0) |
| **Has Header Row** | Si la primera fila es encabezado | `Sí` (recomendado) |
| **Delimiter** | Separador de columnas del CSV | `,` (coma; usar `;` si corresponde) |
| **Chunk Size** | Filas procesadas por lote | `1000` (ajustar según volumen) |
| **Status** | Estado del template | `Active` para que pueda usarse |

> **Chunk Size:** determina cuántas filas del CSV procesa el motor en cada invocación del plugin async.
> A mayor chunk size, menos invocaciones pero mayor riesgo de timeout en el plugin (límite de 2 minutos
> de ejecución Dataverse). Para CSVs grandes, `500–1000` es un rango seguro.

Guardá el template antes de continuar.

---

## PASO 2 — Crear los Table Mappings

Desde el formulario del template, navegá a la subgrilla **Table Mappings** → **Nuevo**.
Creá **un Table Mapping por cada tabla destino** (o por cada "variante" de una misma tabla — ver la sección
de columnas paralelas más adelante).

| Campo | Descripción | Notas |
|-------|-------------|-------|
| **Name** | Nombre descriptivo | `Cliente`, `Direccion - Casa` |
| **Import Template** | Lookup al template padre | Se completa automáticamente |
| **Target Table** | Nombre lógico de la tabla Dataverse | `sanic_cliente`, `contact`, `account` |
| **Processing Order** | Orden de procesamiento (número entero) | Padres: 1 · Hijos: 2 · Nietos: 3 |
| **Dedup Columns** | Columnas de la alternate key, separadas por coma | `sanic_cif` o `sanic_cif,sanic_tipo` |
| **Create If Not Exists** | Crear registro si no existe match | `Sí` en casi todos los casos |
| **Update If Exists** | Actualizar si ya existe un match | `Sí` para tablas de estado actual; `No` para append-only |
| **Is Junction Table** | Marcar si es tabla de intersección N:M | Solo para relaciones muchos-a-muchos |

### Sobre Dedup Columns

Este campo es el **puente entre Smelter y la alternate key de Dataverse**. El valor debe ser una lista
separada por coma de los **nombres lógicos de las columnas** que componen la alternate key en la tabla
destino. Si la alternate key es compuesta (más de una columna), listá todas en orden.

Ejemplo para una clave compuesta:

```
sanic_cif,sanic_tipo
```

Sin Dedup Columns, el motor no puede emitir un `UpsertRequest` idempotente y degrada a
`Create` sin deduplicación, con riesgo de registros duplicados.

### Sobre Processing Order

El número en este campo es un **hint de DAG**: los Table Mappings con orden menor se procesan antes.
Las tablas padre (sin dependencias entrantes) van en orden `1`; sus hijos directos en `2`; nietos en `3`,
etc. El motor usa el algoritmo de Kahn para la ordenación topológica real; los números no necesitan ser
consecutivos, solo coherentes.

---

## PASO 3 — Crear los Column Mappings

Desde el formulario del Table Mapping, navegá a la subgrilla **Column Mappings** → **Nuevo**.
Creá **un Column Mapping por cada columna de la tabla destino que deba poblarse** desde el CSV.

| Campo | Descripción | Obligatorio |
|-------|-------------|:-----------:|
| **Name** | Nombre descriptivo | Sí |
| **Table Mapping** | Lookup al Table Mapping padre | Sí (auto) |
| **Source Column** | Nombre de la columna en el CSV | Condicional* |
| **Target Column** | Nombre lógico de la columna en Dataverse | Sí |
| **Data Type** | Tipo de dato del campo destino | Sí |
| **Is Required** | Si la fila falla cuando el valor está vacío | No |
| **Default Value** | Valor a usar cuando Source Column está vacío | No |
| **Lookup Table** | Para tipo Lookup: tabla lógica del registro referenciado | Condicional |
| **Lookup Match Column** | Para tipo Lookup: columna de la alternate key del registro referenciado | Condicional |
| **OptionSet Mapping** | Para tipo OptionSet: JSON de traducción `{"valor_csv": 123456}` | Condicional |
| **Transform Expression** | Expresión DSL aplicada antes del mapeo | No |

*`Source Column` es opcional cuando se usa `Transform Expression` con `STATIC()` o cuando el valor viene
enteramente de un `Default Value`.

### Tipos de dato disponibles

| Data Type | Cuándo usarlo |
|-----------|---------------|
| `String` | Texto libre, códigos, identificadores de negocio |
| `Integer` | Números enteros (días mora, nivel, cantidad) |
| `Decimal` | Montos, porcentajes, decimales |
| `DateTime` | Fechas y fechas-hora; si el formato no es ISO 8601, usar `DATEPARSE` |
| `Boolean` | Booleanos: `true`/`false`, `1`/`0`, `yes`/`no` |
| `Lookup` | Referencia a otro registro por GUID o por valor de alternate key |
| `OptionSet` | Choice de Dataverse; requiere `OptionSet Mapping` |
| `Money` | Campos currency de Dataverse |
| `Guid` | Identificadores únicos que vienen directamente en el CSV |

### Configurar un Lookup por valor de negocio

Cuando el CSV trae un código o nombre (no un GUID) que referencia un registro en otra tabla, configurá:

- **Data Type:** `Lookup`
- **Lookup Table:** nombre lógico de la tabla referenciada (ej. `contact`, `sanic_cliente`)
- **Lookup Match Column:** nombre lógico de la alternate key de esa tabla (ej. `emailaddress1`, `sanic_cif`)

El motor construirá una `EntityReference` con `KeyAttributes` en lugar de un GUID, y Dataverse resolverá el
registro usando la alternate key. Si esa alternate key no existe o no está `Active`, el import fallará con
error `TargetData` en esas filas.

### Configurar un OptionSet Mapping

El campo `OptionSet Mapping` acepta JSON con el formato `{"valor_del_csv": valor_entero_del_optionset}`.

Ejemplo: el CSV trae `"Activo"` o `"Inactivo"` y el OptionSet de Dataverse tiene los valores `1` y `2`:

```json
{"Activo": 1, "Inactivo": 2}
```

### Usar Default Value como campo estático

Cuando una columna no viene del CSV sino que siempre tiene el mismo valor (por ejemplo, el campo `sanic_tipo`
en el patrón de columnas paralelas), dejá `Source Column` vacío y completá solo `Default Value`.

El motor aplica el default **antes** de la validación de `Is Required`, por lo que es seguro marcar un campo
como requerido con default.

---

## PASO 4 — Crear los Relationship Mappings

Desde el formulario del template, navegá a la subgrilla **Relationship Mappings** → **Nuevo**.
Creá **un Relationship Mapping por cada relación padre-hijo** entre Table Mappings que necesite resolverse.

| Campo | Descripción | Obligatorio |
|-------|-------------|:-----------:|
| **Name** | Nombre descriptivo | Sí |
| **Import Template** | Lookup al template | Sí (auto) |
| **Parent Table Mapping** | Lookup al Table Mapping del padre | Sí |
| **Child Table Mapping** | Lookup al Table Mapping del hijo | Sí |
| **Relationship Type** | `OneToMany` (1:N) o `ManyToMany` (N:M) | Sí |
| **Lookup Column On Child** | Nombre lógico del campo lookup en la tabla hija | Para 1:N |
| **Intersect Table** | Nombre lógico de la tabla de intersección | Para N:M |
| **Parent Dedup Columns** | Columnas del CSV que identifican al padre | Sí |

### Cómo funciona Parent Dedup Columns

Este campo es el más importante y el que más confusión genera. Responde a la pregunta:
**"¿qué columnas del CSV contienen los valores con los que identifico al padre?"**

No son los nombres lógicos de Dataverse — son los nombres de las columnas en el archivo CSV fuente.

En tiempo de procesamiento, el motor:
1. Lee los valores de esas columnas del CSV (por fila).
2. Los combina con los nombres de las columnas de la alternate key del padre (de `Dedup Columns` en el Table
   Mapping padre).
3. Construye un `EntityReference` con `KeyAttributes` apuntando al padre.

Ejemplo concreto:

- El CSV tiene una columna `CIF`.
- El Table Mapping `Cliente` tiene `Dedup Columns = sanic_cif`.
- El Relationship Mapping `Cliente → Dirección` tiene `Parent Dedup Columns = CIF`.

El motor toma el valor de `CIF` en la fila actual, lo asocia con `sanic_cif` (el nombre lógico de la alternate
key del padre), y produce `new EntityReference("sanic_cliente", "sanic_cif", valorDeCIF)`.

Si la clave del padre es compuesta, listá las columnas del CSV en el mismo orden que las columnas en
`Dedup Columns` del padre, separadas por coma.

---

## Patrón: columnas paralelas (explotar múltiples valores por fila)

Un CSV denormalizado suele traer varios valores del mismo tipo en una sola fila. Por ejemplo, tres
direcciones (`DireccionCasa`, `DireccionEC`, `DireccionTrabajo`) o dos teléfonos (`NumeroCelular`,
`NumeroCasa`) que deben normalizarse a registros individuales en la misma tabla destino.

**La solución en Smelter es crear un Table Mapping por cada variante**, todos apuntando a la misma tabla
destino, diferenciados por el `Default Value` de un campo discriminador (por ejemplo, `sanic_tipo`).

Smelter produce **un registro por Table Mapping por fila del CSV**. Al procesar una fila con 10 Table
Mappings, genera hasta 10 registros Dataverse desde esa fila.

### Ejemplo: tres tipos de dirección

| Table Mapping | Target Table | Dedup Columns | Default Value de `sanic_tipo` | Source Column de `sanic_direccion` |
|---------------|-------------|---------------|-------------------------------|-------------------------------------|
| `Direccion - Casa` | `sanic_direccion` | `sanic_cif,sanic_tipo` | `Casa` | `DireccionCasa` |
| `Direccion - EC` | `sanic_direccion` | `sanic_cif,sanic_tipo` | `EC` | `DireccionEC` |
| `Direccion - Trabajo` | `sanic_direccion` | `sanic_cif,sanic_tipo` | `Trabajo` | `DireccionTrabajo` |

La alternate key compuesta `sanic_cif + sanic_tipo` garantiza que el upsert identifique unívocamente cada
registro (CIF `12345`, tipo `Casa` es distinto de CIF `12345`, tipo `EC`).

Los tres Table Mappings comparten el mismo Relationship Mapping padre-hijo (Cliente → Dirección), con
`Lookup Column On Child = sanic_clienteid` y `Parent Dedup Columns = CIF`.

---

## Patrón: orden DAG (padres primero)

El motor usa ordenación topológica (algoritmo de Kahn) para garantizar que los registros padre existan
en Dataverse antes de que los hijos intenten referenciarlos. Las reglas son simples:

1. Asigná `Processing Order = 1` a todas las tablas sin dependencias (catálogos, entidades raíz).
2. Asigná `Processing Order = 2` a las tablas que referencian tablas de orden 1.
3. Continuá incrementando para cada nivel adicional de dependencia.

No es necesario que los números sean consecutivos — lo que importa es la relación de orden. Si dos tablas
tienen el mismo número pero no dependen una de la otra, no hay problema.

---

## Consejo: configurar y verificar por capas

La práctica recomendada para templates complejos (muchas tablas) es la siguiente:

1. **Primera capa:** configurá solo el primer Table Mapping (una tabla padre) con sus Column Mappings.
   Ejecutá un Import Job de prueba con 10-20 filas. Verificá que los registros se creen correctamente.
2. **Sumar una capa:** agregá los Table Mappings de la segunda capa (hijos directos) con sus Column Mappings
   y Relationship Mappings. Volvé a probar.
3. **Repetir** por cada nivel del DAG.

Este enfoque permite aislar errores de configuración en la capa donde aparecen, en lugar de depurar
un template completo con 10 tablas fallando a la vez.

> Para exportar un template configurado y trasladarlo a otro entorno, ver
> [Export/Import de Templates](./export-import-templates.md).

---

---

## ⚠️ Campos sin efecto en v2 / no soportados todavía

Los siguientes campos están visibles en la UI porque existen en el modelo de datos, pero el motor
de procesamiento v2 **no los lee**. Configurarlos no produce el comportamiento esperado.

> **No son defectos, son "no soportado todavía".** Configurarlos no rompe el import — simplemente no
> tiene efecto. Quedan documentados como candidatos de roadmap; mientras tanto, dejalos en su valor por
> defecto y seguí las columnas "Qué hacer" de abajo.

| Campo | Estado | Qué hacer |
|-------|--------|-----------|
| **IsRetry** (`sanic_smlt_ij_retry` en Import Job) | No implementado. `ImportContextLoader` no lo carga; ni `ProcessChunk` ni `NormalizationEngine` lo leen. No existe "reprocesar solo filas fallidas". | Para reintentar: usar la retry policy automática del Cloud Flow (errores transitorios) o volver el job a `Queued` para re-correr desde cero (el upsert es idempotente). |
| **Is Junction Table** (`sanic_smlt_tm_isjunctiontable` en Table Mapping) | No implementado. El campo se persiste pero ningún código de procesamiento lo lee. Es un placeholder para N:M, que no está soportado. | Modelar la tabla join como un `TableMapping` adicional con `DedupColumns` y lookups explícitos hacia ambas tablas padre. |
| **Create If Not Exists** (`sanic_smlt_tm_createifnotexists` en Table Mapping) | No usado en v2. El `UpsertWriter` siempre hace upsert por alternate key independientemente de este flag. Herencia del diseño v1. | Ignorar. El comportamiento real es siempre upsert idempotente cuando `DedupColumns` está configurado. |
| **Update If Exists** (`sanic_smlt_tm_updateifexists` en Table Mapping) | No usado en v2. Igual que `CreateIfNotExists`: no se lee en ninguna ruta de código. | Ignorar. El upsert siempre actualiza si el registro existe (cuando hay alternate key configurada). |
| **Relationship Type = ManyToMany** / **Intersect Table** | No implementado. `NormalizationEngine` filtra con `.Where(r => r.IsOneToMany)`: cualquier mapping `ManyToMany` se ignora en silencio. | Solo usar `OneToMany`. Para N:M, ver el ítem Is Junction Table arriba. |

### Comportamientos implícitos que conviene conocer

**Sin Relationship Mappings configurados:** el `DependencyResolver` devuelve las tablas en el orden
original (sin DAG). Los lookups padre-hijo no se resuelven y los FKs quedan `null`. Correcto para
un template de una sola tabla o tablas sin relaciones entre sí; incorrecto si las tablas se
referencian mutuamente.

**Dedup Columns vacío:** el `UpsertWriter` cae al fallback `Create` (no idempotente). Consecuencias:
(a) reprocesar el mismo CSV duplica registros — se traza un warning; (b) esa tabla no puede
ser referenciada como padre en un Relationship Mapping porque el motor no puede construir el
`KeyAttributes` del lookup ("cannot resolve parent lookup" → FK `null` en los hijos).

---

## Referencias

- [Alternate Keys — el contrato de las claves de negocio](./alternate-keys.md)
- [Configurar un import de punta a punta](./new-import-end-to-end.md)
- [Ejemplo completo: Import de Cobranza](../06-examples/cobranza.md)
- [Transform Expressions (DSL)](../03-developer-guide/transform-dsl.md)
- [Referencia de tablas: Table Mapping](../02-solution-reference/tables/table-mapping.md)
- [Referencia de tablas: Column Mapping](../02-solution-reference/tables/column-mapping.md)
- [Referencia de tablas: Relationship Mapping](../02-solution-reference/tables/relationship-mapping.md)
