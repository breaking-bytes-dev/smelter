[← Volver al índice de documentación](../README.md)

# Connections — Smelter

## Connection Reference

El Cloud Flow orquestador de Smelter usa una única Connection Reference hacia Dataverse, vinculada al
**application user dedicado** de Smelter.

| Propiedad | Valor |
|-----------|-------|
| Tipo de conexión | Dataverse |
| Connection Reference (nombre sugerido) | `sanic_smlt_cr_dataverse` |
| Autenticación | Service Principal (application user) |

## Application User Dedicado

### Qué es

Un **application user** es un usuario de Dataverse asociado a un Azure AD App Registration (Service
Principal), sin licencia de usuario. No consume un asiento de usuario licenciado.

### Por qué se usa uno dedicado para Smelter

1. **Pool de requests separado**: el entitlement de API de Dataverse tiene un límite diario de
   requests (el número varía según el plan de licenciamiento del tenant). Un application user
   pertenece al pool de la organización, **separado del pool personal de los usuarios licenciados**.
   Si un import masivo consume muchos requests, no satura las cuotas de los usuarios humanos.

2. **Sin suplantación accidental**: el flow no corre "como" ningún usuario de negocio — corre como
   una identidad técnica propia de Smelter, lo que hace más predecible el comportamiento de seguridad.

3. **Facilita la rotación de credenciales**: el secreto del App Registration es independiente de los
   usuarios humanos. Rotar el secreto no afecta a nadie más.

### Privilegios necesarios

El application user debe tener asignado un **rol de seguridad** con los siguientes permisos mínimos:

| Tabla | Permisos | Motivo |
|-------|----------|--------|
| Import Job (`sanic_tbl_smlt_importjob`) | Read, Write | Leer config, actualizar estado/contadores |
| Import Template (`sanic_tbl_smlt_importtemplate`) | Read | Cargar config del template |
| Table Mapping (`sanic_tbl_smlt_tablemapping`) | Read | Cargar mappings |
| Column Mapping (`sanic_tbl_smlt_columnmapping`) | Read | Cargar mappings |
| Relationship Mapping (`sanic_tbl_smlt_relationshipmapping`) | Read | Cargar mappings DAG |
| Import Job Log (`sanic_tbl_smlt_importjoblog`) | Create, Write | Escribir logs de errores |
| Tablas destino (negocio) | Create, Read, Write | Ejecutar upserts de los registros importados |
| File Column (sobre Import Job) | Read | Descargar el CSV fuente |

> Las tablas destino (de negocio) varían según el template. El administrador debe asegurarse de que
> el application user tenga permisos de escritura sobre cada tabla que Smelter pueda poblar.

## Service Protection Limits — Aclaración Importante

Hay dos contextos de ejecución con comportamiento diferente respecto a los límites de Dataverse:

### Invocaciones del Cloud Flow → Dataverse (sujetas a limits)

Las llamadas que el **Cloud Flow** hace hacia Dataverse (invocar las Custom APIs) son requests de API
estándar y **sí están sujetas a service protection limits**:
- Si el ambiente recibe demasiadas solicitudes en poco tiempo, Dataverse responde con HTTP 429.
- El **retry policy exponencial** configurado en la acción `ProcessChunk` del flow maneja este escenario
  automáticamente.

### Operaciones internas del plugin → Dataverse (exentas de limits)

Las operaciones que el **plugin** ejecuta dentro del sandbox (los `UpsertRequest` de filas, las
lecturas de configuración, la escritura de logs) están **exentas de service protection limits**.
Esto está documentado por Microsoft:
> "Requests made from within a plug-in or custom workflow activity are not counted against API limits."
> — [Microsoft Learn: API limits](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/api-limits)

Por lo tanto:
- Un import de 100.000 filas que genera 300.000 upserts internos **no consume** entitlement de requests
  del application user por esas operaciones internas.
- El **entitlement diario sí cuenta** para las invocaciones externas (cada llamada al flow, cada
  invocación a las Custom APIs desde el flow).

## Volver a

- [Cloud Flows](cloud-flows.md)
- [Architecture](../01-overview/architecture.md)
