# Design: Smelter Security (config-only)

## Technical Approach

Seguridad 100% sobre el modelo nativo de Dataverse, **sin código**. Dos **roles** + visibilidad de templates
por team (sharing/ownership) + **cascade sharing** en la relación Template→Import Job para que los jobs hijos
hereden el acceso del template padre. El motor de import no se toca (corre SYSTEM). Implementa
`smelter-access-control`.

## Architecture Decisions

| Decisión | Elegido | Alternativa | Rationale |
|----------|---------|-------------|-----------|
| Visibilidad de jobs del team | **Cascade Share/Reparent** en Template→Import Job | Plugin que reasigna el job al team (Post-Op Create) | Verificado (MS Learn): los hijos creados DESPUÉS del share heredan el acceso del padre vía POA. Nativo, cero código. El plugin era sobre-ingeniería. |
| Visibilidad de templates | Template compartido con / owned por el team + Read Basic | Sharing 1×1 por usuario | El team agrupa operadores; el cascade necesita que el team tenga acceso al template |
| Mappings para el Operador | Read nivel **Organización** | Read Basic / sin acceso | Config no sensible; el operador necesita leerlos; la restricción real es Template + no-write |
| Quién restringe configurar | Ausencia de create/write/delete en el rol Operador | Lógica custom | Nativo |
| Gating Export/Import API | No exponer el botón al Operador (default) | Execute Privilege en la Custom API | Más simple |

## Data Flow

```
Configurador comparte Template T con Team A  (relación T→Job: Share/Reparent = Cascade All)
        │
Operador O1 (miembro de Team A) crea Import Job J bajo T
        │  cascade/reparent inheritance (POA)
        ▼
J hereda el acceso de T → Team A tiene acceso a J
        ▼
Operador O2 (miembro de Team A) ve J y sus logs   ← sin plugin, nativo
```

## File Changes

| Archivo | Acción | Descripción |
|---------|--------|-------------|
| (ninguno) | — | **Cambio sin código.** Todo es configuración de portal/solución. |
| Relación Template→Import Job | Config | Share = Cascade All; Reparent = Cascade All |
| Roles de seguridad (solución) | New (config) | Smelter Configurador, Smelter Operador |
| Teams + sharing + membresía | New (config) | 1 team por template (o grupo); template compartido con el team; operadores miembros |

## Interfaces / Contracts

No aplica — no hay código nuevo. La relación clave: el lookup `sanic_smlt_ij_importtemplateid` (Import Job →
Import Template) es la que lleva el cascade sharing.

## Testing Strategy

| Capa | Qué | Cómo |
|------|-----|------|
| Manual (E2E) | Operador no puede configurar | Login como Operador → intentar crear template → bloqueado |
| Manual (E2E) | Visibilidad de templates por team | Operador ve solo templates de sus teams |
| Manual (E2E) | **Cascade sharing de jobs** | O1 crea un job DESPUÉS del share → O2 (mismo team) lo ve |
| Manual (E2E) | Configurador ve todo | Login como Configurador → ve todos |

No hay unit tests (no hay código). La verificación es manual en el entorno.

## Migration / Rollout

Sin código, sin esquema, sin migración. Pasos: (1) configurar el cascade en la relación Template→Import Job;
(2) crear los 2 roles; (3) crear teams, compartir templates con sus teams, agregar operadores como miembros;
(4) asignar roles. Rollback: revertir el cascade a su default, quitar roles/teams/shares. Sin la config,
todo funciona como hoy.

## Open Questions

- [ ] Tipo de cascade exacto (Cascade All vs Cascade Active) — confirmar en el smoke test cuál da la visibilidad esperada sin sobre-compartir.
- [ ] ¿Gatear las Custom APIs Export/Import por Execute Privilege o basta con no dar el botón al Operador? (default: no botón).
- [ ] Privilegios exactos del Operador para subir el archivo (File/Note) y Append/AppendTo — confirmar al construir el rol.
- [ ] Caveat POA: a volúmenes normales de Smelter es irrelevante; documentar si el volumen de jobs crece mucho.
