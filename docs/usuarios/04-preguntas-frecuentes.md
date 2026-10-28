# Preguntas frecuentes

Respuestas cortas a las dudas que más se repiten al configurar y ejecutar importaciones en Smelter, agrupadas por tema. Cada respuesta dice qué hacer y dónde está la explicación completa. Si un término no te queda claro, mira [Conceptos básicos](00-conceptos.md) o el [Glosario](../glosario.md).

- [Acceso y pantallas](#acceso-y-pantallas)
- [Templates y mappings](#templates-y-mappings)
- [Ejecución de jobs](#ejecución-de-jobs)
- [Errores y resultados](#errores-y-resultados)
- [Exportar e importar](#exportar-e-importar)

## Acceso y pantallas

### No veo la app Smelter — Configuración de importaciones

La app es solo para el rol **Smelter Configurador**. Si eres Operador, trabajas desde la app que consume Smelter (por ejemplo, Cobranza) y no ves la app de configuración. Si necesitas configurar, pídele a quien administra los accesos que te asigne el rol **Smelter Configurador**.

Más detalle: [Conceptos](00-conceptos.md#qué-es-smelter).

### Veo textos en inglés aunque la app esté en español

Es normal y viene de tres lugares:

- Algunos nombres no se traducen por decisión del producto: **Import Jobs**, **Import Job** y las vistas de sistema, como **Active TBL | SMLT | Import Jobs**.
- Las columnas estándar de la plataforma, como **Owner** o **Created On**, aparecen en inglés.
- Los mensajes del motor (**Motivo de la falla** y **Mensaje de error** de los logs) están siempre en inglés.

Más detalle: [Mensajes frecuentes](02-ejecucion-y-monitoreo.md#mensajes-frecuentes-y-qué-hacer).

### Algunos avisos me tratan de «vos» («Subí el CSV…», «podés cerrar esta ventana»)

Son los avisos de los botones **Ejecutar** y **Reintentar** y de algunos motivos de falla. Están escritos así en la aplicación y se muestran tal cual en estas guías. No cambian lo que debes hacer.

### ¿Puedo crear un log de importación a mano?

No. Los logs los escribe solo el motor de Smelter al procesar, y el rol Configurador no puede crearlos: la subgrilla de logs no muestra el botón **Nuevo Log de importación**.

### El Panel de Smelter no muestra nada

El panel (gráficos de jobs por **Estado** y por template, y la lista de jobs) se arma con los jobs que tienes a la vista. Si todavía no se creó ningún job, los gráficos y la lista aparecen vacíos. Pulsa **Actualizar** o crea un job.

Más detalle: [El Panel de Smelter](02-ejecucion-y-monitoreo.md#el-panel-de-smelter).

## Templates y mappings

### Mi template no aparece cuando creo un Import Job

La lista de **Template de importación** del job ofrece solo los **Templates activos**. Abre el template y comprueba que **Estado** sea **Activo**. Si está en **Borrador** o **Archivado**, no aparece.

Más detalle: [Activar el template](01-configurador.md#paso-5-revisa-y-activa-el-template).

### No me deja guardar el Tamaño de chunk

El **Tamaño de chunk** debe estar **entre 100 y 10000**. Si lo dejas vacío, Smelter usa **1000**. Un valor fuera de ese rango se rechaza al guardar. Si necesitas la opción por defecto, borra el valor y deja el campo vacío.

Más detalle: [Tamaño de chunk](01-configurador.md#tamaño-de-chunk).

### ¿Cómo escribo un tabulador como delimitador?

En **Delimitador** escribe `TAB` o `\t` (no distingue mayúsculas de minúsculas). El tabulador real no se puede teclear en un campo de texto. Cualquier otro texto, como `;` o `|`, se usa tal cual; vacío equivale a la coma.

Más detalle: [Delimitador](01-configurador.md#delimitador).

### ¿Qué pongo en «Columnas de deduplicación»?

Los nombres lógicos de las columnas **destino** (no los del CSV) que identifican un registro, separados por coma, por ejemplo `emailaddress1` o `sanic_cif,sanic_tipo`. Cada una tiene que ser la **Columna destino** de algún mapping de columna de ese mismo mapping de tabla, y la tabla destino tiene que tener una clave alternativa activa sobre ellas.

Más detalle: [Cómo elegir las columnas de deduplicación](01-configurador.md#cómo-elegir-las-columnas-de-deduplicación).

### ¿Qué diferencia hay entre «Actualizar si existe» y «Solo inserción»?

- **Actualizar si existe** (por defecto **Sí**): si la clave ya existe, la fila actualiza ese registro. En **No**, la fila se omite.
- **Solo inserción (permite duplicados)**: el mapping siempre crea registros y no usa claves. Si reprocesas el archivo, se duplican.

Más detalle: [Conceptos](00-conceptos.md#crear-si-no-existe-y-actualizar-si-existe).

### Un Sí/No del CSV falla como «Error de tipo»

El tipo **Booleano** solo entiende `true`/`false`, `1`/`0` y `yes`/`no`. Valores como `Sí` o `Verdadero` no se aceptan. Conviértelos con una transformación, por ejemplo `IF(Activo, "Sí", 1, 0)`.

Más detalle: [Tipos de dato](01-configurador.md#tipos-de-dato).

### Una fecha del CSV se guarda con el día y el mes cambiados

Los formatos como `03/04/2026` se leen con el mes primero. Si tu archivo trae día/mes/año, usa `DATEPARSE`: `DATEPARSE(Fecha, "dd/MM/yyyy")`, con **Tipo de dato** **Fecha y hora**.

### Escribí mal una columna dentro de una transformación y no hay error

Es así: si el nombre de una columna dentro de una expresión no existe en el CSV, el valor queda **vacío** y no se marca error. Si esa columna es obligatoria, la fila fallará por vacío. Revisa que el nombre coincida con el encabezado.

### ¿Puedo cambiar un template mientras hay jobs corriendo?

Evítalo. Smelter lee la configuración del template al procesar cada bloque de filas, así que un cambio a mitad de camino puede dar resultados mezclados. Espera a que los jobs terminen.

### ¿Qué son los botones «Activar» y «Desactivar» de la barra de comandos?

Son comandos estándar de la plataforma Dataverse para el registro. **No** cambian el campo **Estado** del template (Borrador, Activo, Archivado). Para habilitar un template usa el campo **Estado**.

## Ejecución de jobs

### No encuentro el botón «Ejecutar»

Está dentro del menú **⋮** (**Más comandos para Import Job**) de la barra de comandos del job. Solo está disponible en un job que **nunca corrió** (con **Estado** vacío). En un job que ya corrió, el comando es **Reintentar**.

Más detalle: [Paso 3: Ejecuta](02-ejecucion-y-monitoreo.md#paso-3-ejecuta).

### Pulso «Ejecutar» y aparece un cuadro de error

Es una validación previa. El job **no** cambia de estado. Los avisos y qué hacer en cada caso están en [Mensajes frecuentes](02-ejecucion-y-monitoreo.md#al-pulsar-ejecutar-o-reintentar-cuadro-de-error-el-job-no-cambia-de-estado). El más común: falta subir el CSV en **Archivo de origen**.

### El job se quedó «En cola»

El procesamiento lo hace un flujo de Power Automate en el servidor. Mientras el job esté **En cola** o **Procesando**, pulsa **Actualizar** en la barra de comandos para ver el **Estado** al día. Si el **Estado** no cambia después de un tiempo razonable, avisa a quien administra el flujo de Smelter para que revise su historial de ejecuciones. No intentes ejecutarlo de nuevo: mientras esté en cola, **Ejecutar** y **Reintentar** no están disponibles.

### ¿Puedo cerrar el navegador mientras el job corre?

Sí. El procesamiento corre en el servidor, no en tu navegador. Vuelve más tarde a **Import Jobs** y mira el **Estado**.

### Quiero probar un archivo sin escribir datos

Crea el job con **Es simulación (Dry Run)** en **Sí**. Valida formato, tipos y transformaciones sin escribir en Dataverse. Necesitas un template **Activo**. Una simulación no detecta problemas que solo Dataverse ve, como una clave alternativa que no existe.

Más detalle: [Hacer una prueba con simulación](02-ejecucion-y-monitoreo.md#hacer-una-prueba-con-simulación-dry-run).

### ¿Reintentar duplica los datos?

Depende del mapping:

- Con **Columnas de deduplicación**: **no**. Las filas ya cargadas se actualizan con los mismos valores.
- Con **Solo inserción (permite duplicados)** en **Sí**: **sí**, esos registros se crean de nuevo.

Más detalle: [Reintentar](02-ejecucion-y-monitoreo.md#reintentar).

## Errores y resultados

### El job quedó «Fallido». ¿Qué hago?

1. Abre el job y lee **Motivo de la falla** (viene en inglés).
2. Búscalo en [Mensajes frecuentes](02-ejecucion-y-monitoreo.md#mensajes-frecuentes-y-qué-hacer) y corrige la causa (el template, un mapping o el archivo).
3. Usa **Reintentar**.

Los más comunes: `... is not Active (status=Draft)` (activa el template), `CSV is missing mapped source column(s): ...` (al CSV le falta una columna o el mapping tiene mal su nombre) y `... has no dedup columns ...` (falta **Columnas de deduplicación**).

### «Completado con errores»: ¿se guardó algo?

Sí. **Completado con errores** significa que el job terminó y **al menos una fila falló**; las demás filas **sí se guardaron**. Abre la pestaña **Logs de importación** del job para ver cuáles fallaron y por qué.

Más detalle: [Un job Completado con errores](02-ejecucion-y-monitoreo.md#un-job-completado-con-errores).

### El log dice «Número de fila 4» pero yo cuento la fila 3

El **Número de fila** es el número de fila **del archivo, contando el encabezado**. Si el archivo tiene encabezado, la primera fila de datos es la 2: la **fila de datos 3** aparece como **4**. Es la misma fila que ves en una hoja de cálculo. Con **Tiene fila de encabezados** en **No**, la numeración empieza en 1.

Más detalle: [El «Número de fila» de un log](02-ejecucion-y-monitoreo.md#el-número-de-fila-de-un-log).

### ¿Qué pasa con el Motivo de la falla cuando reintento?

Se limpia cuando el servidor empieza a procesar el job de nuevo. Si el job vuelve a fallar, mostrará el motivo nuevo.

### El job está «Completado» pero no se creó ningún registro

Revisa tres cosas:

1. ¿Era una **simulación**? Mira **Es simulación (Dry Run)**: con **Sí** no se escribe nada.
2. ¿Los mappings tienen **Crear si no existe** o **Actualizar si existe** en **No**? Esas filas se omiten y cuentan en **Filas omitidas**.
3. ¿El template apunta a la **Tabla destino** correcta? Revisa el nombre lógico.

### Las filas exitosas más las fallidas no suman el total

Hay dos motivos posibles:

- Las **Filas omitidas** (por **Crear si no existe** o **Actualizar si existe** en **No**) no están en los logs, pero sí son parte del total.
- Si tu template tiene **varios mappings de tabla**, cada fila genera un resultado por mapping, y los contadores cuentan esos resultados.

### Veo «Dataverse rejected the write (ErrorCode 0x…)» en muchas filas

Es la categoría **Datos de destino**: Dataverse rechazó la escritura. El mensaje solo trae el código, nunca datos de la fila. Si se repite en muchas filas, revisa que la tabla destino tenga una **clave alternativa activa** sobre las **Columnas de deduplicación**, y pasa el código de error a quien administra Dataverse.

## Exportar e importar

### Importé un template y no puedo ejecutarlo

Revisa el **Estado** del template importado: el estado viaja dentro del archivo y puede haber llegado en **Borrador**. Ábrelo, cambia **Estado** a **Activo** y guarda. También mira si el cuadro de importación mostró una advertencia de tabla destino inexistente.

Más detalle: [Con qué Estado llega el template](03-exportar-e-importar.md#con-qué-estado-llega-el-template).

### Reimporté un template. ¿Se duplicaron los mappings?

No. Si ya existe un template con el mismo **Nombre**, se actualiza y **todos sus mappings se reemplazan** por los del archivo. Los cambios que hubieras hecho a mano en esos mappings se pierden.

### La importación se rechaza por el «chunkSize»

El archivo trae un **Tamaño de chunk** fuera de rango. Debe ser **0** (usa el valor por defecto) o estar **entre 100 y 10000**. Corrige el valor en el archivo `.json` (`"ChunkSize"`) o el template original y vuelve a exportar.

### ¿Cómo hago una copia de un template?

Expórtalo, abre el `.json`, cambia el valor de `"Name"` dentro de `"Template"` e impórtalo: se crea un template nuevo con el nuevo nombre.

Más detalle: [Exportar e importar templates](03-exportar-e-importar.md).
