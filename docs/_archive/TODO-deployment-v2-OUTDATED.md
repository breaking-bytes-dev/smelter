[← Volver al índice de documentación](../README.md)

# Smelter v2 — TODO manual (portal + ejecución)

> Tareas que **NO son código C#** (eso ya está hecho y compila en verde). Esto es lo que hacés vos en
> el maker portal, la solución y tu entorno. Orden recomendado: A → B → C → D.
> Prerrequisito: el assembly `Smelter.Plugins` compilado (`dotnet build` ✅).

---

## A. Esquema Dataverse

### [ ] A.1 — Campo `chunkSize` en Import Template
- **Qué:** una columna para configurar el tamaño de chunk por template.
- **Cómo:** Maker portal → tabla **Import Template** (`sanic_tbl_smlt_importtemplate`) → Columns → New.
  - Display name: `Chunk Size` · Logical name: **`sanic_smlt_it_chunksize`** · Type: **Whole Number** · Min 0.
- **Nota:** el plugin usa default 1000 si queda en 0, y lo acota a [100, 10000]. Calibralo por template
  midiendo (templates anchos/multi-tabla → más chico; livianos → más grande).

### [ ] A.2 — Columna `Error Category` en Import Job Log
- **Qué:** clasifica cada fila fallida por origen.
- **Cómo:** tabla **Import Job Log** (`sanic_tbl_smlt_importjoblog`) → Columns → New.
  - Display name: `Error Category` · Logical name: **`sanic_smlt_ijl_errorcategory`** · Type: **Choice** (OptionSet local).
  - Valores EXACTOS (label : value):
    | Label | Value |
    |-------|-------|
    | SourceError | 0 |
    | TransformError | 1 |
    | TypeError | 2 |
    | ValidationError | 3 |
    | LookupUnresolved | 4 |
    | TargetData | 5 |
    | Transient | 6 |

### [ ] A.3 — Alternate Key en Import Job Log (log idempotente)
- **Qué:** permite el upsert del log → reanudar no duplica.
- **Cómo:** tabla **Import Job Log** → **Keys** → New key.
  - Columnas: `sanic_smlt_ijl_importjobid` + `sanic_smlt_ijl_rownumber` + `sanic_smlt_ijl_targettable`.
  - **Esperá a que el estado de la key sea `Active`** (Dataverse crea el índice asíncrono) antes de probar.

### [ ] A.4 — Alternate Keys en las tablas DESTINO
- **Qué:** habilitan el upsert idempotente y la resolución de lookups por valor de negocio.
- **Cómo:** por cada Table Mapping, en su **tabla destino** → Keys → New key con las columnas que
  configuraste en `DedupColumns` de ese mapping.
  - Ej.: `country` → key sobre `sanic_code`. `city` → key sobre `sanic_name` (+ lo que aplique).
  - Para los lookups padre: la tabla **padre** debe tener su alternate key (la usa `EntityReference` por KeyAttributes).
  - **Esperá `Active` en todas** antes de correr un import.

### [ ] A.5 — (Si existía) eliminar la tabla de staging
- Si en el v1 creaste `sanic_tbl_smlt_importstagingrow`, ya no se usa. Podés eliminarla (no guarda datos de negocio).

---

## B. Custom APIs + assembly

### [ ] B.1 — Desplegar el assembly `Smelter.Plugins`
- **Cómo:** Plugin Registration Tool → Update Assembly, o `pac plugin push`. Confirmá que aparecen los
  tipos `Smelter.Plugins.PrepareImportPlugin` y `Smelter.Plugins.ProcessChunkPlugin`.

### [ ] B.2 — Custom API `sanic_PrepareImport`
- **Cómo:** crear/actualizar el registro de **Custom API**.
  - Unique name: `sanic_PrepareImport` · Binding: **Global** · Plugin type: **`Smelter.Plugins.PrepareImportPlugin`**.
  - Request param: `JobId` (**Guid**, Required).
  - Response property: `TotalRows` (**Integer**).
- **Nota:** si tu entorno no ofrece el tipo `Guid`, usá `String` y ajustá el plugin (`Guid.Parse`).

### [ ] B.3 — Custom API `sanic_ProcessChunk`
  - Unique name: `sanic_ProcessChunk` · Binding: **Global** · Plugin type: **`Smelter.Plugins.ProcessChunkPlugin`**.
  - Request params: `JobId` (**Guid**, Required), `ChunkSize` (**Integer**, Optional — override; si se omite usa el del template).
  - Response props: `HasMore` (**Boolean**), `ProcessedSoFar` (**Integer**).
- **Importante:** los backing plugins de Custom API **NO** se registran como SDK message steps — se vinculan
  en el campo *Plugin Type* de la Custom API. El assembly sí va en la solución.

---

## C. Cloud Flow (Power Automate)

### [ ] C.1 — Crear el flow orquestador
- **Trigger:** *When a row is added or modified* (Dataverse) sobre **Import Job**, filtro
  `sanic_smlt_ij_status` = **Queued** (0).
- **Paso 1:** *Perform an unbound action* → `sanic_PrepareImport`, `JobId` = id del trigger.
- **Paso 2 — Do until** (`HasMore` es `false`):
  - Dentro: *Perform an unbound action* → `sanic_ProcessChunk`, `JobId` = id del job (no pases `ChunkSize`,
    sale del template).
  - Configurá los **límites del Do until**: Count alto (p.ej. 5000), Timeout largo (p.ej. `PT24H`).

### [ ] C.2 — Retry policy + manejo de error
- En la acción `ProcessChunk`: Settings → **Retry Policy** = Exponential, Count ~4. (Seguro: el chunk es idempotente.)
- Envolvé el `Do until` en un **Scope**; agregá un segundo scope con *Configure run after* = **has failed**
  que actualice el Import Job a status **Failed** con el error. (Resume: re-disparar continúa desde el offset.)

### [ ] C.3 — Connection con application user
- La connection de Dataverse del flow debe usar un **application user / service principal dedicado**
  (NO una cuenta personal) → consume del pool de requests separado del tenant.

---

## D. Ejecución y verificación

### [ ] D.1 — Correr los tests
- **Por qué no acá:** el proyecto es **net462**; ejecutar requiere **.NET Framework (Windows)** o **Mono**.
  El build ya está verde; falta validar la lógica ejecutando.
- **Cómo:** en Windows / tu CI → `dotnet test Smelter.sln` (o `dotnet test` con vstest). En Linux: instalar Mono.

### [ ] D.2 — Verificación funcional (import real)
- [ ] Crear template + mappings + relationship mappings, con las alternate keys (A.4) **activas**.
- [ ] Subir `Smelter_Test_500.csv` (chico) y crear un Import Job (status Queued).
- [ ] Confirmar: el flow corre, el job pasa a `Processing` → `Completed`, los registros aparecen
      **normalizados** con sus lookups resueltos.
- [ ] **Header inválido:** subir un CSV al que le falte una columna mapeada → debe fallar en `PrepareImport`
      con mensaje claro (no a mitad del proceso).
- [ ] **Idempotencia:** reprocesar el mismo archivo → NO duplica registros ni logs.
- [ ] **Volumen:** `Smelter_Load_Test_100k.csv` → completa sin tocar depth/timeout; los fallidos quedan
      en Import Job Log con su `Error Category`.

---

## Notas del modelo v2 (para entender qué estás probando)
- **Sin staging:** cada `ProcessChunk` descarga el CSV, parsea en streaming, salta al offset y procesa su tajada.
- **Solo CSV** (Excel eliminado).
- **Offset** = filas de datos ya procesadas (campo `sanic_smlt_ij_processingbatch`); avanza DESPUÉS de procesar → resume seguro.
- **chunkSize** por template; el flow es agnóstico.
- **Log** solo de filas fallidas, idempotente, categorizado.
- **Transient interno** (SQL deadlock/timeout) diferido — el service protection lo cubre el retry de PA.
