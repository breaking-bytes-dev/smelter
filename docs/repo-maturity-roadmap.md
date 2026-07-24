[← Volver al índice de documentación](README.md)

# Roadmap de madurez del repositorio — Smelter al nivel de Sigil

> Análisis comparativo (2026-07-24) contra [bitbreaker01/sigil](https://github.com/bitbreaker01/sigil),
> el repo de referencia por su estructura, docs, releases y CI/CD. Este documento define las brechas y el
> plan para cerrarlas, en orden de prioridad.

## Estado comparado

| Dimensión | Sigil | Smelter | Brecha |
|---|---|---|:--:|
| Git con historia + remoto | ✅ | ❌ cero commits | 🔴 |
| CI (tests en cada PR/push) | ✅ Linux + windows-latest | ❌ | 🔴 |
| Releases + tags de solución | ✅ `sol-vX.Y.Z.B` | ❌ | 🔴 |
| Metadata de solución versionada (`solutions/unpacked/`) | ✅ | ❌ | 🔴 |
| Deploy automatizado por SDK (`tools/Deploy`) | ✅ | ❌ (PRT manual) | 🟠 |
| Núcleo puro testeable en Linux | ✅ (net8) | ❌ (todo net462) | 🟠 |
| CHANGELOG mantenido (Keep a Changelog) | ✅ | 🟡 stale | 🟠 |
| Docs por audiencia | ✅ | ✅ | 🟢 |
| Contratos doc↔test (catálogo corroborado) | ✅ | ❌ | 🟡 |
| Higiene de raíz | ✅ | 🟡 (CSVs, tmp/, scratch/) | 🟡 |

## Plan por fases

### Fase 0 — Git y GitHub ✅ (completada 2026-07-24)
- [x] Higiene: `Smelter_*.csv` → `tests/data/`; `tmp/`, `scratch/`, `.atl/` al `.gitignore`.
- [x] **Primer commit** (`cfcabc9`) de todo el repo (código + docs + openspec).
- [x] Repo creado: `bitbreaker01/smelter` (**privado** — el README dice Proprietary; pasarlo a público es 1 click si se decide) + push a `main`.
- [x] README raíz al estilo Sigil (reescrito a la realidad v2: CSV-only, chunked, 4 Custom APIs, MDA) + CHANGELOG actualizado (Keep a Changelog, estado real).

### Fase 1 — CI (mata el gate eterno de "corré dotnet test en Windows")
- [ ] `.github/workflows/ci.yml`:
  - Job `build` (ubuntu): `dotnet build Smelter.sln` — rápido, valida compilación.
  - Job `tests` (**windows-latest**): `dotnet test Smelter.sln -c Release` — los ~60 tests net462 POR FIN ejecutan en CI.
  - `concurrency` + trigger en PR y push a main. Rojo bloquea merge.
- [ ] `global.json` para fijar la versión del SDK (mismo compilador local y CI).

### Fase 2 — ALM de solución en dos niveles (el patrón estrella de Sigil)
- [ ] Carpeta `solutions/` con su README (qué es managed vs unmanaged, dónde viven los zips → Releases).
- [ ] Workflow `solution-sync.yml`: al publicar un Release → `pac solution unpack` + prune + commit de la metadata **no-código** en `solutions/unpacked/` (tablas, forms, vistas, roles, Custom APIs — diffeable).
- [ ] Workflow `solution-release.yml` (manual, un botón): Service Principal exporta managed+unmanaged de Dev con `pac`, crea el GitHub Release con los zips, tag `sol-vX.Y.Z.B`, y dispara `solution-sync`.
- [ ] Secrets del environment `dev`: `SMELTER_DATAVERSE_URL`, `SMELTER_CLIENT_ID`, `SMELTER_CLIENT_SECRET`, `SMELTER_TENANT_ID` (reusar el application user de smelter-security).
- [ ] CHANGELOG real: formato Keep a Changelog, secciones Added/Changed/Fixed, versiones linkeadas a los Releases. Primer release: el estado actual (motor v2 + export/import + security).

### Fase 3 — Deploy por SDK (adiós PRT manual)
- [ ] `tools/Smelter.Deploy/`: consola C# con `ServiceClient` que (1) sube/actualiza el plugin package, (2) crea/actualiza las 4 Custom APIs con sus request/response properties EXACTOS. Idempotente.
- [ ] Elimina de raíz los dolores conocidos: DLL viejo, versión que rompe plugin types, parámetros case-sensitive mal tipeados en el portal.

### Fase 4 — Núcleo puro net8 (tests en Linux)
- [ ] Extraer la lógica PURA a `src/Smelter.Core/` multi-target (`net8.0;net462`): CsvParser, TransformEngine, TypeCoercer, DependencyResolver, TemplateSerializer, modelos sin SDK.
- [ ] `Smelter.Core.Tests` (net8) corre en ubuntu (rápido/barato); `Smelter.Plugins.Tests` (net462, orquestación con SDK) queda en windows-latest.
- [ ] Beneficio: feedback de tests en segundos en cualquier máquina (incluida esta), sin Mono.

### Fase 5 — Contratos doc↔test (el toque fino de Sigil)
- [ ] `docs/02-solution-reference/` ya documenta OptionSets (status, categorías de error, data types). Agregar **tests de contrato** que corroboren esos catálogos contra los enums del código (`ErrorCategory`, `ImportJobStatus`, `ImportLogStatus`, `ColumnDataType`) — si alguien cambia un valor, el test lo delata.
- [ ] (Con Fase 2) los docs de tablas pueden validarse contra `solutions/unpacked/` (la metadata real).

## Qué NO copiar de Sigil (por ahora)
- **Playwright E2E**: Sigil tiene frontend Code App; la MDA de Smelter no lo justifica hoy.
- **Suite de conformance contra Dev**: valiosa, pero primero Fases 0–2. Candidata a roadmap posterior.

## Dependencias
- Fase 2 y 3 requieren el **Service Principal / application user** (ya previsto en smelter-security).
- Todo lo demás es autocontenido.
