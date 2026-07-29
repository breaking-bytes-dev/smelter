# Smelter — Documentación

Smelter transforma archivos CSV planos (denormalizados) en tablas Dataverse normalizadas, mediante
configuración declarativa, procesamiento por lotes idempotente y portabilidad de configuración entre
ambientes.

> Empezá por tu rol. Cada sección está pensada para una audiencia distinta.

## 🧭 Por dónde empezar según tu rol

| Soy… | Empezá por |
|------|-----------|
| **Nuevo en Smelter** | [01 · Overview → ¿Qué es Smelter?](01-overview/what-is-smelter.md) |
| **Desarrollador** | [03 · Developer Guide](03-developer-guide/) |
| **Administrador / Configurador** | [04 · Admin Guide](04-admin-guide/) |
| **Operador (cargo datos)** | [05 · User Guide](05-user-guide/) |
| **Necesito la referencia técnica** | [02 · Solution Reference](02-solution-reference/) |

## 📚 Índice

### 01 · Overview
- [¿Qué es Smelter?](01-overview/what-is-smelter.md) — propósito, problema que resuelve, capacidades.
- [Arquitectura](01-overview/architecture.md) — el motor chunked, el flujo de procesamiento.

### 02 · Solution Reference (la solución Dataverse)
- [Modelo de datos](02-solution-reference/data-model.md) — diagrama ER y relaciones.
- [Tablas](02-solution-reference/tables/) — una página por tabla (campos, tipos, OptionSets):
  Import Template · Table Mapping · Column Mapping · Relationship Mapping · Import Job · Import Job Log.
- [Custom APIs](02-solution-reference/custom-apis.md) — PrepareImport, ProcessChunk, ExportTemplate, ImportTemplate.
- [Plugins](02-solution-reference/plugins.md) — assembly, plugin types, steps registrados.
- [Cloud Flows](02-solution-reference/cloud-flows.md) — el flow orquestador.

### 03 · Developer Guide
- [Codebase](03-developer-guide/codebase.md) — estructura del código y responsabilidad de cada componente.
- [Build & Deploy](03-developer-guide/build-and-deploy.md) — compilar, desplegar con PRT, gotchas.
- [Testing](03-developer-guide/testing.md) — xUnit + NSubstitute, strict TDD.
- [Transform DSL](03-developer-guide/transform-dsl.md) — el lenguaje de transformaciones.

### 04 · Admin Guide (configuradores)
- [Configurar un template](04-admin-guide/configuring-a-template.md) — armar los mappings.
- [Alternate Keys](04-admin-guide/alternate-keys.md) — el contrato de las claves alternativas.
- [Import nuevo de punta a punta](04-admin-guide/new-import-end-to-end.md) — del CSV a las tablas.
- [Exportar / Importar templates](04-admin-guide/export-import-templates.md) — portabilidad entre ambientes.
- [Seguridad y roles](04-admin-guide/security-and-roles.md) — roles, teams, ownership de jobs por team, cascade sharing.

### 05 · User Guide (operadores)
- [Usar la app](05-user-guide/using-the-app.md) — la Model-Driven App, pantalla por pantalla.
- [Correr un import](05-user-guide/running-an-import.md) — paso a paso.
- [Monitoreo y reintentos](05-user-guide/monitoring-and-retry.md) — dashboard, logs, errores, reintentar.

### 06 · Examples
- [Cobranza](06-examples/cobranza.md) — ejemplo completo: CSV de 32 columnas → tablas normalizadas.

### 🗄️ Archivo
`_archive/` contiene documentos históricos o superados (análisis de issues original, guías previas a
rediseños). No usar como referencia actual.

---

## Conceptos transversales (los 4 que hay que entender sí o sí)

1. **Alternate Keys + Upsert** — cada tabla destino necesita una clave alternativa; el import es idempotente.
2. **Procesamiento chunked** — cada lote es una invocación independiente; sin límite de tiempo ni de depth.
3. **Orden DAG** — los padres se procesan antes que los hijos; los lookups se resuelven por valor de negocio.
4. **El motor corre como SYSTEM** — la seguridad controla el acceso en la app, nunca rompe el procesamiento.

## Estado del producto
- **Solo CSV** (Excel descartado). **100% Power Platform** (sin Azure).
- Stack del plugin: C# .NET Framework 4.6.2.
