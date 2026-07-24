[← Volver al índice de documentación](../README.md)

# Smelter — Qué es y cómo se usa

---

## 1. Qué es Smelter

Smelter es una herramienta sobre **Microsoft Dataverse / Power Platform** que **transforma archivos planos
CSV denormalizados en tablas Dataverse normalizadas**.

El problema que resuelve: muchos sistemas exportan un CSV "ancho" donde **cada fila mezcla datos de varias
entidades** (ej. una fila de cobranza trae cliente + cuenta + producto + agente + fiador, todo junto y
repetido). Cargar eso tal cual a una sola tabla pierde la normalización. Smelter toma ese CSV y, según una
**configuración declarativa** (sin programar), reparte cada fila en varias tablas relacionadas, deduplica,
y resuelve las relaciones entre ellas.

**Características clave:**
- **Solo CSV** (no Excel).
- **Idempotente**: reprocesar el mismo archivo no duplica registros (upsert por clave de negocio).
- **Escala a volúmenes grandes** (~100k+ filas) sin chocar contra los límites de tiempo de la plataforma.
- **100% Power Platform** (no requiere Azure).
- **Trazabilidad de errores** por fila y por categoría.

---

## 2. Cómo funciona (alto nivel)

```
Usuario (app / MDA) ── crea un Import Job (status=Queued) + sube el CSV ──► Dataverse
                                                                              │ (trigger por status=Queued)
                                                                              ▼
                                                                        Cloud Flow (Power Automate)
                                                                          1. PrepareImport (1 vez)
                                                                          2. Do until: ProcessChunk (por lotes)
                                                                              ▼
                                                  Tablas destino normalizadas + Import Job (progreso) + Import Job Log (errores)
```

- **PrepareImport**: cuenta las filas, valida que el CSV tenga las columnas mapeadas, y deja el Job listo.
- **ProcessChunk**: procesa el archivo por **lotes** (chunks). Cada lote es una invocación independiente,
  lee su porción del CSV, arma los registros en orden de dependencia y los inserta/actualiza por upsert.
  El Cloud Flow lo repite hasta terminar.
- El **progreso** (filas procesadas, éxitos, fallos) se actualiza en el Import Job en tiempo real.
- Las **filas que fallan** se registran en Import Job Log con su causa y categoría.

> No hace falta entender el código para usar Smelter. Todo se controla con **configuración** (las tablas
> de la sección 3) y se opera desde una app.

---

## 3. El modelo de configuración

Smelter se configura con estas tablas (todas con prefijo `sanic_`):

### Configuración (la definís una vez por tipo de archivo)
| Tabla | Qué define |
|-------|-----------|
| **Import Template** | El "plano" de un tipo de import: formato (CSV), delimitador, si tiene encabezado, tamaño de lote (Chunk Size). |
| **Table Mapping** | Por cada tabla destino: a qué tabla Dataverse va, en qué orden, y por qué columna se deduplica (**Dedup Columns**). Un template tiene varios. |
| **Column Mapping** | Por cada columna: qué columna del CSV va a qué columna de la tabla destino, su tipo de dato, si es requerida, valor por defecto, transformación, etc. |
| **Relationship Mapping** | Define las relaciones padre→hijo: cómo un registro hijo encuentra a su padre (por valor de negocio, no por GUID). |

### Ejecución y monitoreo (se generan al usar)
| Tabla | Qué guarda |
|-------|-----------|
| **Import Job** | Una ejecución: el archivo, el template usado, el estado, contadores de progreso. |
| **Import Job Log** | Una entrada por **fila fallida**: número de fila, tabla, categoría de error y mensaje. |

---

## 4. Conceptos clave (los que hay que entender sí o sí)

### Alternate Keys + Upsert — el cimiento
Cada tabla destino **debe tener una Alternate Key** sobre su columna de identidad de negocio (ej. Cliente
por `CIF`, Cuenta por `NumeroCuenta`). Smelter usa esa clave para hacer **upsert**: "si existe, actualizá;
si no, creá". Esto da:
- **Idempotencia**: reprocesar no duplica.
- **Resolución de relaciones sin GUID**: un hijo referencia a su padre por valor de negocio (ej. el CIF),
  y Dataverse resuelve el vínculo.

> ⚠️ **Regla de oro:** si una tabla destino NO tiene su Alternate Key **Activa**, el import de esa tabla
> falla con error de categoría `TargetData`. Es el error más común al configurar.

### Orden de dependencia (DAG)
Smelter procesa las tablas en orden: **primero los padres, después los hijos** (ej. Cliente antes que
Cuenta). Lo deduce de los Relationship Mappings. Así, cuando crea un hijo, el padre ya existe.

### Chunking y reanudación
El archivo se procesa por lotes (Chunk Size, configurable por template). El avance se guarda en el Job, así
que si el proceso se interrumpe, **reanuda desde donde quedó** sin duplicar (gracias al upsert).

### Categorías de error
Cada fila fallida se clasifica por origen, para diagnóstico rápido:
`SourceError`, `TransformError`, `TypeError`, `ValidationError`, `LookupUnresolved`, `TargetData`, `Transient`.

---

## 5. Cómo se usa (paso a paso)

### A. Configurar (una vez por tipo de archivo)
1. Crear las **tablas destino** en Dataverse con sus columnas.
2. Crear la **Alternate Key** de cada tabla destino (y dejar que quede **Active**).
3. Crear un **Import Template** y sus **Table Mappings / Column Mappings / Relationship Mappings** según
   cómo se reparte el CSV. (Ver `cobranza-import-config.md` como ejemplo completo y trabajado.)

### B. Ejecutar (cada vez que llega un archivo)
4. Crear un **Import Job**, elegir el template y **subir el CSV**.
5. Poner el Job en estado **Queued** → el Cloud Flow arranca solo.
6. Monitorear el progreso en el Import Job (filas procesadas, éxitos, fallos).

### C. Diagnosticar
7. Si el Job termina como **CompletedWithErrors**, revisar **Import Job Log**: cada fila fallida con su
   categoría y mensaje.
8. Corregir la causa (lo más común: falta una Alternate Key, un dato no convierte, o un padre no existe) y
   **reintentar** (volver a poner el Job en Queued; el upsert hace que no se dupliquen las filas ya cargadas).

---

## 6. Reglas y límites

- **Solo CSV.** Excel no está soportado.
- **Cada tabla destino necesita su Alternate Key activa** sobre sus Dedup Columns.
- Los **lookups a padres** se resuelven por valor de negocio (la columna del CSV que identifica al padre),
  definido en el Relationship Mapping; el padre debe tener su Alternate Key.
- Las **fechas** vienen como texto en el CSV; si el formato no es ISO, se usa una transformación
  (`DATEPARSE`) en el Column Mapping.
- **Idempotencia garantizada**: reprocesar/reanudar nunca duplica.

---

## 7. Patrones útiles de modelado

- **Explotar columnas paralelas**: si una fila trae varias columnas del mismo tipo (ej. 3 direcciones:
  Casa/EC/Trabajo), se crea **un Table Mapping por cada una** apuntando a la misma tabla, diferenciadas por
  un valor por defecto (el "tipo").
- **Alternate key compuesta con valor copiado**: para una tabla hija que se deduplica por (padre + tipo),
  se copia la columna de negocio del padre (ej. CIF) dentro de la hija y la clave es `padre+tipo` en texto;
  el vínculo real al padre lo arma el Relationship Mapping aparte.
- **Estado actual vs. histórico**: una tabla con upsert por clave simple guarda el **estado actual** (se
  pisa). Para guardar **histórico diario**, se usa una tabla aparte con clave compuesta que incluye la
  **fecha** (cada día = registro nuevo; mismo día re-subido = upsert).

---

## 8. Archivos relacionados
- **`cobranza-import-config.md`** — ejemplo completo y trabajado: un CSV de cobranza de 32 columnas
  normalizado a varias tablas, con todos los mappings, alternate keys y el histórico diario.
