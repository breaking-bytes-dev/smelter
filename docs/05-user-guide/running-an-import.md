[← Volver al índice de documentación](../README.md)

# Cómo ejecutar una carga

Esta guía describe el proceso completo de subir un archivo CSV y cargarlo en Dataverse, paso a paso.

---

## Antes de empezar

Necesitás:

- El **archivo CSV** listo para cargar.
- Saber qué **template** corresponde a ese archivo. Si no sabés cuál usar, consultá al equipo configurador.

Si el template todavía no existe, primero hay que crearlo (ver la sección al final de esta guía). Eso lo hace el configurador, no el operador.

---

## Paso a paso

### 1. Ir a Import Jobs

En el menú lateral, en el grupo **Ejecución y Monitoreo**, hacé click en **Import Jobs**.

### 2. Crear un nuevo Import Job

Hacé click en **Nuevo** (botón de la barra superior).

Se abre el formulario de Import Job con los campos vacíos.

### 3. Elegir el template

En el campo **Import Template**, hacé click y buscá el template que corresponde al tipo de archivo que vas a cargar. Seleccionalo.

> Si no aparece el template que buscás, puede que no haya sido creado todavía. Consultá al configurador.

### 4. Subir el archivo CSV

En el campo **Source File**, hacé click en el botón de carga. Buscá el archivo CSV en tu computadora y seleccionalo.

El archivo se adjunta al job. El tamaño máximo permitido es **128 MB**.

### 5. (Opcional) Activar Dry Run

Si querés verificar que el archivo esté bien formado y que el template lo procese correctamente **sin cargar nada en Dataverse**, activá el campo **Is Dry Run** antes de ejecutar.

Con Dry Run activado:
- El proceso recorre todo el archivo y detecta errores.
- No crea ni modifica ningún registro en Dataverse.
- Al terminar, podés ver qué filas habrían fallado y por qué.

Una vez que verificaste que todo está bien, podés desactivar Dry Run y ejecutar la carga real.

### 6. Guardar el job

Hacé click en **Guardar** para registrar el job. Todavía no empezó a procesar.

> **Trabajo en equipo:** si el template pertenece a un equipo (lo normal), el job queda **a nombre del
> equipo** automáticamente — cualquier compañero del equipo puede verlo, seguir su progreso y
> reintentarlo. Buscalo en las vistas **Jobs Activos** o **Todos los Jobs** (la vista "My Import Jobs"
> solo muestra jobs a tu nombre personal).

### 7. Ejecutar

Hacé click en el botón **▶ Ejecutar** en la barra de comandos.

El sistema valida que haya un template y un archivo seleccionados. Si falta alguno, muestra un mensaje de error.

Si todo está bien, el job pasa a estado **Queued** y el procesamiento arranca automáticamente en segundos.

### 8. Seguir el progreso

El job pasa por los siguientes estados durante el proceso:

| Estado | Qué significa |
|--------|---------------|
| **Queued** | El trabajo está en la cola, esperando que el sistema lo tome. |
| **Processing** | El sistema está procesando el archivo. Los campos **Processed Rows** y **Total Rows** van avanzando. |
| **Completed** | Todas las filas se procesaron correctamente. No hubo errores. |
| **CompletedWithErrors** | El proceso terminó, pero algunas filas fallaron. Los campos **Failed** y **Error Summary** tienen el detalle. |
| **Failed** | El proceso no pudo completarse por un error grave. No se procesaron todas las filas. Conviene revisar el Error Summary y contactar al configurador. |

Para ver el avance en tiempo real:
- Recargá el form del job (F5 o el botón de refrescar del navegador).
- O seguilo desde el **dashboard**, en la lista "Jobs en Proceso".

Los campos **Processed Rows** y **Total Rows** te dicen cuánto avanzó. Por ejemplo: `1.500 / 5.000` significa que se procesaron 1.500 filas de un total de 5.000.

### 9. Verificar el resultado

Cuando el estado llega a **Completed** o **CompletedWithErrors**, el proceso terminó.

- Si está **Completed**: la carga fue exitosa. Podés verificar los datos en las tablas destino.
- Si está **CompletedWithErrors**: hay filas que fallaron. Abrí la pestaña **Errores** del job para ver el detalle. Consultá la guía de [Monitoreo y reintento](monitoring-and-retry.md) para saber cómo proceder.

---

## Si el template no existe todavía

Los templates los crea el equipo configurador, no el operador. Lo que sí podés hacer es verificar si existe uno adecuado:

1. En el menú lateral, en el grupo **Configuración**, hacé click en **Import Templates**.
2. Buscá por nombre o revisá la lista.
3. Si no encontrás el template que necesitás, contactá al configurador con la descripción del archivo que querés cargar (qué columnas tiene, a qué tablas va la información).
