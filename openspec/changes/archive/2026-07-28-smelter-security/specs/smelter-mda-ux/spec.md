# Smelter MDA UX Specification

## Purpose

Dar a los usuarios de la app model-driven de Smelter vistas útiles de Import Job, acciones de ejecución
desde el command bar, un dashboard accesible desde el sitemap, y garantizar que el rename de la Custom API
`processchunk` (fix del typo `anic_capi_smelter_processchunk` → `sanic_capi_smelter_processchunk`) no rompa
el Cloud Flow que la invoca durante el deploy.

## Requirements

### Requirement: Vistas custom de Import Job

La entidad Import Job MUST incluir 3 vistas públicas provisionadas vía SDK como solution components:
**"Jobs Activos"** (status en progreso o preparando), **"Completados con Errores"** (`failedcount` > 0), y
**"Todos los Jobs"** (sin filtro, orden descendente por fecha de creación).

#### Scenario: Cada vista filtra correctamente

- GIVEN jobs en estados Preparando, En Progreso, Completado (con y sin `failedcount` > 0) y Fallido
- WHEN un usuario abre "Jobs Activos", "Completados con Errores" y "Todos los Jobs"
- THEN la primera SHALL mostrar solo Preparando/En Progreso, la segunda solo el Completado con errores, y
  la tercera SHALL mostrarlos todos ordenados por fecha descendente

### Requirement: Botones de command bar Ejecutar/Reintentar

El formulario/grid de Import Job MUST exponer botones de ribbon **Ejecutar** (visible en estado
Pendiente/inicial) y **Reintentar** (visible en estado Fallido), provisionados vía SDK. Ambos MUST invocar
el flujo existente (`prepareimport`/`processchunk`) y MUST respetar los privilegios del rol Operador
definidos en `smelter-access-control` (solo jobs de sus templates).

#### Scenario: Ejecutar y Reintentar visibles según estado

- GIVEN un Import Job Pendiente y otro Fallido, ambos con template accesible al operador
- WHEN el operador abre cada registro
- THEN el Pendiente SHALL mostrar Ejecutar (sin Reintentar) y el Fallido SHALL mostrar Reintentar (sin Ejecutar)

#### Scenario: Sin acceso al template, sin acción posible

- GIVEN un Import Job cuyo template no está compartido con ningún team del operador
- WHEN el operador intenta abrir o accionar sobre ese job
- THEN el acceso SHALL denegarse por privilegios (consistente con `smelter-access-control`)

### Requirement: Dashboard "Smelter Dashboard" en el sitemap

MUST existir un dashboard **"Smelter Dashboard"** (con al menos un resumen de jobs por estado y jobs con
errores recientes), provisionado vía SDK y accesible como entrada del sitemap de la app.

#### Scenario: Dashboard accesible desde el sitemap

- GIVEN un usuario con acceso a la app Smelter (rol Configurador)
- WHEN navega el sitemap
- THEN SHALL encontrar una entrada que abra "Smelter Dashboard" con un resumen de jobs por estado

### Requirement: Rename de processchunk sin ventana rota

La Custom API `anic_capi_smelter_processchunk` MUST renombrarse a `sanic_capi_smelter_processchunk`. El
Cloud Flow que la invoca MUST actualizarse al nombre corregido, y ambos cambios MUST desplegarse en el
mismo release de `tools/Smelter.Deploy`, sin dejar al flow apuntando a una API inexistente en ningún momento
posterior al deploy.

#### Scenario: Post-deploy el flow resuelve la API

- GIVEN un deploy exitoso que incluye el rename y la actualización del Cloud Flow
- WHEN el Cloud Flow invoca la acción de procesar chunk
- THEN SHALL resolver `sanic_capi_smelter_processchunk` y ejecutar sin error de "acción no encontrada"

#### Scenario: Verify detecta un flow desalineado

- GIVEN un ambiente donde el rename se aplicó pero el Cloud Flow no se actualizó
- WHEN corre `tools/Smelter.Verify`
- THEN SHALL reportar la inconsistencia en vez de dejarla pasar silenciosamente
