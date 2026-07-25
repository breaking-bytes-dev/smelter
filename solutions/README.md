# Solución de Power Platform — `smlt_core_smelter`

Los **artefactos de la solución** (zips managed/unmanaged) NO se versionan en git: se publican como
**[GitHub Releases](../../releases)** con tag `sol-vX.Y.Z.B`, exportados desde Dev por el workflow
[`solution-release`](../.github/workflows/solution-release.yml) (un botón, con el Service Principal).

Lo que SÍ se versiona acá es la **metadata no-código** de la solución:

```
solutions/
└── unpacked/     # tablas, columnas, forms, vistas, roles, Custom APIs, flows… (XML diffeable)
```

`solutions/unpacked/` lo mantiene el workflow [`solution-sync`](../.github/workflows/solution-sync.yml):
al publicarse un Release, descarga el zip unmanaged, lo desempaqueta con `pac solution unpack`, poda los
binarios (dll/nupkg — el código ya vive en `src/`) y commitea. Así, **cada cambio de la solución queda
como un diff en git** (¿quién cambió este form? ¿cuándo apareció esta columna? → `git log`).

## Cómo publicar una versión

1. Actions → **solution-release** → *Run workflow* → ingresá la versión (ej. `1.0.0.1`).
2. El workflow exporta managed+unmanaged de Dev, crea el Release `sol-v1.0.0.1` con ambos zips, y
   sincroniza `solutions/unpacked/`.
3. Actualizá el `CHANGELOG.md` (mover lo de `[Unreleased]` a la nueva versión, linkeando el Release).

## Secrets

Los workflows usan los **organization secrets** de `breaking-bytes-dev`:
`DATAVERSE_URL`, `PP_CLIENT_ID`, `PP_TENANT_ID`, `PP_CLIENT_SECRET`.
El Service Principal debe estar como application user en Dev con permiso de exportar la solución.
