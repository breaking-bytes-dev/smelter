[← Volver al índice de documentación](../README.md)

# Guía paso a paso — Export/Import de Templates en Power Platform

> Implementación de portal del feature **template-export-import**. El código C# (plugins
> `ExportTemplatePlugin` / `ImportTemplatePlugin`) ya está compilado. Esta guía es TODO lo que tenés que
> hacer en Power Platform, paso a paso.
>
> **Tiempo estimado:** 60–90 min. **Herramientas:** Plugin Registration Tool (PRT) + XrmToolBox (Ribbon Workbench).

---

## ⚠️ Antes de empezar — 3 lecciones que ya pagamos caro

1. **NO incrementes la versión del assembly** (`<Version>1.0.0</Version>` en el csproj). Con Custom APIs
   vinculadas, cambiar la versión causa el error *"Unable to delete plugintype due to steps registered"*.
   El Update Assembly reemplaza el código igual con la misma versión.
2. **Los nombres de parámetro son CASE-SENSITIVE** y deben coincidir EXACTO con el código:
   `TemplateId`, `Json`, `Warnings`. Si Dataverse no devuelve un Out, el 99% de las veces el nombre no matchea.
3. **El `operationName` del JS = el Unique Name de la Custom API** (con prefijo). Si no coincide, el botón tira NotFound.

---

## PASO 0 — Compilar el assembly

En PowerShell, en la raíz del repo:
```powershell
dotnet build src\Smelter.Plugins\Smelter.Plugins.csproj -c Release
```
El DLL queda en: `src\Smelter.Plugins\bin\Release\net462\Smelter.Plugins.dll`

Verificá la fecha (debe ser de recién):
```powershell
Get-Item src\Smelter.Plugins\bin\Release\net462\Smelter.Plugins.dll | Select-Object LastWriteTime
```

---

## PASO 1 — Desplegar el assembly (PRT)

1. Abrí **Plugin Registration Tool** → **Create New Connection** → conectá a tu entorno.
2. En la lista, ubicá **Smelter.Plugins** (ya registrado de antes).
3. Click derecho → **Update**.
4. En el diálogo, **Browse** → seleccioná `bin\Release\net462\Smelter.Plugins.dll`.
5. En la lista de tipos, asegurate de que estén tildados los DOS tipos nuevos:
   - `Smelter.Plugins.ExportTemplatePlugin`
   - `Smelter.Plugins.ImportTemplatePlugin`
6. **Update Selected Plugins**.
7. ✅ Verificación: expandí el assembly en PRT → deben aparecer los 2 tipos nuevos.

> Si NO aparecen: el DLL es viejo o apuntaste a `bin\Debug`. Recompilá y reintentá.

---

## PASO 2 — Crear los 2 Web Resources (JavaScript)

### 2.1 — Web resource de EXPORT

1. [make.powerapps.com](https://make.powerapps.com) → **Solutions** → abrí tu solución de Smelter.
2. **+ New** → **More** → **Web resource**.
3. Completá:
   - **Display name:** `Smelter Export Template`
   - **Name:** `sanic_smlt_exporttemplate.js` (el prefijo `sanic_` se agrega solo; el nombre completo queda `sanic_smlt_exporttemplate.js`)
   - **Type:** `JavaScript (JS)`
4. **Text Editor** → pegá ESTE código:

```javascript
"use strict";
var Sanic = window.Sanic || {};
Sanic.Smelter = Sanic.Smelter || {};

// ⚠️ Debe coincidir EXACTO con el Unique Name de la Custom API de export (Paso 3.1).
Sanic.Smelter.EXPORT_API = "sanic_capi_smelter_exporttemplate";

Sanic.Smelter.exportTemplate = function (selectedId) {
    var id = (selectedId || "").replace(/[{}]/g, "");
    if (!id) {
        Xrm.Navigation.openAlertDialog({ text: "Seleccioná un template para exportar." });
        return;
    }

    var request = {
        TemplateId: { guid: id },
        getMetadata: function () {
            return {
                boundParameter: null,
                operationType: 0, // 0 = Action
                operationName: Sanic.Smelter.EXPORT_API,
                parameterTypes: {
                    TemplateId: { typeName: "Edm.Guid", structuralProperty: 1 }
                }
            };
        }
    };

    Xrm.WebApi.online.execute(request)
        .then(function (response) { return response.json(); })
        .then(function (result) {
            var blob = new Blob([result.Json], { type: "application/json" });
            var url = URL.createObjectURL(blob);
            var a = document.createElement("a");
            a.href = url;
            a.download = "smelter-template.json";
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }, function (error) {
            Xrm.Navigation.openErrorDialog({ message: error.message });
        });
};
```
5. **Save** → **Publish**.

### 2.2 — Web resource de IMPORT

1. **+ New** → **More** → **Web resource**.
2. Completá:
   - **Display name:** `Smelter Import Template`
   - **Name:** `sanic_smlt_importtemplate.js`
   - **Type:** `JavaScript (JS)`
3. Pegá ESTE código:

```javascript
"use strict";
var Sanic = window.Sanic || {};
Sanic.Smelter = Sanic.Smelter || {};

// ⚠️ Debe coincidir EXACTO con el Unique Name de la Custom API de import (Paso 3.2).
Sanic.Smelter.IMPORT_API = "sanic_capi_smelter_importtemplate";

Sanic.Smelter.importTemplate = function (primaryControl) {
    var input = document.createElement("input");
    input.type = "file";
    input.accept = ".json,application/json";

    input.onchange = function (ev) {
        var file = ev.target.files[0];
        if (!file) { return; }

        var reader = new FileReader();
        reader.onload = function () {
            var request = {
                Json: reader.result,
                getMetadata: function () {
                    return {
                        boundParameter: null,
                        operationType: 0,
                        operationName: Sanic.Smelter.IMPORT_API,
                        parameterTypes: {
                            Json: { typeName: "Edm.String", structuralProperty: 1 }
                        }
                    };
                }
            };

            Xrm.WebApi.online.execute(request)
                .then(function (response) { return response.json(); })
                .then(function (result) {
                    var msg = "Template importado correctamente.";
                    if (result.Warnings && result.Warnings.length > 0) {
                        msg += "\n\nAdvertencias:\n" + result.Warnings;
                    }
                    return Xrm.Navigation.openAlertDialog({ text: msg });
                })
                .then(function () {
                    if (primaryControl && primaryControl.refresh) { primaryControl.refresh(); }
                }, function (error) {
                    Xrm.Navigation.openErrorDialog({ message: error.message });
                });
        };
        reader.readAsText(file);
    };

    input.click();
};
```
4. **Save** → **Publish**.

---

## PASO 3 — Crear las 2 Custom APIs

> En make.powerapps.com, dentro de la solución: **+ New → More → Custom API** (si no aparece, usá la tabla
> "Custom API" desde **+ New → Table row → Custom API**, o el plugin de XrmToolBox "Custom API Manager").

### 3.1 — Custom API: Export

Creá un registro **Custom API** con:
| Campo | Valor |
|-------|-------|
| **Unique Name** | `sanic_capi_smelter_exporttemplate` (debe matchear `EXPORT_API` del JS) |
| **Name** | `sanic_capi_smelter_exporttemplate` |
| **Display Name** | `Export Smelter Template` |
| **Binding Type** | `Global` |
| **Bound Entity Logical Name** | (vacío) |
| **Plugin Type** | buscá y seleccioná `Smelter.Plugins.ExportTemplatePlugin` |
| **Is Function** | `No` |
| **Enabled for Workflow** | `No` |
| **Allowed Custom Processing Step Type** | `None` |
| **Is Private** | `No` |

Luego agregá (tablas relacionadas **Custom API Request Parameter** y **Custom API Response Property**):

**Request Parameter:**
| Campo | Valor |
|-------|-------|
| **Unique Name** | `TemplateId` |
| **Name** | `TemplateId` |
| **Display Name** | `Template Id` |
| **Type** | `Guid` |
| **Is Optional** | `No` |
| **Custom API** | (el de export) |

**Response Property:**
| Campo | Valor |
|-------|-------|
| **Unique Name** | `Json` |
| **Name** | `Json` |
| **Display Name** | `Json` |
| **Type** | `String` |
| **Custom API** | (el de export) |

### 3.2 — Custom API: Import

Custom API:
| Campo | Valor |
|-------|-------|
| **Unique Name** | `sanic_capi_smelter_importtemplate` (debe matchear `IMPORT_API` del JS) |
| **Display Name** | `Import Smelter Template` |
| **Binding Type** | `Global` |
| **Plugin Type** | `Smelter.Plugins.ImportTemplatePlugin` |
| **Is Function** | `No` |
| **Is Private** | `No` |

**Request Parameter:**
| Campo | Valor |
|-------|-------|
| **Unique Name** | `Json` |
| **Type** | `String` |
| **Is Optional** | `No` |

**Response Properties (DOS):**
| Unique Name | Type |
|-------------|------|
| `TemplateId` | `Guid` |
| `Warnings` | `String` |

3.3 — **Publish all customizations**.

> Verificación rápida de las Custom APIs (opcional, en el navegador, logueado):
> `https://<tuorg>.crm.dynamics.com/api/data/v9.2/sanic_capi_smelter_exporttemplate(TemplateId=<guid>)` no —
> mejor probalo desde el botón en el Paso 5.

---

## PASO 4 — Botones en la command bar (Ribbon Workbench / XrmToolBox)

> ⚠️ **Por qué Ribbon Workbench y no el command designer moderno:** los botones necesitan abrir un selector
> de archivo (import) y disparar una descarga (export). Eso requiere **JavaScript**, y el command designer
> moderno solo soporta Power Fx (que NO accede a archivos del navegador). Ribbon Workbench es el camino.

### 4.1 — Preparar
1. Abrí **XrmToolBox** → conectá a tu entorno.
2. Abrí la herramienta **Ribbon Workbench 2021** (instalala desde el Tool Library si no la tenés).
3. Te pide elegir una **solución**: usá una solución que contenga **solo la tabla Import Template**
   (`sanic_tbl_smlt_importtemplate`). Si no tenés una chica, creá una nueva con solo esa tabla (Ribbon
   Workbench carga más rápido y evita tocar otras tablas).

### 4.2 — Botón EXPORTAR
1. En el diseñador, seleccioná la entidad **Import Template**. Vas a ver 3 zonas de ribbon: **Home, SubGrid, Form**.
2. Arrastrá un **Button** a la zona **Home** (grid principal) de la tabla.
3. Propiedades del botón:
   - **Id:** `sanic.smlt.ExportTemplate.Button`
   - **Label:** `Exportar Smelter Template`
   - **ToolTipTitle:** `Exportar`
   - (opcional) **Image16/32:** algún ícono de descarga.
4. En el panel **Commands** → **Add Command** → Id `sanic.smlt.ExportTemplate.Command`.
5. Con el comando seleccionado, **Actions** → **Add** → **JavaScript Action**:
   - **Library:** `$webresource:sanic_smlt_exporttemplate.js`
   - **Function Name:** `Sanic.Smelter.exportTemplate`
   - **Add Parameter** → **Crm Parameter** → valor: `FirstSelectedItemId`
6. **Enable Rules** → **Add Enable Rule** → agregá un **SelectionCountRule**:
   - **AppliesTo:** `SelectedEntity`
   - **Minimum:** `1`  · **Maximum:** `1`
   *(así el botón solo se habilita cuando hay exactamente 1 template seleccionado).*
7. Asigná el comando al botón: en el botón, **Command** = `sanic.smlt.ExportTemplate.Command`.

### 4.3 — Botón IMPORTAR
1. Arrastrá otro **Button** a la zona **Home** de Import Template.
2. Propiedades:
   - **Id:** `sanic.smlt.ImportTemplate.Button`
   - **Label:** `Importar Smelter Template`
3. **Add Command** → Id `sanic.smlt.ImportTemplate.Command`.
4. **Actions** → **JavaScript Action**:
   - **Library:** `$webresource:sanic_smlt_importtemplate.js`
   - **Function Name:** `Sanic.Smelter.importTemplate`
   - **Add Parameter** → **Crm Parameter** → valor: `PrimaryControl`
5. **Enable Rules:** ninguna (siempre habilitado, no necesita selección).
6. Asigná el comando al botón.

### 4.4 — Publicar
- Click en **Publish** (botón arriba a la izquierda de Ribbon Workbench). Esperá a que termine.

---

## PASO 5 — Smoke test (en UN entorno)

1. Abrí la **MDA de Smelter** → vista **Import Templates**.
2. **Importar:** el botón "Importar Smelter Template" debe estar visible sin selección. (Lo probamos en el Paso 6.)
3. **Exportar:** seleccioná 1 template (checkbox) → el botón "Exportar Smelter Template" se habilita → click →
   debe **descargar `smelter-template.json`**. Abrilo y verificá que tenga `template`, `tableMappings`
   (con `ref`), `relationshipMappings` (con `parentRef`/`childRef`).

> Si el botón da error: abrí F12 (consola del navegador) y mirá el mensaje. Casi siempre es el
> `operationName` que no coincide con el Unique Name de la Custom API, o un nombre de parámetro mal escrito.

---

## PASO 6 — Prueba End-to-End (entre 2 entornos)

1. En **entorno A** (origen): exportá un template configurado → guardá el `.json`.
2. En **entorno B** (destino), **ANTES**: asegurate de que existan las **tablas destino** del template y sus
   **alternate keys** (el JSON NO crea esquema). Si faltan, el import va a devolver Warnings.
3. En **entorno B** → vista Import Templates → **Importar Smelter Template** → elegí el `.json`.
4. Esperá el diálogo "Template importado correctamente" (+ Warnings si hay).
5. ✅ Verificá en B:
   - El template existe con su nombre.
   - Sus Table Mappings, Column Mappings y Relationship Mappings están.
   - **Las relaciones apuntan a los Table Mappings correctos** (abrí un Relationship Mapping → su Parent/Child
     deben ser los TM de ESTE entorno, GUIDs nuevos).
6. **Idempotencia:** importá el MISMO `.json` otra vez en B → NO debe duplicar (reemplaza los mappings del
   template existente por nombre).
7. **Warning de tabla faltante:** importá un `.json` cuyo `targetTable` no exista en B → debe importar el
   template igual y avisar en el diálogo qué tabla falta.

---

## PASO 7 — Troubleshooting

| Síntoma | Causa probable | Solución |
|---------|----------------|----------|
| Botón → "NotFound" | `operationName` del JS ≠ Unique Name de la Custom API | Igualá `EXPORT_API`/`IMPORT_API` al Unique Name exacto |
| Export no descarga / `result.Json` undefined | Response Property mal nombrada | El Unique Name debe ser EXACTO `Json` (case-sensitive) |
| Import no devuelve TemplateId/Warnings | Response Properties faltantes o mal nombradas | Crear `TemplateId` (Guid) y `Warnings` (String) exactos |
| "Unable to delete plugintype..." al actualizar | Cambiaste la versión del assembly | Volvé a `1.0.0`, recompilá, Update |
| El botón no aparece | Ribbon no publicado / solución equivocada en RW | Republicá en Ribbon Workbench; usá la solución con la tabla |
| Falla y no sé por qué | — | Habilitá **Plug-in Trace Log** (System Settings → Customization → All) y leé el último registro: los plugins trazan el marcador `Smelter ExportTemplate/ImportTemplate` y el stack en `FAILED` |

---

## Resumen del flujo final
```
EXPORTAR:  vista Import Templates → seleccionar 1 → "Exportar Smelter Template"
           → descarga smelter-template.json
IMPORTAR:  vista Import Templates → "Importar Smelter Template" → elegir .json
           → valida + crea/actualiza por nombre + recablea GUIDs → diálogo con Warnings → refresca grid
```
