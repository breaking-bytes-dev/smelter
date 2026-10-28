[← Volver al índice de documentación](../README.md)

# Smelter — Análisis de problemas

> Análisis del código realizado el **2026-06-01**. Severidad de mayor a menor.
> Marcá `[x]` a medida que resolvemos cada punto.

## Cómo usar este documento
- Cada ítem tiene: severidad, ubicación (`archivo:línea`), descripción y por qué importa.
- El orden de resolución recomendado está al final.

---

## 🔴 Críticos

### [x] #1 — La normalización NO funciona: las relaciones nunca se cargan ✅ (chunked-import-redesign: `ImportContextLoader.LoadRelationshipMappings`)
- **Ubicación:** `src/Smelter.Plugins/ImportProcessorPlugin.cs:25` (constante declarada) / `LoadImportContext` (nunca se consulta).
- **Problema:** `RelationshipMappingTable` se declara pero nunca se hace `RetrieveMultiple`. `importContext.RelationshipMappings` queda **siempre vacío**.
- **Consecuencias:**
  - `DependencyResolver.cs:29-30` retorna las tablas sin orden topológico real (el DAG no hace nada).
  - `NormalizationEngine.cs:150-152` → `parentRelations` siempre vacío → **los lookups padre-hijo nunca se resuelven**.
- **Por qué importa:** Es la razón de existir del producto. Sin esto, se crean registros sueltos sin relacionar.

### [x] #2 — Claves de dedup construidas de forma inconsistente (carga vs chequeo) ✅ (eliminado: upsert por alternate key en `UpsertWriter`)
- **Ubicación:** `DedupService.cs:107-110` (preload, valores crudos de Dataverse) vs `NormalizationEngine.cs:265` (valores coercionados) vs `NormalizationEngine.cs:243-251` (lookup padre con strings crudos del source).
- **Problema:** Tres formas distintas de derivar la misma clave. `Money.ToString()`, `EntityReference.ToString()` (devuelve el tipo, no el GUID) y `OptionSetValue.ToString()` no matchean los valores crudos.
- **Extra:** `DedupService.cs:51-57` aplica `ToLowerInvariant()` ("ABC" == "abc") y `string.Join("|")` sin escapar (colisión si un valor tiene `|`).
- **Por qué importa:** Aunque se arregle #1, el dedup y los lookups siguen rotos para todo lo que no sea string puro.

### [x] #3 — El modelo de timeout/auto-continue choca con el límite real del sandbox ✅ (chunked: ProcessChunk = request fresco, resetea depth/2min)
- **Ubicación:** `docs/architecture.md:70-72` (afirma ~2h, falso) / `NormalizationEngine.cs:42` (guard de 90s) / `ProgressReporter.cs:162-175` (re-queue).
- **Problema:**
  - Los plugins en sandbox (sync **y** async) tienen límite de **2 minutos** por ejecución, no 2 horas.
  - **Plugin depth:** cada re-queue acumula `depth` en la correlación → Dataverse cancela cerca de depth 8 → ~8 tandas × 90s ≈ 12 min máximos → insuficiente para 100k.
  - **O(n²):** cada continuación re-parsea el archivo, re-ejecuta `PreloadCache` completo y recarga todos los logs (`ImportProcessorPlugin.cs` / query crece cada vuelta).
- **Por qué importa:** No es un bug, es una decisión de arquitectura. Procesar 100k multi-tabla dentro de un plugin sandbox tiene un techo físico.
- **Decisión arquitectónica pendiente:** ver sección "Opciones para #3" más abajo.

---

## 🟠 Altos

### [x] #4 — `PreloadCache` carga la tabla destino ENTERA en memoria ✅ (eliminado: upsert reemplaza el cache; DedupService borrado)
- **Ubicación:** `DedupService.cs:86-117`.
- **Problema:** `RetrieveMultiple` paginado de a 5000 sobre toda la tabla destino, **sin filtro**. Importar 500 contactos a una tabla con 2M de registros se trae los 2M al sandbox.
- **Por qué importa:** OOM / lentitud brutal. Se repite en cada continuación.

### [x] #5 — El archivo se carga entero en memoria (dos veces) ✅ (staging: se parsea una sola vez en PrepareImport)
- **Ubicación:** `ImportProcessorPlugin.cs:129-148`.
- **Problema:** `List<byte>` + `AddRange` + `ToArray()` = doble memoria del archivo. Después el parser materializa todas las filas en `List<SourceRow>`. El comentario de `NormalizationEngine.cs:77` dice "streaming" pero no lo es.
- **Por qué importa:** Sandbox con memoria limitada + archivo de 100k = pólvora.

---

## 🟡 Medios

### [x] #6 — Default vs Required en orden invertido ✅ (NormalizationEngine.ApplyColumnMappings: default antes de validar)
- **Ubicación:** `NormalizationEngine.cs:176-188`.
- **Problema:** Se valida "requerido y vacío" → falla la fila, y *recién después* se aplica `DefaultValue`. Una columna requerida con default se rechaza igual. El default debe aplicarse **antes** de validar.

### [x] #7 — Lookups por string no implementados (fallan en silencio) ✅ (ApplyColumnMappings: EntityReference por LookupMatchColumn = alternate key)
- **Ubicación:** `NormalizationEngine.cs:218-224`.
- **Problema:** Si el lookup no es GUID literal, solo se escribe un `Trace` y el campo queda nulo **sin marcar la fila como fallida**. `LookupMatchColumn` no hace nada.
- **Por qué importa:** Datos incompletos en silencio.

### [x] #8 — Write amplification en logs ✅ de hecho (el camino nuevo NO loguea por fila; solo contadores agregados). Logging detallado → cambio separado si se requiere.
- **Ubicación:** `ProgressReporter.cs:97-115`.
- **Problema:** Un `ImportJobLog` por cada fila + un `Update` del job por chunk. 100k filas = 100k creates extra.

### [x] #9 — `totalBatches` mal calculado ✅ (vivía en el `Process` viejo; eliminado en la limpieza)
- **Ubicación:** `NormalizationEngine.cs:44-48`.
- **Problema:** Suma `CalculateBatchCount(sourceRows.Count)` por tabla, sin filtrar por sheet ni skips. El número que ve el usuario miente.

### [x] #10 — Doc dice batches de 1000, código usa 100 + doble chunking ✅ (`BatchProcessor` eliminado; `UpsertWriter` usa 1000 sin doble chunk)
- **Ubicación:** `docs/architecture.md:64` vs `BatchProcessor.cs:19`.
- **Problema:** Mismatch doc/código. Además `BatchProcessor` re-chunkea lo que `NormalizationEngine` ya chunkeó.

### 🟡 #11 — Money sin moneda — EVALUADO: NO es bug, es feature (requiere config de `transactioncurrencyid` por mapping). En single-currency Dataverse asigna la base. Pendiente solo si se necesita multi-currency.

### [ ] ~~#11 original~~ — Money sin moneda
- **Ubicación:** `TypeCoercer.cs:72-75`.
- **Problema:** Se crea `Money` sin `transactioncurrencyid`. Según config del entorno puede fallar o tomar moneda equivocada.

### [x] #12 — `FromOADate` como fallback de fecha ✅ (acotado a rango plausible 1900–2100; números fuera de rango ahora fallan claro)
- **Ubicación:** `TypeCoercer.cs:60-62`.
- **Problema:** Cualquier string numérico válido se interpreta como fecha serial de Excel.

---

## ⚪ Menores / a revisar

- [x] #13 — `TypeCoercer` String ahora trimea bordes (consistente con los demás tipos). ✅
- [ ] 🟡 #14 — Lexer `-`: EVALUADO, NO se arregla. Un `-` suelto ya lanza excepción hoy (solo cambiaría el mensaje). Sin test de valor → cosmético.
- [ ] 🟡 #15 — JSON por regex en `MAP`: EVALUADO, NO se arregla. Funciona para JSON bien formado (config-controlled); reescribir = sobre-ingeniería.
- [x] #16 — `ExcelParserService` analizado. Lee correctamente una hoja con header/no-header; pero ver #17.

### [x] #17 — Soporte multi-hoja de Excel incompleto en el camino nuevo ✅ (ExcelParser parsea todas las hojas cuando no se especifica; rownumber global único)
- **Ubicación:** `StagingReader.ParseRows` (no pasa `SheetName`) + `ExcelParserService.Parse` (lee SOLO una hoja: la indicada o la primera).
- **Problema:** `PrepareImport` parsea sin `SheetName` → el `ExcelParser` devuelve solo la primera hoja. Las TableMappings con `SourceSheet` apuntando a otras hojas quedan sin filas. El `FilterBySheet` del engine no puede recuperar lo que el parser nunca leyó.
- **Por qué importa:** rompe la promesa multi-hoja de la arquitectura. Fix: parsear TODAS las hojas en staging (cada fila con su `SheetName`) o iterar hojas en `Prepare`.
- **Estado:** pendiente — candidato al próximo cambio.

---

## Opciones para resolver #3 (arquitectura)

### Restricciones del proyecto (confirmadas con el usuario)
- El disparo debe poder hacerse **desde Canvas App** (hoy: Canvas App crea ImportJob → procesa).
- La solución debe ser **migrable entre ambientes (ALM por soluciones)**.
- El usuario **controla el esquema** de las tablas destino → puede crear **Alternate Keys**.

### Concepto transversal: Alternate Keys + Upsert (mata #2 y #4)
Como se controla el esquema, definir claves alternativas en las tablas destino (ej: `country.code`,
`city` por `name+state`) y delegar a la plataforma:
- **Upsert** → si existe actualiza, si no crea. Elimina `PreloadCache`, el cache en memoria y el dedup manual.
- **Lookup por `KeyAttributes`** → `new EntityReference("country", "code", "AR")` resuelve el padre sin
  conocer su GUID. Elimina la resolución manual del DAG en memoria.
- Aplica a CUALQUIERA de las arquitecturas de abajo.

### Descartado: Dataflows (Power Query)
Razones válidas del usuario, confirmadas:
- ALM débil entre ambientes (conexiones/binding/refresh se recablean a mano por ambiente).
- No hay disparo on-demand limpio desde Canvas App; un dataflow **tira** de un origen, no recibe el archivo subido.

### Candidatos vigentes (ambos reusan el código de servicios actual)

**Arquitectura 1 — 100% Power Platform: Power Automate + Custom API por chunks**
- Cloud Flow disparado por creación del ImportJob → `Do until` que llama una Custom API que procesa 1 chunk por invocación.
- Cada invocación es request fresco → `depth` se resetea, cada chunk tiene sus 2 min → desaparece el techo de depth.
- Pros: sin Azure, ALM por soluciones. Contras: throttling de Power Automate, más lento a gran volumen.

**Arquitectura 2 — Con Azure: Service Bus + Azure Function**
- Service Endpoint (en la solución) postea contexto a cola → Azure Function consume sin límite de tiempo/sandbox/depth.
- `CreateMultiple`/`UpdateMultiple` + Upsert. Retry y dead-letter por Service Bus.
- Pros: escala a 100k+, robusto. Contras: infra Azure, pipeline propio del Function, Managed Identity.

### ✅ DECISIÓN (2026-06-02): Arquitectura 1 — 100% Power Platform
- Volumen objetivo: ~100k filas. Stack: 100% Power Platform (sin Azure), orquestado con Power Automate.

**Diseño acordado:**
```
Canvas App → crea ImportJob (Queued) + archivo
   Cloud Flow (trigger: creación del ImportJob)
     1. Custom API "PrepareImport": parsea el archivo UNA vez → persiste filas en tabla STAGING → devuelve totalRows
     2. Do until (status == Completed):
          Custom API "ProcessChunk(jobId)":
            - lee N filas de staging desde offset (paginado, sin re-parsear)
            - coerce + transforms + Upsert por ALTERNATE KEY (bulk)
            - CreateMultiple/UpdateMultiple (≤1000 por mensaje)
            - lookups vía EntityReference(tabla, key, valor)  ← sin GUID
            - actualiza offset + contadores en ImportJob → devuelve hasMore
```

**Cómo este diseño resuelve los issues:**
- #3 (depth/timeout): cada ProcessChunk es request fresco → depth se resetea, 2 min se reinician.
- #2 + #4: Upsert + Alternate Keys → sin cache en memoria, sin PreloadCache, sin dedup manual.
- #5: staging → el archivo se parsea una sola vez.
- Techo de API requests/24h: CreateMultiple/UpdateMultiple bajan el conteo de 100k a ~100.

**Techos a respetar:**
- Custom API = plugin → 2 min por llamada → dimensionar tamaño de chunk.
- `Do until` tiene tope de iteraciones/timeout → lotes grandes, no fila por fila.

**A verificar en el tenant (dependen de licencia/esquema):**
- Límite de API requests por 24h de la cuenta que ejecuta el flow.
- Soporte de `UpsertMultiple` por tabla (fallback: CreateMultiple + UpdateMultiple separados por existencia).

**Componentes nuevos a construir:**
- [ ] Tabla staging `sanic_importstagingrow` (+ alternate keys en tablas destino).
- [ ] Custom API `PrepareImport` (parseo único → staging).
- [ ] Custom API `ProcessChunk` (procesa lote desde staging, bulk upsert, reporta progreso).
- [ ] Cloud Flow orquestador (trigger + Do until).
- [ ] Refactor: mover `NormalizationEngine`/`TransformEngine`/`TypeCoercer` al backing de `ProcessChunk`.

---

## Orden de resolución recomendado
1. **#1** — sin esto, la herramienta no normaliza.
2. **#2** — unificar derivación de clave de dedup (diseño, no parche).
3. **#3** — reconciliar el modelo de timeout con el límite real (decisión de arquitectura).
4. Resto por severidad.
