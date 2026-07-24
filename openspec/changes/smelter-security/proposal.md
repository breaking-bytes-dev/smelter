# Proposal: Smelter Security (roles + team-per-template)

## Intent

Hoy Smelter no tiene seguridad: cualquiera con acceso puede configurar templates y ver todo. Se necesita
separar **quién configura** de **quién solo opera**: configuradores que crean/editan templates y ven todo,
y operadores que solo cargan datos (crean Import Jobs) y ven logs, **viendo únicamente los templates que
tienen asignados**. La asignación se modela con **equipos por template** (ownership).

## Scope

### In Scope
- Rol de seguridad **Smelter Configurador**: CRUD Org sobre Template + mappings; ve todo.
- Rol de seguridad **Smelter Operador**: Create/Read Import Job, Read logs, Read mappings (Org);
  Read Template a nivel **Basic/Usuario**; SIN create/write/delete de config.
- Modelo de asignación: **team por template** (compartido con/owned por el team) + membresía de operadores.
- **Cascade sharing** en la relación Template→Import Job (Share/Reparent = Cascade All): los jobs hijos
  heredan el acceso del template padre → operadores del team ven todos los jobs de ese template. **Sin código.**

### Out of Scope
- Cambios al motor de import (`PrepareImport`/`ProcessChunk`) — corre como SYSTEM, no se toca.
- Seguridad a nivel campo (field-level security).
- Aprobaciones/flujos de autorización.
- Gating fino de las Custom APIs Export/Import por privilegio (se documenta como opción; default: no exponer el botón al operador).

## Capabilities

### New Capabilities
- `smelter-access-control`: roles Configurador/Operador, visibilidad de templates por team ownership, y auto-asignación del Import Job al team de su template para compartir la visibilidad de jobs/logs.

### Modified Capabilities
- None (no cambia el comportamiento de import; es acceso/ownership).

## Approach

Usar el modelo de seguridad nativo de Dataverse, **sin código**. El template se comparte con un team del que
los operadores son miembros; con **Read Basic** sobre Import Template lo ven (el Configurador con **Read
Organization** ve todos). Para que el operador vea **todos los jobs de su template**, se configura
**cascade sharing** en la relación Template→Import Job (Share/Reparent = Cascade All): verificado que los
hijos creados DESPUÉS del share heredan el acceso del padre (POA). Los mappings quedan Read Org (config no
sensible); la imposibilidad de configurar viene de no tener create/write/delete sobre Template/mappings.

> **Cambio de diseño:** se descartó el plugin de auto-asignación (sobre-ingeniería). El cascade sharing
> nativo logra lo mismo sin código.

## Affected Areas

| Área | Impacto | Descripción |
|------|---------|-------------|
| Relación Template→Import Job | Config | Share/Reparent = Cascade All (los jobs heredan el acceso del template) |
| Roles de seguridad (solución) | New (config) | Smelter Configurador, Smelter Operador |
| Teams + sharing + membresía | New (config) | Template compartido con el team; operadores miembros |
| Código (plugins/motor) | Reuse | **Sin cambios. Este cambio NO tiene código.** |

## Risks

| Riesgo | Prob. | Mitigación |
|--------|-------|------------|
| Operador no ve jobs de otros del mismo template | Med | El plugin reasigna el job al team → visibilidad por membresía |
| Operador con privilegios para llamar Export/Import API | Med | No exponer el botón al operador; opcional: Execute Privilege en la Custom API |
| Operador no puede subir el archivo / vincular template | Med | Otorgar privilegio de File/Note + Append/AppendTo en el rol Operador |
| Template user-owned (no team) | Low | El plugin no reasigna; documentar que los templates compartidos deben ser team-owned |
| Reasignación falla si el lookup de template está vacío | Low | El plugin valida y no hace nada si no hay template/owner-team |

## Rollback Plan

Config reversible: quitar los 2 roles de seguridad y la membresía de teams; el ownership de templates/jobs
vuelve a manejarse a mano. El plugin es aditivo: **desregistrar el step Post-Op Create** y quitar el plugin
type. No hay cambio de esquema ni migración de datos. Sin los roles, todo sigue funcionando como hoy.

## Dependencies

- Las tablas de Smelter y la MDA existen.
- (Opcional) El cambio `template-export-import` desplegado, si se quiere gatear sus botones por rol.

## Success Criteria

- [ ] Un usuario con rol **Operador** NO puede crear ni editar templates/mappings (botones/forms bloqueados).
- [ ] El Operador ve SOLO los templates de los teams a los que pertenece; el Configurador ve todos.
- [ ] Al crear un Import Job, queda owned por el team del template; otro operador del mismo team lo ve.
- [ ] El Operador puede crear un Import Job, subir el CSV, ejecutar y ver los logs de sus templates.
- [ ] Tests xUnit cubren la lógica de auto-asignación (con team, sin team, sin template).
