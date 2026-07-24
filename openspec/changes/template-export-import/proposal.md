# Proposal: Template Export / Import (portabilidad de config)

## Intent

Hoy un Import Template y todo su árbol de mappings se configura a mano en cada ambiente. Migrar esa config
(dev → test → prod) es tedioso y propenso a error. Este cambio permite **exportar un template completo a un
archivo JSON e importarlo en otro ambiente** desde la MDA, resolviendo el problema de que los GUIDs no
sobreviven entre ambientes. Es portabilidad de configuración (ALM), sin tocar el motor de import.

## Scope

### In Scope
- Custom API `sanic_ExportTemplate` (In: TemplateId; Out: Json) — serializa el árbol a JSON.
- Custom API `sanic_ImportTemplate` (In: Json; Out: TemplateId, Warnings) — valida, recablea GUIDs, upsert por nombre.
- Serializador/deserializador del árbol con **refs estables** (slug) en vez de GUIDs.
- Validación del JSON en import (bien formado, formatVersion soportada, refs consistentes).
- 2 JavaScript web resources para la command bar: **Exportar** (descarga .json) e **Importar** (file picker).

### Out of Scope
- El motor de import de datos (`PrepareImport`/`ProcessChunk`) — NO se toca, es aditivo.
- Exportar/crear el **esquema** (tablas destino + alternate keys): el JSON lleva solo CONFIG; el esquema debe preexistir en destino.
- Seguridad/roles (es el cambio `smelter-security`, separado).
- Versionado/historial de exports.

## Capabilities

### New Capabilities
- `template-portability`: exportar un Import Template (con table/column/relationship mappings) a JSON e importarlo en otro ambiente, remapeando GUIDs y haciendo upsert por nombre.

### Modified Capabilities
- None.

## Approach

Dos Custom APIs backing-plugin (mismo patrón que las existentes), que solo manejan **strings** — el archivo
lo maneja el navegador (JS). El export recorre el árbol y emite JSON donde cada TableMapping tiene un **`ref`**
estable; los RelationshipMappings referencian `parentRef`/`childRef` en vez de GUIDs. El import deserializa,
valida, **borra el template existente por nombre (y sus mappings) o crea uno nuevo**, recrea TableMappings,
arma un mapa `ref → GUID-nuevo`, y recrea los RelationshipMappings con los GUIDs del destino. Serialización
con `DataContractJsonSerializer` o helper propio (sandbox .NET 4.6.2; `System.Web.Extensions` no whitelisted).

## Affected Areas

| Área | Impacto | Descripción |
|------|---------|-------------|
| `src/Smelter.Plugins/TemplateSerializer.cs` (nuevo) | New | DTOs + serialize/deserialize del árbol con refs |
| `src/Smelter.Plugins/ExportTemplatePlugin.cs` (nuevo) | New | Backing de `sanic_ExportTemplate` |
| `src/Smelter.Plugins/ImportTemplatePlugin.cs` (nuevo) | New | Backing de `sanic_ImportTemplate` (validación + remapeo + upsert) |
| `ImportContextLoader` | Reused | Reutilizar la lógica de lectura de mappings si aplica |
| Web resources JS (export/import) | New | command bar de la MDA |
| Custom API defs + assembly | New | registro en la solución |

## Risks

| Riesgo | Prob. | Mitigación |
|--------|-------|------------|
| Refs duplicados/ambiguos al exportar | Med | Generar refs únicos (slug + índice); validar unicidad en import |
| JSON de versión futura/incompatible | Low | `formatVersion`; import rechaza versiones no soportadas con Warning claro |
| Tabla destino referenciada no existe en destino | Med | Import valida y devuelve Warnings (no crea esquema); el usuario crea tablas+alt keys antes |
| Tamaño del string param de la Custom API | Low | Config = KB a cientos de KB; dentro de límites |
| Replace pisa cambios locales en destino | Med | Documentar; el upsert por nombre es intencional (sync) |

## Rollback Plan

Es aditivo: nuevas Custom APIs + web resources, sin tocar tablas ni el motor. Rollback = desregistrar las 2
Custom APIs, quitar los plugin types nuevos y los web resources de la solución, revertir el commit. No hay
migración de datos ni cambio de esquema que deshacer. Los templates existentes quedan intactos.

## Dependencies

- Las tablas de config (Import Template + mappings) y sus columnas ya existen.
- En el ambiente destino, las tablas destino del template + sus alternate keys deben existir antes de importar datos.

## Success Criteria

- [ ] Exportar un template genera un `.json` descargable con todo su árbol (template + mappings + relaciones).
- [ ] Importar ese `.json` en un ambiente limpio recrea el template idéntico, con las relaciones padre-hijo bien recableadas (GUIDs nuevos).
- [ ] Re-importar el mismo `.json` (mismo nombre) actualiza el existente sin duplicar (idempotente).
- [ ] El import valida y reporta Warnings claros (JSON inválido, versión no soportada, tabla destino faltante).
- [ ] Tests xUnit cubren serialize/deserialize y el remapeo ref→GUID.
