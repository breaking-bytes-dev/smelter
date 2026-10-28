# Decisiones de diseño

Las decisiones que conviene conocer **antes** de cambiar algo en Smelter. Cada una dice qué se decidió, por qué, qué alternativa se descartó y qué se rompe si se cambia. Está escrito para desarrolladores y administradores que van a modificar la solución; el funcionamiento general está en [Arquitectura](01-arquitectura.md).

Las decisiones se reconstruyeron del [`CHANGELOG.md`](../../CHANGELOG.md), de los planes de diseño de [`openspec/`](../../openspec/) y de la historia de commits (`git log`). Donde un hecho se verificó en el código, se indica.

## Cómo usar este documento

Antes de tocar una pieza, búscala en el [índice por pieza](#índice-por-pieza) y lee las decisiones que la afectan. Si un cambio contradice una decisión, primero hay que resolver el «qué se rompe»; no alcanza con que la prueba pase.

Las decisiones están agrupadas por área:

1. [Procesamiento](#1-procesamiento)
2. [Concurrencia y ejecución](#2-concurrencia-y-ejecución)
3. [Seguridad](#3-seguridad)
4. [Datos y esquema](#4-datos-y-esquema)
5. [Interfaz y localización](#5-interfaz-y-localización)
6. [Ingeniería y ALM](#6-ingeniería-y-alm)

## 1. Procesamiento

### D-01. Sin tabla de staging: chunks orquestados por Power Automate

- **Decisión:** el archivo no se vuelca a una tabla intermedia. Cada `ProcessChunk` lee su tramo del CSV, lo procesa y termina. Un Cloud Flow repite la llamada hasta que no queda nada.
- **Por qué:** escribir 100.000 filas en una tabla de staging no entra en los 2 minutos de un plugin síncrono. El diseño anterior (v1) era un plugin asíncrono monolítico con auto-continuación que moría por la profundidad máxima de plugins. Reparsear el CSV por rangos es barato y distribuido.
- **Alternativa descartada:** tabla de staging con plugin asíncrono encadenado.
- **Qué se rompe si se cambia:** volver al staging reintroduce el límite de tiempo y de profundidad; ver `openspec/changes/chunked-import-redesign/design.md`. Origen: commit `cfcabc9`.

### D-02. Solo CSV

- **Decisión:** el único origen soportado es CSV. El choice `Source Type` tiene una sola opción, está oculto en el formulario y el importador de templates siempre escribe CSV.
- **Por qué:** `.xlsx` no se puede leer en streaming (las *shared strings* se cargan enteras y saltar filas obliga a reparsear XML). CSV permite saltar al byte exacto.
- **Alternativa descartada:** CSV y Excel.
- **Qué se rompe si se cambia:** el cursor de bytes y la lectura por rangos solo son válidos para texto plano. Se eliminaron `sanic_smlt_tm_sourcesheet` y la dependencia `ExcelDataReader`.

### D-03. Idempotencia por alternate key y gate de Dedup Columns

- **Decisión:** el motor escribe con `UpsertRequest` por la alternate key de la tabla destino (las *Dedup Columns*). Un Table Mapping sin Dedup Columns es **rechazado** por `PrepareImport` y `ProcessChunk`, salvo que tenga el opt-in explícito «Solo inserción (permite duplicados)».
- **Por qué:** reprocesar un chunk (reintento del flow, reanudación, **Reintentar**) no debe duplicar datos. Antes de 1.0.8, un mapping sin clave degradaba en silencio a `Create`/`Update` no idempotente y solo dejaba una advertencia en el trace.
- **Alternativa descartada:** deduplicación en memoria o consulta previa de existencia para todos los casos. Se mantiene solo cuando *Create If Not Exists* o *Update If Exists* están en No.
- **Qué se rompe si se cambia:** quitar el gate hace que una configuración incompleta duplique datos sin avisar. Los templates existentes sin Dedup Columns dejan de correr al actualizar (aviso en el `CHANGELOG`). Origen: commit `8939129`.

### D-04. Cursor de bytes con descarga parcial

- **Decisión:** el Import Job guarda en `bytecursor` el byte del próximo registro sin leer, y cada chunk baja solo su rango con `InitializeFileBlocksDownload` y `DownloadBlock`.
- **Por qué:** descargar y reparsear el archivo entero en cada chunk costaba O(N²). El cursor lo baja a O(N).
- **Detalles que no se pueden perder:** las posiciones se calculan sobre el texto decodificado recodificado a UTF-8 (`Parser.CharCount`), porque `CountBytes` de CsvHelper cuenta mal los pares subrogados (un emoji da 6 en vez de 4). Un registro está completo si hay otro después o si se llegó al final del archivo; el último registro de un segmento que no llega al final se descarta (puede estar cortado dentro de un campo entrecomillado). La columna es **Decimal de precisión 0** porque un Whole Number de Dataverse es de 32 bits (~2,1 GB).
- **Alternativa descartada:** acotar el tamaño de archivo; seguir descargando todo.
- **Qué se rompe si se cambia:** archivos UTF-16/32 o UTF-8 inválido no se pueden recorrer por bytes y usan el camino anterior (descarga completa y salto de filas). Origen: commit `8939129`.

### D-05. El offset avanza después de procesar; el log solo registra fallos y es idempotente

- **Decisión:** `processingbatch` y `bytecursor` se actualizan al **final** del chunk. El Import Job Log guarda solo filas fallidas, con *upsert* sobre `(job, fila, tabla)`.
- **Por qué:** si el chunk muere, el reintento reprocesa el mismo tramo sin saltearse filas. Registrar también los éxitos amplificaría las escrituras (un log por fila). La key idempotente hace que reescribir un log no lo duplique.
- **Alternativa descartada:** log por fila, y avanzar el offset antes de procesar.
- **Qué se rompe si se cambia:** la key original incluía el autonumérico del log por un error: cada upsert fallaba con `Incorrect type of attribute value ... EntityReference` y el log quedaba vacío. Corregido en 1.2.1.0 (commit `3504068`); el path de errores no estaba cubierto por el smoke y hoy lo cubren `[6b]` y `[CHUNK]`.
- **Categorías de error:** el diseño original tenía `Transient` (reintento por fallo transitorio). Se difirió porque el flow procesa los chunks de a uno (sin concurrencia de escritura, los deadlocks son raros) y se eliminó más tarde porque nunca se producía.

### D-06. Claim con lock de fila, no `RowVersion` más `catch`

- **Decisión:** `PrepareImport` toma el lock exclusivo de la fila con un `Update` de la columna técnica `claimlock` y recién entonces lee el status. Si no es `Queued` ni vacío, devuelve `Claimed = false` sin lanzar.
- **Por qué:** los triggers de Dataverse son *at-least-once*, así que un job encolado puede disparar varias corridas del flow; sin exclusión, los upserts competían y fallaban con `DuplicateRecordEntityKey`. La primera solución (1.0.12) usaba `IfRowVersionMatches` y atrapaba el fault de concurrencia, pero `PrepareImport` corre dentro de la transacción de la Custom API y **atrapar una excepción de `OrganizationService` y seguir condena la transacción** («ISV code reduced the open transaction count»). Con el lock, la perdedora espera y ve el job ya `Processing`.
- **Alternativa descartada:** concurrencia optimista (`RowVersion`), concurrencia `runs = 1` en el trigger (serializaría jobs *distintos*).
- **Qué se rompe si se cambia:** agregar un `try/catch` alrededor de llamadas al servicio en el camino del claim, o mover el lock después de la descarga. Origen: commits `c89558b` y `73a541b`.

## 2. Concurrencia y ejecución

### D-07. Un solo camino de ejecución: el Cloud Flow

- **Decisión:** los botones **Ejecutar** y **Reintentar** solo guardan, validan y ponen el job en `Queued`. El flow hace `PrepareImport` y el bucle de `ProcessChunk`, y su handler marca `Failed`.
- **Por qué:** antes los botones corrían el pipeline desde el navegador: cerrar la pestaña dejaba el job en `Processing` para siempre, y un fallo de `Prepare` desde el botón no marcaba `Failed`.
- **Alternativa descartada:** dos caminos (botón y flow) conviviendo.
- **Qué se rompe si se cambia:** `Smelter.Verify` falla si el web resource desplegado vuelve a referenciar `processchunk` o `prepareimport`. Origen: commit `3b47a19`.

### D-08. Los jobs corren en paralelo; los duplicados del mismo job los neutraliza el claim

- **Decisión:** el trigger filtra por la columna `status` (`status eq 0`) y **no** tiene concurrencia global. Cada job es independiente.
- **Por qué:** una concurrencia `runs = 1` serializaría jobs distintos; cobranza encola varios jobs «partes» a la vez. El transformer `ClientDataClaimGate` quita cualquier `concurrency` que hubiera.
- **Qué se rompe si se cambia:** poner concurrencia global degrada el paralelismo entre jobs sin resolver los duplicados del mismo job (eso lo resuelve D-06).

### D-09. Guarda de re-encolado

- **Decisión:** `ValidateQueueAccess` rechaza poner `Queued` sobre un job `Processing`.
- **Por qué:** un `Update` por API durante la corrida dispararía otra corrida del flow cuyo claim vería `Queued` y reseteaba el job a mitad de camino. El pipeline del servidor nunca setea `Queued` (solo `Processing` y `Failed`), así que no se ve afectado.
- **Qué se rompe si se cambia:** `Queued → Queued`, los estados finales y el status vacío (reintento) siguen permitidos; endurecer más bloquearía **Reintentar**.

## 3. Seguridad

### D-10. La tabla de claves HMAC viaja en la solución; el valor, no

- **Decisión:** `sanic_tbl_smlt_hmackey` es componente de `smlt_core_smelter` (el esquema viaja a Test y Prod con el managed). El **valor** de cada clave se carga a mano en cada ambiente: nunca por CI, ni repositorio, ni GitHub secret.
- **Por qué:** un `IHmacKeyProvider` basado en Key Vault no es viable hoy: un plugin de Custom API no puede recibir *secure configuration* y los secretos de variables de entorno respaldados por Key Vault no llegan a los plugins. La tabla de la organización con seguridad de columna, sin auditoría del valor y fuera de todos los roles de Smelter es el mejor punto intermedio.
- **Riesgo aceptado:** cualquier System Administrator del ambiente puede leer la clave, y las copias y backups del ambiente la arrastran. Endurecimiento futuro posible: Key Vault con identidad administrada; la misma clave da los mismos hashes, así que migrar no rompe nada.
- **Alternativa descartada:** mantener la tabla solo en Dev, fuera de la solución (el enfoque inicial, commit `621ee71`), y un proveedor basado en Key Vault.
- **Qué se rompe si se cambia:** cambiar o rotar la clave cambia todos los hashes y duplica registros en la siguiente carga.
- **Inconsistencias conocidas en el código:** algunos comentarios (`IHmacKeyProvider`, `TableLabelsSpec`, el commit `621ee71`) aún llaman a la tabla «DEV-only» o dicen que PROD debe usar Key Vault; son residuos de una decisión anterior. Origen: commits `b3dab07` y `4835fdd`.

### D-11. El nombre de la clave HMAC es un literal con forma de etiqueta

- **Decisión:** el segundo argumento de `HMAC()` debe ser un literal (`^[A-Za-z][A-Za-z0-9_-]{0,49}$`), validado **antes** de evaluar los argumentos.
- **Por qué:** si la clave pudiera venir de una columna (`HMAC(x, NumeroCuenta)`), el valor sensible terminaría en el mensaje de error y en la consulta de la clave. Commit `68ae0f9`.

### D-12. Doble validación de acceso: al encolar y al preparar

- **Decisión:** `ValidateQueueAccessPlugin` valida a la **persona** al poner `Queued`; `PrepareImport` valida a la identidad que lo llama (el flow).
- **Por qué:** el flow corre con la identidad de su conexión, así que el `InitiatingUserId` de `PrepareImport` es esa identidad. Sin el chequeo al encolar, cualquiera con Write sobre un job podía poner `status = 0` por API y hacer que el flow lo ejecutara con su propia identidad. Commit `595df9d`.
- **Alternativa descartada:** `PreImage` para leer el estado anterior (un step image más para registrar y mantener); el plugin lee el job persistido.
- **Qué se rompe si se cambia:** el control por persona deja de existir. La cuenta dueña de la conexión del flow tiene que leer todos los templates.

### D-13. Seguridad por teams: el job hereda el dueño del template

- **Decisión:** el Operador tiene Read/Write a nivel *Usuario* sobre el job y Read/AppendTo a nivel *Usuario* sobre el template. `AssignJobOwnerPlugin` hace que el job nazca a nombre del team dueño del template (gana a un `ownerid` explícito), y la relación Template → Import Job tiene `Share` y `Reparent` en `Cascade`.
- **Por qué:** nivel *Usuario* cubre registros propios y de los teams del usuario. Con eso, todo el equipo ve y opera los jobs de cualquiera de ellos sin tocar la matriz de privilegios, y sin la ventana en la que el job pertenece al creador (el plugin es pre-operación).
- **Alternativa descartada:** visibilidad por unidad de negocio; un plugin que reasigne el job después del `Create`.
- **Qué se rompe si se cambia:** apagar el step hace que los jobs nazcan a nombre del creador y los compañeros no los vean. Commits `766590d` y `1907c7c`.

### D-14. La app es del Configurador; los operadores usan su propia aplicación

- **Decisión:** la MDA se asocia solo a `Smelter Configurador` (más los roles de plataforma). El Operador crea y encola jobs desde la aplicación del cliente.
- **Por qué:** el Operador no configura; ver la MDA sin poder usarla es ruido. Las Custom APIs de export e import requieren Write sobre el template, que el Operador no tiene.
- **Qué se rompe si se cambia:** darle la app al Operador no está en la solución y no está verificado en un upgrade.

### D-15. Ningún rol puede crear Import Job Logs

- **Decisión:** el privilegio Create sobre el log se quita de **ambos** roles y `RoleSpec.Denied` lo declara prohibido.
- **Por qué:** los logs los escribe solo el motor, con un servicio SYSTEM que ignora los privilegios del usuario. Sin Create, la plataforma esconde los botones «Nuevo Log de importación» sin tocar el ribbon.
- **Qué se rompe si se cambia:** devolver el privilegio vuelve a mostrar botones «Nuevo» que no tienen sentido, porque los logs los escribe el motor. `AddPrivilegesRole` es aditivo, por eso quitar requiere `RemovePrivilegeRole`. Commit `5dc0aae`.

### D-16. Los mensajes del motor no llevan valores de filas

- **Decisión:** los mensajes de error, el Import Job Log y el trace nombran mapping, tabla, columna, tipo y largo, nunca el valor. Un fallo de escritura de Dataverse se reduce a su código (`Dataverse rejected the write (ErrorCode 0x...)`).
- **Por qué:** el mensaje original de Dataverse suele traer el valor de la clave duplicada, y un CSV puede traer números de tarjeta o documentos.
- **Qué se rompe si se cambia:** el log pasa a ser un almacén de datos sensibles. Commit `c29d360`.

## 4. Datos y esquema

### D-17. Chunk Size entre 100 y 10.000; se rechaza lo que quede fuera

- **Decisión:** el rango es [100, 10000], el valor por defecto 1000 (vacío), y la columna y el import de templates **rechazan** lo que quede fuera.
- **Por qué:** el mínimo evita lotes tan chicos que amplifican la sobrecarga de la API; el máximo evita el timeout del sandbox por el costo de `ExecuteMultiple`. Antes la columna admitía valores fuera de rango y el motor los acotaba en silencio: un 50 se aceptaba y corría con 100.
- **Qué se rompe si se cambia:** el motor sigue acotando como defensa, pero el formulario y la importación dejan de avisar. Commit `b279b4f`.

### D-18. El estado del template gatea la ejecución; los indicadores Create/Update If se respetan

- **Decisión:** solo un template `Active` corre. `Create If Not Exists` y `Update If Exists` se respetan (filas `Skipped`).
- **Por qué:** antes ambos campos existían pero el motor los ignoraba: un template a medio configurar corría igual y `Update If Exists = No` seguía actualizando. Se detectó en la auditoría de campos 1.3.0.0 (commit `cedd575`).
- **Qué se rompe si se cambia:** el cambio de 1.3.0.0 fue un cambio de comportamiento: los templates con *Update If Exists = No* pasaron a omitir filas existentes.

### D-19. Se eliminó lo que no hacía nada

- **Decisión:** se quitaron del esquema `isjunctiontable`, `intersecttable`, `isretry`, `totalbatches`, `errorsummary`, `sourcesheet`, `createdrecordid`, la opción `ManyToMany` y las opciones `Created`, `Updated`, `Skipped` y `Transient` de los logs.
- **Por qué:** campos y opciones que ningún código leía ni escribía llevaban a creer que existía una función (N:M, retry por fila). Una relación `ManyToMany` se ignoraba en silencio; ahora se rechaza con un mensaje claro.
- **Cómo:** por SDK (`SchemaCleanupProvisioner`), detrás de `--allow-destructive`, y el provisioner se niega si alguna fila usa un valor a quitar (salvo los logs). Commits `cedd575`, `f7abd50` y `0ee2a8f`.

### D-20. Update If Exists nace en Sí

- **Decisión:** el default de `sanic_smlt_tm_updateifexists` es Sí, igual que el fallback del loader (`?? true`) y que la documentación.
- **Por qué:** el esquema decía No y todo lo demás decía Sí.
- **Qué se rompe si se cambia:** solo afecta a mappings **nuevos**; los existentes conservan su valor.

## 5. Interfaz y localización

### D-21. Inglés en 1033, español en 3082; los nombres técnicos del 1033 no se tocan

- **Decisión:** el inglés va bajo el idioma base de la organización (1033) y el español bajo 3082. Los nombres 1033 de tablas y columnas (`TBL | SMLT | Import Job`, `[IJ] Status`) no se renombran.
- **Por qué:** otras *canvas apps* los referencian en Power Fx. Antes el español estaba escrito bajo 1033.
- **Mecanismos:** `SetLocLabels` para atributos de registros (`appmodule.name`, `savedquery.name`, `systemform.name`), `MergeLabels` para metadata, `UpdateOptionValue` para opciones (porque `UpdateAttribute` **ignora** las opciones, incluso en Sí/No) y `FormLabelsReader` para formularios (Dataverse devuelve el formxml solo con las etiquetas del idioma de quien lo lee).
- **Qué se rompe si se cambia:** reescribir el 1033 rompe las canvas apps que lo usan. Commits `952a48e`, `4fbd702` y `45d91db`.

### D-22. Dashboard clásico en lugar de *interaction-centric*

- **Decisión:** el panel es un dashboard clásico (`systemform` tipo 0) creado por código.
- **Por qué:** el tipo *interaction-centric* (tipo 10) se renderizaba en la MDA como el *overview* genérico de «Dynamics 365 Social» y no aparecía en el selector.
- **Qué se rompe si se cambia:** el formxml de un dashboard clásico es `<form><tabs>`; un hijo `<dashboard>` dentro de `<form>` es rechazado. Commit `97d01e3`.

### D-23. El lookup de template del job solo ofrece templates `Active`

- **Decisión:** una vista pública dedicada («Active templates») y el control con `DefaultViewId`, `AvailableViewIds`, `DisableViewPicker` y `DisableMru`.
- **Por qué:** `PrepareImport` rechaza Draft y Archived, así que ofrecerlos no sirve. La vista de lookup global **no** se tocó porque la comparten los lookups de los mappings, que sí deben poder apuntar a un Draft.
- **Qué se rompe si se cambia:** se usa `AvailableViewIds` porque `ViewIds` no pasa el schema del formxml. El rechazo del lado del servidor no cambia. Commits `f6204f7` y `acad2da`.

### D-24. Hay que declararlo todo en una spec, y las specs son puras

- **Decisión:** formularios, vistas, sitemap, íconos y etiquetas se declaran en specs con un `Apply` **puro e idempotente** sobre el XML en memoria. `Smelter.Verify` reutiliza ese `Apply` como aserción.
- **Por qué:** la misma lógica que escribe es la que comprueba; cualquier edición manual en Dev que contradiga la spec la reescribe el próximo deploy.

## 6. Ingeniería y ALM

### D-25. El núcleo puro está separado del código con Dataverse

- **Decisión:** `Smelter.Core` (netstandard2.0, sin dependencias del SDK de Dataverse) contiene el parser CSV, el DSL, el DAG y la serialización; `Smelter.Plugins` (net462) contiene el acceso a Dataverse. Los namespaces se mantienen en `Smelter.Plugins.*`.
- **Por qué:** el núcleo se prueba en Linux con .NET 8 (CI barato), mientras que los tests net462 solo ejecutan en Windows. Commit `4aa0341`.

### D-26. Los plugins se prueban con dobles, no con FakeXrmEasy

- **Decisión:** xUnit, FluentAssertions y NSubstitute sobre `IOrganizationService` y las interfaces del proyecto. Cada plugin delega en un `*Handler` que recibe sus dependencias por constructor.
- **Por qué:** el comportamiento relevante (qué consulta, qué escribe, qué lanza) se afirma con dobles sin emular un contexto completo de Dataverse.
- **Qué se rompe si se cambia:** el patrón handler es el que hace testeable cada plugin; un plugin con lógica en `Execute` pierde cobertura.

### D-27. Todo cambio de plataforma en Dev va por SDK, solo en Dev

- **Decisión:** esquema, roles, vistas, formularios y flow se aprovisionan con `Smelter.Deploy` (specs declarativas e idempotentes). La herramienta exige `SMELTER_ENV=dev`, y los pasos destructivos van detrás de `--allow-destructive`. Test y Prod reciben **solo** el managed de un Release.
- **Por qué:** reproducible en un ambiente limpio, revisable en un pull request y sin riesgo de aplicar una herramienta de desarrollo a producción. Commit `c6f6c3e`.
- **Qué se rompe si se cambia:** una modificación hecha a mano en el portal de Dev puede ser reescrita por el próximo deploy y no queda versionada.

### D-28. El Cloud Flow se ajusta con transformers puros

- **Decisión:** el JSON del flow (columna `clientdata`) se reescribe con transformers idempotentes aplicados en un único ciclo Draft → Update → Activate, con restauración del estado si falla.
- **Por qué:** el flow no es metadata declarable. El reemplazo por **token exacto** (no por subcadena) evita corromper un flow ya migrado en el segundo deploy.

### D-29. El rename de `processchunk` se hizo en dos pasos, sin ventana rota

- **Decisión:** el typo `anic_capi_smelter_processchunk` se corrigió primero con un rename aditivo y la migración del flow en el mismo deploy, y la entrada vieja se borró recién después de un `Smelter.Verify` verde (commits `dcb85cc` y anteriores).
- **Por qué:** una integración que llamara al nombre viejo no podía quedar rota entre medio.
- **Qué se rompe si se cambia:** integraciones externas deben usar el nombre `sanic_`.

### D-30. El código del plugin se recarga por versión del package

- **Decisión:** cada cambio de código sube `<Version>` en el csproj (hoy 1.0.15).
- **Por qué:** Dataverse cachea el package por versión; sin el bump, algunos workers del sandbox siguen con el código viejo (síntoma: `Unknown function: HMAC` intermitente con la versión 1.0.5). Commits `cf5eca2` y `34da782`.

### D-31. Export de templates con refs estables

- **Decisión:** el JSON del template usa una `ref` estable por Table Mapping (`tm-<slug>`) y relaciones por `parentRef` y `childRef`. La importación hace *upsert por nombre*, borra los mappings anteriores y los recrea.
- **Por qué:** los GUID no sobreviven entre ambientes. La serialización usa `DataContractJsonSerializer` porque `System.Web.Extensions` no está en la lista blanca del sandbox. El formato está versionado (`1.1`, compatible con `1.0`): una versión mayor se rechaza para evitar pérdida silenciosa de datos.

## Índice por pieza

| Pieza | Decisiones |
|---|---|
| Formato de archivo y lectura | [D-02](#d-02-solo-csv), [D-04](#d-04-cursor-de-bytes-con-descarga-parcial) |
| `PrepareImport` | [D-03](#d-03-idempotencia-por-alternate-key-y-gate-de-dedup-columns), [D-06](#d-06-claim-con-lock-de-fila-no-rowversion-más-catch), [D-12](#d-12-doble-validación-de-acceso-al-encolar-y-al-preparar) |
| `ProcessChunk` y logs | [D-01](#d-01-sin-tabla-de-staging-chunks-orquestados-por-power-automate), [D-04](#d-04-cursor-de-bytes-con-descarga-parcial), [D-05](#d-05-el-offset-avanza-después-de-procesar-el-log-solo-registra-fallos-y-es-idempotente), [D-16](#d-16-los-mensajes-del-motor-no-llevan-valores-de-filas), [D-17](#d-17-chunk-size-entre-100-y-10000-se-rechaza-lo-que-quede-fuera) |
| Cloud Flow | [D-07](#d-07-un-solo-camino-de-ejecución-el-cloud-flow), [D-08](#d-08-los-jobs-corren-en-paralelo-los-duplicados-del-mismo-job-los-neutraliza-el-claim), [D-28](#d-28-el-cloud-flow-se-ajusta-con-transformers-puros) |
| Roles y acceso | [D-12](#d-12-doble-validación-de-acceso-al-encolar-y-al-preparar), [D-13](#d-13-seguridad-por-teams-el-job-hereda-el-dueño-del-template), [D-14](#d-14-la-app-es-del-configurador-los-operadores-usan-su-propia-aplicación), [D-15](#d-15-ningún-rol-puede-crear-import-job-logs) |
| `HMAC()` y la tabla de claves | [D-10](#d-10-la-tabla-de-claves-hmac-viaja-en-la-solución-el-valor-no), [D-11](#d-11-el-nombre-de-la-clave-hmac-es-un-literal-con-forma-de-etiqueta) |
| Esquema | [D-18](#d-18-el-estado-del-template-gatea-la-ejecución-los-indicadores-createupdate-if-se-respetan), [D-19](#d-19-se-eliminó-lo-que-no-hacía-nada), [D-20](#d-20-update-if-exists-nace-en-sí) |
| App de configuración | [D-21](#d-21-inglés-en-1033-español-en-3082-los-nombres-técnicos-del-1033-no-se-tocan), [D-22](#d-22-dashboard-clásico-en-lugar-de-interaction-centric), [D-23](#d-23-el-lookup-de-template-del-job-solo-ofrece-templates-active), [D-24](#d-24-hay-que-declararlo-todo-en-una-spec-y-las-specs-son-puras) |
| Pruebas | [D-25](#d-25-el-núcleo-puro-está-separado-del-código-con-dataverse), [D-26](#d-26-los-plugins-se-prueban-con-dobles-no-con-fakexrmeasy) |
| Despliegue y versiones | [D-27](#d-27-todo-cambio-de-plataforma-en-dev-va-por-sdk-solo-en-dev), [D-29](#d-29-el-rename-de-processchunk-se-hizo-en-dos-pasos-sin-ventana-rota), [D-30](#d-30-el-código-del-plugin-se-recarga-por-versión-del-package) |
| Export e import de templates | [D-31](#d-31-export-de-templates-con-refs-estables) |
