# Plugins y Custom APIs

Este documento describe cada pieza de código que Dataverse ejecuta en Smelter: las 4 *Custom APIs* (mensajes propios respaldados por un plugin) y los 2 plugins de *message step* (código que se ejecuta dentro de una operación de guardado). Para cada una: entradas, salidas, qué escribe, transacciones y errores. Está pensado para quien va a modificar el motor, llamar a una API desde otra aplicación o interpretar un error del Plugin Trace Log.

El motor en sí (tipos de dato, DSL, claves del padre) se explica en [Motor y DSL](05-motor-y-dsl.md); el flujo completo, en [Arquitectura](01-arquitectura.md).

## Cómo funciona

Todo el código vive en un único *plugin package* de Dataverse, `sanic_Smelter.Plugins`, que contiene dos ensamblados:

| Ensamblado | Framework | Contenido |
|---|---|---|
| `Smelter.Plugins` | .NET Framework 4.6.2 (lo exige el sandbox de Dataverse) | Los 6 plugins, los servicios con acceso a Dataverse y los modelos |
| `Smelter.Core` | netstandard2.0 | Lógica pura sin dependencias del SDK: parser CSV, `TransformEngine`, `DependencyResolver`, validador y serializador de templates. Trae CsvHelper 30.0.1 |

El split permite probar el núcleo en Linux con .NET 8. Los namespaces se mantuvieron en `Smelter.Plugins.*` para no mover `using`s.

### Convenciones que comparten los seis plugins

- **Plugin y handler.** Cada clase `IPlugin` se limita a armar dependencias y delegar en un `*Handler` testable sin `IServiceProvider` (por ejemplo `PrepareImportHandler`).
- **SYSTEM.** Los servicios se crean con `factory.CreateOrganizationService(null)`, es decir, como SYSTEM. El motor no depende de los privilegios del usuario que dispara la operación.
- **`BuildMarker`.** Cada plugin escribe en el trace una línea `=== Smelter <Plugin> — BUILD <marca> ===` al empezar. Si no aparece en el Plugin Trace Log, el ensamblado no se actualizó.
- **Traza y relanza.** Un `catch` traza `Tipo: mensaje` y la pila, y vuelve a lanzar. Las `InvalidPluginExecutionException` son los mensajes que ve el usuario.
- **Sin valores de filas** en mensajes ni trazas (ver [Seguridad](03-seguridad.md#datos-sensibles-en-mensajes-y-trazas)).

### Registro

Todo se registra por SDK desde `Smelter.Deploy`, dentro de `smlt_core_smelter`:

| Qué | Cómo | Fuente de verdad |
|---|---|---|
| El package | `pluginpackage` con solo `content` (Dataverse deriva la versión del `.nuspec` y rechaza actualizar `version`) | [`Program.cs`](../../tools/Smelter.Deploy/Program.cs) |
| Plugin types | Se descubren solos al procesar el package; el deploy espera hasta 120 s | `Program.cs` |
| Custom APIs | `customapi` con parámetros y propiedades de respuesta **reemplazados de forma declarativa** (se borran y recrean desde la spec) | [`ApiSpec.cs`](../../tools/Smelter.Deploy/ApiSpec.cs) |
| Message steps | `sdkmessageprocessingstep` por clave natural, re-habilita los que alguien apagó a mano | [`StepSpec.cs`](../../tools/Smelter.Deploy/StepSpec.cs) |

Las Custom APIs se crean con `isprivate = false` (los flows leen sus parámetros del `$metadata` y `IsPrivate` no es control de acceso), `bindingtype = Global`, `isfunction = false` y `allowedcustomprocessingsteptype = None`. Los tipos de parámetro usan los valores de la plataforma: Boolean `0`, Integer `7`, String `10`, Guid `12`. El `uniquename` de cada parámetro **es la clave que lee el plugin** (`context.InputParameters["JobId"]`): nombre desnudo y sensible a mayúsculas.

| Pieza | Tipo | Registro |
|---|---|---|
| `PrepareImportPlugin` | Custom API `sanic_capi_smelter_prepareimport` | Execute privilege: Create sobre Import Job |
| `ProcessChunkPlugin` | Custom API `sanic_capi_smelter_processchunk` | Execute privilege: Create sobre Import Job |
| `ExportTemplatePlugin` | Custom API `sanic_capi_smelter_exporttemplate` | Execute privilege: Write sobre Import Template |
| `ImportTemplatePlugin` | Custom API `sanic_capi_smelter_importtemplate` | Execute privilege: Write sobre Import Template |
| `AssignJobOwnerPlugin` | Message step | `Create` de Import Job, pre-operación (20), síncrono, rank 1 |
| `ValidateQueueAccessPlugin` | Message step | `Create` de Import Job, pre-operación (20), síncrono, rank 2 |
| `ValidateQueueAccessPlugin` | Message step | `Update` de Import Job, pre-operación (20), síncrono, rank 1, columna filtro `sanic_smlt_ij_status` |

## `PrepareImportPlugin`

**Custom API `sanic_capi_smelter_prepareimport`** — «CAPI | Smelter | Prepare Import». Valida un job, cuenta sus filas y lo **reclama**, dejándolo listo para procesar. No hace staging.

| Dirección | Nombre | Tipo | Obligatorio | Descripción |
|---|---|---|---|---|
| Entrada | `JobId` | Guid | Sí | El Import Job |
| Salida | `TotalRows` | Integer | — | Filas de datos del archivo (0 si no se reclamó) |
| Salida | `Claimed` | Boolean | — | `true`: esta llamada es dueña del job y el llamador debe procesarlo. `false`: otra corrida ya lo reclamó o no está en cola; **no procesar** |

**Qué hace, en orden:**

1. Carga el contexto: job, template, Table, Column y Relationship Mappings.
2. **Gate de estado:** el template debe estar `Active`.
3. **Gate de acceso:** `InitiatingUserId` (quien llama; si es el flow, la identidad de su conexión) debe tener Read sobre el template (`RetrievePrincipalAccessRequest`, que incluye acceso por teams).
4. **Gate de idempotencia:** todo Table Mapping necesita Dedup Columns o «Solo inserción».
5. **Coherencia del template** (`TemplateValidator`): cada Dedup Column es la Target Column de algún Column Mapping del mismo mapping (salvo «Solo inserción»); un Column Mapping de tipo `Lookup` exige Lookup Table; una relación `OneToMany` exige `Lookup column on child`, un padre con Dedup Columns y tantas Parent Dedup Columns como Dedup Columns tiene el padre; `ManyToMany` no está soportado.
6. **Claim con lock de fila:** `Update` de `sanic_smlt_ij_claimlock`. Ver [Arquitectura](01-arquitectura.md#el-claim-con-lock-de-fila).
7. Lee el status. Si no es `Queued` ni vacío, devuelve `Claimed = false`, `TotalRows = 0`.
8. Descarga el CSV completo, valida los encabezados (si `HasHeaderRow`), cuenta las filas.
9. Calcula el cursor de bytes (vacío si el archivo no se puede recorrer por bytes).
10. **Borrado de los logs de la corrida anterior** (`StaleLogPurger`, 1.0.16): como SYSTEM, en lotes de 500 con `ExecuteMultiple`, con un presupuesto de **40 s**, sobre los logs del job con `modifiedon` anterior al `starttime` de esta corrida. Ver [D-32](12-decisiones.md#d-32-reintentar-borra-los-logs-de-la-corrida-anterior-por-lotes-y-con-presupuesto-de-tiempo).
11. **Reset del job** con un `Update` plano: `status = Processing`, `bytecursor`, `totalrows`, los contadores y `processingbatch` en 0, `starttime` = ahora, y `endtime` y `failurereason` en `null`.

Los pasos 2 a 5 **lanzan** si fallan: el flow marca el job `Failed`. Todas las validaciones ocurren **antes** de tomar el lock.

**Errores** (`InvalidPluginExecutionException`):

| Mensaje | Causa |
|---|---|
| `PrepareImport requires a 'JobId' input parameter.` | Falta el parámetro |
| `Import Job has no template reference.` | El job no tiene template |
| `Import Template '<nombre>' (<id>) is not Active (status=<Draft\|Archived>). Activate the template before running imports.` | Gate de estado |
| `User <id> does not have Read access to the Import Template (<id>) referenced by this Import Job. Prepare aborted.` | Gate de acceso |
| `Table Mapping '<nombre>' (<tabla>) has no dedup columns: Smelter requires an alternate key so reprocessing never duplicates. Set dedup columns or enable 'Insert only (allows duplicates)'.` | Gate de idempotencia |
| `Import Template '<nombre>' has configuration errors: <lista>` | Coherencia del template |
| `CSV is missing mapped source column(s): <lista>.` | El encabezado no trae columnas que el template mapea. Las columnas con `TransformExpression` quedan excluidas de la comprobación |

**Transacciones.** `PrepareImport` corre dentro de la transacción de la Custom API. Por eso **no atrapa** faults de `OrganizationService`: atrapar una excepción y seguir condena la transacción («ISV code reduced the open transaction count»). Es la razón del diseño del claim.

## `ProcessChunkPlugin`

**Custom API `sanic_capi_smelter_processchunk`** — «CAPI | Smelter | Process Chunk». Procesa **un** tramo del archivo.

> [!NOTE]
> El nombre original tenía una errata, `anic_capi_smelter_processchunk` (sin la `s`). Se renombró en dos pasos sin ventana rota (rename aditivo, migración del flow, borrado del registro viejo en Dev). Integraciones que usaran el nombre viejo deben migrar al `sanic_`.

| Dirección | Nombre | Tipo | Obligatorio | Descripción |
|---|---|---|---|---|
| Entrada | `JobId` | Guid | Sí | El Import Job |
| Entrada | `ChunkSize` | Integer | No | Reemplaza el tamaño del template para esta llamada. Se acota a 100–10.000 |
| Salida | `HasMore` | Boolean | — | `true` si el tramo vino lleno (quedan filas probables) |
| Salida | `ProcessedSoFar` | Integer | — | Filas del archivo leídas hasta ahora, sumando todos los chunks |

**Qué hace:**

1. Vuelve a cargar el contexto y **repite los gates** de estado del template y de idempotencia. Además exige que el **job esté en `Processing`**: `Queued` significa que `Prepare` no corrió; `Completed`, `CompletedWithErrors` y `Failed` son terminales, y reprocesar duplicaría contadores y reescribiría datos.
1b. Si el job tiene `starttime`, **continúa el borrado** de los logs de la corrida anterior que `PrepareImport` no alcanzó a borrar (presupuesto de 20 s por llamada; en el caso normal es una consulta que no devuelve nada).
2. Resuelve el tamaño del chunk: parámetro `ChunkSize` si es mayor que 0, si no el del template, si no 1000; acotado a `[100, 10000]`.
3. Lee las filas del tramo: con cursor de bytes, abre el File con un solo `InitializeFileBlocksDownload` y lee por `DownloadBlock`; sin cursor, descarga completa y saltea `offset` filas.
4. Si no hay filas, completa el job (`Completed` o `CompletedWithErrors` según `failedcount`) y devuelve `HasMore = false`.
5. Procesa el lote (`NormalizationEngine.ProcessBatch`): para cada Table Mapping en **orden de dependencias** construye los registros y los escribe (`UpsertWriter`). Una fila con la clave de dedup vacía falla antes de escribirse (ver [Motor y DSL](05-motor-y-dsl.md#resolución-de-cada-celda)).
6. Cuenta resultados, escribe los logs de las filas fallidas y avanza `processingbatch`, `bytecursor` y los contadores.
7. `HasMore = filasLeídas >= chunkSize`. Un tramo parcial marca el job `Completed` (si `failedcount = 0`) o `CompletedWithErrors` y fija `endtime`.

Si el archivo tiene exactamente un múltiplo del tamaño de chunk, hay una llamada final con cero filas que cierra el job.

**Escritura (`UpsertWriter`).** Agrupa los registros en lotes de hasta **1000** `ExecuteMultiple` con `ContinueOnError = true`. Con alternate key: `UpsertRequest` con `KeyAttributes`; sin ella y con «Solo inserción»: `CreateRequest`. Si *Create If Not Exists* o *Update If Exists* están en **No**, antes de cada lote consulta qué registros existen y emite `Create`, `Update` o una fila `Skipped`. Un fallo de Dataverse sobre un registro se informa como `Dataverse rejected the write (ErrorCode 0x<código>).`, nunca con el mensaje original.

**Logs de filas fallidas.** Se escriben con un `ExecuteMultiple` de `UpsertRequest` sobre la alternate key `(importjobid, rownumber, targettable)`, así que reprocesar un tramo es idempotente. Si la escritura de logs falla, **no interrumpe el chunk**: se traza el fallo y el job sigue.

**Errores.** Cualquier excepción del paso 1 (`... is not in Processing state (status=...). Run PrepareImport first; finished or failed jobs cannot be reprocessed.`) o del acceso al archivo se propaga y el flow reintenta con su política (4 reintentos exponenciales); agotados, marca el job `Failed`. Una fila que no se puede construir **no** lanza: se registra como fallida y el chunk sigue.

## `ExportTemplatePlugin`

**Custom API `sanic_capi_smelter_exporttemplate`** — «Export Smelter Template». Serializa un template completo a JSON portable.

| Dirección | Nombre | Tipo | Descripción |
|---|---|---|---|
| Entrada | `TemplateId` | Guid | El Import Template |
| Salida | `Json` | String | El árbol del template |

Lee el template, sus Table Mappings (ordenados por `processingorder`), los Column Mappings de cada uno y las relaciones. Como los GUID no sobreviven entre ambientes, cada Table Mapping recibe una **`ref` estable** (`tm-` + nombre en minúsculas con todo lo no alfanumérico reemplazado por `-`; si colisiona se agrega `-2`, `-3`...) y las relaciones apuntan a `parentRef` y `childRef`.

El formato actual es la **versión 1.1** (compatible con 1.0). Ejemplo ilustrativo:

```json
{
  "FormatVersion": "1.1",
  "Template": {
    "Name": "Mi template", "SourceType": 0, "HasHeaderRow": true,
    "Delimiter": ",", "ChunkSize": 0, "Description": "…", "Status": 1
  },
  "TableMappings": [
    {
      "Ref": "tm-clientes", "Name": "Clientes", "TargetTable": "contact",
      "ProcessingOrder": 1, "DedupColumns": "emailaddress1",
      "CreateIfNotExists": true, "UpdateIfExists": true, "InsertOnly": false,
      "ColumnMappings": [
        { "Name": "Email", "SourceColumn": "Email", "TargetColumn": "emailaddress1",
          "DataType": 0, "IsRequired": true, "DefaultValue": null, "LookupTable": null,
          "LookupMatchColumn": null, "OptionSetMapping": null, "TransformExpression": null }
      ]
    }
  ],
  "RelationshipMappings": []
}
```

`Description` y `Status` solo se emiten si tienen valor. `IsJunctionTable` (campo de versiones anteriores) ya no se emite, y un JSON viejo que lo traiga se importa igual: el miembro se ignora. El propio archivo no incluye datos de la instalación (ni GUID, ni dueño, ni credenciales).

## `ImportTemplatePlugin`

**Custom API `sanic_capi_smelter_importtemplate`** — «Import Smelter Template». Recrea el árbol de un template a partir de un JSON.

| Dirección | Nombre | Tipo | Descripción |
|---|---|---|---|
| Entrada | `Json` | String | El JSON exportado |
| Salida | `TemplateId` | Guid | El template creado o actualizado |
| Salida | `Warnings` | String | Advertencias no fatales, una por línea (vacío si no hay) |

**Qué hace:**

1. **Valida el JSON** antes de tocar datos (`TemplateSerializer`).
2. Busca un template **con el mismo nombre**. Si existe, lo actualiza y **borra todos sus mappings** (relaciones, luego columnas, luego tablas); si no, lo crea.
3. Crea cada Table Mapping y sus Column Mappings, guarda el mapa `ref → GUID` nuevo y crea las relaciones apuntando a los GUID del ambiente.
4. Por cada tabla destino distinta comprueba que exista en el ambiente (`RetrieveEntityRequest`) y, si falta, agrega una advertencia: `Target table '<tabla>' does not exist in this environment. Create the table and its alternate key before importing data.`

Reglas de valores:

- `SourceType` siempre se escribe como CSV (0), aunque el JSON traiga otro valor.
- `ChunkSize = 0` en el JSON significa «valor por defecto» y se guarda **vacío** (la columna tiene mínimo 100 y rechazaría el 0). En una actualización, el `null` limpia el valor anterior.
- `Description` solo se escribe si el JSON la trae (un export 1.0 no pisa la descripción existente).
- **Status:** al crear, el del JSON si viene, y si no `Active` (el gate exige `Active`). Al actualizar, el del JSON si viene; si no, se respeta el status que ya tenía.

**Errores de validación** (`InvalidPluginExecutionException`, sin tocar datos):

| Mensaje | Causa |
|---|---|
| `Template JSON is empty.` | Entrada vacía |
| `Template JSON is malformed: <detalle>` | No se puede deserializar |
| `Template JSON is invalid (missing template).` | Falta el nodo `Template` |
| `Unsupported formatVersion '<v>'. Expected 1.1 or lower.` | Versión mayor soportada |
| `Template JSON is invalid: chunkSize <n> is out of range; it must be between 100 and 10000 (or 0 for the system default).` | Chunk Size fuera de rango |
| `Template JSON is invalid: status <n> is not a valid template status (0=Draft, 1=Active, 2=Archived).` | Status inexistente |
| `Relationship '<nombre>' is ManyToMany, which is not supported; only OneToMany is.` | Relación `ManyToMany` |
| `Relationship references unknown parentRef '<ref>'.` / `... childRef '<ref>'.` | Relación huérfana |

La serialización usa `DataContractJsonSerializer` porque `System.Web.Extensions` (`JavaScriptSerializer`) no está en la lista blanca del sandbox.

## `AssignJobOwnerPlugin`

Message step **síncrono, pre-operación, `Create` de Import Job** (rank 1). Si el template del job nuevo es propiedad de un **team**, el job nace propiedad de ese mismo team.

- Como corre en pre-operación, basta con mutar el `Target`: no hay `AssignRequest` posterior ni ventana en la que el job pertenezca al creador.
- Gana sobre un `ownerid` explícito del payload.
- Si el template es de un usuario, o el job no tiene template, no toca nada.
- Lee el dueño del template como SYSTEM.

No lanza mensajes de usuario; si el template referenciado no existe, el `Retrieve` falla y se propaga.

## `ValidateQueueAccessPlugin`

Message step **síncrono, pre-operación** en `Create` (rank 2) y en `Update` filtrado por `sanic_smlt_ij_status` (rank 1) del Import Job. Cuando la operación deja el job en `Queued`, la **persona** que la inicia (`InitiatingUserId`) debe poder leer el template del job.

1. Si el `Target` no pone `status = Queued`, no hace nada.
2. En `Update`, el `Target` solo trae lo que cambió, así que lee el job persistido (status y lookup al template).
3. **Guarda de re-encolado:** si el job persistido está en `Processing`, rechaza antes del chequeo de acceso. Re-encolar un job en curso dispararía otra corrida del flow cuyo claim lo vería `Queued` y lo resetearía a mitad de camino. `Queued → Queued`, los estados finales y el status vacío (reintento) siguen permitidos.
4. Si no hay template, no valida (lo rechaza `PrepareImport` más tarde).
5. Comprueba Read con `RetrievePrincipalAccessRequest`.

| Mensaje | Causa |
|---|---|
| `El Import Job se está procesando: no se puede volver a poner en cola hasta que termine.` | El job está `Processing` |
| `No tenés acceso al Import Template de este job: no se puede poner en cola.` | La persona no puede leer el template |

> [!NOTE]
> Estos dos mensajes están en español rioplatense y se muestran al usuario tal cual. El resto de los mensajes del motor está en inglés.

## Cómo se modifica

**Agregar una Custom API.**

1. Escribir el plugin y su `*Handler` en `src/Smelter.Plugins/`, siguiendo las convenciones de arriba, con sus pruebas primero (`tests/Smelter.Plugins.Tests/Plugins/`).
2. Declararla en [`ApiSpec.cs`](../../tools/Smelter.Deploy/ApiSpec.cs) con `UniqueName`, `PluginTypeName`, parámetros, propiedades de respuesta y `ExecutePrivilegeName`, reutilizando un privilegio existente.
3. Subir `<Version>` en `Smelter.Plugins.csproj`.
4. Correr `backend-deploy` y `backend-verify`; publicar un Release para que `solution-sync` actualice el espejo. Hasta entonces `CustomApiContractTests` falla en rojo para ese `UniqueName`: es el estado esperado de esa ventana.

**Agregar un message step.** Una entrada en [`StepSpec.cs`](../../tools/Smelter.Deploy/StepSpec.cs) (mensaje, tabla, etapa, modo, rank, columnas filtro) y su aserción en `StepAssertions`. El provisioner crea el step con clave natural, y `Smelter.Verify` comprueba etapa, modo, rank y que esté habilitado.

**Cambiar el comportamiento de un plugin existente.** Subir la versión del package y el `BuildMarker`.

## Trampas y errores conocidos

- **Sin subir la versión no hay recarga.** Dataverse cachea el package por versión. Sin el cambio, algunos workers del sandbox siguen con el código viejo: en 1.0.5 la carga fallaba de forma intermitente con `Unknown function: HMAC`.
- **Los nombres de los parámetros son sensibles a mayúsculas** y son la clave que lee el plugin.
- **Un parámetro nuevo no existe para el Web API** hasta publicar las customizaciones: `Smelter.Deploy` ejecuta `PublishAllXml` al final por esa razón.
- **`ProcessChunk` no es idempotente con «Solo inserción».** Un reintento del mismo tramo vuelve a hacer `Create`.
- **Atrapar un fault del servicio dentro de `PrepareImport` arruina la transacción.** No agregues `try/catch` alrededor de llamadas a `OrganizationService` en el camino del claim.
- **El gate de acceso de `PrepareImport` evalúa a la identidad del flow.** El control sobre la persona es `ValidateQueueAccess`.
- **El JSON de export no es un respaldo completo.** No incluye dueño del template, ni jobs, ni la clave HMAC.

## Preguntas abiertas

- **Atomicidad de `ImportTemplate`.** El comentario de `PrepareImport` indica que los plugins de Custom API corren dentro de la transacción de la llamada, lo que debería deshacer un import fallido a mitad de camino. El repositorio no tiene una prueba E2E de ese comportamiento sobre `ImportTemplate`.
- **Plantillas con el mismo nombre.** El import usa el primer template que coincide con el nombre; el repositorio no impide dos templates con el mismo nombre en la tabla.
