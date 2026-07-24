[← Volver al índice de documentación](../README.md)

# Custom APIs — Smelter

Smelter expone cuatro Custom APIs con binding **Global** (no vinculadas a ninguna tabla). Todas son
ejecutadas por su plugin de respaldo (`backing plugin`) y corren en modo **SYSTEM**.

Los nombres de parámetro son **case-sensitive** — deben usarse exactamente como se documentan aquí,
tanto al invocarlos desde Power Automate como desde `Xrm.WebApi.execute` en web resources.

## Resumen

| Custom API | Unique Name | Plugin de respaldo |
|------------|-------------|-------------------|
| Prepare Import | `sanic_PrepareImport` | `PrepareImportPlugin` |
| Process Chunk | `sanic_ProcessChunk` | `ProcessChunkPlugin` |
| Export Template | `sanic_ExportTemplate` | `ExportTemplatePlugin` |
| Import Template | `sanic_ImportTemplate` | `ImportTemplatePlugin` |

---

## sanic_PrepareImport

Valida el archivo CSV, cuenta las filas de datos y resetea el estado del Import Job a `Processing`.
Se llama **una sola vez** por job, antes de iniciar el loop de chunks.

**Binding**: Global | **Plugin**: `PrepareImportPlugin`

### Parámetros de entrada

| Nombre | Tipo | Requerido | Descripción |
|--------|------|-----------|-------------|
| `JobId` | Guid | Sí | ID del Import Job a preparar |

### Propiedades de salida

| Nombre | Tipo | Descripción |
|--------|------|-------------|
| `TotalRows` | Integer | Número de filas de datos en el CSV (sin contar el encabezado si `hasheaderrow = true`) |

### Comportamiento interno

1. Carga la configuración completa del template via `ImportContextLoader`.
2. Descarga el CSV desde `sanic_smlt_ij_sourcefile`.
3. Valida que todas las columnas fuente mapeadas existan en el encabezado del CSV.
4. Cuenta las filas de datos.
5. Actualiza el Import Job: `status = Processing`, `totalrows = N`, `processingbatch = 0`, `processedrows = 0`, `successcount = 0`, `failedcount = 0`, `skippedcount = 0`, `starttime = now`.

---

## sanic_ProcessChunk

Procesa un lote de filas del CSV. Descarga el archivo, salta al offset actual, lee `chunkSize` filas,
las procesa en orden DAG y avanza el offset. Se llama en loop hasta que `HasMore = false`.

**Binding**: Global | **Plugin**: `ProcessChunkPlugin`

### Parámetros de entrada

| Nombre | Tipo | Requerido | Descripción |
|--------|------|-----------|-------------|
| `JobId` | Guid | Sí | ID del Import Job a procesar |
| `ChunkSize` | Integer | No | Override del tamaño del chunk para esta invocación. Si se omite o es 0, se usa `sanic_smlt_it_chunksize` del template; si ese también es 0, se usa el default del sistema (1000). Clamped al rango 100–10000. |

### Propiedades de salida

| Nombre | Tipo | Descripción |
|--------|------|-------------|
| `HasMore` | Boolean | `true` si quedan filas por procesar; `false` si el job terminó |
| `ProcessedSoFar` | Integer | Total de filas procesadas acumuladas (incluyendo este chunk) |

### Comportamiento interno

1. Carga el contexto del job (incluye el offset actual desde `sanic_smlt_ij_processingbatch`).
2. Descarga el CSV.
3. Lee el rango `[offset, offset+chunkSize)` en streaming (sin materializar filas anteriores).
4. Para cada tabla en orden de `ProcessingOrder` (ascendente):
   - Aplica transforms DSL, coerciona tipos, realiza upsert por alternate key.
5. Loguea solo las filas fallidas en `ImportJobLog` (via `ExecuteMultipleRequest` + upsert idempotente).
6. Actualiza el job con el nuevo offset, contadores y status.
7. Retorna `HasMore = (filas leídas >= chunkSize)`.

---

## sanic_ExportTemplate

Serializa un Import Template completo (con su árbol de mappings) a JSON portable.
Diseñado para migrar templates entre ambientes.

**Binding**: Global | **Plugin**: `ExportTemplatePlugin`

### Parámetros de entrada

| Nombre | Tipo | Requerido | Descripción |
|--------|------|-----------|-------------|
| `TemplateId` | Guid | Sí | ID del Import Template a exportar |

### Propiedades de salida

| Nombre | Tipo | Descripción |
|--------|------|-------------|
| `Json` | String | JSON portable del template con todo su árbol de mappings |

### Notas

- Las relaciones en el JSON usan `ref` estables (slug del nombre) en lugar de GUIDs; los GUIDs no
  sobreviven entre ambientes.
- Formato serializado: `DataContractJsonSerializer` con `formatVersion: "1.0"`.
- La descarga del archivo `.json` desde el navegador la maneja un web resource JS.

---

## sanic_ImportTemplate

Recrea un Import Template en el ambiente destino a partir del JSON exportado. Usa upsert por nombre:
si ya existe un template con el mismo nombre, sus mappings se reemplazan.

**Binding**: Global | **Plugin**: `ImportTemplatePlugin`

### Parámetros de entrada

| Nombre | Tipo | Requerido | Descripción |
|--------|------|-----------|-------------|
| `Json` | String | Sí | JSON exportado por `sanic_ExportTemplate` |

### Propiedades de salida

| Nombre | Tipo | Descripción |
|--------|------|-------------|
| `TemplateId` | Guid | ID del Import Template creado o actualizado en este ambiente |
| `Warnings` | String | Advertencias separadas por `\n` (ej. tabla destino no encontrada en el ambiente). Vacío si no hay advertencias. |

### Notas

- El plugin valida el JSON antes de tocar datos; si el `formatVersion` es mayor al soportado, falla con excepción.
- El proceso de import reasigna GUIDs: los `ref` del JSON se traducen a IDs reales del ambiente destino.
- Si una `targettable` no existe en el ambiente destino, se agrega un `Warning` pero el import continúa.

## Volver a

- [Plugins](plugins.md)
- [Architecture](../01-overview/architecture.md)
