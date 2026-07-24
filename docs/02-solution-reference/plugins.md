[← Volver al índice de documentación](../README.md)

# Plugins — Smelter

## Assembly

**`Smelter.Plugins`** — .NET Framework 4.6.2, compilado para sandbox de Dataverse.

El assembly contiene exactamente cuatro plugin types, uno por Custom API. No hay steps de mensaje
registrados (ni pre-operation, ni post-operation). Cada plugin type se vincula directamente a su
Custom API como **backing plugin** — Dataverse lo invoca cuando la API es ejecutada.

## Plugin Types

| Clase | Custom API de respaldo | Modo |
|-------|------------------------|------|
| `PrepareImportPlugin` | `sanic_PrepareImport` | SYSTEM |
| `ProcessChunkPlugin` | `sanic_ProcessChunk` | SYSTEM |
| `ExportTemplatePlugin` | `sanic_ExportTemplate` | SYSTEM |
| `ImportTemplatePlugin` | `sanic_ImportTemplate` | SYSTEM |

### Modo SYSTEM

Todos los plugins crean el `IOrganizationService` con:

```csharp
var service = factory.CreateOrganizationService(null); // null = SYSTEM
```

Esto tiene tres consecuencias:
1. Las operaciones no están sujetas a los privilegios de seguridad del usuario que disparó la invocación.
2. El audit log no genera entradas asociadas al application user por cada fila procesada.
3. El procesamiento es consistente independientemente de quién inicia el import.

## Estructura interna

Cada plugin type actúa exclusivamente como adaptador de entrada/salida (lee `InputParameters`, escribe
`OutputParameters`) y delega toda la lógica a una clase `*Handler` testeable por separado:

```
*Plugin.Execute()
    └─► *Handler.Run()
            └─► Services (ImportContextLoader, FileDownloader, CsvParserService,
                          NormalizationEngine, UpsertWriter, TemplateExporter, etc.)
```

Los `*Handler` no dependen de `IServiceProvider` ni de `IPluginExecutionContext`, lo que permite
testearlos con mocks en tests unitarios sin un contexto Dataverse real.

## Sin steps de mensaje

Smelter no registra ningún step en el pipeline de mensajes de Dataverse (pre/post-operation en Create,
Update, Delete, etc.). Si en el futuro se necesita lógica de negocio reactiva (ej. auditoría al crear
un job), el administrador puede registrar steps adicionales — no hay conflicto con los backing plugins.

## Requisitos del assembly

| Atributo | Valor |
|----------|-------|
| Target framework | .NET Framework 4.6.2 |
| Isolation mode | Sandbox |
| Serialización | `DataContractJsonSerializer` (disponible en sandbox) |
| Dependencia CSV | CsvHelper 30.0.1 (última versión compatible con net462) |

> CsvHelper 31+ eliminó el soporte para `net462`. La versión 30.0.1 está licenciada bajo MS-PL /
> Apache 2.0 (apta para uso comercial).

## Volver a

- [Custom APIs](custom-apis.md)
- [Architecture](../01-overview/architecture.md)
