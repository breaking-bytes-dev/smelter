[← Volver al índice de documentación](../README.md)

# Smelter — Data Model

## Overview

Smelter usa 6 tablas Dataverse organizadas en dos grupos:

- **Tablas de configuración** (4): Templates reutilizables que definen cómo fluyen los datos del origen al destino.
- **Tablas de runtime** (2): Seguimiento de ejecución y log por job.

No existe ninguna tabla de staging. El procesamiento opera directamente sobre el CSV descargado desde
la columna `File` del Import Job.

## Diagrama ER

```
┌──────────────────────────────────────────────────────────────────────┐
│                        IMPORT TEMPLATE (IT)                          │
│              sanic_tbl_smlt_importtemplate                           │
└───────┬────────────────────────┬──────────────────┬──────────────────┘
        │ 1:N                    │ 1:N              │ 1:N
        ▼                        ▼                  ▼
┌─────────────────┐   ┌────────────────────┐  ┌──────────────────────┐
│  TABLE MAPPING  │   │ RELATIONSHIP MAP.  │  │    IMPORT JOB (IJ)   │
│      (TM)       │   │      (RM)          │  │ sanic_tbl_smlt_      │
│ sanic_tbl_smlt_ │   │ sanic_tbl_smlt_    │  │ importjob            │
│ tablemapping    │   │ relationshipmapping│  └──────────┬───────────┘
└────┬────────────┘   │ parentTableMapping ├──┐         │ 1:N
     │ 1:N            │ childTableMapping  ├──┤         ▼
     ▼                └────────────────────┘  │ ┌──────────────────────┐
┌─────────────────┐                           │ │  IMPORT JOB LOG (IJL)│
│ COLUMN MAPPING  │◄──────────────────────────┘ │ sanic_tbl_smlt_      │
│      (CM)       │  (TM referenciado como       │ importjoblog         │
│ sanic_tbl_smlt_ │   parent o child en RM)      └──────────────────────┘
│ columnmapping   │
└─────────────────┘
```

### Relaciones 1:N

| Padre | Hijo | Columna FK en el hijo |
|-------|------|-----------------------|
| Import Template | Table Mapping | `sanic_smlt_tm_importtemplateid` |
| Import Template | Relationship Mapping | `sanic_smlt_rm_importtemplateid` |
| Import Template | Import Job | `sanic_smlt_ij_importtemplateid` |
| Table Mapping | Column Mapping | `sanic_smlt_cm_tablemappingid` |
| Import Job | Import Job Log | `sanic_smlt_ijl_id` (alternate key, lookup al job) |

Relationship Mapping además contiene dos lookups hacia Table Mapping:
- `sanic_smlt_rm_parenttablemappingid` — tabla padre en el DAG
- `sanic_smlt_rm_childtablemappingid` — tabla hija en el DAG

## Tablas de Configuración

### [Import Template](tables/import-template.md)

Entidad raíz de configuración. Define el formato del archivo fuente y actúa como padre de todos los mappings.

**Logical name**: `sanic_tbl_smlt_importtemplate` | **Prefijo de columnas**: `sanic_smlt_it_`

### [Table Mapping](tables/table-mapping.md)

Define qué tablas Dataverse recibirán datos. Cada template puede apuntar a múltiples tablas (normalización).

**Logical name**: `sanic_tbl_smlt_tablemapping` | **Prefijo de columnas**: `sanic_smlt_tm_`

### [Column Mapping](tables/column-mapping.md)

Mapea columnas individuales del CSV a columnas Dataverse con tipo, defaults y expresiones de transform.

**Logical name**: `sanic_tbl_smlt_columnmapping` | **Prefijo de columnas**: `sanic_smlt_cm_`

### [Relationship Mapping](tables/relationship-mapping.md)

Define cómo se relacionan las tablas destino entre sí (1:N, N:M). Usado por el DAG resolver para determinar
el orden de procesamiento y resolver lookups padre→hijo.

**Logical name**: `sanic_tbl_smlt_relationshipmapping` | **Prefijo de columnas**: `sanic_smlt_rm_`

## Tablas de Runtime

### [Import Job](tables/import-job.md)

Un registro por ejecución de import. Almacena el archivo CSV, contadores de progreso y estado.

**Logical name**: `sanic_tbl_smlt_importjob` | **Prefijo de columnas**: `sanic_smlt_ij_`

### [Import Job Log](tables/import-job-log.md)

Log detallado de filas fallidas. Solo se registran las filas que fallaron (no las exitosas). Tiene una
alternate key para escritura idempotente al reintentar un chunk.

**Logical name**: `sanic_tbl_smlt_importjoblog` | **Prefijo de columnas**: `sanic_smlt_ijl_`

## Convención de Nombres

| Elemento | Patrón | Ejemplo |
|----------|--------|---------|
| Logical name de tabla | `sanic_tbl_smlt_{nombre}` | `sanic_tbl_smlt_importtemplate` |
| Logical name de columna | `sanic_smlt_{siglas}_{columna}` | `sanic_smlt_it_name` |
| Siglas por tabla | Abreviatura 2–3 letras | IT, TM, CM, RM, IJ, IJL |
