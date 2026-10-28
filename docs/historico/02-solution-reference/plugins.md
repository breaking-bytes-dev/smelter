[← Volver al índice de documentación](../README.md)

# Plugins — Smelter

## Assembly

**`Smelter.Plugins`** — .NET Framework 4.6.2, compilado para sandbox de Dataverse.

El assembly contiene exactamente cinco plugin types: cuatro **backing plugins** (uno por Custom API —
Dataverse los invoca cuando la API es ejecutada) y un **message step** (`AssignJobOwnerPlugin`, sync
Pre-operation en el Create del Import Job).

## Plugin Types

| Clase | Vínculo | Modo |
|-------|---------|------|
| `PrepareImportPlugin` | Backing de `sanic_PrepareImport` | SYSTEM |
| `ProcessChunkPlugin` | Backing de `sanic_ProcessChunk` | SYSTEM |
| `ExportTemplatePlugin` | Backing de `sanic_ExportTemplate` | SYSTEM |
| `ImportTemplatePlugin` | Backing de `sanic_ImportTemplate` | SYSTEM |
| `AssignJobOwnerPlugin` | Step: Create de `sanic_tbl_smlt_importjob`, sync, Pre-operation | SYSTEM |

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

## Message steps

Smelter registra exactamente **un** step en el pipeline de mensajes de Dataverse:

| Step | Mensaje | Entidad | Stage | Modo | Qué hace |
|------|---------|---------|-------|------|----------|
| `AssignJobOwnerPlugin` | Create | `sanic_tbl_smlt_importjob` | Pre-operation (20) | Sync | Si el Import Template referenciado por el job nuevo es owned por un **team**, setea `ownerid` del Target a ese mismo team — el job nace owned por el team y todos sus miembros lo ven/operan con Read/Write Basic. Template owned por user (o job sin template): no toca nada, aplica el default de la plataforma (el creador). |

El registro NO se hace con el Plugin Registration Tool: la spec declarativa vive en
`tools/Smelter.Deploy/StepSpec.cs` y el upsert idempotente en `StepProvisioner.cs` (paso `[4b]` del
deploy), con verificación E2E en `tools/Smelter.Verify/StepAssertions.cs` — mismo patrón
spec → provisioner → assertion que las Custom APIs y la seguridad. Si en el futuro se necesita más
lógica reactiva, se agrega una entrada a `StepCatalogo.Steps` — no hay conflicto con los backing plugins.

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
