# Glosario

Los términos de Smelter para quien configura y ejecuta importaciones. Cada término tiene su nombre en español como lo muestra la app **Smelter — Configuración de importaciones**, su equivalente en la interfaz en inglés (**Smelter — Import configuration**) y una definición en lenguaje simple, con un enlace a la guía donde se explica a fondo.

> **Nota sobre los nombres en inglés:** en la interfaz en inglés, las tablas llevan un prefijo técnico (`TBL | SMLT |`, por ejemplo *TBL | SMLT | Import Job*) y las columnas llevan una sigla entre corchetes (`[IT]`, `[TM]`, `[CM]`, `[RM]`, `[IJ]`, `[IJL]`, por ejemplo *[IJ] Status*). En este glosario se omiten esos prefijos para que el nombre sea legible. Los botones **Ejecutar**, **Reintentar**, **Exportar Smelter Template** e **Importar Smelter Template** se ven igual en los dos idiomas.

- [La app y su menú](#la-app-y-su-menú)
- [Templates y mappings](#templates-y-mappings)
- [Campos del template](#campos-del-template)
- [Campos del mapping de tabla](#campos-del-mapping-de-tabla)
- [Campos del mapping de columna](#campos-del-mapping-de-columna)
- [Tipos de dato](#tipos-de-dato)
- [Campos del mapping de relación](#campos-del-mapping-de-relación)
- [Import Jobs](#import-jobs)
- [Estados](#estados)
- [Logs y categorías de error](#logs-y-categorías-de-error)
- [Transformaciones](#transformaciones)
- [Roles](#roles)
- [Conceptos generales](#conceptos-generales)

## La app y su menú

| Término | En inglés | Qué es |
|---|---|---|
| **Smelter — Configuración de importaciones** | Smelter — Import configuration | La model-driven app donde el Configurador arma templates, ejecuta imports y revisa errores. Se abre desde Power Apps |
| **Configuración** | Configuration | Grupo del menú. Contiene **Templates de importación** |
| **Templates de importación** | Import templates | Pantalla con la lista de templates. Ver [Guía del Configurador](usuarios/01-configurador.md) |
| **Ejecución y monitoreo** | Execution & monitoring | Grupo del menú. Contiene **Import Jobs** y **Panel de Smelter** |
| **Import Jobs** | Import jobs | Pantalla con la lista de jobs. El nombre no se traduce. Ver [Ejecución y monitoreo](usuarios/02-ejecucion-y-monitoreo.md) |
| **Panel de Smelter** | Smelter dashboard | Panel clásico de Dataverse con un gráfico circular de jobs por estado, un gráfico de barras de jobs por template y una lista de jobs |
| **Diagnóstico** | Diagnostics | Grupo del menú. Contiene **Logs de importación** |
| **Logs de importación** | Import logs | Pantalla con los errores de todas las filas fallidas de todos los jobs |
| **Active TBL \| SMLT \| …** | Active TBL \| SMLT \| … | Nombre de las vistas de sistema de cada tabla (por ejemplo, **Active TBL \| SMLT \| Import Jobs**). Se dejan sin traducir por decisión del producto |
| **Jobs activos** | Active jobs | Vista de Import Jobs con los jobs **En cola** o **Procesando** |
| **Completados con errores** | Completed with errors | Vista de Import Jobs con los jobs que tienen al menos una fila fallida |
| **Todos los jobs** | All jobs | Vista de Import Jobs sin filtro, con los más recientes primero |
| **Templates activos** | Active templates | Vista que usa la lista de templates al crear un Import Job: ofrece solo los templates en estado **Activo** |

## Templates y mappings

| Término | En inglés | Qué es |
|---|---|---|
| **Template de importación** (Template) | Import Template | La descripción completa de un tipo de archivo CSV: separador, encabezados, tamaño de bloque, estado y los mappings. Se arma una vez y se usa en todos los jobs. Ver [Conceptos](usuarios/00-conceptos.md#las-piezas-de-un-template) |
| **Mapping de tabla** | Table Mapping | Indica a qué tabla de Dataverse se escriben las filas, en qué orden y cómo se reconoce un registro existente. Hay uno por cada tabla que el archivo llena |
| **Mapping de columna** | Column Mapping | Une una columna del CSV con una columna de la tabla destino, con su tipo, su obligatoriedad, su valor por defecto y su transformación. Pertenece a un mapping de tabla |
| **Mapping de relación** | Relationship Mapping | Une dos mappings de tabla como padre e hijo, para que el registro hijo apunte al padre correcto. Pertenece al template |
| **Deduplicación** | Deduplication (dedup) | Mecanismo para no crear el mismo registro dos veces: las **Columnas de deduplicación** forman la clave que decide si el registro ya existe. Ver [Conceptos](usuarios/00-conceptos.md#deduplicación) |
| **Solo insertar** (Solo inserción) | Insert only | Opción de un mapping de tabla: siempre crea registros nuevos, sin clave. Reprocesar el archivo puede duplicar datos |
| **Actualizar si existe** | Update If Exists | Opción de un mapping de tabla. Por defecto **Sí**: si la clave ya existe, actualiza el registro. En **No**, omite la fila |
| **Crear si no existe** | Create If Not Exists | Opción de un mapping de tabla. Por defecto **Sí**: si la clave no existe, crea el registro. En **No**, omite la fila |
| **Fila omitida** | Skipped row | Fila que Smelter no escribió porque **Crear si no existe** o **Actualizar si existe** estaban en **No**. Cuenta en **Filas omitidas** y no genera log |
| **Exportar / Importar template** | Export / Import template | Guardar un template en un archivo `smelter-template.json` y volver a crearlo desde ese archivo. Ver [Exportar e importar](usuarios/03-exportar-e-importar.md) |

## Campos del template

| Término | En inglés | Qué es |
|---|---|---|
| **Nombre** | Name | Nombre del template. Smelter lo usa para decidir si, al importar un archivo, crea un template nuevo o actualiza uno existente |
| **Descripción** | Description | Texto libre para el equipo. El motor no lo usa; viaja en la exportación |
| **Tipo de origen** | Source Type | Tipo de archivo de origen. Solo existe **CSV** |
| **Tiene fila de encabezados** | Has Header Row | **Sí**: la primera fila del CSV son los nombres de las columnas. **No**: el archivo empieza con datos y las columnas se llaman `Column0`, `Column1`… |
| **Delimitador** | Delimiter | Carácter que separa las columnas del CSV. Vacío es la coma. Para un tabulador se escribe `TAB` o `\t` |
| **Tamaño de chunk** | Chunk Size | Cuántas filas procesa Smelter por bloque. Entre **100 y 10000**; vacío usa **1000**. Fuera de rango se rechaza |
| **Estado** (del template) | Status | **Borrador**, **Activo** o **Archivado**. Solo un template **Activo** se puede ejecutar |

## Campos del mapping de tabla

| Término | En inglés | Qué es |
|---|---|---|
| **Nombre** | Name | Nombre descriptivo del mapping. Solo informativo |
| **Template de importación** | Import Template | El template al que pertenece el mapping |
| **Tabla destino** | Target Table | Nombre lógico de la tabla de Dataverse que recibe las filas (por ejemplo, `contact`) |
| **Orden de procesamiento** | Processing Order | Orden en que se procesan los mappings de tabla del template: el menor primero. Los padres antes que los hijos |
| **Columnas de deduplicación** | Dedup Columns | Nombres lógicos de las columnas **destino**, separados por coma, que forman la clave del registro. La tabla necesita una clave alternativa activa sobre ellas |
| **Crear si no existe** | Create If Not Exists | Ver *Templates y mappings* |
| **Actualizar si existe** | Update If Exists | Ver *Templates y mappings* |
| **Solo inserción (permite duplicados)** | Solo inserción (permite duplicados) | Ver *Solo insertar*. Esta etiqueta se ve en español también en la interfaz en inglés |

## Campos del mapping de columna

| Término | En inglés | Qué es |
|---|---|---|
| **Nombre** | Name | Nombre descriptivo del mapping de columna. Solo informativo |
| **Mapping de tabla** | Table Mapping | El mapping de tabla al que pertenece la columna |
| **Columna de origen** | Source Column | Nombre de la columna en el CSV. No distingue mayúsculas de minúsculas. Es opcional si una transformación no lee del CSV |
| **Columna destino** | Target Column | Nombre lógico de la columna de Dataverse donde se escribe el valor (por ejemplo, `emailaddress1`) |
| **Tipo de dato** | Data Type | Tipo al que se convierte el valor antes de escribirlo. Si no se puede convertir, la fila falla con **Error de tipo** |
| **Es obligatoria** | Is Required | **Sí**: si el valor queda vacío (y no hay valor por defecto), la fila falla con **Error de validación** |
| **Valor por defecto** | Default Value | Valor que se usa cuando la celda del CSV viene vacía. Se aplica antes de revisar **Es obligatoria** |
| **Tabla del lookup** | Lookup Table | Solo para el tipo **Lookup**: nombre lógico de la tabla a la que apunta |
| **Columna de coincidencia del lookup** | Lookup Match Column | Solo para **Lookup**: columna de la tabla referenciada (una clave alternativa) por la que se busca el registro cuando el valor no es un GUID |
| **Mapeo de opciones** | Option Set Mapping | Solo para **Conjunto de opciones**: JSON que traduce el texto del CSV al número de la opción, por ejemplo `{"Activo": 1}` |
| **Expresión de transformación** | Transform Expression | Expresión que cambia el valor antes de guardarlo, por ejemplo `UPPER(Apellido)`. Tiene prioridad sobre el valor crudo del CSV |

## Tipos de dato

| Término | En inglés | Qué es |
|---|---|---|
| **Texto** | String | Texto libre. Se quitan los espacios de los extremos |
| **Entero** | Integer | Número sin decimales |
| **Decimal** | Decimal | Número con punto decimal |
| **Fecha y hora** | DateTime | Fecha o fecha con hora. Para formatos día/mes/año, se usa `DATEPARSE` |
| **Booleano** | Boolean | Acepta `true`/`false`, `1`/`0` o `yes`/`no` |
| **Lookup** | Lookup | Referencia a un registro de otra tabla, por GUID o por un valor de negocio |
| **Conjunto de opciones** | OptionSet | Lista de opciones de Dataverse. Acepta el número de la opción o texto traducido con **Mapeo de opciones** |
| **Moneda** | Money | Campo de moneda de Dataverse |
| **GUID** | GUID | Identificador único |

## Campos del mapping de relación

| Término | En inglés | Qué es |
|---|---|---|
| **Nombre** | Name | Nombre descriptivo de la relación. Solo informativo |
| **Template de importación** | Import Template | El template al que pertenece. Filtra los mappings de tabla que se pueden elegir como padre e hijo |
| **Mapping de tabla padre** | Parent Table Mapping | El mapping del lado «uno». Debe tener **Columnas de deduplicación** |
| **Mapping de tabla hija** | Child Table Mapping | El mapping del lado «muchos», el que recibe el lookup hacia el padre |
| **Tipo de relación** | Relationship Type | Solo existe **Uno a muchos** (OneToMany) |
| **Columna lookup en la tabla hija** | Lookup column on child | Nombre lógico de la columna lookup de la tabla hija que apunta al padre. Obligatoria |
| **Columnas de deduplicación del padre** | Parent Dedup Columns | Columnas **del CSV** que identifican al padre. Se emparejan por posición con las **Columnas de deduplicación** del padre y deben ser la misma cantidad |

## Import Jobs

| Término | En inglés | Qué es |
|---|---|---|
| **Import Job** (job) | Import Job | Una ejecución de importación: un template, un archivo CSV y, opcionalmente, la marca de simulación. Ver [Ejecución y monitoreo](usuarios/02-ejecucion-y-monitoreo.md) |
| **Nombre** | Name | Nombre descriptivo del job |
| **Template de importación** | Import Template | El template que define cómo se lee el archivo. Debe estar **Activo** |
| **Archivo de origen** | Source File | El CSV a importar, de hasta 128 MB. Mensajes de la app lo nombran en inglés: `'Source File'` |
| **Es simulación (Dry Run)** | Is Dry Run | **Sí**: valida formato, tipos y transformaciones de todas las filas sin escribir en Dataverse. Ver [Simulación](usuarios/02-ejecucion-y-monitoreo.md#hacer-una-prueba-con-simulación-dry-run) |
| **Total de filas** | Total Rows | Filas de datos del CSV, sin el encabezado |
| **Filas procesadas** | Processed Rows | Filas ya leídas, hayan salido bien o mal |
| **Filas exitosas** | Success Count | Filas escritas en Dataverse (creadas o actualizadas). En una simulación, las que pasaron las validaciones |
| **Filas fallidas** | Fail Count | Filas que fallaron. Su detalle está en los logs |
| **Filas omitidas** | Skipped Count | Filas salteadas por **Crear si no existe** o **Actualizar si existe** en **No**. No quedan en los logs |
| **Offset de procesamiento** | Processing Batch | Cantidad de filas de datos ya consumidas; el punto desde el que retoma el siguiente bloque |
| **Progreso** | Progreso | Porcentaje de avance (filas procesadas sobre total). Solo informativo |
| **Hora de inicio** | Starttime | Cuándo empezó el procesamiento |
| **Hora de fin** | Endtime | Cuándo terminó el procesamiento |
| **Motivo de la falla** | Failure Reason | Mensaje (en inglés) del paso que hizo fallar el job. Solo se llena si el job quedó **Fallido**; se limpia cuando el job se vuelve a procesar |
| **Ejecutar** | Ejecutar | Botón del menú **⋮** de un job que nunca corrió. Pone el job **En cola**; el procesamiento corre en el servidor |
| **Reintentar** | Reintentar | Botón del menú **⋮** de un job terminado. Vuelve a encolarlo y lo reprocesa desde cero |
| **Panel / Dashboard** | Smelter Dashboard | Ver *Panel de Smelter* |
| **Chunk** (bloque) | Chunk | Grupo de filas que el servidor procesa de una vez. Su tamaño se define en el template |

## Estados

### Estado de un job

| Término | En inglés | Qué es |
|---|---|---|
| *(vacío)* | — | Job creado que todavía no se ejecutó |
| **En cola** | Queued | Pidió ejecutarse; el servidor aún no empezó |
| **Procesando** | Processing | El servidor está leyendo el archivo y escribiendo en Dataverse |
| **Completado** | Completed | Terminó y ninguna fila falló |
| **Completado con errores** | CompletedWithErrors | Terminó, pero al menos una fila falló. Las demás se guardaron |
| **Fallido** | Failed | Falló el proceso completo. El motivo está en **Motivo de la falla** |

### Estado de un template

| Término | En inglés | Qué es |
|---|---|---|
| **Borrador** | Draft | En configuración. No se puede ejecutar |
| **Activo** | Active | Listo para usar. Es el único estado ejecutable |
| **Archivado** | Archived | Retirado. No se puede ejecutar |

## Logs y categorías de error

| Término | En inglés | Qué es |
|---|---|---|
| **Log de importación** | Import Job Log | Registro de una fila que falló. Lo escribe solo el sistema; no se crea a mano. Solo se registran filas fallidas |
| **Identificador** | ID | Identificador autonumérico del log (por ejemplo, `IJL-84874`) |
| **Número de fila** | Row Number | Número de fila **del archivo, contando la fila de encabezados**: la fila de datos 3 aparece como 4. Sin encabezado, empieza en 1. Ver [Número de fila](usuarios/02-ejecucion-y-monitoreo.md#el-número-de-fila-de-un-log) |
| **Tabla destino** | Target Table | Tabla donde falló la escritura |
| **Estado** (del log) | Status | Siempre **Fallido**: el log solo guarda filas fallidas |
| **Categoría del error** | Error Category | Etapa donde falló la fila. Ver la tabla siguiente |
| **Mensaje de error** | Error Message | Detalle del error, en inglés, con la columna implicada y la causa. Nunca incluye el valor de la celda |

| Categoría | En inglés | Cuándo ocurre |
|---|---|---|
| **Error de origen** | SourceError | La fila no se pudo leer o falló algo inesperado al armarla |
| **Error de transformación** | TransformError | Una expresión de transformación está mal escrita o falló |
| **Error de tipo** | TypeError | El valor no se puede convertir al **Tipo de dato** del mapping |
| **Error de validación** | ValidationError | Una columna **Es obligatoria** quedó vacía |
| **Lookup sin resolver** | LookupUnresolved | El lookup trae un valor que no es GUID y falta **Columna de coincidencia del lookup** |
| **Datos de destino** | TargetData | Dataverse rechazó la escritura |

## Transformaciones

| Función | Qué hace | Ejemplo |
|---|---|---|
| `UPPER` | Pasa a mayúsculas | `UPPER(Apellido)` |
| `LOWER` | Pasa a minúsculas | `LOWER(Correo)` |
| `TRIM` | Quita espacios de los extremos | `TRIM(Nombre)` |
| `CONCAT` | Junta valores | `CONCAT(Nombre, " ", Apellido)` |
| `LEFT` / `RIGHT` | Primeros / últimos *n* caracteres | `LEFT(Codigo, 3)` |
| `REPLACE` | Reemplaza un texto por otro | `REPLACE(Telefono, "-", "")` |
| `SPLIT` | Divide un texto y toma una parte (empieza en 0) | `SPLIT(NombreCompleto, " ", 0)` |
| `DATEPARSE` | Lee una fecha con el formato indicado | `DATEPARSE(Fecha, "dd/MM/yyyy")` |
| `IF` | Si el valor es igual a otro, devuelve un resultado; si no, otro | `IF(Estado, "Activo", 1, 0)` |
| `COALESCE` | Primer valor no vacío | `COALESCE(Celular, Telefono)` |
| `MAP` | Traduce un valor con un mapa JSON | `MAP(Region, "{""N"":1,""S"":2}")` |
| `STATIC` | Valor fijo, sin leer del CSV | `STATIC("Importado")` |
| `HMAC` | Código hash del valor con una clave guardada en el sistema; el segundo argumento es solo el nombre de la clave | `HMAC(Documento, "mi-clave")` |

Los detalles de uso están en [Expresión de transformación](usuarios/01-configurador.md#expresión-de-transformación).

## Roles

| Término | En inglés | Qué es |
|---|---|---|
| **Smelter Configurador** | Smelter Configurador | Rol de quien arma y administra templates, ejecuta y monitorea imports. Único rol que ve la app **Smelter — Configuración de importaciones** |
| **Smelter Operador** | Smelter Operador | Rol de quien usa Smelter desde otra app (por ejemplo, Cobranza): crea y ejecuta jobs con los templates que le compartieron. No ve la app de configuración |

## Conceptos generales

| Término | En inglés | Qué es |
|---|---|---|
| **CSV** | CSV | Archivo de texto con valores separados por un delimitador. Es el único formato de origen de Smelter |
| **Dataverse** | Dataverse | La base de datos de la plataforma donde Smelter escribe los registros |
| **Clave alternativa** | Alternate key | Índice único definido en una tabla de Dataverse sobre una o más columnas. Smelter la necesita sobre las **Columnas de deduplicación** para crear o actualizar sin duplicar. La crea quien administra Dataverse |
| **Nombre lógico** | Logical name | El nombre interno de una tabla o columna de Dataverse (por ejemplo, `contact`, `emailaddress1`). Es el que se escribe en **Tabla destino** y **Columna destino** |
| **Upsert** | Upsert | Operación que crea el registro si no existe y lo actualiza si existe. Es lo que hace Smelter con los mappings que tienen deduplicación |
| **Dry run** (simulación) | Dry run | Ejecución de prueba que valida sin escribir en Dataverse. Ver *Es simulación (Dry Run)* |
| **Padre / hijo** | Parent / child | En una relación, la tabla del lado «uno» (padre) y la del lado «muchos» (hijo), que apunta al padre con un lookup |
| **Flujo de Power Automate** (Cloud Flow) | Cloud Flow | El proceso del servidor que toma los jobs **En cola** y los procesa por bloques. Lo administra quien administra Smelter |
