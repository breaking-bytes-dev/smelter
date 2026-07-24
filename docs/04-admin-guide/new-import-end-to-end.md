[← Volver al índice de documentación](../README.md)

# Checklist — Configurar un import nuevo de punta a punta

> Esta guía es el recorrido completo para poner en producción un import que aún no existe: desde el
> modelado de las tablas hasta la revisión de logs después de la primera ejecución.
>
> Usá esta guía como checklist. Cada sección referencia las guías de detalle donde corresponde.

---

## Vista general del proceso

```
1. Modelar tablas y alternate keys (Dataverse)
        ↓
2. Crear el template + mappings (Smelter)
        ↓
3. Verificar que el Cloud Flow está activo
        ↓
4. Crear un Import Job, subir el CSV, ejecutar
        ↓
5. Monitorear el progreso
        ↓
6. Revisar logs — reintentar si es necesario
```

---

## FASE 1 — Modelar las tablas destino y sus alternate keys

Antes de tocar Smelter, las tablas que van a recibir datos deben existir en Dataverse y sus alternate
keys deben estar `Active`. Sin esto, el import no puede arrancar correctamente.

### 1.1 — Identificar las tablas destino y sus relaciones

Partí del CSV fuente y respondé estas preguntas:

- **¿Qué entidades de negocio representa cada columna?** Agrupar columnas relacionadas en tablas.
- **¿Qué relaciones hay entre esas entidades?** Dibujá el grafo: qué tabla referencia a qué.
- **¿Cuál es la clave de negocio de cada tabla?** El valor único que identifica un registro en el CSV
  (CIF, código de producto, número de cuenta, etc.).

El resultado es el **modelo de datos destino** y el **orden DAG**: las tablas sin dependencias entrantes
son los padres (se procesan primero); las que dependen de ellas son los hijos.

Consultá el [Ejemplo de Cobranza](../06-examples/cobranza.md) para ver un caso concreto de 7 tablas
con relaciones 1:N y alt keys simples y compuestas.

### 1.2 — Crear las tablas en Dataverse (si no existen)

Para cada tabla nueva:

1. [make.powerapps.com](https://make.powerapps.com) → **Solutions** → tu solución → **+ New** → **Table**.
2. Definí el nombre, prefijo y las columnas necesarias (nombre lógico, tipo de dato).
3. Publicá las customizaciones.

### 1.3 — Crear las alternate keys y verificar que estén Active

Para cada tabla destino, creá la alternate key sobre su clave de negocio.

Seguí los pasos detallados en [Alternate Keys](./alternate-keys.md). El punto crítico:

- [ ] Cada alternate key muestra estado **Active** antes de continuar.
- [ ] Si alguna queda en `Failed`, revisá duplicados o nulos en esas columnas.

> No avances al Paso 2 hasta que todas las alternate keys estén `Active`. Un estado `Pending` produce el
> mismo error que una alternate key inexistente cuando el import intenta escribir.

---

## FASE 2 — Crear el template y los mappings en Smelter

Con las tablas y claves listas, configurá la lógica de transformación en Smelter.

### 2.1 — Crear el Import Template

En la MDA de Smelter → **Import Templates** → **Nuevo**:

- [ ] Name descriptivo que identifique el caso de uso.
- [ ] Source Type = `CSV`.
- [ ] Has Header Row = `Sí`.
- [ ] Delimiter correcto (revisá el CSV: `,` o `;`).
- [ ] Chunk Size: `1000` como punto de partida.
- [ ] Status = `Active`.

Ver detalle en [Configurar un Import Template — PASO 1](./configuring-a-template.md#paso-1--crear-el-import-template).

### 2.2 — Crear los Table Mappings (uno por tabla destino, o por variante)

Para cada tabla destino:

- [ ] Target Table: nombre lógico exacto de la tabla en Dataverse.
- [ ] Processing Order: padres en `1`, hijos en `2`, etc.
- [ ] Dedup Columns: lista separada por coma de los nombres lógicos de las columnas de la alternate key.
- [ ] Create If Not Exists: `Sí` (en casi todos los casos).
- [ ] Update If Exists: `Sí` para tablas de estado actual; `No` para tablas append-only.

Si el CSV tiene **columnas paralelas** (ej. tres tipos de dirección en una fila), creá un Table Mapping
por cada variante, todos apuntando a la misma tabla destino. Ver
[Patrón: columnas paralelas](./configuring-a-template.md#patrón-columnas-paralelas-explotar-múltiples-valores-por-fila).

### 2.3 — Crear los Column Mappings (uno por columna a mapear)

Para cada Table Mapping, agregá los Column Mappings:

- [ ] Source Column: nombre exacto de la columna en el CSV (case-sensitive).
- [ ] Target Column: nombre lógico de la columna en Dataverse.
- [ ] Data Type: tipo correcto (String, Integer, Decimal, DateTime, Lookup, OptionSet, etc.).
- [ ] Default Value: para columnas que no vienen del CSV (como el campo discriminador en columnas paralelas).
- [ ] Is Required: activar para columnas cuya ausencia debe invalidar la fila completa.
- [ ] Para Lookup: completar Lookup Table y Lookup Match Column.
- [ ] Para OptionSet: completar OptionSet Mapping con JSON `{"valor_csv": valor_entero}`.
- [ ] Para fechas con formato no ISO: agregar Transform Expression `DATEPARSE(Columna, "dd/MM/yyyy")`.

Ver detalle en [Configurar un Import Template — PASO 3](./configuring-a-template.md#paso-3--crear-los-column-mappings).

### 2.4 — Crear los Relationship Mappings (uno por relación padre-hijo)

Para cada relación 1:N entre tablas:

- [ ] Parent Table Mapping: el Table Mapping de la tabla padre.
- [ ] Child Table Mapping: el Table Mapping de la tabla hija.
- [ ] Relationship Type: `OneToMany`.
- [ ] Lookup Column On Child: nombre lógico del campo lookup en la tabla hija (ej. `sanic_clienteid`).
- [ ] Parent Dedup Columns: nombres de las columnas en el **CSV** que identifican al padre (ej. `CIF`).

Ver detalle en [Configurar un Import Template — PASO 4](./configuring-a-template.md#paso-4--crear-los-relationship-mappings).

### 2.5 — Verificación cruzada de la configuración

Antes de ejecutar, revisá:

- [ ] Cada Table Mapping tiene al menos un Column Mapping.
- [ ] Las Dedup Columns del Table Mapping coinciden exactamente con las columnas de la alternate key.
- [ ] Los Parent Dedup Columns del Relationship Mapping son nombres de columnas del CSV (no nombres lógicos de Dataverse).
- [ ] El Processing Order refleja el DAG: ningún hijo tiene orden menor o igual al de su padre.

> **Consejo:** la primera vez que configurás un template complejo, probá capa por capa. Configurá y ejecutá
> solo la primera tabla, verificá, luego sumá la segunda capa. Ver
> [Consejo: configurar y verificar por capas](./configuring-a-template.md#consejo-configurar-y-verificar-por-capas).

---

## FASE 3 — Verificar que el Cloud Flow está activo

El motor de Smelter se dispara mediante un Cloud Flow de Power Automate que escucha cambios de estado en
el Import Job. Si el Flow está desactivado, los jobs quedan en estado `Queued` indefinidamente sin
procesarse.

### 3.1 — Verificar el estado del Flow

1. Navegá a [make.powerapps.com](https://make.powerapps.com) → **Solutions** → tu solución.
2. Buscá el Cloud Flow de Smelter (suele llamarse algo como `Smelter - Process Import Job` o similar).
3. Verificá que el estado sea **On** (activo).
4. Si está **Off**: abrilo → **Turn on**.

> Si el Flow falla con error de conexión (Dataverse connector), revisá que el usuario de conexión tenga
> el rol `Smelter Configurador` o permisos equivalentes. Ver [Seguridad y Roles](./security-and-roles.md).

---

## FASE 4 — Crear un Import Job, subir el CSV y ejecutar

Con el template configurado y el Flow activo, es el momento de la primera ejecución real.

### 4.1 — Preparar el archivo CSV

- [ ] El delimitador del CSV coincide con el configurado en el template.
- [ ] La primera fila es el encabezado (si Has Header Row = Sí).
- [ ] Los nombres de columna en el encabezado coinciden exactamente (case-sensitive) con los Source Column
      configurados en los Column Mappings.
- [ ] Los formatos de fecha son los esperados (o tenés Transform Expression para convertirlos).
- [ ] Para prueba inicial: usá un subconjunto de 50-100 filas, no el archivo completo.

### 4.2 — Crear el Import Job

En la MDA de Smelter → **Import Jobs** → **Nuevo**:

1. **Import Template:** seleccioná el template configurado.
2. **Source File:** subí el archivo CSV.
3. **Is Dry Run:** activar para la primera prueba (procesa sin escribir en Dataverse — solo valida y logea).
4. Guardá.

> **Dry Run** es un seguro para la primera ejecución. Activalo siempre que estés probando una configuración
> nueva o un CSV con formato diferente. Los logs muestran qué se habría creado o actualizado sin impactar
> los datos reales.

### 4.3 — Ejecutar el import

Cambiá el estado del Import Job a `Queued`. El Cloud Flow detecta el cambio y dispara el plugin async,
que comienza a procesar el CSV en chunks.

---

## FASE 5 — Monitorear el progreso

El Import Job se actualiza en tiempo real con el progreso del procesamiento.

### 5.1 — Campos de progreso en el Import Job

| Campo | Qué muestra |
|-------|-------------|
| **Status** | Estado actual: `Queued`, `Processing`, `Completed`, `CompletedWithErrors`, `Failed` |
| **Total Rows** | Total de filas del CSV (excluye el encabezado) |
| **Processed Rows** | Filas procesadas hasta el momento |
| **Success Count** | Operaciones exitosas (Create + Update) |
| **Failed Count** | Filas con error |
| **Skipped Count** | Filas omitidas por dedup (registro ya existe, Update If Exists = No) |
| **Processing Batch** / **Total Batches** | Progreso por chunks |
| **Error Summary** | Resumen de los primeros 100 errores |

Refrescá el formulario periódicamente para ver el avance.

### 5.2 — Estados finales

- **Completed:** todas las filas se procesaron sin errores.
- **CompletedWithErrors:** el procesamiento terminó pero algunas filas fallaron. Revisá los logs.
- **Failed:** error fatal que detuvo el proceso (generalmente configuración incorrecta o template inválido).

---

## FASE 6 — Revisar logs y reintentar si es necesario

### 6.1 — Revisar los Import Job Logs

Desde el formulario del Import Job, abrí la subgrilla **Import Job Logs**. Cada log corresponde a una
operación (una fila del CSV, una tabla destino).

| Campo | Descripción |
|-------|-------------|
| **Row Number** | Número de fila en el CSV (incluye el encabezado; fila 2 = primera fila de datos) |
| **Target Table** | Tabla Dataverse donde se intentó escribir |
| **Status** | `Created`, `Updated`, `Skipped`, `Failed` |
| **Created Record ID** | GUID del registro creado o actualizado (solo en éxito) |
| **Error Message** | Detalle del error (solo en fallo) |

Filtrá por `Status = Failed` para ver solo los errores.

### 6.2 — Errores frecuentes y su causa

| Error en el log | Causa probable | Acción |
|-----------------|----------------|--------|
| `The key attributes '...' are not defined as keys` | Alternate key faltante o no Active | Crear/activar la alternate key; ver [Alternate Keys](./alternate-keys.md) |
| `Required field '...' is empty` | Campo marcado Is Required sin valor en esa fila | Revisar el CSV o ajustar Is Required / Default Value |
| `Lookup '...' value '...' is not a GUID and no LookupMatchColumn` | Lookup mal configurado | Completar Lookup Match Column en el Column Mapping |
| `TypeError` | Valor no convertible al Data Type configurado | Revisar el formato del dato en el CSV o agregar Transform Expression |
| `TransformError` | Error en la Transform Expression | Revisar la expresión DSL; ver [Transform Expressions](../03-developer-guide/transform-dsl.md) |

### 6.3 — Reintentar un import fallido

Los imports de Smelter son idempotentes: si un import falla a mitad de proceso, podés crear un nuevo
Import Job con el mismo CSV y el mismo template. Las filas ya procesadas exitosamente se reupsertan sin
efecto neto (mismo registro, mismo valor). Las filas que fallaron se reintentarán.

No es necesario "corregir" las filas exitosas ni generar un CSV parcial.

Si los errores son de configuración (alternate key faltante, Column Mapping incorrecto), corregí la
configuración primero y luego ejecutá el nuevo Import Job.

---

## Checklist de verificación final

Antes de considerar el import en producción:

- [ ] Las alternate keys de todas las tablas destino están en estado `Active`.
- [ ] El Cloud Flow está activo y sin errores de conexión.
- [ ] El Dry Run no mostró errores estructurales (campos requeridos vacíos, tipos incorrectos).
- [ ] La primera ejecución real con datos de prueba (50-100 filas) completó sin errores o con errores
      esperados y documentados.
- [ ] Los registros creados en Dataverse tienen los valores correctos (revisá manualmente al menos 3-5).
- [ ] Si el import es recurrente, verificaste que reprocesar el mismo CSV no duplica registros.
- [ ] Los usuarios operadores tienen acceso al template y pueden crear Import Jobs.
  Ver [Seguridad y Roles](./security-and-roles.md).

---

## Referencias

- [Alternate Keys — el contrato de las claves de negocio](./alternate-keys.md)
- [Configurar un Import Template](./configuring-a-template.md)
- [Export/Import de Templates entre entornos](./export-import-templates.md)
- [Seguridad y Roles](./security-and-roles.md)
- [Ejemplo completo: Import de Cobranza](../06-examples/cobranza.md)
- [Transform Expressions (DSL)](../03-developer-guide/transform-dsl.md)
