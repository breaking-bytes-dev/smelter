# Verification Report

**Change**: smelter-security
**Version**: v2 (proposal.md — post-auditoría 2026-07-27) + re-audit 2026-07-28 (fixes applied)
**Mode**: Strict TDD

> **UPDATE 2026-07-28**: all CRITICAL/WARNING items from the first pass were fixed. See
> "Re-Audit (2026-07-28) — Fix Verification" at the end of this document for the follow-up review.
> **Final verdict: PASS** (superseding the original "PASS WITH WARNINGS").

---

## Completeness

| Metric | Value |
|--------|-------|
| Tasks total | 46 |
| Tasks complete | 38 |
| Tasks incomplete | 8 |

Incomplete tasks — **all 8 are expected/by-design, none are defects**:
- `1.2`, `6.4`, `7.7` — explicitly **BLOQUEADA**: require live Dataverse (Dev/CI), not runnable in this sandbox. Documented with the exact blocking reason in each line.
- `9.1`–`9.5` — Phase 9 (delete legacy `anic_capi_smelter_processchunk`), correctly **GATED** to a green `Smelter.Verify` in Dev. Confirmed **not implemented** anywhere in the active deploy path (see Coherence table).

---

## Build & Tests Execution

**Build** (`dotnet build Smelter.sln -c Release`): ✅ Passed — 0 Warnings, 0 Errors (includes `Smelter.Plugins.Tests`, net462, confirming task 5.8's build-only claim).

**Smelter.Core.Tests** (`dotnet test tests/Smelter.Core.Tests -c Release`): ✅ 49/49 passed.

**Smelter.Contract.Tests** (`dotnet test tests/Smelter.Contract.Tests -c Release`): ⚠️ 45/48 passed, 3 failed — all 3 are the documented transitional failures for `sanic_capi_smelter_processchunk` (metadata not yet in `solutions/unpacked/` mirror, which only updates via `solution-sync` after a real deploy):
- `CustomApiContractTests.Api_ExistsInSolutionMetadata(uniqueName: "sanic_capi_smelter_processchunk")`
- `CustomApiContractTests.Api_RequestParams_MatchSolutionMetadata(uniqueName: "sanic_capi_smelter_processchunk")`
- `CustomApiContractTests.Api_ResponseProps_MatchSolutionMetadata(uniqueName: "sanic_capi_smelter_processchunk")`

Numbers match exactly what `tasks.md` (6.2) and the orchestrator's briefing predicted. No unexpected failures.

**Smelter.Plugins.Tests** (net462): not executable in this Linux sandbox ("Could not find 'mono' host") — confirmed compiles cleanly as part of the solution build above; real execution is gated to CI `windows-latest`.

**Coverage**: not available (no coverage tool configured per `openspec/config.yaml`).

---

## TDD Compliance

No formal `apply-progress` artifact exists (openspec mode) — `tasks.md` itself carries the RED/GREEN narrative per phase, used here as the equivalent evidence source.

| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | ✅ | Phase 3 (3.1 RED → 3.2 GREEN), Phase 5 (5.1/5.2 RED → 5.3 confirmed red via compile failure → 5.4–5.7 GREEN), Phase 6.2 (RED confirmed before GREEN) all narrated in `tasks.md`. |
| All tasks have tests | ⚠️ | `PrepareImportHandler`/`PrincipalAccessChecker`/`SecuritySpec` all have tests. **`CloudFlowUpdater.cs` (task 6.3) has zero test files** — implemented without a RED/GREEN cycle, confirmed via `rg -ln "CloudFlowUpdater"` (only `Program.cs`/`ApiSpec.cs`/`SecurityAssertions.cs` reference it, no test project does). |
| RED confirmed (tests exist) | ✅ | `PrepareImportHandlerTests.cs`, `PrincipalAccessCheckerTests.cs`, `SecuritySpecContractTests.cs`, `ProcessChunkRenameContractTests.cs` all present and exercised. |
| GREEN confirmed (tests pass) | ✅ | All of the above pass on execution (Core.Tests 49/49; Contract.Tests 45/48 with only the 3 documented transitional gaps; Plugins.Tests build-clean, execution gated to CI Windows). |
| Triangulation adequate | ✅ | Access-check has both allowed/denied cases; privilege matrix has per-table/per-role cases; rename contract has existence/plugin-type/shape cases. |
| Safety Net for modified files | ✅ | `PrepareImportHandlerTests.cs` pre-existing tests updated to the new `Run(jobId, initiatingUserId)` signature and still pass. |

**TDD Compliance**: 5/6 checks passed — the one gap is `CloudFlowUpdater.cs`, which correlates directly with the CRITICAL bug found below.

---

## Test Layer Distribution

| Layer | Tests | Files | Tools |
|-------|-------|-------|-------|
| Unit | ~9 (2 new access-check cases + 3 PrincipalAccessChecker + pre-existing PrepareImportHandler tests) | `PrepareImportHandlerTests.cs`, `PrincipalAccessCheckerTests.cs` | xUnit + NSubstitute + FluentAssertions (net462, CI Windows only) |
| Contract | ~26 (SecuritySpecContractTests + ProcessChunkRenameContractTests + affected rows of CustomApiContractTests) | `SecuritySpecContractTests.cs`, `ProcessChunkRenameContractTests.cs`, `CustomApiContractTests.cs` | xUnit + FluentAssertions vs. `solutions/unpacked/` (net8, runs on Linux/CI) |
| E2E | 5 asserts (roles/privileges, cascade, execute-privileges, AppModuleRoles, Cloud Flow alignment) | `SecurityAssertions.cs`, invoked from `Smelter.Verify/Program.cs` | `tools/Smelter.Verify` vs. real Dev — **not run this session** (blocked, no Dataverse access) |
| **Total** | **~40** | | |

`CloudFlowUpdater.cs` has 0 tests at any layer — the substring-collision bug below would have been caught by a unit test asserting "second call with an already-migrated `clientdata` is a no-op."

---

## Assertion Quality

Scanned `PrepareImportHandlerTests.cs`, `PrincipalAccessCheckerTests.cs`, `SecuritySpecContractTests.cs`, `ProcessChunkRenameContractTests.cs`.

**Assertion quality**: ✅ All assertions verify real behavior — no tautologies, no ghost loops, no assertion-without-production-call. `Received(1)`/`DidNotReceive()` checks are always paired with value assertions (not bare implementation-detail coupling). Contract tests on `SecuritySpec`/`Catalogo.Apis` are appropriately data-level (testing the declarative source of truth itself, matching design.md's stated strategy).

---

## Spec Compliance Matrix

### smelter-access-control

| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| Rol Smelter Configurador | Configurador con CRUD total | `SecuritySpecContractTests.cs > Configurador_HasFullCrudAppendAppendToAtOrgDepthOnAllSixTables` (data-level) | ✅ COMPLIANT (data) / ⚠️ E2E via `SecurityAssertions.AssertRolesExistAndMatchPrivileges` — not run (task 7.7 blocked) |
| Rol Smelter Operador | Operador bloqueado en write, habilitado en lectura y jobs | `SecuritySpecContractTests.cs > Operador_HasNoCreateWriteOrDeleteOnImportTemplate/OnMappingTables/CanCreateImportJobAtOrgDepthAndReadWriteAtUserDepth` | ✅ COMPLIANT (data) / ⚠️ E2E pending (7.7 blocked) |
| MDA restringida al Configurador | Operador no ve la app, Configurador sí | `SecurityAssertions.AssertAppModuleRolesOnlyConfigurador` | ❌ UNTESTED at runtime — no unit-testable surface (Dataverse-only), E2E pending (7.7 blocked). Expected given design.md's documented risk. |
| Cascade share/reparent Template→ImportJob | Job hereda acceso del team; sin share no propaga | `SecurityAssertions.AssertCascade` | ❌ UNTESTED at runtime (7.7 blocked). Relationship schema name manually verified by this review against `solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml` — matches exactly. No automated contract test pins this (see SUGGESTION). |
| Gating de Custom APIs por ExecutePrivilegeName | Operador bloqueado en export/import, habilitado en prepare/process | `SecuritySpecContractTests.cs > ExportImportTemplate_Apis_RequireWriteOnImportTemplate`, `PrepareImportAndProcessChunk_Apis_RequireCreateOnImportJob`, `Api_ExecutePrivilegeName_MatchesPrvTipoTablaFormat` | ✅ COMPLIANT |
| Validación de acceso al template en PrepareImportPlugin | Sin acceso es rechazado, con acceso es aceptado | `PrepareImportHandlerTests.cs > Run_InitiatingUserWithoutReadAccessOnTemplate_ThrowsAndDoesNotDownloadOrUpdate`, `Run_InitiatingUserWithReadAccessOnTemplate_CompletesNormally` | ✅ COMPLIANT |
| Provisioning idempotente | Segunda corrida no duplica y Verify confirma | No automated test (by design — needs live Dataverse, `SecurityProvisioner`/`SecurityAssertions` logic reviewed statically) | ⚠️ PARTIAL — roles/views/webresources/dashboard/app-role logic is idempotent by construction (query-then-create-or-update); **`CloudFlowUpdater` is NOT idempotent** (CRITICAL, see below) |
| Ownership de templates por team | Operador acotado, Configurador sin límite | No automated test (relies on cascade + GrantAccess, both live-Dataverse-only) | ❌ UNTESTED — inherent to the design (relies on POA), acceptable given constraints |

### smelter-mda-ux

| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| Vistas custom de Import Job | Cada vista filtra correctamente | `SecuritySpecContractTests.cs > Views_ThreeViewsDefined...`, `ActiveJobsView_FiltersQueuedAndProcessing...`, `CompletedWithErrorsView_FiltersOnFailedCountGreaterThanZero`, `AllJobsView_HasNoFilter` | ✅ COMPLIANT (FetchXML shape) |
| Botones de command bar Ejecutar/Reintentar | Ejecutar y Reintentar visibles según estado | No automated test (RibbonDiffXml is static asset text; `SolutionZipBuilderTests.cs` tests the zip shape, not the ribbon content) | ⚠️ PARTIAL — zip-building mechanics tested, ribbon content itself only manually reviewed (verified `ValueRule` conditions on `sanic_smlt_ij_status` = 0/4 match the spec) |
| Dashboard "Smelter Dashboard" en el sitemap | Dashboard accesible desde el sitemap | No automated test (Dataverse-only) | ❌ UNTESTED — E2E only, not run (7.7 blocked) |
| Rename de processchunk sin ventana rota | Post-deploy el flow resuelve la API / Verify detecta un flow desalineado | `ProcessChunkRenameContractTests.cs` (3 tests, all pass) covers the API-catalog side | ⚠️ PARTIAL — catalog-side contract is solid; the actual flow-update mechanism (`CloudFlowUpdater`) has **zero tests and a real correctness bug** (CRITICAL below) that the E2E assert (`AssertProcessChunkCloudFlowReferencesNewApi`) would not reliably catch either |

**Compliance summary**: 10/15 scenarios fully COMPLIANT at the layer testable in this sandbox; 5 PARTIAL/UNTESTED are consistent with the documented, agreed sandbox constraints (no live Dataverse) — except the `CloudFlowUpdater` gap, which is a real, fixable coverage hole, not an environment constraint.

---

## Correctness (Static — Structural Evidence)

| Requirement | Status | Notes |
|------------|--------|-------|
| Privilege matrix (`SecuritySpec.cs`) | ✅ Implemented | Configurador: CRUD+Append+AppendTo, `PrivilegeDepth.Global` (Org), all 6 tables — matches spec exactly. Operador: Template Read+AppendTo `Basic` (User); Mappings Read `Global` (Org), no Create/Write/Delete; ImportJob Create `Global` (Org) + Read/Write `Basic` (User); Log Read `Basic` (User) — matches proposal/design/tasks.md's closed decision. `PrivilegeName(type, table) => $"prv{type}{table}"` matches the real Dataverse autogenerated-privilege convention for custom tables. |
| `ExecutePrivilegeName` (4 values) | ✅ Implemented | export/import-template → `prvWritesanic_tbl_smlt_importtemplate`; prepareimport/processchunk (both `anic_`/`sanic_` entries) → `prvCreatesanic_tbl_smlt_importjob`. Confirmed in `ApiSpec.cs` and covered by `SecuritySpecContractTests`. |
| `SecurityProvisioner` idempotency (roles/views/webresources/subareas) | ✅ Implemented | Query-by-natural-key-then-create-or-update pattern throughout; `AddPrivilegesRoleRequest` is safe to re-apply. `CreateRequest.Parameters["SolutionUniqueName"]` is the (valid, simpler) equivalent of `AddSolutionComponentRequest` — used consistently on every `CrearEnSolucion` call, so new roles/views/webresources/forms travel in `smlt_core_smelter`. |
| Cascade `UpdateRelationshipRequest` | ✅ Implemented | Schema name confirmed byte-for-byte against `solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml`. Only `CascadeConfiguration.Share`/`.Reparent` mutated on the retrieved full metadata object before `UpdateRelationshipRequest` — all other cascade fields (`CascadeAssign`, `CascadeDelete`, `CascadeArchive`, `CascadeUnshare`, `CascadeRollupView`) pass through untouched. |
| `PrepareImportHandler`/`IAccessChecker` | ✅ Implemented | Validates `initiatingUserId` Read access on the job's Template via `RetrievePrincipalAccessRequest`, **before** file download or job reset (`ValidateTemplateAccess` runs first in `Run()`). Throws `InvalidPluginExecutionException` with a message naming the user and template. `PrepareImportPlugin.Execute` passes `context.InitiatingUserId` (not `context.UserId`) — correct, confirmed at `PrepareImportPlugin.cs:49`. |
| `CloudFlowUpdater` idempotency | ❌ **Bug** | See CRITICAL finding below. |
| Phase 9 (legacy API deletion) gating | ✅ Implemented correctly | `Program.cs` and `ApiSpec.cs` contain **no** `DeleteRequest` for `anic_capi_smelter_processchunk` anywhere in the active deploy path — confirmed by reading both files in full. Both catalog entries coexist as designed for Release N. |
| `Smelter.Verify`/`SecurityAssertions` | ✅ Implemented | Covers roles+privileges (`AssertRolesExistAndMatchPrivileges`, incl. explicit Operador-forbidden-on-Template check), cascade (`AssertCascade`), execute privileges across all 5 APIs (`AssertExecutePrivileges`), AppModuleRoles exclusivity (`AssertAppModuleRolesOnlyConfigurador`), and Cloud Flow alignment (`AssertProcessChunkCloudFlowReferencesNewApi`, WARNs instead of failing when no flow is found — matches spec's "reportar en vez de pasar en silencio"). One blind spot noted below (WARNING). |
| Docs (`security-and-roles.md`) | ✅ Implemented | Matrix, `ExecutePrivilegeName` table, runtime check, cascade, MDA caveat (explicitly documents that MDA restriction is NOT record-level security), teams operational guide, and troubleshooting table all match the actual code — not aspirational. One claim (idempotent re-run, line 339) is contradicted by the CloudFlowUpdater bug. |

---

## Coherence (Design)

| Decision | Followed? | Notes |
|----------|-----------|-------|
| Matriz de privilegios como `record`/`static class` en `SecuritySpec.cs` | ✅ Yes | |
| Alta de roles vía `role` Create + `SolutionUniqueName` + `AddPrivilegesRoleRequest` | ✅ Yes | |
| Idempotencia de roles sin diff previo | ✅ Yes | |
| Cascade vía `UpdateRelationshipRequest` nativo (no plugin) | ✅ Yes | |
| Vistas + Dashboard vía CRUD directo (`savedquery`/`systemform`) | ✅ Yes | |
| Botones vía RibbonDiff clásico + JS, mini-solution.zip in-memory | ✅ Yes | Ribbon component `Id`s are static strings (not regenerated GUIDs), so re-import via `ImportSolutionRequest` is safely idempotent. |
| `solutions/unpacked/` read-only | ✅ Yes | Never written to by any reviewed code. |
| Verificación Operador vía assert estático (no 2º usuario E2E) | ✅ Yes | `AssertRolesExistAndMatchPrivileges`'s explicit "forbidden on Template" check. |
| Único cambio de motor: `PrepareImportHandler` check, resto SYSTEM | ✅ Yes | `ProcessChunkPlugin` and downstream untouched; only `PrepareImportPlugin`/`PrepareImportHandler` changed. |
| Migration/Rollout: Release N additive, N+1 gated cleanup | ✅ Yes | Confirmed no Phase 9 code active. |
| Dashboard `formxml` control `uniqueid` stability | ⚠️ Deviated | Design doesn't call this out explicitly, but the implementation regenerates a random `Guid.NewGuid()` for the subgrid `uniqueid` on every `UpsertDashboardSubArea` run — see WARNING below. |

---

## Issues Found

### CRITICAL

1. **`CloudFlowUpdater.cs:26-27,57,79,85` — non-idempotent, self-corrupting rename update.** `NewActionUniqueName` (`"sanic_capi_smelter_processchunk"`) is literally `OldActionUniqueName` (`"anic_capi_smelter_processchunk"`) prefixed with `"s"`. This means:
   - The `QueryExpression` `LIKE '%anic_capi_smelter_processchunk%'` (line 57) matches flows that reference **only the new name**, because the new name *contains* the old name as a substring.
   - `clientData.Contains(OldActionUniqueName)` (line 79) is then `true` even when the flow was already migrated.
   - `clientData.Replace(OldActionUniqueName, NewActionUniqueName)` (line 85) finds `"anic_capi_smelter_processchunk"` starting at index 1 of the already-correct `"sanic_capi_smelter_processchunk"` and replaces it, producing `"s" + "sanic_capi_smelter_processchunk"` = `"ssanic_capi_smelter_processchunk"`.
   - Every subsequent deploy run (CI runs this on every merge per the "100% vía SDK, idempotente, CI" approach in proposal.md) prepends another `"s"`, permanently corrupting the flow's action reference into a string that matches no real Custom API — the Cloud Flow breaks with "action not found" on the *second* deploy after the rename succeeds, i.e. the very failure mode Requirement "Rename de processchunk sin ventana rota" exists to prevent.
   - Zero test coverage exists for `CloudFlowUpdater` (confirmed via `rg -ln "CloudFlowUpdater"` across the repo — only production code references it), so Strict TDD's RED/GREEN cycle was skipped for this file, and this bug slipped through.
   - Directly contradicts `docs/04-admin-guide/security-and-roles.md:339` ("`Smelter.Deploy` corrido 2 veces duplica algo → No debería").
   - **Fix direction**: use an exact/whole-token match (e.g., check for the old name wrapped in `"`/`/` delimiters as it appears in the connector URL, or check `!clientData.Contains(NewActionUniqueName)` before touching anything) instead of a bare substring `Contains`/`Replace`, and add a unit test asserting a second call on already-migrated `clientdata` is a true no-op.

### WARNING

1. **`SecurityAssertions.cs:200` — `AssertProcessChunkCloudFlowReferencesNewApi` would not reliably detect the corruption above.** Because it also checks via `Contains(NewProcessChunkUniqueName)`, a corrupted `"ssanic_capi_smelter_processchunk"` still contains `"sanic_capi_smelter_processchunk"` as a substring, so `Smelter.Verify` would report this piece green even though the literal action reference is broken and the flow would fail at runtime. Recommend an exact-match check (or at minimum, also assert the *old* name is absent) rather than pure substring containment.

2. **`SecurityProvisioner.cs` (`BuildDashboardCell`, inside `UpsertDashboardSubArea`) regenerates `Guid.NewGuid()` for the subgrid `uniqueid` on every run.** The dashboard record itself is upserted by `formid` (idempotent), but the `formxml` payload differs on every single redeploy even when nothing logically changed — a minor deviation from the "idempotent provisioning" intent (harmless functionally as far as could be determined statically, but worth pinning to a stable, deterministic id derived from the control's purpose instead of a fresh random one per run).

3. **CI hygiene — 3 transitional `CustomApiContractTests` failures merged red (see point 8 verdict below).**

### SUGGESTION

1. **No automated contract test pins `SecuritySpec.TemplateToJobRelationshipSchemaName` against `solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml`.** This review verified it manually and it is correct today, but a cheap test in the same style as `CustomApiContractTests` (parse the XML, assert the constant matches an `EntityRelationship/@Name`) would catch future drift on Linux/CI without needing Dev access — cheaper than relying on `SecurityAssertions.AssertCascade` at E2E time only.

2. **`SecurityProvisioner.UpsertRoles` (`tools/Smelter.Deploy/SecurityProvisioner.cs:82-110`) only sets `SolutionUniqueName` on the `Create` branch.** If a role with the same name+BU already exists in the target environment but was never added to `smlt_core_smelter` (plausible given the audit's own finding that a v1 attempt left "roles vacíos"), re-running `Smelter.Deploy` will apply privileges but silently skip adding it to the solution. An explicit (idempotent, no-op-if-present) `AddSolutionComponentRequest` in the "existing role" branch would close this gap deterministically.

---

## Verdict on Point 8 (3 transitional test failures — merge red or skip-with-reason?)

**Current state**: merged red, documented extensively in `tasks.md` (6.2) and `ApiSpec.cs`, called out by name in the orchestrator's briefing as a known, accepted, "not to be relitigated" decision.

**Recommendation: convert to an explicit, documented exclusion (skip-with-reason) rather than leaving them red**, with the following tradeoff:

| Option | Pro | Con |
|---|---|---|
| **Merge red (current)** | Zero extra code; the failure count is a literal, undeniable signal that Phase 9's gate condition ("`solutions/unpacked/` reflects `sanic_...`") isn't met yet — self-clearing once `solution-sync` runs post-deploy. | Every CI run on `main` shows 3 failing tests for the foreseeable future (until the first `backend-deploy`+`solution-sync`) — normalizes "red is sometimes OK," which is exactly the kind of erosion that lets a *real* regression hide next to expected noise. `dotnet test` exit code stays non-zero, which is awkward if any CI gate treats Contract.Tests as a hard blocker. |
| **Skip-with-reason** | CI stays green and trustworthy; a real regression elsewhere in Contract.Tests would stand out immediately. Self-documenting via the skip reason string. | xUnit 2.9 `[Theory]`/`[MemberData]` doesn't support a per-data-row `Skip` without either adding `Xunit.SkippableFact` as a new dependency or filtering the 3 known cases out of `ApiUniqueNames()` with an inline comment/TODO pointing at Phase 9 (task 9.4 already plans to touch the *rename* contract test at that point, so extending that plan to also un-filter `CustomApiContractTests` is a small addition, not new scope). Risk of the filter being forgotten if Phase 9 is delayed indefinitely — mitigate by dating the TODO and having `Smelter.Verify`'s Phase 7 asserts (already green-gating Phase 9) as the real safety net regardless of what Contract.Tests reports. |

Given this repo's own stated bar ("100% vía SDK, idempotente, CI... verificado con `tools/Smelter.Verify`") and that Strict TDD Mode is active project-wide, I'd lean towards filtering `sanic_capi_smelter_processchunk` out of `CustomApiContractTests.ApiUniqueNames()` with a dated comment ("remove when Phase 9 lands — see tasks.md 6.2/9.4") rather than three permanently-red assertions — but this is **not a blocker for archive**: it's a CI-hygiene call, not a correctness defect, and the current state is honestly documented rather than silently swept under the rug. WARNING level, not CRITICAL.

---

## Verdict

**PASS WITH WARNINGS**

The implementation is thorough, well-documented, and the privilege matrix, cascade, ExecutePrivilegeName gating, and the one engine code change (`PrepareImportHandler`) are all correct and match the specs exactly, with solid TDD evidence and passing tests everywhere they're runnable in this sandbox. All "incomplete" tasks are legitimately blocked by the documented environment constraint (no live Dataverse) or correctly gated (Phase 9). However, one real, non-environment-dependent bug was found — the `CloudFlowUpdater` substring-collision self-corruption — that will break the very Cloud Flow the change exists to fix, on the second deploy run, and that bug is currently untested and would likely slip past `Smelter.Verify`'s existing assert too. **Recommend fixing `CloudFlowUpdater`'s match logic (and adding a regression test) before or immediately after the first `backend-deploy`, ideally before task 6.4/7.7 are unblocked in CI**, since those are exactly the tasks that will trigger the second deploy run where this bug manifests.

---

## Re-Audit (2026-07-28) — Fix Verification

Scope: **only** the 5 points flagged in the first pass, per the coordinator's request. Full audit not repeated.

### 1. CRITICAL — CloudFlowUpdater → `ClientDataProcessChunkRenamer.cs` (new file)

**Verdict: FIXED, correctly.**

- `tools/Smelter.Deploy/ClientDataProcessChunkRenamer.cs:35-36` — `BuildTokenPattern` wraps the escaped uniquename in `(?<![A-Za-z0-9_])...(?![A-Za-z0-9_])` (negative lookbehind/lookahead on identifier characters). This is the correct fix: `"anic_capi_smelter_processchunk"` inside `"sanic_capi_smelter_processchunk"` is always preceded by `'s'` (a word char), so the lookbehind blocks the false match that caused the original bug. Verified by hand-tracing both the old-token and new-token patterns against `"sanic_..."` — neither matches the embedded substring, only a truly standalone token.
- **Covers the 2nd-run-no-op case**: `Rename_ClientDataAlreadyWithNewApi_IsNoOp_RegressionForTheDoubleSBug` (`ClientDataProcessChunkRenamerTests.cs:40-52`) is the exact regression test for the original bug — `changed` is `false` and the output is byte-identical to the input. `CloudFlowUpdater.UpdateOne` (`CloudFlowUpdater.cs:86-91`) checks `changed` and returns early, skipping `Update`/`SetState` entirely — confirmed by reading the code, not just the test.
- **Edge case: longer identifier containing the token** (e.g. `xanic_capi_smelter_processchunk_v2` or `anic_capi_smelter_processchunk_legacy`) — correctly excluded by the same lookbehind/lookahead boundaries (traced by hand: any adjacent `[A-Za-z0-9_]` on either side blocks the match). No test explicitly covers a *longer-identifier* edge case (only "no reference" and the old/new pair are tested in `ReferencesOldToken_OnlyMatchesStandaloneOldName...`), but the regex construction makes this structurally safe, not just test-safe — SUGGESTION below.
- **Case sensitivity**: no `RegexOptions.IgnoreCase` — correct, since Dataverse uniquenames and the connector URL segment are case-sensitive by contract; an accidental case-insensitive match could rewrite an unrelated identifier.
- **LIKE filter in the query** (`CloudFlowUpdater.cs:61` and `SecurityAssertions.cs:180`): both still use a broad substring `LIKE '%anic_capi_smelter_processchunk%'` / `LIKE '%capi_smelter_processchunk%'` at the Dataverse query level (FetchXML/QueryExpression `LIKE` has no word-boundary support). This is **intentionally** a superset pre-filter, not the actual decision — the real exact-token logic runs client-side afterward (`Rename`/`ReferencesNewToken`/`ReferencesOldToken`/`HasDoubleSCorruption`). Confirmed safe: since `"sanic_..."` always contains `"anic_..."` as a substring, this broad filter can only over-fetch candidate flows, never under-fetch (no coverage gap), and the precise token check downstream prevents any false action. Not a bug.
- **SUGGESTION (new, minor)**: `DoubleSCorruptionPattern` (`ClientDataProcessChunkRenamer.cs:31-33`) only repairs a *single* extra leading `"s"` (`"ssanic_..."` → `"sanic_..."`). I hand-traced a hypothetical `"sssanic_..."` (would only exist if the *original* buggy `string.Replace` had run twice against the same flow before this fix ever shipped): `HasDoubleSCorruption`, `ReferencesNewToken`, and `ReferencesOldToken` **all** return `false` for that string (the lookbehind rejects every candidate position), so `Rename` treats it as an unrecognized no-op rather than repairing it. This is very low risk in practice — per `tasks.md`, no real `backend-deploy` has run against Dev yet (6.4/7.7 still blocked), so the original bug never had a chance to corrupt a real flow even once, let alone twice. And critically, it **fails safe**: `SecurityAssertions.AssertProcessChunkCloudFlowReferencesNewApi`'s first assert (`ReferencesNewToken`) would still throw for a triple-s string (since it also returns `false`), so `Smelter.Verify` would still catch and report it — just with a slightly misleading message ("never migrated" instead of "corrupted"). Not a blocker; consider generalizing the repair regex to `s+` before the token if you want it airtight.

### 2. WARNING — `SecurityAssertions.cs` 3-assert check

**Verdict: FIXED, correctly.**

`AssertProcessChunkCloudFlowReferencesNewApi` (`SecurityAssertions.cs:170-216`) now asserts, per flow, all three:
- `ReferencesNewToken(clientData)` must be true (line 204) — catches "never migrated."
- `!ReferencesOldToken(clientData)` (line 207) — catches **residual old reference** (partial rename), directly answering the coordinator's question.
- `!HasDoubleSCorruption(clientData)` (line 210) — catches the specific `"ssanic_..."` corruption pattern.

All three reuse `ClientDataProcessChunkRenamer`'s exact same regex logic (no reimplementation/drift risk). This closes the original blind spot completely for the corruption shape the bug actually produces. The only residual gap is the same triple-s edge case noted above, which still fails loudly via the `ReferencesNewToken` assert rather than passing silently — acceptable.

### 3. WARNING — Dashboard control `uniqueid`

**Verdict: FIXED, correctly.**

`SecuritySpec.cs:185-186` — `DashboardActiveJobsControlUniqueId`/`DashboardCompletedWithErrorsControlUniqueId` are now fixed string constants (`"64a99bb5-..."`, `"d9be5fbb-..."`), consumed by `SecurityProvisioner.cs:454-457` and threaded into `BuildDashboardCell(..., uniqueId)` (line 464/467) instead of a fresh `Guid.NewGuid()` per run. Re-running `UpsertDashboardSubArea` now produces byte-identical `formxml` when nothing logically changed — idempotency concern fully resolved.

### 4. CI-hygiene — `PendingSolutionSync` exclusion

**Verdict: FIXED, correctly scoped.**

`CustomApiContractTests.cs:22-25` — `PendingSolutionSync` is a `HashSet<string>` containing **only** `"sanic_capi_smelter_processchunk"`, with a dated comment pointing at `tasks.md` 6.2/9.4 explaining exactly when to remove it. `ApiUniqueNames()` (lines 27-34) filters this single entry out of the theory data for all 3 test methods; the other 4 APIs (`prepareimport`, `anic_...processchunk` legacy, `exporttemplate`, `importtemplate`) are untouched and still run through `Api_ExistsInSolutionMetadata`/`Api_RequestParams_MatchSolutionMetadata`/`Api_ResponseProps_MatchSolutionMetadata` normally. Confirmed by execution: **56/56 passed**, 0 skipped, 0 excluded-silently (the exclusion is visible in source, not hidden). This is exactly the recommended approach from the original report (dated, scoped, self-documenting) rather than the standing-red alternative.

### 5. SUGGESTIONS — RelationshipSchemaContractTests + EnsureRoleInSolution

**Verdict: both FIXED, correctly.**

- `RelationshipSchemaContractTests.cs` — two tests parse `solutions/unpacked/Other/Relationships/sanic_tbl_smlt_importtemplate.xml` directly and assert `SecuritySpec.TemplateToJobRelationshipSchemaName` exists as an `<EntityRelationship Name="...">` (line 21-32) **and** that it's `OneToMany` with `ReferencingEntityName=ImportJob`/`ReferencedEntityName=ImportTemplate` (line 34-46) — matches this reviewer's manual verification from the first pass exactly, now automated and Linux/CI-runnable without Dev access.
- `EnsureRoleInSolution` (`SecurityProvisioner.cs:125-161`) — on the "role already exists" branch, queries `solutioncomponent` for `(solutionid, objectid=roleId, componenttype=20/Role)` and only calls `AddSolutionComponentRequest` if absent (lines 140-161). Correctly reconciles the orphan-role scenario (a role created outside `smlt_core_smelter`, e.g. a stale v1 attempt) without duplicating solution components on repeat runs. Closes the gap exactly as suggested.

### Numbers confirmed (this session)

| Command | Result |
|---|---|
| `dotnet test tests/Smelter.Contract.Tests -c Release` | ✅ **56/56 passed**, 0 failed, 0 skipped |
| `dotnet test tests/Smelter.Core.Tests -c Release` | ✅ **49/49 passed** |
| `dotnet build Smelter.sln -c Release` | ✅ 0 Warnings, 0 Errors |

Both expected numbers from the coordinator's brief (56/56, 49/49) match exactly.

### Final Verdict

**PASS.** All CRITICAL and WARNING items from the first pass are fixed and verified — the fixes are correct, well-tested (9 new regression tests for the exact bug shape, 2 new contract tests for the relationship schema), and don't introduce new correctness issues. One new low-severity SUGGESTION was found (triple-s corruption edge case in `ClientDataProcessChunkRenamer`) — not a blocker: it requires a real-world precondition (the original bug running twice against the same live flow) that never occurred in this repo's actual deploy history, and even in that scenario `Smelter.Verify` still fails loudly rather than passing silently. No new CRITICAL or WARNING findings. Safe to proceed to `sdd-archive` once the (still-blocked, environment-gated) tasks 6.4/7.7 confirm green against real Dev.
