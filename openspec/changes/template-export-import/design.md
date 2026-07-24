# Design: Template Export / Import

## Technical Approach

Dos Custom APIs backing-plugin (mismo patrón que `PrepareImport`/`ProcessChunk`) que manejan solo **strings**.
Un `TemplateSerializer` con DTOs serializa/deserializa el árbol con **refs estables**. El export lee el árbol
y emite JSON; el import valida, borra-y-recrea por nombre, y recablea GUIDs por `ref`. El archivo (descarga /
file picker) lo maneja el navegador via 2 web resources JS. Implementa `template-portability`.

## Architecture Decisions

| Decisión | Elegido | Alternativa | Rationale |
|----------|---------|-------------|-----------|
| Vínculo en el JSON | `ref` estable por TableMapping; relationships por parentRef/childRef | GUIDs crudos | Los GUIDs no sobreviven entre ambientes; el ref permite recablear en destino |
| Generación del ref | `slug(name)` + índice si colisiona (ej. `tm-cliente`, `tm-cliente-2`) | GUID origen como ref | Legible en el JSON y estable; evita colisión por nombres repetidos |
| Serialización | `DataContractJsonSerializer` (System.Runtime.Serialization.Json) | System.Web.Extensions (no whitelisted); helper propio | Disponible en sandbox; menos código que un parser propio |
| Conflicto por nombre | Borrar mappings del existente + recrear (upsert) | Crear siempre nuevo / fallar | El usuario lo pidió: sync idempotente entre ambientes |
| Manejo de archivo | Web resource JS (Blob download / input file) | Power Fx; columna File; tabla utilitaria | Power Fx no accede a file picker ni descargas; JS es el único camino para ese UX |
| Validación | En `ImportTemplate` antes de tocar datos | En el cliente JS | El plugin es la fuente de verdad; el JSON puede venir de cualquier lado |

## Data Flow

```
EXPORT:  Botón JS ─ Xrm.WebApi execute ExportTemplate(TemplateId) ─► plugin lee árbol ─► TemplateSerializer.Serialize ─► JSON string
         ◄── JSON ── JS crea Blob ── descarga .json

IMPORT:  Botón JS ─ <input file> ─ FileReader.readAsText ─ execute ImportTemplate(Json) ─► plugin
            └─► Deserialize+Validate ─► (match por nombre) borrar mappings viejos / crear template
                ─► crear TableMappings (GUIDs nuevos) ─► mapa ref→GUID ─► crear ColumnMappings + RelationshipMappings
            ◄── TemplateId + Warnings ── JS notifica + refresca grid
```

## File Changes

| Archivo | Acción | Descripción |
|---------|--------|-------------|
| `src/Smelter.Plugins/Models/TemplateDto.cs` | Create | DTOs `[DataContract]`: TemplateExportDto, TableMappingDto (con `Ref`), ColumnMappingDto, RelationshipMappingDto (parentRef/childRef), formatVersion |
| `src/Smelter.Plugins/Services/TemplateSerializer.cs` | Create | `Serialize(tree)→json` / `Deserialize(json)→dto` + validación (formatVersion, refs consistentes) |
| `src/Smelter.Plugins/Services/TemplateExporter.cs` | Create | Lee Template+mappings de Dataverse → arma DTO con refs |
| `src/Smelter.Plugins/Services/TemplateImporter.cs` | Create | DTO → borrar-por-nombre/crear → recrea árbol, remapea ref→GUID, junta Warnings |
| `src/Smelter.Plugins/ExportTemplatePlugin.cs` | Create | Backing `sanic_ExportTemplate` (In TemplateId, Out Json) |
| `src/Smelter.Plugins/ImportTemplatePlugin.cs` | Create | Backing `sanic_ImportTemplate` (In Json, Out TemplateId, Warnings) |
| `webresources/sanic_smlt_exporttemplate.js` | Create | command bar: execute + Blob download |
| `webresources/sanic_smlt_importtemplate.js` | Create | command bar: input file + execute + refresh |
| `ImportContextLoader` | Reuse | Referencia de logical names / lectura de mappings |

## Interfaces / Contracts

```csharp
// Logical names (existentes): tabla/columnas
// TableMapping: sanic_smlt_tm_name, _targettable, _processingorder, _dedupcolumns,
//   _isjunctiontable, _createifnotexists, _updateifexists, _importtemplateid
// ColumnMapping: sanic_smlt_cm_name, _sourcecolumn, _targetcolumn, _datatype, _isrequired,
//   _defaultvalue, _lookuptable, _lookupmatchcolumn, _optionsetmapping, _transformexpression, _tablemappingid
// RelationshipMapping: sanic_smlt_rm_parenttablemappingid, _childtablemappingid, _relationshiptype,
//   _lookupcolumnonchild, _parentdedupcolumns, _importtemplateid

public interface ITemplateSerializer {
    string Serialize(TemplateExportDto dto);
    TemplateExportDto Deserialize(string json);   // throws on malformed / unsupported formatVersion
}
public interface ITemplateExporter { TemplateExportDto Export(Guid templateId); }
public interface ITemplateImporter {
    ImportResult Import(TemplateExportDto dto);    // ImportResult { Guid TemplateId; List<string> Warnings; }
}
```
- **Ref**: `slug(tableMapping.name)`; si colisiona, sufijo `-N`. Map en import: `Dictionary<string,Guid> refToGuid`.
- **formatVersion**: `"1.0"`. Import rechaza `major` mayor a la soportada.
- **EntityExists(targetTable)**: el importer chequea metadata (`RetrieveEntity` o intento controlado) → si falta, agrega Warning, no aborta.

## Testing Strategy

| Capa | Qué | Cómo |
|------|-----|------|
| Unit | `TemplateSerializer` round-trip (serialize→deserialize == original) | DTO en memoria; assert igualdad |
| Unit | Deserialize rechaza JSON malformado / formatVersion futura | strings inválidos → exception |
| Unit | `TemplateExporter` arma refs únicos (nombres repetidos) | mock IOrganizationService devuelve mappings |
| Unit | `TemplateImporter` remapea ref→GUID (relationships apuntan a nuevos TM) | mock service captura Creates; assert lookups |
| Unit | Import por nombre: existe → borra+recrea; no existe → crea | mock RetrieveMultiple con/sin match |
| Unit | targetTable faltante → Warning, no aborta | mock metadata sin la entidad |

Strict TDD: test primero. NSubstitute + FluentAssertions. `dotnet test` lo corre el usuario.

## Migration / Rollout

Aditivo: sin migración de datos ni cambio de esquema. Desplegar = assembly + 2 Custom APIs + 2 web resources +
2 command buttons en la MDA. Rollback = quitar esos componentes de la solución.

## Open Questions

- [ ] ¿`slug` translitera acentos/espacios o solo normaliza a minúsculas+guiones? (arrancar simple: minúsculas, no-alfanumérico→`-`).
- [ ] Chequeo de existencia de `targetTable`: `RetrieveEntityRequest` (limpio) vs intento de Retrieve (más barato). Definir en apply.
