[← Volver al índice de documentación](../README.md)

# Guía paso a paso — Seguridad de Smelter (roles + cascade sharing)

> Implementación de portal del feature **smelter-security**. **NO hay código** — es 100% configuración.
> Separa **Configuradores** (crean/editan templates, ven todo) de **Operadores** (solo cargan datos y ven
> logs, viendo solo sus templates asignados).
>
> **Tiempo estimado:** 45–60 min. **Herramienta:** make.powerapps.com (+ Settings clásico para roles si hace falta).

---

## Cómo funciona (el modelo, en 3 piezas)

1. **Templates compartidos con un Team** → los operadores miembros ven solo esos templates.
2. **Cascade sharing** en la relación Template→Import Job → los jobs creados bajo un template **heredan** el
   acceso del template. Así un operador ve los jobs de sus templates (de cualquier operador del team), pero
   no los de templates que no tiene.
3. **Dos roles de seguridad** → el Operador NO puede configurar; el Configurador puede todo.

> 🔑 **Insight:** el motor de Smelter corre como **SYSTEM**, así que NADA de esta config puede romper un
> import. La seguridad solo controla qué ve y qué toca el usuario en la app.

---

## PASO 1 — Cascade sharing en la relación Template → Import Job

Esto es lo que hace que los jobs hereden el acceso del template.

1. [make.powerapps.com](https://make.powerapps.com) → **Solutions** → abrí tu solución de Smelter.
2. Abrí la tabla **Import Job** (`sanic_tbl_smlt_importjob`) → pestaña **Relationships**.
3. Abrí la relación **Many-to-one** hacia **Import Template** (la del lookup `sanic_smlt_ij_importtemplateid`).
4. Expandí **Advanced options** (comportamiento de la relación).
5. Cambiá el **Type of behavior** a **`Configurable Cascading`**.
6. Seteá:
   - **Share:** `Cascade All`
   - **Reparent:** `Cascade All`  ← (clave: hace que los jobs NUEVOS hereden el acceso del template)
   - **Assign:** `Cascade None` (dejalo así)
   - **Delete:** `Remove Link` (NO uses Cascade en Delete: borrar un template no debería borrar sus jobs)
   - **Unshare:** `Cascade All` (para que al des-compartir el template también se quite de los jobs)
7. **Save** y **Publish**.

> ⚠️ Si elegís `Parental` en vez de `Configurable Cascading`, OJO: Parental cascadea TODO incluido Delete
> (borrar el template borraría sus jobs). Por eso usamos Configurable Cascading.

---

## PASO 2 — Crear el rol "Smelter Configurador"

> Si la UI moderna no te muestra el detalle de privilegios, usá el editor clásico:
> Settings (engranaje) → **Advanced Settings** → Settings → Security → **Security Roles** → New.
> Niveles (círculo): ⚪ None · 🔵 User (Basic) · 🟢 Organization.

1. Creá un rol nuevo: **Name** = `Smelter Configurador`.
2. En la pestaña **Custom Entities**, ubicá las tablas de Smelter y seteá:

| Tabla | Create | Read | Write | Delete | Append | Append To | Assign | Share |
|-------|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Import Template | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 |
| Table Mapping | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 |
| Column Mapping | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 |
| Relationship Mapping | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 |
| Import Job | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 |
| Import Job Log | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | 🟢 | — | — |

3. **Save and Close**.

---

## PASO 3 — Crear el rol "Smelter Operador"

1. Creá un rol nuevo: **Name** = `Smelter Operador`.
2. Seteá los privilegios EXACTOS (lo que NO está, queda en ⚪ None):

| Tabla | Create | Read | Write | Delete | Append | Append To |
|-------|:--:|:--:|:--:|:--:|:--:|:--:|
| Import Template | ⚪ | 🔵 User | ⚪ | ⚪ | ⚪ | 🔵 User |
| Table Mapping | ⚪ | 🟢 Org | ⚪ | ⚪ | ⚪ | ⚪ |
| Column Mapping | ⚪ | 🟢 Org | ⚪ | ⚪ | ⚪ | ⚪ |
| Relationship Mapping | ⚪ | 🟢 Org | ⚪ | ⚪ | ⚪ | ⚪ |
| Import Job | 🟢 Org | 🔵 User | 🔵 User | ⚪ | 🔵 User | 🔵 User |
| Import Job Log | ⚪ | 🔵 User | ⚪ | ⚪ | ⚪ | ⚪ |

**Por qué cada uno:**
- **Import Template Read = User (Basic):** ve solo los templates compartidos con sus teams. **Append To** =
  permite que un Import Job referencie el template (lookup).
- **Mappings Read = Org:** config no sensible; el operador necesita leerlos. Sin Write → no puede configurar.
- **Import Job:** Create + Read/Write Basic (sus jobs + los heredados del template) + Append/Append To (para
  setear el lookup al template y subir el archivo).
- **Import Job Log Read = Basic:** ve los logs de los jobs que ve (heredan del template). Los logs los crea
  el plugin como SYSTEM → el operador NO necesita Create.

3. **Miscellaneous / privilegios de mensaje** (pestaña Customization o Business Management según versión):
   asegurate de que el rol tenga permitido **subir archivos** (las operaciones de columna File). En general
   con Write sobre Import Job alcanza; si el upload falla, revisá los privilegios de `Note`/`Attachment`.
4. **Save and Close**.

> 🔒 **Export/Import de templates (botones del otro feature):** son acción de configuración. Para que el
> Operador NO los use, simplemente **no le pongas esos botones en su vista**, o configurá un Execute
> Privilege en las Custom APIs. (Opcional.)

---

## PASO 4 — Teams: compartir templates y agregar operadores

1. **Crear un Team** (Advanced Settings → Settings → Security → **Teams** → New):
   - **Name:** ej. `Cobranza - Operadores` (o uno por template/grupo).
   - **Team Type:** `Owner`.
   - **Administrator:** vos.
2. **Agregar miembros:** abrí el team → **Members** → **Add** → seleccioná los usuarios operadores.
3. **Asignar el rol al team (o a cada usuario):** en el team → **Security Roles** (Manage Roles) → tildá
   **Smelter Operador**. *(O asigná el rol directo a cada usuario; lo importante es que los operadores tengan
   el rol Smelter Operador.)*
4. **Compartir cada template con el team:**
   - Andá a la vista de **Import Templates** (como Configurador/admin).
   - Seleccioná el template → botón **Share** (icono de compartir).
   - Agregá el **Team** → otorgá al menos **Read** (y **Append To** si la UI lo ofrece).
   - **Share**.
   - 🔁 Gracias al cascade del Paso 1, todos los Import Jobs de ese template (presentes y futuros) quedan
     compartidos con el team automáticamente.

> Repetí el paso 4.4 por cada template, asignándolo al team de los operadores que deben usarlo. Ese es el
> acto de "asignar un template a un operador".

5. **Asignar el rol Configurador** a los usuarios que configuran (Advanced Settings → Users → seleccionar →
   Manage Roles → **Smelter Configurador**).

---

## PASO 5 — Verificación

Probá con un usuario **Operador** de prueba (que tenga solo el rol Smelter Operador y sea miembro de UN team):

- [ ] **5.1 No puede configurar:** abrí un Import Template → intentar editar/crear → debe estar bloqueado
      (campos read-only / sin botón New).
- [ ] **5.2 Ve solo lo asignado:** en la vista Import Templates, ve SOLO los templates compartidos con su
      team. Creá un template asignado a OTRO team → el operador NO debe verlo.
- [ ] **5.3 Carga datos:** crea un Import Job, elige un template asignado, sube el CSV, ejecuta → procesa OK
      y ve los logs.
- [ ] **5.4 Visibilidad de team (la prueba del cascade):** con un SEGUNDO operador del MISMO team, verificá
      que ve el job que creó el primero (y sus logs). Creá un job NUEVO y confirmá que el segundo operador lo
      ve → esto valida que el cascade aplica a jobs creados DESPUÉS del share.
- [ ] **5.5 Configurador:** con un usuario Configurador, confirmá que ve TODOS los templates, jobs y logs, y
      que puede crear/editar.

---

## PASO 6 — Troubleshooting

| Síntoma | Causa probable | Solución |
|---------|----------------|----------|
| Operador no ve NINGÚN template | El template no está compartido con su team / no es miembro | Compartí el template con el team (Paso 4.4) y verificá membresía |
| Operador no ve los jobs de otro operador | Cascade no configurado o Reparent ≠ Cascade All | Revisá Paso 1: Share y **Reparent** = Cascade All en la relación |
| Operador ve TODOS los templates | Import Template Read quedó en Org, no User | Bajalo a 🔵 User (Basic) en el rol Operador |
| Operador no puede subir el CSV | Falta Write/privilegio de archivo | Confirmá Write en Import Job + privilegios de Note/Attachment |
| Operador puede editar templates | Tiene Write/Create en Template o un rol extra | Quitá esos privilegios; verificá que no tenga otro rol amplio |
| Jobs viejos no se ven tras compartir | El share se hizo antes de configurar el cascade | Re-compartí el template (o configurá cascade primero, después compartí) |

---

## Resumen
```
1. Relación Template→Job: Share=Cascade All, Reparent=Cascade All
2. Rol Configurador (Org CRUD, ve todo)
3. Rol Operador (Template Read Basic, mappings Read Org, Job CRUD básico, sin configurar)
4. Team por grupo → compartir templates con el team → agregar operadores como miembros → asignar roles
5. Verificar: operador no configura, ve solo lo asignado, el team ve los jobs (cascade)
```

> **Caveat de escala:** el cascade sharing escribe en la tabla **PrincipalObjectAccess (POA)**. A volúmenes
> normales de Smelter (jobs por import) es irrelevante. Si algún día el volumen de jobs crece muchísimo,
> revisá el tamaño de POA (admin center).
