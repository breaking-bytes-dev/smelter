[← Volver al índice de documentación](../README.md)

# Build & Deploy

> Cómo compilar el assembly, desplegarlo en Dataverse y verificar que el deploy fue exitoso.

---

## Prerequisitos

| Herramienta | Versión mínima | Para qué |
|-------------|---------------|----------|
| .NET SDK | 6.x o superior | Compila `net462` vía reference assemblies |
| Plugin Registration Tool (PRT) | Compatible con SDK 9.0 | Actualizar el assembly en Dataverse |
| `pac` CLI (Power Platform CLI) | Cualquier versión reciente | Alternativa al PRT para `pac plugin push` |

El proyecto usa `Microsoft.NETFramework.ReferenceAssemblies` para poder compilar `net462` en cualquier OS (incluyendo Linux/macOS) sin tener el .NET Framework completo instalado.

---

## Compilar

```bash
dotnet build src\Smelter.Plugins\Smelter.Plugins.csproj -c Release
```

El DLL de deploy queda en:

```
src\Smelter.Plugins\bin\Release\net462\Smelter.Plugins.dll
```

Para compilación de desarrollo (Debug):

```bash
dotnet build src\Smelter.Plugins\Smelter.Plugins.csproj -c Debug
```

DLL en `bin\Debug\net462\Smelter.Plugins.dll`.

También se puede compilar toda la solución:

```bash
dotnet build Smelter.sln -c Release
```

---

## Desplegar

### Opción A — Plugin Registration Tool (PRT)

1. Abrir el Plugin Registration Tool.
2. Conectarse al ambiente destino.
3. Localizar el assembly `Smelter.Plugins` en el árbol.
4. Botón derecho → **Update** (no registrar de nuevo — ya tiene los plugin types y steps registrados).
5. En el diálogo de actualización, apuntar al DLL recién compilado: `bin\Release\net462\Smelter.Plugins.dll`.
6. Confirmar. El PRT reemplaza el IL del assembly manteniendo todos los plugin types y steps.

### Opción B — pac CLI

```bash
pac plugin push --pluginFile src\Smelter.Plugins\bin\Release\net462\Smelter.Plugins.dll
```

Requiere estar autenticado (`pac auth`). El comportamiento es equivalente al Update del PRT.

---

## Verificar el deploy

El Plugin Trace Log es la fuente de verdad. Cada plugin de Smelter traza un marcador de build al inicio de su ejecución:

- **PrepareImport:** `=== Smelter PrepareImport — BUILD chunked-v2 (no-probe, with-tracing) ===`
- **ProcessChunk:** `=== Smelter ProcessChunk — BUILD chunked-v2 (tracing) ===`
- **ExportTemplate:** `=== Smelter ExportTemplate (chunked-v2) ===`
- **ImportTemplate:** `=== Smelter ImportTemplate (chunked-v2) ===`

Si estos marcadores no aparecen en el Plugin Trace Log después de invocar la Custom API correspondiente, el assembly no se actualizó. Revisar si el PRT apuntó al DLL correcto.

---

## Gotchas

### No incrementar la versión del assembly

El `Smelter.Plugins.csproj` tiene `<Version>1.0.0</Version>`. **No cambiar este número.**

Cuando hay Custom APIs con plugin types registrados, Dataverse vincula cada step al tipo por nombre e identidad del assembly. Si se incrementa la versión, Dataverse trata el assembly actualizado como un assembly nuevo con tipos nuevos, y los pasos del assembly anterior quedan huérfanos. El resultado es el error:

```
Unable to delete plugintype due to steps registered
```

Para romper ese ciclo hay que desvincular los steps manualmente antes de borrar el tipo — es una operación destructiva. **La versión del assembly se mantiene en 1.0.0 indefinidamente.** El PRT hace Update (no Create), lo que reemplaza el IL sin cambiar los identificadores registrados.

---

### Usar el DLL de la configuración correcta

El PRT no sabe qué configuración compilaste. Si el último build fue en Debug y desplegás el DLL de Release que existía de una compilación anterior, podés estar desplegando código viejo.

Flujo seguro:

1. `dotnet build ... -c Release` — compilar.
2. Verificar el timestamp de `bin\Release\net462\Smelter.Plugins.dll` antes de abrir el PRT.
3. Apuntar el PRT a ese DLL.

El marcador de build en el Plugin Trace Log confirma que el deploy fue exitoso; si el marcador no aparece, el DLL no cambió o el PRT apuntó al archivo equivocado.

---

### El build NO requiere Windows

`dotnet build` con `Microsoft.NETFramework.ReferenceAssemblies` compila `net462` en Linux y macOS. El assembly resultante es idéntico al que se produciría en Windows. Solo la ejecución de los tests requiere un runtime compatible (ver [Testing](testing.md)).

---

### Los binarios del directorio `publish` no son los que deployás

`dotnet publish` genera una subcarpeta `publish` con solo las DLLs necesarias para redistribución (sin las de desarrollo). Para el deploy del plugin se usa el DLL de `bin\Release\net462\`, no el de `publish`. El PRT necesita el assembly standalone, no un conjunto publicado.
