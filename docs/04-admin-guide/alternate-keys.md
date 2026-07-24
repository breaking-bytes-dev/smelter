[← Volver al índice de documentación](../README.md)

# Alternate Keys — el contrato de las claves de negocio

> Las alternate keys son el mecanismo central que hace funcionar a Smelter. Esta guía explica por qué
> son indispensables, cómo crearlas y qué pasa cuando no están.

---

## El problema: el CSV habla en valores de negocio, Dataverse habla en GUIDs

Cuando Dataverse recibe un `UpsertRequest` o un `EntityReference`, por defecto espera identificar los
registros por su clave primaria: un GUID auto-generado que el sistema origen (el CSV) nunca conoce.

Un archivo de cobranza trae valores como `CIF = "20304050"` o `CodigoProducto = "PREST-01"`. No trae
GUIDs. El trabajo de Smelter es convertir esos valores de negocio en operaciones idempotentes de
upsert en Dataverse. Para eso existe la alternate key.

---

## Qué es una alternate key en Dataverse

Una alternate key es un **índice único** definido sobre una o más columnas de una tabla de Dataverse.
Una vez que la alternate key está activa, Dataverse la acepta como identificador alternativo al GUID
primario. Esto habilita:

- **Upsert por valor de negocio:** `UpsertRequest` con `Entity.KeyAttributes` en lugar de `Entity.Id`.
- **Lookup por valor de negocio:** `EntityReference` con `KeyAttributes` en lugar de `Id`.
- Sin alternate key, ambos mecanismos fallan.

---

## Por qué Smelter la requiere

El motor de Smelter emite un `UpsertRequest` por cada registro a crear o actualizar. El upsert utiliza
las columnas listadas en `Dedup Columns` del Table Mapping para poblar `Entity.KeyAttributes`. Dataverse
entonces busca un registro que coincida con esos valores y decide si crear o actualizar.

Esto tiene tres consecuencias directas:

1. **Idempotencia:** reprocesar el mismo CSV no duplica registros. Cada fila siempre upserta al mismo
   registro identificado por su valor de negocio.
2. **Resolución de lookups padre-hijo:** cuando el motor construye la referencia al padre (por ejemplo,
   el lookup `sanic_clienteid` en `sanic_direccion`), usa `new EntityReference(tabla, keyColumn, valor)`.
   Dataverse resuelve esa referencia en tiempo real usando la alternate key del padre.
3. **Reanudación segura:** si un import falla a mitad de proceso y se reintenta, las filas ya procesadas
   se reupsertan sin efecto neto.

Sin alternate key, el `UpsertRequest` falla con el error:

```
The key attributes {nombre_columna} are not defined as keys for entity {nombre_tabla}.
```

---

## Lección real: el error que detiene el import

Durante la configuración del import de cobranza, se intentó importar a la tabla `contact` usando la
columna `emailaddress1` como clave de deduplicación. El import falló con el error:

```
The key attributes 'emailaddress1' are not defined as keys for entity 'contact'.
```

La causa: `emailaddress1` no tenía una alternate key definida en esa tabla. Crear la alternate key sobre
`emailaddress1` en la tabla `contact` resolvió el problema completamente.

Este error aparece en el `Error Message` del Import Job Log con categoría `TargetData`. Es el síntoma
inequívoco de una alternate key faltante.

---

## Cómo crear una alternate key

### En make.powerapps.com

1. Navegá a [make.powerapps.com](https://make.powerapps.com) → **Solutions** → abrí tu solución.
2. Ubicá la tabla destino (por ejemplo `sanic_cliente`) → hacé clic para abrirla.
3. Ir a la sección **Keys** (Claves) en el panel izquierdo.
4. Clic en **+ New key** (Nueva clave).
5. Completá:
   - **Display Name:** nombre descriptivo (ej. `CIF único`)
   - **Name:** se genera automáticamente con el prefijo de la solución
   - **Columns:** seleccioná la o las columnas que componen la clave (ej. `sanic_cif`)
6. Guardá.
7. **Publicá las customizaciones** (botón Publish all customizations).

### Verificar que la clave quedó Active

Luego de publicar, Dataverse crea el índice de forma **asíncrona**. El estado inicial es `Pending`; hasta
que pase a `Active` la clave no puede usarse.

Para verificar el estado:

1. En la tabla, sección **Keys**, mirá la columna **Status** de la clave que creaste.
2. Esperá hasta que muestre `Active`. En entornos de desarrollo suele tardar menos de un minuto; en
   entornos con muchos datos puede tardar varios minutos.
3. Si queda en `Failed`, revisá la causa: columnas con valores duplicados o vacíos son la razón más
   frecuente (ver sección siguiente).

> No ejecutes un Import Job hasta que **todas** las alternate keys del template estén en estado `Active`.
> Un estado `Pending` o `Failed` produce exactamente el mismo error que una clave inexistente.

---

## Requisitos para que una alternate key se active

Dataverse exige **unicidad estricta** de los valores en las columnas elegidas. Antes de crear la clave,
asegurate de que:

1. **No hay duplicados:** si la columna `sanic_cif` ya tiene dos registros con el mismo CIF, la activación
   falla.
2. **No hay valores nulos/vacíos:** una alternate key no puede incluir filas con valor nulo en las columnas
   clave. Si hay registros existentes con `sanic_cif = null`, la activación falla.
3. **Los datos históricos son coherentes:** si la tabla ya tiene datos de carga anterior sin la clave,
   limpiá duplicados antes de activarla.

---

## Alternate keys compuestas

Una alternate key puede abarcar más de una columna. Smelter las soporta de forma nativa: el campo
`Dedup Columns` del Table Mapping acepta una lista separada por coma.

Ejemplo del import de cobranza:

- La tabla `sanic_direccion` necesita identificar cada dirección por el par (CIF del cliente, tipo de
  dirección).
- La alternate key se crea sobre las columnas `sanic_cif` y `sanic_tipo`.
- El `Dedup Columns` del Table Mapping es: `sanic_cif,sanic_tipo`.

Un detalle importante del diseño: la alternate key compuesta **no debe incluir la columna lookup al padre**
(`sanic_clienteid`). En su lugar, se copia el valor de negocio del padre (`sanic_cif`) como columna de
texto propia en la tabla hija, y la alternate key se construye sobre esa copia. Esto evita la fragilidad
de indexar sobre un campo de tipo lookup.

---

## Alternate keys y los lookups padre-hijo

Cuando el motor resuelve el lookup de un hijo a su padre (via Relationship Mapping), construye:

```
new EntityReference("sanic_cliente", "sanic_cif", "20304050")
```

Dataverse resuelve esta referencia en tiempo real contra la alternate key de `sanic_cliente` sobre
`sanic_cif`. Si la alternate key no está `Active`, la escritura del hijo falla con `TargetData`.

Por lo tanto, la alternate key del padre debe estar `Active` antes de procesar cualquier hijo que la
referencie. El orden correcto siempre es: **crear y activar todas las alternate keys primero, luego
ejecutar el import**.

---

## Tabla de resumen: alternate keys del ejemplo de cobranza

| Tabla | Alternate Key | Columnas |
|-------|---------------|----------|
| `sanic_cliente` | `sanic_ak_cif` | `sanic_cif` |
| `sanic_producto` | `sanic_ak_codigo` | `sanic_codigo` |
| `sanic_agente` | `sanic_ak_nombre` | `sanic_nombre` |
| `sanic_fiador` | `sanic_ak_nombre` | `sanic_nombre` |
| `sanic_direccion` | `sanic_ak_cif_tipo` | `sanic_cif`, `sanic_tipo` |
| `sanic_telefono` | `sanic_ak_cif_tipo` | `sanic_cif`, `sanic_tipo` |
| `sanic_cuentamora` | `sanic_ak_numerocuenta` | `sanic_numerocuenta` |

---

## Referencias

- [Configurar un Import Template](./configuring-a-template.md)
- [Configurar un import de punta a punta](./new-import-end-to-end.md)
- [Ejemplo completo: Import de Cobranza](../06-examples/cobranza.md)
