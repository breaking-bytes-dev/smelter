# Tasks: Smelter Security (config-only)

> **Sin código.** 100% configuración de portal (roles + sharing + cascade). El plugin de auto-asignación
> se DESCARTÓ: la visibilidad de jobs se logra con **cascade sharing** nativo (los jobs hijos heredan el
> share del template padre). Guía paso a paso.

## Phase 1: Relación Template→Import Job con cascade sharing

- [ ] 1.1 Maker portal → tabla Import Job → relación **N:1 a Import Template** (`sanic_smlt_ij_importtemplateid`) → editar comportamiento.
- [ ] 1.2 Setear **Share** = `Cascade All` y **Reparent** = `Cascade All` (o Cascade Active según preferencia).
- [ ] 1.3 (Opcional) Revisar Assign/Delete cascade — para Smelter dejarlos en su default es suficiente.
- [ ] 1.4 Publicar.

## Phase 2: Roles de seguridad (paso a paso)

> Settings → Security → Security Roles → New (en la solución). Niveles: 🟢 Organización, 🔵 Usuario/Basic, ⚪ None.

- [ ] 2.1 **Smelter Configurador**: Import Template + Table/Column/Relationship Mapping → 🟢 Create/Read/Write/Delete/Append/AppendTo. Import Job + Import Job Log → 🟢 Read (Create/Write opcional).
- [ ] 2.2 **Smelter Operador — config**: Import Template → 🔵 **Read** (Basic), SIN Create/Write/Delete. Table/Column/Relationship Mapping → 🟢 **Read** (Org), SIN Create/Write/Delete.
- [ ] 2.3 **Smelter Operador — jobs/logs**: Import Job → Create + 🔵 Read + 🔵 Write + Append + AppendTo. Import Job Log → 🔵 Read. (Read Basic alcanza: los jobs heredan el share del template.)
- [ ] 2.4 **Smelter Operador — archivo**: privilegios para subir la columna File del Import Job (y Note/Attachment si aplica).
- [ ] 2.5 (Opcional) Gating de las Custom APIs Export/Import: no exponer el botón al Operador (o Execute Privilege).

## Phase 3: Teams + asignación

- [ ] 3.1 Crear un **Team** por template (o por grupo de operadores que comparten templates).
- [ ] 3.2 **Compartir** cada Import Template con su team — Read (y lo que corresponda). *(O hacer el template team-owned; lo importante es que el team tenga acceso al template para que el cascade propague a los jobs.)*
- [ ] 3.3 Agregar los operadores como **miembros** del/los team(s).
- [ ] 3.4 Asignar roles: Configuradores → **Smelter Configurador**; operadores → **Smelter Operador**.

## Phase 4: Verificación

- [ ] 4.1 Operador de prueba: intentar crear/editar un template → **bloqueado**.
- [ ] 4.2 El Operador ve **solo** los templates de sus teams.
- [ ] 4.3 O1 crea un Import Job + sube CSV + ejecuta → un **segundo** operador del mismo team **ve el job y sus logs** (confirma el cascade sharing).
- [ ] 4.4 Configurador: ve **todos** los templates, jobs y logs; puede configurar.
- [ ] 4.5 Confirmar que el cascade aplica a jobs creados DESPUÉS del share (crear un job nuevo y verificar visibilidad del team).

## Notas
- **Cero código.** Aditivo y reversible: quitar cascade, roles, teams y shares.
- Caveat: el cascade sharing escribe en la tabla POA; a volúmenes normales de Smelter es irrelevante.
- Insight: el motor corre como SYSTEM → la seguridad nunca rompe el procesamiento; solo controla acceso en la app.
