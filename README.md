# Smelter

> **Mineral crudo entra, metal refinado sale.**

Normalizador de **CSV → Dataverse** sobre Power Platform: toma archivos planos denormalizados y los
carga en tablas Dataverse correctamente normalizadas, mediante **configuración declarativa** (sin
código por import), procesamiento **por lotes idempotente** que escala a 100k+ filas, y
**portabilidad de la configuración** entre ambientes.

## Estructura del repositorio

```
smelter/
├── src/Smelter.Plugins/       # Backend (C# .NET 4.6.2): Custom APIs + motor de normalización
│   ├── Models/                #   Modelos y DTOs
│   └── Services/              #   Servicios (parser CSV, motor, upsert, template export/import…)
├── tests/
│   ├── Smelter.Plugins.Tests/ # Unit tests (xUnit + NSubstitute) — ejecutan en Windows (net462)
│   └── data/                  # CSVs de prueba (500 / 5k / 100k filas)
├── docs/                      # 📖 Documentación por audiencia → ver docs/README.md
│   ├── 01-overview/           #   Qué es + arquitectura
│   ├── 02-solution-reference/ #   Referencia de la solución (tablas, Custom APIs, flows…)
│   ├── 03-developer-guide/    #   Codebase, build & deploy, testing, Transform DSL
│   ├── 04-admin-guide/        #   Configurar templates, alternate keys, export/import, seguridad
│   ├── 05-user-guide/         #   La app (MDA), correr imports, monitoreo
│   └── 06-examples/           #   Ejemplo end-to-end (cobranza)
└── openspec/                  # Artefactos SDD (proposals, specs, designs, tasks por cambio)
```

## Por dónde empezar

- **Toda la documentación** → **[docs/README.md](docs/README.md)** (índice navegable por rol:
  desarrollador, administrador, operador).
- **¿Qué es y cómo funciona?** → [docs/01-overview/what-is-smelter.md](docs/01-overview/what-is-smelter.md).
- **Configurar un import nuevo** → [docs/04-admin-guide/new-import-end-to-end.md](docs/04-admin-guide/new-import-end-to-end.md).

## Componentes

| Pieza | Stack | Ubicación |
|-------|-------|-----------|
| Motor de import (4 Custom APIs) | C# / .NET 4.6.2, Dataverse plugins | `src/Smelter.Plugins/` |
| App de administración/operación | Model-Driven App | Solución Power Platform |
| Orquestador | Power Automate (Cloud Flow) | Solución Power Platform |
| Configuración | 6 tablas Dataverse (templates + mappings + jobs + logs) | Solución Power Platform |

## Cómo funciona (en una pantalla)

1. Un **Import Template** define, con configuración declarativa, cómo repartir cada fila del CSV en
   varias tablas: Table Mappings, Column Mappings (con tipos y [Transform DSL](docs/03-developer-guide/transform-dsl.md))
   y Relationship Mappings (lookups padre→hijo por valor de negocio).
2. El operador crea un **Import Job**, sube el CSV y lo ejecuta desde la app.
3. Un **Cloud Flow** orquesta: `PrepareImport` (valida y cuenta) + `ProcessChunk` en bucle — cada
   lote es una invocación independiente (sin límite de tiempo ni de profundidad del sandbox).
4. La escritura es **upsert por alternate key** → idempotente: reprocesar o reanudar nunca duplica.
5. Las filas que fallan quedan en el **Import Job Log**, categorizadas por origen del error.

Claves de diseño: solo CSV · 100% Power Platform (sin Azure) · el motor corre como SYSTEM ·
[limitaciones conocidas](docs/01-overview/architecture.md#limitaciones-conocidas-y-configuración-no-soportada-todavía).

## Build y tests

```bash
dotnet build Smelter.sln              # compila en cualquier OS (reference assemblies)
dotnet test  Smelter.sln              # ejecutar requiere Windows/.NET Framework (net462)
```

Ver [Build & Deploy](docs/03-developer-guide/build-and-deploy.md) y [Testing](docs/03-developer-guide/testing.md).

## Licencia

Propietario — SANIC. Todos los derechos reservados.
