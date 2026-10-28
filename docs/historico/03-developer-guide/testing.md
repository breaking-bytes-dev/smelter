[← Volver al índice de documentación](../README.md)

# Testing

> Estrategia de tests, herramientas, convenciones y cómo correr la suite.

---

## Stack de testing

| Herramienta | Versión | Rol |
|-------------|---------|-----|
| xUnit | 2.9.0 | Framework de tests |
| NSubstitute | 5.1.0 | Mocking de interfaces (`IOrganizationService`, `ICsvReader`, etc.) |
| FluentAssertions | 6.12.0 | Assertions expresivas |
| ClosedXML | 0.102.3 | Construcción de fixtures Excel (disponible, no activo en tests actuales) |
| Microsoft.CrmSdk.CoreAssemblies | 9.0.2.56 | Tipos de Dataverse en tests (`Entity`, `EntityReference`, `OptionSetValue`, etc.) |

El proyecto de tests está en `tests/Smelter.Plugins.Tests/Smelter.Plugins.Tests.csproj`, también targeting `net462`.

---

## Modo TDD estricto

**Este proyecto opera en Strict TDD.** El flujo es:

1. **RED** — escribir el test primero. Debe fallar por la razón correcta (lógica ausente, no error de compilación).
2. **GREEN** — escribir el mínimo código productivo para que el test pase.
3. **REFACTOR** — mejorar el código sin romper los tests.

No se escribe código productivo sin un test rojo previo. No se modifica un test para que pase código incorrecto.

---

## Cómo correr los tests

```bash
dotnet test
```

O apuntando al proyecto de tests directamente:

```bash
dotnet test tests\Smelter.Plugins.Tests\Smelter.Plugins.Tests.csproj
```

### Advertencia crítica: requiere runtime de .NET Framework

Los tests son `net462`. El comando `dotnet test` requiere un runtime capaz de ejecutar assemblies .NET Framework 4.6.2:

- **Windows:** el runtime está disponible si .NET Framework 4.6.2 o superior está instalado (viene con Windows 10/11). `dotnet test` funciona sin configuración adicional.
- **Linux / macOS:** `dotnet build` **sí compila** `net462` (gracias a `Microsoft.NETFramework.ReferenceAssemblies`), pero `dotnet test` **aborta en tiempo de ejecución** con el error:

  ```
  Could not find 'mono' host
  ```

  Para ejecutar los tests en Linux/macOS es necesario tener [Mono](https://www.mono-project.com/) instalado. Sin Mono, el build pasa pero los tests no corren.

**Recomendación:** desarrollar y correr tests en Windows, o usar un contenedor Windows si el entorno es Linux.

---

## Organización de los tests

```
tests/Smelter.Plugins.Tests/
├── Plugins/
│   ├── PrepareImportHandlerTests.cs
│   ├── ProcessChunkHandlerTests.cs
│   └── TemplateApiHandlerTests.cs
└── Services/
    ├── CsvParserServiceTests.cs
    ├── CsvReaderTests.cs
    ├── DependencyResolverTests.cs
    ├── FileDownloaderTests.cs
    ├── ImportContextLoaderTests.cs
    ├── NormalizationEngineBatchTests.cs
    ├── NormalizationEngineErrorCategoryTests.cs
    ├── TemplateExporterTests.cs
    ├── TemplateImporterTests.cs
    ├── TemplateSerializerTests.cs
    ├── TransformEngineTests.cs
    ├── TypeCoercerTests.cs
    ├── UpsertWriterErrorTests.cs
    └── UpsertWriterTests.cs
```

Cada archivo de tests cubre una clase productiva. Los tests de handlers van en `Plugins/`, los de servicios en `Services/`.

---

## Patrón de tests por handler

Los handlers son los componentes más críticos del sistema. El patrón es uniforme en todos:

### 1. Clase `Mocks` interna (sealed)

Agrupa todos los sustitutos en una clase interna del test. Centraliza la construcción del SUT e impide que los tests se contaminen entre sí:

```csharp
private sealed class Mocks
{
    public IImportContextLoader Loader = Substitute.For<IImportContextLoader>();
    public IFileDownloader Downloader = Substitute.For<IFileDownloader>();
    public ICsvReader Csv = Substitute.For<ICsvReader>();
    public IOrganizationService Service = Substitute.For<IOrganizationService>();

    public PrepareImportHandler Build() =>
        new PrepareImportHandler(Loader, Downloader, Csv, Service);
}
```

### 2. Configuración de sustitutos

Los sustitutos se configuran con `.Returns(...)` justo antes del ejercicio. No se usan `Setup` globales que oculten el comportamiento esperado:

```csharp
m.Loader.LoadContext(jobId).Returns(ContextWith(true, "name", "code"));
m.Downloader.Download(Arg.Any<EntityReference>(), Arg.Any<string>()).Returns(bytes);
m.Csv.ReadHeaders(bytes, true, ",").Returns(new List<string> { "name", "code" });
m.Csv.CountRows(bytes, true, ",").Returns(42);
```

### 3. Captura de requests a `IOrganizationService`

Para verificar qué se escribió en Dataverse, se usa `Arg.Do<T>` para capturar el request antes de devolverlo:

```csharp
ExecuteMultipleRequest captured = null;
m.Service.Execute(Arg.Do<OrganizationRequest>(r => captured = r as ExecuteMultipleRequest))
         .Returns(_ => new ExecuteMultipleResponse());
```

Después del ejercicio se verifica `captured`:

```csharp
var upserts = captured.Requests.OfType<UpsertRequest>().ToList();
upserts.Should().ContainSingle("solo la fila fallida se loguea");
```

### 4. Verificación de llamadas recibidas / no recibidas

```csharp
// Verificar que se llamó exactamente una vez con atributos específicos
m.Service.Received(1).Update(Arg.Is<Entity>(e =>
    (int)e["sanic_smlt_ij_totalrows"] == 42
    && (int)e["sanic_smlt_ij_processingbatch"] == 0));

// Verificar que NO se llamó (ej: cuando la validación falla antes de actualizar el job)
m.Service.DidNotReceive().Update(Arg.Any<Entity>());

// Verificar que el engine no fue invocado cuando no hay filas
m.Engine.DidNotReceive().ProcessBatch(
    Arg.Any<ImportContext>(),
    Arg.Any<IList<SourceRow>>(),
    Arg.Any<IUpsertWriter>());
```

---

## Patrón de tests de servicios

Para servicios que usan `IOrganizationService` (como `UpsertWriter` o `ImportContextLoader`), el patrón es el mismo: `Substitute.For<IOrganizationService>()` con `Arg.Do` para capturar.

Para servicios sin dependencias externas (`DependencyResolver`, `TypeCoercer`, `TransformEngine`, `CsvParserService`), los tests son unitarios puros — sin mocks, solo datos de entrada y assertions sobre el resultado.

Ejemplo con `NormalizationEngine`:

```csharp
private static NormalizationEngine Engine() =>
    new NormalizationEngine(
        Substitute.For<IOrganizationService>(),
        Substitute.For<ITracingService>());

private static IUpsertWriter CapturingWriter(Action<TableMappingConfig, IList<TargetRecord>> onWrite)
{
    var writer = Substitute.For<IUpsertWriter>();
    writer.Write(Arg.Any<IList<TargetRecord>>(), Arg.Any<TableMappingConfig>(), Arg.Any<bool>())
          .Returns(ci =>
          {
              onWrite(ci.Arg<TableMappingConfig>(), ci.Arg<IList<TargetRecord>>());
              return ci.Arg<IList<TargetRecord>>()
                       .Select(r => ProcessingResult.Success(r.SourceRowNumber, "t", Guid.NewGuid(), false))
                       .ToList();
          });
    return writer;
}
```

Este patrón de "capturing writer" permite verificar el orden de procesamiento (DAG) y los atributos de cada `TargetRecord` sin necesitar Dataverse real.

---

## Tests de regresión

Algunos tests están marcados explícitamente como tests de regresión para bugs corregidos. Esto es intencional: si el test desaparece, el bug puede volver.

| Test | Bug que guarda |
|------|---------------|
| `LoadedRelationships_FedToResolver_OrderParentBeforeChild` | Bug #1: relaciones no cargadas → DAG inerte |
| `LoadTemplateConfig_RetrievesTemplateDirectly_DoesNotProbeImportJob` | Bug #2: LoadTemplateConfig probaba el job con el template id → "Does Not Exist" |
| `ProcessBatch_RequiredFieldWithDefault_UsesDefaultInsteadOfSkipping` | Bug #6: default aplicado después de la validación → filas válidas saltadas |
| `ProcessBatch_ExternalLookupByBusinessKey_UsesAlternateKey` | Bug #7: lookup sin GUID y sin LookupMatchColumn caía silenciosamente |

No eliminar ni modificar estos tests salvo que el comportamiento que guardan cambie intencionalmente.

---

## Convenciones de nomenclatura

Los tests siguen el patrón `Método_Escenario_ResultadoEsperado`:

```
Run_CountsRowsValidatesHeadersAndResetsJob
Run_MissingMappedHeader_ThrowsAndDoesNotStart
Write_WithAlternateKey_BuildsUpsertWithKeyAttributes
Write_DryRun_DoesNotCallServiceAndReturnsSuccess
ProcessBatch_ProcessesParentBeforeChild_AndResolvesParentLookupByAlternateKey
```

Los nombres de los tests son documentación ejecutable. No acortarlos.
