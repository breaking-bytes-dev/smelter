[← Volver al índice de documentación](../README.md)

# Smelter — Diseño de la Model-Driven App

> App "Smelter Admin" sobre las 6 tablas. Enfoque: **MDA pura** (forms, vistas, dashboards, command bar
> con Power Fx). Reintentar = re-correr completo (idempotente). Se construye en el maker portal.
>
> Prerrequisitos: las tablas, el plugin, las Custom APIs, el Cloud Flow y las **alternate keys** ya
> deben existir (ver `TODO-deployment-v2.md`). La app NO reemplaza eso — lo opera.

## Tablas y rol en la app

| Tabla | Logical name | Rol |
|-------|--------------|-----|
| Import Template | `sanic_tbl_smlt_importtemplate` | Configuración (raíz) |
| Table Mapping | `sanic_tbl_smlt_tablemapping` | Config (hija de Template) |
| Column Mapping | `sanic_tbl_smlt_columnmapping` | Config (hija de Table Mapping) |
| Relationship Mapping | `sanic_tbl_smlt_relationshipmapping` | Config (hija de Template) |
| Import Job | `sanic_tbl_smlt_importjob` | Ejecución + monitoreo |
| Import Job Log | `sanic_tbl_smlt_importjoblog` | Errores |

---

## Sitemap (navegación)

```
Área: Smelter
├── Grupo "Configuración"
│     └── Import Templates          (entrada principal de config)
├── Grupo "Ejecución y Monitoreo"
│     ├── Smelter Dashboard         (default — gráficos)
│     └── Import Jobs
└── Grupo "Diagnóstico"
      └── Import Job Logs
```

---

## CASO 1 — Configurar templates

### Form: Import Template (Main)
- **Sección General**: `Name`, `Source Type` (= CSV), `Has Header Row`, `Delimiter`, `Chunk Size`.
- **Sub-grid "Table Mappings"** (relación 1:N por `sanic_smlt_tm_importtemplateid`).
- **Sub-grid "Relationship Mappings"** (1:N por `sanic_smlt_rm_importtemplateid`).
- **Tab "Ayuda"** (web resource HTML o texto): recordatorio — *"Cada tabla destino y cada padre necesita
  su Alternate Key activa sobre sus Dedup Columns. Sin eso, el import falla con TargetData."*

### Form: Table Mapping (Main)
- `Target Table`, `Processing Order`, `Dedup Columns` (⚠️ deben coincidir con la alternate key de la tabla destino),
  `Create If Not Exists`, `Update If Exists`, `Is Junction Table`.
- **Sub-grid "Column Mappings"** (1:N por `sanic_smlt_cm_tablemappingid`).

### Form: Column Mapping (Quick Create + Main)
- `Source Column`, `Target Column`, `Data Type`, `Is Required`, `Default Value`,
  `Lookup Table`, `Lookup Match Column`, `OptionSet Mapping`, `Transform Expression`.
- Quick Create activado → agregar columnas rápido desde el sub-grid.

### Form: Relationship Mapping (Quick Create + Main)
- `Parent Table Mapping`, `Child Table Mapping`, `Relationship Type`, `Lookup Column On Child`,
  `Parent Dedup Columns`.

> **Business rule sugerida**: si `Source Type` = CSV, mostrar `Delimiter`; ocultar lo de Excel (ya no aplica).

---

## CASO 2 — Monitorear

### Dashboard "Smelter Dashboard" (default de la app)
- **Gráfico** "Jobs por Estado" (Import Job, por `Status`, conteo) — pie o barras.
- **Gráfico** "Jobs por Template" (por `Import Template`, conteo).
- **Gráfico** "Errores por Categoría" (Import Job Log, por `Error Category`, conteo).
- **Lista** "Jobs en Proceso" (vista filtrada `Status = Processing`, con progreso).

### Vistas de Import Job
| Vista | Filtro | Columnas |
|-------|--------|----------|
| Jobs Activos | Status ∈ {Queued, Processing} | Name, Template, Status, TotalRows, ProcessedRows, % |
| Completados con Errores | Status = CompletedWithErrors | Name, Template, FailedCount, SuccessCount, EndTime |
| Todos los Jobs | — (orden CreatedOn desc) | Name, Template, Status, Total/Processed, Start/End |

> **Progreso %**: agregá una **columna calculada** `Progress` = `processedrows / totalrows * 100`
> (o mostralo en el dashboard). En MDA pura, una columna calculada es lo más simple.

---

## CASO 3 — Ejecutar

### Form: Import Job (Main)
- **Sección Setup**: `Name`, `Import Template` (lookup), `Source File` (columna File → acá se sube el CSV), `Is Dry Run`.
- **Sección Progreso** (read-only): `Status`, `Total Rows`, `Processed Rows`, `Success`, `Failed`, `Skipped`, `Start/End Time`.
- **Sección Resumen**: `Error Summary` (multiline, read-only).

### Command button "▶ Ejecutar" (command bar del Import Job)
Valida que haya template + archivo, y pone el Job en `Queued` (el Cloud Flow se dispara solo con ese status).
**Power Fx (command):**
```powerfx
If(
    IsBlank(Self.Selected.Item.'Import Template') || IsBlank(Self.Selected.Item.'Source File'),
    Notify("Falta el Template o el archivo CSV.", NotificationType.Error),
    Patch(
        'Import Jobs', Self.Selected.Item,
        { Status: 'Status (Import Jobs)'.Queued }
    )
)
```
- **Visibilidad**: mostrar solo si `Status` ∈ {borrador/null, Failed, CompletedWithErrors}.

---

## CASO 4 — Errores y Reintentar

### En el form de Import Job — Tab "Errores"
- **Sub-grid "Import Job Logs"** (1:N por `sanic_smlt_ijl_importjobid`), vista filtrada `Status = Failed`.
- Columnas: `Row Number`, `Target Table`, `Error Category`, `Error Message`.

### Command button "↻ Reintentar"
Re-corre el import completo. Gracias a la idempotencia (upsert por alternate key), lo ya cargado se
actualiza y lo fallido se reintenta — sin duplicar. Resetea offset y contadores y vuelve a `Queued`.
**Power Fx (command):**
```powerfx
Patch(
    'Import Jobs', Self.Selected.Item,
    {
        Status: 'Status (Import Jobs)'.Queued,
        'Processing Batch': 0,
        'Processed Rows': 0, 'Success Count': 0, 'Failed Count': 0, 'Skipped Count': 0,
        'Error Summary': Blank()
    }
)
```
- **Visibilidad**: mostrar solo si `Status` ∈ {Completed, CompletedWithErrors, Failed}.

> Nota: `PrepareImport` igual resetea estos campos al arrancar, pero limpiarlos acá da feedback inmediato
> en la UI. El Cloud Flow se dispara por el cambio a `Queued`.

### Vista global "Import Job Logs"
- Agrupable/filtrable por `Error Category` (TypeError, TargetData, LookupUnresolved, etc.).
- Útil para ver patrones: si hay 5.000 `LookupUnresolved`, falta una alternate key en un padre.

---

## Guía de construcción (orden en el maker portal)

1. **Solución**: usá la solución de Smelter (la misma del plugin/Custom APIs).
2. **Forms**: editá los Main forms de cada tabla con las secciones/sub-grids de arriba. Activá Quick Create en Column Mapping y Relationship Mapping.
3. **Columna calculada** `Progress` en Import Job (opcional).
4. **Vistas**: creá las vistas de Import Job y la de Logs por categoría.
5. **Gráficos**: creá los 3 charts (jobs por estado, por template, errores por categoría).
6. **Dashboard**: armá "Smelter Dashboard" con los charts + la lista de jobs en proceso.
7. **App (MDA designer)**: New → Model-driven app → agregá las 6 tablas, el dashboard, y armá el **sitemap** de arriba.
8. **Command bar**: en Import Job, agregá los comandos **Ejecutar** y **Reintentar** con el Power Fx de arriba y sus reglas de visibilidad.
9. **Publicá** y probá el ciclo completo: crear template → crear job → subir CSV → Ejecutar → ver progreso → revisar errores → Reintentar.

## Notas
- Los nombres entre comillas en Power Fx (`'Import Jobs'`, `'Status (Import Jobs)'.Queued`) usan los
  **display names** — ajustalos a los tuyos. Los valores del choice `Status` deben coincidir con tu OptionSet
  (Queued/Processing/Completed/CompletedWithErrors/Failed).
- Todo esto es **config**, va en la solución → ALM-friendly, se exporta entre ambientes.
- Para una UI más visual (barra de progreso animada, drag&drop del archivo) podrías sumar después un
  Canvas embebido o un PCF — pero esta MDA pura ya cubre los 4 casos de punta a punta.
