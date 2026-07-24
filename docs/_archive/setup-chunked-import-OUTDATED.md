[← Volver al índice de documentación](../README.md)

# Smelter — Guía de Setup (modelo chunked)

> Configuración de portal/plataforma para el rediseño `chunked-import-redesign`.
> Cubre lo que **no es código**: esquema Dataverse, alternate keys, Custom APIs, plugin registration y el Cloud Flow.
> El código (plugins + servicios) se despliega con el assembly `Smelter.Plugins`.

## Visión general del flujo

```
Canvas App ── crea Import Job (status=Queued) + sube archivo ──► Dataverse
                                                                    │ (trigger: row added/modified, status=Queued)
                                                                    ▼
                                                              Cloud Flow (Power Automate)
                                                                │ 1. sanic_PrepareImport(JobId)         → TotalRows
                                                                │ 2. Do until HasMore == false:
                                                                │      sanic_ProcessChunk(JobId, 1000)  → HasMore, ProcessedSoFar
                                                                ▼
                                                              Import Job (progreso) + Staging
```

---

## 1. Prerrequisitos

- Una **solución no administrada** con su publisher (prefijo `sanic`).
- El assembly **`Smelter.Plugins`** registrado en la solución (Plugin Registration Tool o `pac plugin push`).
- Las tablas de configuración (Import Template, Table/Column/Relationship Mapping, Import Job, Import Job Log) ya existentes.
- **Cuenta de ejecución**: un **application user** (service principal) dedicado para la connection del flow — ver §6.

---

## 2. Tabla de staging — `sanic_tbl_smlt_importstagingrow`

Crear la tabla con estas columnas (logical names EXACTOS — el código depende de ellos):

| Columna | Tipo | Notas |
|---------|------|-------|
| `sanic_smlt_isr_importjobid` | Lookup → Import Job | dueño del lote; usado para filtrar/borrar |
| `sanic_smlt_isr_rownumber` | Whole Number | orden estable; **único por job** (watermark del offset) |
| `sanic_smlt_isr_sheetname` | Single Line Text | nombre de hoja (Excel); vacío para CSV |
| `sanic_smlt_isr_payload` | Multiple Lines of Text | fila serializada como JSON (`{"col":"val",...}`) |

> El `payload` se serializa con `StagingPayloadSerializer` (JSON plano string→string). Subí el límite de
> caracteres del campo multiline si esperás filas anchas (default ~1 MB alcanza para una fila típica).

---

## 3. Alternate Keys en las tablas DESTINO

Este es el corazón del nuevo modelo: la idempotencia (upsert) y la resolución de lookups padre dependen de
**claves alternativas** en tus tablas destino.

1. Por cada **Table Mapping**, definí una **Alternate Key** en la tabla destino cuyas columnas sean exactamente
   las **DedupColumns** configuradas en ese mapping.
   - Ej.: tabla `country`, DedupColumns = `sanic_code` → Alternate Key sobre `sanic_code`.
   - Ej.: tabla `city`, DedupColumns = `sanic_name,sanic_stateid` → Alternate Key compuesta.
2. Por cada **Relationship Mapping** (padre→hijo), la tabla **padre** debe tener una Alternate Key sobre sus
   DedupColumns; el `ParentDedupColumns` (columnas del archivo) provee los valores.
3. Esperá a que la **Alternate Key quede activa** (Dataverse crea un índice asíncrono; el portal muestra el estado).

> Sin Alternate Key, el `UpsertWriter` degrada a Create/Update por existencia (NO idempotente) y lo registra
> en el trace. Definir las keys es lo que habilita el upsert idempotente y los lookups por valor de negocio.

---

## 4. Custom APIs

Registrar dos Custom APIs (tabla `customapi`), cada una respaldada por su plugin.

### 4.1 `sanic_PrepareImport`
- **Binding type**: Global (Unbound)
- **Plugin type**: `Smelter.Plugins.PrepareImportPlugin`
- **Is Function / Is Private**: No / No
- **Request parameters**:

| Nombre | Tipo | Requerido |
|--------|------|-----------|
| `JobId` | Guid | Sí |

- **Response properties**:

| Nombre | Tipo |
|--------|------|
| `TotalRows` | Integer |

### 4.2 `sanic_ProcessChunk`
- **Binding type**: Global (Unbound)
- **Plugin type**: `Smelter.Plugins.ProcessChunkPlugin`
- **Request parameters**:

| Nombre | Tipo | Requerido |
|--------|------|-----------|
| `JobId` | Guid | Sí |
| `ChunkSize` | Integer | No (default 1000 en el plugin) |

- **Response properties**:

| Nombre | Tipo |
|--------|------|
| `HasMore` | Boolean |
| `ProcessedSoFar` | Integer |

> **Nota de compatibilidad:** si tu entorno no ofrece el tipo **Guid** en parámetros de Custom API, definí
> `JobId` como **String** y ajustá los plugins para `Guid.Parse((string)context.InputParameters["JobId"])`.

> Los backing plugins de Custom API **no** se registran como SDK message steps: se vinculan en la definición
> del Custom API (campo *Plugin Type*). El assembly sí debe estar en la solución.

---

## 5. Cloud Flow (Power Automate)

1. **Trigger**: *When a row is added or modified* (Dataverse) sobre **Import Job**, filtro de columna
   `sanic_smlt_ij_status` = **Queued** (valor 0). Alternativa: trigger manual invocado desde la Canvas App.
2. **Acción — Prepare**: *Perform an unbound action* → `sanic_PrepareImport`
   - `JobId` = id del Import Job del trigger.
   - (Guardá `TotalRows` si querés mostrarlo.)
3. **Acción — Do until** (`HasMore` es `false`):
   - Dentro: *Perform an unbound action* → `sanic_ProcessChunk`
     - `JobId` = id del Import Job
     - `ChunkSize` = 1000 (ajustable; ver §7)
   - La salida `HasMore` controla el loop.
   - **Límites del Do until**: subí *Count* (p. ej. 5000) y *Timeout* (p. ej. `PT24H`) acorde al volumen.
4. (Opcional) Acción final: notificar / actualizar la Canvas App. El estado ya queda en el Import Job
   (`Completed` / `CompletedWithErrors`).

> El **offset** lo maneja el plugin internamente (campo `sanic_smlt_ij_processingbatch` = último row number
> procesado). El flow NO necesita pasar offset — solo repetir `ProcessChunk` hasta `HasMore = false`.

---

## 6. Cuenta de ejecución y límites de API

- Configurá la **connection del flow** con un **application user / service principal dedicado**, NO una cuenta
  personal. Los application users consumen de un **pool de requests separado del tenant** y no saturan cuentas
  interactivas licenciadas.
- Los plugins corren como **SYSTEM** (el código usa `CreateOrganizationService(null)`) y, por correr en el
  sandbox, las operaciones están **exentas de los service protection limits** (la ráfaga de 5 min).
- El **entitlement diario** (24 h) sí cuenta por operación CRUD: dimensioná el application user para el volumen.

---

## 7. Tuning del tamaño de chunk

- Cada `ProcessChunk` es un plugin → **límite de 2 minutos**. El `ChunkSize` debe procesarse holgado dentro de eso.
- Default **1000**. Si tus filas tienen muchos lookups/transforms y se acercan al límite, bajalo (500/250).
- Como cada invocación es un request fresco, **el `depth` se reinicia** y no hay techo de continuaciones.

---

## 8. Checklist de despliegue (ALM)

- [ ] Tabla `sanic_tbl_smlt_importstagingrow` + sus 4 columnas en la solución.
- [ ] Alternate Keys en las tablas destino (activas).
- [ ] Custom APIs `sanic_PrepareImport` y `sanic_ProcessChunk` con sus parámetros.
- [ ] Assembly `Smelter.Plugins` registrado y vinculado a las Custom APIs.
- [ ] Cloud Flow + connection reference (application user).
- [ ] (Si quedaba) desregistrar el step async viejo del `ImportProcessorPlugin` (eliminado del código).

## 9. Verificación

1. Crear un Import Template con sus Table/Column/Relationship Mappings.
2. Definir las Alternate Keys correspondientes (§3) y esperar a que estén activas.
3. Crear un Import Job (status=Queued) con un CSV chico (p. ej. `Smelter_Test_500.csv`).
4. Confirmar: el flow corre, el Import Job pasa a `Processing` y luego `Completed`, y los registros
   aparecen normalizados en las tablas destino con sus lookups resueltos.
5. **Reprocesar el mismo archivo** → no debe duplicar (upsert idempotente por Alternate Key).
