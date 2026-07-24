[← Volver al índice de documentación](../README.md)

# Usando la app Smelter

Esta guía describe la aplicación Smelter pantalla por pantalla. Está pensada para operadores que cargan archivos al sistema: no es necesario conocer los detalles técnicos, pero sí entender para qué sirve cada sección.

---

## Navegación

La app se divide en tres grupos en el menú lateral:

### Configuración

Contiene **Import Templates**. Los templates son las plantillas que definen cómo se interpreta un archivo CSV: qué columnas mapean a qué campos, qué tablas se van a cargar, etc.

> Como operador, generalmente no vas a crear templates vos mismo. Los prepara el equipo configurador. Tu trabajo es elegir el template correcto al momento de cargar.

### Ejecución y Monitoreo

Tiene dos secciones:

- **Smelter Dashboard** — es la pantalla de inicio de la app. Muestra gráficos con el estado general de todos los trabajos de carga.
- **Import Jobs** — la lista de todos los trabajos de carga (uno por cada archivo que se subió o se está subiendo).

### Diagnóstico

Contiene **Import Job Logs**. Son los registros de error detallados: cada fila que falló durante una carga aparece acá con su motivo. Se puede filtrar y agrupar por tipo de error.

---

## El form de Import Job

Cuando abrís un Import Job (ya sea para crearlo o para ver su estado), vas a ver tres secciones:

### Sección Setup

Los campos que completás al crear el trabajo:

| Campo | Qué es |
|-------|--------|
| **Nombre** | Se genera automáticamente como `{template} - {fecha}`, pero se puede cambiar. |
| **Import Template** | El template que define cómo procesar el archivo. Elegís uno de la lista. |
| **Source File** | El archivo CSV a cargar. Hacés click y subís el archivo desde tu computadora. Tamaño máximo: 128 MB. |
| **Is Dry Run** | Si está activado, el sistema simula la carga pero **no escribe nada** en Dataverse. Útil para verificar que el archivo esté bien antes de cargar en serio. |

### Sección Progreso

Estos campos se actualizan solos durante y después de la ejecución. Son de solo lectura: no se pueden editar.

| Campo | Qué muestra |
|-------|-------------|
| **Status** | El estado actual del trabajo (ver detalle abajo). |
| **Total Rows** | Cantidad de filas que tiene el archivo CSV. |
| **Processed Rows** | Cuántas filas ya fueron procesadas. Avanza mientras el trabajo está en curso. |
| **Success** | Filas que se cargaron correctamente (creadas o actualizadas en Dataverse). |
| **Failed** | Filas que fallaron. Si este número es mayor que cero, conviene revisar los errores. |
| **Skipped** | Filas que se saltearon porque el registro ya existía y el template indica que no se debe actualizar. |
| **Start Time** | Cuándo empezó a procesar el sistema. |
| **End Time** | Cuándo terminó (o falló). |

### Sección Resumen

| Campo | Qué muestra |
|-------|-------------|
| **Error Summary** | Un resumen con los primeros 100 errores del trabajo. Para ver todos los errores en detalle, usá la pestaña **Errores** (ver más abajo). |

### Pestaña Errores

Dentro del form de un Import Job hay una pestaña llamada **Errores**. Ahí aparece la lista de Import Job Logs correspondientes a las filas que fallaron, con estas columnas:

| Columna | Qué indica |
|---------|-----------|
| **Row Number** | El número de fila en el archivo CSV original donde ocurrió el error. |
| **Target Table** | La tabla de Dataverse a la que se intentó escribir. |
| **Error Category** | El tipo de error (ver la guía de monitoreo para el detalle de cada categoría). |
| **Error Message** | El mensaje de error concreto que generó el sistema. |

---

## Los botones de la barra de comandos

En la pantalla de un Import Job aparecen dos botones en la barra superior, dependiendo del estado del trabajo:

### ▶ Ejecutar

Inicia la carga. El sistema valida que el job tenga un template y un archivo seleccionado, y pone el trabajo en estado **Queued** (en cola). Desde ahí, el procesamiento arranca automáticamente.

Este botón aparece cuando el job está recién creado, o cuando terminó con errores (`CompletedWithErrors`) o falló (`Failed`).

### ↻ Reintentar

Vuelve a ejecutar el import completo desde el principio. El sistema resetea los contadores y pone el trabajo en **Queued** de nuevo.

> **¿Por qué es seguro reintentar?** Porque el proceso usa una lógica de *upsert*: si un registro ya existe en Dataverse (lo cargó una ejecución anterior), lo actualiza en lugar de duplicarlo. Si falló, lo reintenta. No hay riesgo de cargar el mismo dato dos veces.

Este botón aparece cuando el job terminó (`Completed`, `CompletedWithErrors`) o falló (`Failed`).

---

## El dashboard

La pantalla de inicio muestra cuatro elementos:

- **Jobs por Estado** — gráfico de torta o barras con cuántos trabajos están en cada estado (en cola, procesando, completados, con errores, fallidos).
- **Jobs por Template** — cuántas veces se usó cada template. Útil para ver qué tipos de carga son más frecuentes.
- **Errores por Categoría** — gráfico con los tipos de error más comunes en todos los trabajos. Si hay una categoría que domina, es una señal para revisar la configuración.
- **Jobs en Proceso** — lista en tiempo real de los trabajos que están procesando en este momento, con su progreso.
