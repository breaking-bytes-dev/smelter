[← Volver al índice de documentación](../README.md)

# Monitoreo y manejo de errores

Esta guía explica cómo hacer seguimiento de los trabajos de carga y qué hacer cuando un import termina con errores.

---

## El dashboard

La pantalla de inicio de Smelter es el **Smelter Dashboard**. Muestra un panorama general del estado de los trabajos:

- **Jobs por Estado**: cuántos trabajos están en cada estado ahora mismo. Si hay muchos en `Failed` o `CompletedWithErrors`, es señal de que algo requiere atención.
- **Jobs por Template**: qué templates se usan más. Sirve para entender el volumen de trabajo.
- **Errores por Categoría**: qué tipos de errores están ocurriendo con más frecuencia. Si una categoría domina el gráfico, puede indicar un problema sistemático (por ejemplo, muchos `LookupUnresolved` sugieren que falta configuración de un padre).
- **Jobs en Proceso**: lista en tiempo real de los trabajos que están corriendo ahora.

El dashboard se puede usar para detectar problemas antes de que alguien los reporte.

---

## Las vistas de Import Jobs

Desde **Ejecución y Monitoreo → Import Jobs** podés cambiar entre distintas vistas:

| Vista | Para qué sirve |
|-------|----------------|
| **Jobs Activos** | Ver los trabajos que están en cola o procesando. Muestra el progreso. |
| **Completados con Errores** | Ver los trabajos que terminaron pero con filas fallidas. Desde acá podés abrir cada uno para revisar los errores. |
| **Todos los Jobs** | Historial completo, ordenado por fecha de creación. |

---

## Qué hacer cuando un job termina con CompletedWithErrors

### 1. Abrir el job

Desde la vista **Completados con Errores**, hacé click en el job que querés revisar.

### 2. Ver los errores del job

El detalle de los errores está en la pestaña **Errores** dentro del form del job.

Ahí aparece la lista de **Import Job Logs**, una entrada por cada fila que falló, con estas columnas:

| Columna | Qué indica |
|---------|-----------|
| **Row Number** | El número de fila en el CSV original donde ocurrió el error. Podés abrir el archivo y buscar esa fila para ver qué tiene. |
| **Target Table** | La tabla de Dataverse a la que se intentó escribir. |
| **Error Category** | El tipo de error. Ver la sección siguiente para entender cada categoría. |
| **Error Message** | El mensaje de error específico del sistema, incluyendo la columna exacta que falló. |

También podés ir a **Diagnóstico → Import Job Logs** para ver todos los errores de todos los jobs, con opción de filtrar y agrupar por categoría. Es útil para detectar patrones.

---

## Las categorías de error

Cada error tiene una categoría que indica en qué etapa falló el procesamiento. Entender la categoría ayuda a saber si el problema está en el archivo, en la configuración, o en el sistema.

### SourceError

**Qué pasó**: la fila tiene un problema estructural — columnas faltantes, formato incorrecto, o el archivo no se pudo leer correctamente en esa fila.

**Qué hacer**: revisá la fila indicada en el CSV. Verificá que todas las columnas esperadas estén presentes y que no haya caracteres extraños.

---

### TransformError

**Qué pasó**: el template tiene una expresión de transformación para esa columna (por ejemplo, una fórmula que convierte o combina valores), y la expresión falló.

**Qué hacer**: este error generalmente requiere revisar la configuración del template. Avisá al configurador indicando el campo afectado y el mensaje de error.

---

### TypeError

**Qué pasó**: el valor en la fila no se puede convertir al tipo de dato que espera la columna destino. Por ejemplo: la columna destino es numérica pero el CSV tiene texto en esa celda, o una fecha tiene un formato que el sistema no reconoce.

**Qué hacer**: revisá la fila en el CSV. El **Error Message** debería indicar qué campo y qué valor falló. Corregí el dato en el archivo y reintentá, o avisá al configurador si el tipo definido en el template no es el correcto.

---

### ValidationError

**Qué pasó**: un campo obligatorio está vacío en esa fila, o un valor no pasa una regla de validación del negocio.

**Qué hacer**: revisá la fila en el CSV. El campo que falló debería estar indicado en el mensaje de error. Completá o corregí el dato y reintentá.

---

### LookupUnresolved

**Qué pasó**: el sistema intentó vincular esa fila con un registro relacionado (padre) en otra tabla de Dataverse, pero no encontró ningún registro que coincida con los valores indicados.

Por ejemplo: si el CSV tiene una columna "País" y esa columna se resuelve contra una tabla de países en Dataverse, un `LookupUnresolved` significa que el nombre del país que viene en el CSV no existe en Dataverse.

**Qué hacer**:
- Si son pocos errores: revisá las filas afectadas en el CSV y verificá que los valores coincidan exactamente con los registros de la tabla padre.
- Si son muchos errores (por ejemplo, cientos de filas con este error): es probable que falte cargar los datos maestros (padres) en Dataverse antes de correr este import. Avisá al configurador — puede ser un problema de orden de carga o de configuración de alternate keys.

---

### TargetData

**Qué pasó**: Dataverse recibió los datos pero los rechazó. Puede ser por un problema de datos (por ejemplo, una restricción de unicidad violada, un campo con formato incorrecto para Dataverse) o por una configuración incompleta (por ejemplo, falta una alternate key en la tabla destino).

**Qué hacer**: este error casi siempre requiere intervención del configurador. El **Error Message** tiene el detalle técnico del rechazo.

> Si ves muchos errores `TargetData` con mensajes similares sobre una misma tabla, es muy probable que esa tabla le falte una *alternate key* configurada. Eso es algo que arregla el configurador, no el operador.

---

### Transient

**Qué pasó**: ocurrió un error temporal en el sistema (por ejemplo, un timeout de base de datos o un bloqueo momentáneo). No es un problema en los datos.

**Qué hacer**: en la mayoría de los casos, **Reintentar** resuelve este tipo de errores. Si el error persiste después de varios reintentos, avisá al equipo técnico.

---

## Cómo reintentar

Cuando un job terminó (`Completed`, `CompletedWithErrors` o `Failed`), podés volver a correrlo con el botón **↻ Reintentar** en la barra de comandos del job. El botón está deshabilitado mientras el job está en Queued o Processing.

Al hacer click en Reintentar:

1. El botón guarda cambios pendientes y vuelve a poner el job en estado **Queued** (es el mismo y único camino que **Ejecutar**: el procesamiento corre en el servidor vía Cloud Flow, podés cerrar la pestaña).
2. El flow arranca de nuevo y la preparación resetea los contadores (Processed Rows, Success, Failed, Skipped vuelven a cero) y el avance interno: el proceso corre desde el principio.
3. Si la preparación falla (p. ej. falta una columna en el CSV), el job queda en **Failed** y el detalle está en el trace del sistema.

### Por qué es seguro reintentar

El sistema usa una lógica de *upsert*: al procesar cada fila, primero busca si el registro ya existe en Dataverse (usando la clave de deduplicación definida en el template). Si ya existe, lo actualiza. Si no existe, lo crea.

Esto significa que:
- Las filas que ya se cargaron bien en la primera ejecución **no se duplican** — simplemente se actualizan con los mismos valores.
- Las filas que fallaron **se reintentarán**.

Podés reintentar con tranquilidad.

---

## Cuándo escalar al configurador

No todos los errores los puede resolver el operador. Escalá al configurador cuando:

- Hay muchos errores **TargetData** con mensajes similares → probablemente falta una alternate key.
- Hay muchos errores **LookupUnresolved** → puede faltar cargar datos maestros antes, o hay un problema de configuración del mapeo de relaciones.
- Hay errores **TransformError** → el problema está en una expresión del template.
- El job queda en estado **Failed** y al reintentar vuelve a fallar → hay algo sistémico que no es un problema de datos.

En todos estos casos, describí al configurador: el nombre del job, la categoría de error que predomina, y el mensaje de error de algún ejemplo representativo. Con eso pueden encontrar el problema más rápido.
