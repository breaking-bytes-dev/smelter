# Tasks: Template Export / Import

> Strict TDD: RED antes de GREEN. Gate: `dotnet build` (acá) + `dotnet test` (usuario, Windows/CI).
> Fases 1–4 = código C#. Fase 5 = trabajo de portal (Power Platform), guía paso a paso.

## Phase 1: DTOs + TemplateSerializer (TDD)

- [x] 1.1 `Models/TemplateDto.cs`: DataContract DTOs (TemplateExportDto, TemplateInfoDto, TableMappingDto con `Ref`, ColumnMappingDto, RelationshipMappingDto con ParentRef/ChildRef).
- [x] 1.2 RED: round-trip Serialize→Deserialize equivalente (+ test que el JSON usa refs, no GUIDs). → `TemplateSerializerTests`.
- [x] 1.3 RED: Deserialize lanza con JSON malformado y con formatVersion futura ("2.0"). → `TemplateSerializerTests`.
- [x] 1.4 GREEN: `Services/TemplateSerializer.cs` (`ITemplateSerializer`) — DataContractJsonSerializer + Validate (formatVersion ≤ soportada, parentRef/childRef referencian refs existentes). + test ref huérfano.
  > GATE: `dotnet build Smelter.sln` → Build succeeded, 0/0. `dotnet test` lo corre el usuario.

## Phase 2: TemplateExporter (TDD)

- [x] 2.1 RED: `Export` arma DTO con template + table/column/relationship mappings (mock dispatch por entity name). → `TemplateExporterTests`.
- [x] 2.2 RED: refs únicos con nombres repetidos (`OnlyHaveUniqueItems`). → `TemplateExporterTests`.
- [x] 2.3 RED: relationships usan parentRef/childRef = refs de los table mappings (no GUIDs). → `TemplateExporterTests`.
- [x] 2.4 GREEN: `Services/TemplateExporter.cs` (`ITemplateExporter`) — Retrieve template + RetrieveMultiple (reusa constantes de `ImportContextLoader`), `UniqueRef` (slug + sufijo), mapa GUID→ref, columnas por table mapping.
  > GATE: `dotnet build` → 0/0.

## Phase 3: TemplateImporter (TDD)

- [x] 3.1 RED: ambiente limpio → crea template + 2 TM + 1 CM + 1 Rel. → `TemplateImporterTests`.
- [x] 3.2 RED: match por nombre existente → Update template (mismo id) + Delete de mappings viejos. → `TemplateImporterTests`.
- [x] 3.3 RED: remapeo ref→GUID (rel.parent/child Id == GUID de los TM recién creados). → `TemplateImporterTests`.
- [x] 3.4 RED: targetTable inexistente → Warning, no aborta (importa igual). → `TemplateImporterTests`.
- [x] 3.5 GREEN: `Services/TemplateImporter.cs` (`ITemplateImporter`, `ImportResult{TemplateId,Warnings}`). FindTemplateByName; create/update; DeleteExistingMappings (rel→cm→tm); refToGuid; chequeo de entidad inyectable (`Func<string,bool>`, default RetrieveEntityRequest).
  > GATE: `dotnet build` → 0/0.

## Phase 4: Custom API backings (TDD)

- [x] 4.1 RED: `ExportTemplateHandler.Run(templateId)` → serializa el árbol exportado. → `TemplateApiHandlerTests`.
- [x] 4.2 GREEN: `ExportTemplatePlugin.cs` + `ExportTemplateHandler` (In `TemplateId` → Out `Json`; SYSTEM service + tracing).
- [x] 4.3 RED: `ImportTemplateHandler.Run(json)` → deserializa+valida → importa → TemplateId+Warnings. → `TemplateApiHandlerTests`.
- [x] 4.4 GREEN: `ImportTemplatePlugin.cs` + `ImportTemplateHandler` (In `Json` → Out `TemplateId`, `Warnings`; warnings join con `\n`).
- [x] 4.5 GATE: `dotnet build Smelter.sln` → Build succeeded, 0/0. El usuario corre `dotnet test`.

## Phase 5: Power Platform (portal — guía del usuario)

- [ ] 5.1 Desplegar el assembly (PRT Update Assembly o `pac plugin push`); confirmar los 2 plugin types nuevos.
- [ ] 5.2 Crear Custom API **`sanic_ExportTemplate`** (Global, plugin `ExportTemplatePlugin`): Req `TemplateId` (Guid); Resp `Json` (String).
- [ ] 5.3 Crear Custom API **`sanic_ImportTemplate`** (Global, plugin `ImportTemplatePlugin`): Req `Json` (String); Resp `TemplateId` (Guid) + `Warnings` (String).
- [ ] 5.4 Web resource JS **export** `sanic_smlt_exporttemplate.js` (esqueleto abajo) → subir a la solución.
- [ ] 5.5 Web resource JS **import** `sanic_smlt_importtemplate.js` (esqueleto abajo) → subir a la solución.
- [ ] 5.6 Command button **"Exportar Smelter Template"** en el form/grid de Import Template (visible con 1 registro seleccionado) → acción JS `exportTemplate`.
- [ ] 5.7 Command button **"Importar Smelter Template"** en la vista de Import Template (sin selección) → acción JS `importTemplate`.
- [ ] 5.8 Prueba E2E: exportar un template en ambiente A → importar el `.json` en ambiente B (limpio) → verificar árbol + relaciones → re-importar (idempotente, no duplica) → probar Warning con tabla destino faltante.

### Esqueleto JS — export (5.4)
```javascript
function exportTemplate(selectedId) {
  var req = { TemplateId: { guid: selectedId.replace(/[{}]/g,"") },
    getMetadata: function(){ return { boundParameter:null, operationType:0, operationName:"sanic_ExportTemplate",
      parameterTypes:{ TemplateId:{ typeName:"Edm.Guid", structuralProperty:1 } } }; } };
  Xrm.WebApi.online.execute(req).then(function(r){ return r.json(); }).then(function(res){
    var blob = new Blob([res.Json], { type:"application/json" });
    var a = document.createElement("a"); a.href = URL.createObjectURL(blob);
    a.download = "smelter-template.json"; a.click(); URL.revokeObjectURL(a.href);
  }, function(e){ Xrm.Navigation.openErrorDialog({ message:e.message }); });
}
```

### Esqueleto JS — import (5.5)
```javascript
function importTemplate() {
  var input = document.createElement("input"); input.type="file"; input.accept=".json";
  input.onchange = function(ev){
    var reader = new FileReader();
    reader.onload = function(){
      var req = { Json: reader.result,
        getMetadata: function(){ return { boundParameter:null, operationType:0, operationName:"sanic_ImportTemplate",
          parameterTypes:{ Json:{ typeName:"Edm.String", structuralProperty:1 } } }; } };
      Xrm.WebApi.online.execute(req).then(function(r){ return r.json(); }).then(function(res){
        Xrm.Navigation.openAlertDialog({ text:"Importado. " + (res.Warnings||"Sin warnings.") });
      }, function(e){ Xrm.Navigation.openErrorDialog({ message:e.message }); });
    };
    reader.readAsText(ev.target.files[0]);
  };
  input.click();
}
```

## Notas
- Orden: 1 (serializer) → 2 (export) → 3 (import) → 4 (plugins) → 5 (portal). 2 y 3 son independientes entre sí.
- Aditivo: no toca PrepareImport/ProcessChunk ni el esquema. Rollback = quitar Custom APIs + web resources + commands.
- Open questions del design (slug, chequeo de entidad) se cierran en apply.
