# Tasks: Chunked Import Redesign (v2 — CSV-only, sin staging)

> Reemplaza el plan v1 (staging + Excel). Strict TDD: RED antes de GREEN. Gate: el usuario corre `dotnet test`.

## Phase 1: Limpieza / deshacer v1

- [x] 1.1 Eliminados `StagingReader.cs` + `StagingPayloadSerializer.cs` + sus tests.
- [x] 1.2 Eliminados `ExcelParserService.cs` + `ExcelParserServiceTests.cs`.
- [x] 1.3 Quitados `ExcelDataReader` y `.DataSet` del csproj.
- [x] 1.4 Quitados `SheetName` (SourceRow), `SourceSheet` (TableMappingConfig + loader), `FilterBySheet` (NormalizationEngine + uso), `FileParseOptions.SheetName`.
- [x] 1.5 `ProcessBatch` ya no filtra por sheet. `IFileParser` se mantiene (lo usa CsvParserService + tests; remoción opcional separada).
  > VERIFICADO: `dotnet build Smelter.sln` → Build succeeded, 0 Warnings, 0 Errors.

## Phase 2: Esquema Dataverse (portal / solución)

- [ ] 2.1 Agregar `sanic_smlt_it_chunksize` (Whole Number) a Import Template.
- [ ] 2.2 Agregar `sanic_smlt_ijl_errorcategory` (OptionSet, 7 valores) a Import Job Log.
- [ ] 2.3 Crear alternate key en Import Job Log: `importjobid` + `rownumber` + `targettable`.
- [ ] 2.4 Confirmar alternate keys activas en tablas destino. Eliminar tabla staging si se creó.

## Phase 3: CsvReader streaming + skip (TDD)

- [x] 3.1 RED: tests `ReadRange(skip, take)` — rango correcto, borde (skip > total → vacío), con/sin header, rownumber físico. → `CsvReaderTests`.
- [x] 3.2 RED: test `CountRows` (con header, sin header, vacío). → `CsvReaderTests`.
- [x] 3.3 GREEN: `CsvParserService` implementa `ICsvReader` (`ReadRange` skip sin materializar + take; `CountRows`); `Parse` delega a `ReadRange`. (IFileParser.Parse se mantiene temporal hasta la limpieza.)
  > GATE: `dotnet test` lo corre el usuario.

> **ORDEN DE EJECUCIÓN (ajustado):** la Fase 1 (limpieza/deshacer) se ejecuta AL FINAL, no al principio:
> borrar StagingReader/ExcelParser rompería los plugins que aún los usan. Orden real: 3 → 4 → 5 → 6 → 1 → 2/7.
> Así el código se mantiene compilable en cada paso.

## Phase 4: Clasificación de errores (TDD)

- [x] 4.1 RED/GREEN: `UpsertWriter` clasifica fault de escritura como `TargetData`. → `UpsertWriterErrorTests`.
  > AJUSTE (decisión 9): `Transient` interno DIFERIDO (procesamiento secuencial → deadlocks raros). El enum incluye `Transient` pero v1 no lo detecta ni re-lanza; el service protection a nivel invocación lo cubre el retry policy de PA.
- [x] 4.2 RED: `NormalizationEngine` mapea excepción → categoría (TypeError/TransformError/ValidationError/LookupUnresolved). → `NormalizationEngineErrorCategoryTests`.
- [x] 4.3 GREEN: enum `ErrorCategory` (7) en Models; `ProcessingResult.Category` + `Failure(...,category)`; clasificación en `NormalizationEngine` (`ClassifyBuildException`, required→Validation, lookup no resoluble→LookupUnresolved) y `UpsertWriter`→TargetData.
  > GATE: `dotnet test` lo corre el usuario.

## Phase 5: PrepareImport liviano (TDD)

- [x] 5.1 RED: `PrepareImportHandlerTests` v2 — cuenta filas (TotalRows), valida headers (falla con mensaje si falta SourceColumn), resetea estado. + `ICsvReader.ReadHeaders` test en `CsvReaderTests`.
- [x] 5.2 GREEN: `PrepareImportPlugin`/`PrepareImportHandler` reescritos (loader.LoadContext → downloader → ValidateHeaders → CsvReader.CountRows → ResetJob). + `ICsvReader.ReadHeaders` implementado. Ya NO usa StagingReader.
  > GATE: `dotnet test` lo corre el usuario.

## Phase 6: ProcessChunk sin staging (TDD)

- [x] 6.1 RED: `Run_UsesChunkSizeFromTemplate`. + `ImportTemplateConfig.ChunkSize` + loader lee `sanic_smlt_it_chunksize`.
- [x] 6.2 RED: `Run_FullChunk...` (offset += rows, HasMore), `Run_PartialChunk...` (Completed), `Run_NoRows...` (no procesa).
- [x] 6.3 RED: `Run_LogsOnlyFailedRows_AsIdempotentUpsertWithCategory` (UpsertRequest al log con KeyAttributes + errorcategory).
- [x] 6.4 GREEN: `ProcessChunkPlugin`/`ProcessChunkHandler` reescritos: LoadContext → ResolveChunkSize (template+override, clamp 100-10000) → download → `CsvReader.ReadRange(offset, chunkSize)` → `ProcessBatch` → `LogFailures` (upsert idempotente) → `UpdateJob`. Ya NO usa StagingReader.
  > GATE: `dotnet test` lo corre el usuario.

## Phase 7: Orquestación, docs y verificación

- [ ] 7.1 Cloud Flow: Prepare → `Do until` ProcessChunk, con **retry policy** en la acción + scope-catch → job `Failed`; connection con application user.
- [ ] 7.2 Actualizar `docs/setup-chunked-import.md` (sin staging, solo CSV, chunkSize por template, retry, categorías).
- [ ] 7.3 Actualizar `docs/architecture.md` y `docs/analysis-issues.md` (cerrar v2; #17 obsoleto).
- [ ] 7.4 Verificación manual (usuario): `dotnet test` verde + import 100k CSV → sin depth/timeout, resume no duplica, logs categorizados solo de fallidas.

## Notas
- Orden por dependencia: limpieza (1) → esquema (2) → CsvReader (3) habilita Prepare(5) y ProcessChunk(6); errores (4) antes de ProcessChunk(6).
- Gate de ejecución: cada GREEN se confirma con `dotnet test` en el entorno del usuario (net462 no corre acá + regla no-build).
- Open questions del design a cerrar antes de 4.x/6.x: códigos exactos de Transient; rango/default de chunkSize.
