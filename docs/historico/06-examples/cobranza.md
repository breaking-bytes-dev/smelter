[← Volver al índice de documentación](../README.md)

# Smelter — Import de Cobranza (CSV denormalizado → 7 tablas)

> Modelo totalmente normalizado. Cada fila del CSV alimenta 7 entidades. Decisiones: Cliente 1:N Cuenta;
> direcciones/teléfonos normalizados; Producto/Agente/Fiador como catálogos; Cliente = tabla custom.

## 1. Estructura de tablas en Dataverse

> Prefijo `sanic_`. `Tipo` de Dirección/Teléfono va como **texto simple** (no Choice) para que el alt key
> y el default sean directos.

### `sanic_cliente` — Cliente (deudor) · **Alt key: `sanic_cif`**
| Columna | Tipo | Del CSV |
|---------|------|---------|
| `sanic_nombre` | Text | Cliente |
| `sanic_cif` | Text | CIF |
| `sanic_departamento` | Text | Departamento |
| `sanic_distrito` | Text | Distrito |
| `sanic_trabajo` | Text | Trabajo |

### `sanic_producto` — Producto · **Alt key: `sanic_codigo`**
| `sanic_codigo` | Text | CodigoProducto |
| `sanic_tipo` | Text | TipoProducto |

### `sanic_agente` — Agente · **Alt key: `sanic_nombre`**
| `sanic_nombre` | Text | Agente |

### `sanic_fiador` — Fiador · **Alt key: `sanic_nombre`**
| `sanic_nombre` | Text | NombreFianza |
| `sanic_direccion` | Text | DireccionFianza |

### `sanic_direccion` — Dirección · **Alt key compuesta: `sanic_cif` + `sanic_tipo`**
| `sanic_clienteid` | Lookup → Cliente | (resuelto por CIF) |
| `sanic_cif` | Text | CIF *(copiado para la alt key)* |
| `sanic_tipo` | Text | "Casa" / "EC" / "Trabajo" *(default por mapping)* |
| `sanic_direccion` | Text | DireccionCasa / EC / Trabajo |

### `sanic_telefono` — Teléfono · **Alt key compuesta: `sanic_cif` + `sanic_tipo`**
| `sanic_clienteid` | Lookup → Cliente | (resuelto por CIF) |
| `sanic_cif` | Text | CIF *(copiado para la alt key)* |
| `sanic_tipo` | Text | "Celular" / "Casa" *(default)* |
| `sanic_numero` | Text | NumeroCelular / NumeroCasa |

### `sanic_cuentamora` — Cuenta en Mora (el grano) · **Alt key: `sanic_numerocuenta`**
| Columna | Tipo | Del CSV |
|---------|------|---------|
| `sanic_numerocuenta` | Text | NumeroCuenta |
| `sanic_clienteid` | Lookup → Cliente | (por CIF) |
| `sanic_productoid` | Lookup → Producto | (por CodigoProducto) |
| `sanic_agenteid` | Lookup → Agente | (por Agente) |
| `sanic_fiadorid` | Lookup → Fiador | (por NombreFianza) |
| `sanic_saldovencidonio` `sanic_saldovencidousd` `sanic_totalsaldovencido` `sanic_contagiadousd` `sanic_pagominimonio` `sanic_pagominimousd` `sanic_retironio` `sanic_retirousd` | Decimal | montos |
| `sanic_diasmora` `sanic_nivelmora` | Whole Number | DiasMora, NivelMora |
| `sanic_fechaultimopago` `sanic_fechapago` | DateTime | FechaUltimoPago, FechaPago |
| `sanic_ciclo` `sanic_tipoasignacion` `sanic_tipoespecial` `sanic_nota` | Text | Ciclo, TipoAsignacion, TipoEspecial, Nota |

### Relaciones (1:N)
`Cliente → Direccion` · `Cliente → Telefono` · `Cliente → CuentaMora` · `Producto → CuentaMora` ·
`Agente → CuentaMora` · `Fiador → CuentaMora`

### Orden de procesamiento (DAG)
1. **Padres** (sin dependencias): Cliente, Producto, Agente, Fiador.
2. **Hijas**: Direccion, Telefono, CuentaMora.

---

## 2. Config del Import Template

**Template**: Source Type = CSV, Has Header = Sí, Delimiter = `,`, Chunk Size = 1000.

### Table Mappings (10 — sí, 10: las direcciones/teléfonos se desdoblan)

| # | Target Table | Processing Order | Dedup Columns |
|---|--------------|:---:|---------------|
| 1 | `sanic_cliente` | 1 | `sanic_cif` |
| 2 | `sanic_producto` | 1 | `sanic_codigo` |
| 3 | `sanic_agente` | 1 | `sanic_nombre` |
| 4 | `sanic_fiador` | 1 | `sanic_nombre` |
| 5 | `sanic_direccion` (Casa) | 2 | `sanic_cif,sanic_tipo` |
| 6 | `sanic_direccion` (EC) | 2 | `sanic_cif,sanic_tipo` |
| 7 | `sanic_direccion` (Trabajo) | 2 | `sanic_cif,sanic_tipo` |
| 8 | `sanic_telefono` (Celular) | 2 | `sanic_cif,sanic_tipo` |
| 9 | `sanic_telefono` (Casa) | 2 | `sanic_cif,sanic_tipo` |
| 10 | `sanic_cuentamora` | 3 | `sanic_numerocuenta` |

> 💡 **La sutileza clave:** Smelter genera **1 registro por Table Mapping por fila**. Como cada fila trae
> 3 direcciones y 2 teléfonos, creás **un Table Mapping por cada una**, todos apuntando a la misma tabla,
> diferenciados por el **DefaultValue del campo `sanic_tipo`** y por qué columna del CSV mapean.

### Column Mappings por Table Mapping

**Cliente:** Cliente→`sanic_nombre`, CIF→`sanic_cif`, Departamento→`sanic_departamento`, Distrito→`sanic_distrito`, Trabajo→`sanic_trabajo`.

**Producto:** CodigoProducto→`sanic_codigo`, TipoProducto→`sanic_tipo`.

**Agente:** Agente→`sanic_nombre`.

**Fiador:** NombreFianza→`sanic_nombre`, DireccionFianza→`sanic_direccion`.

**Direccion (Casa):** DireccionCasa→`sanic_direccion`, CIF→`sanic_cif`, `sanic_tipo` = **Default "Casa"** (Source Column vacío).
**Direccion (EC):** DireccionEC→`sanic_direccion`, CIF→`sanic_cif`, `sanic_tipo` = Default "EC".
**Direccion (Trabajo):** DireccionTrabajo→`sanic_direccion`, CIF→`sanic_cif`, `sanic_tipo` = Default "Trabajo".

**Telefono (Celular):** NumeroCelular→`sanic_numero`, CIF→`sanic_cif`, `sanic_tipo` = Default "Celular".
**Telefono (Casa):** NumeroCasa→`sanic_numero`, CIF→`sanic_cif`, `sanic_tipo` = Default "Casa".

**CuentaMora:** NumeroCuenta→`sanic_numerocuenta`; los 8 montos→sus decimales (Data Type **Decimal**); DiasMora/NivelMora→**Whole Number**; FechaUltimoPago/FechaPago→**DateTime**; Ciclo/TipoAsignacion/TipoEspecial/Nota→Text. (Los valores de CIF, CodigoProducto, Agente, NombreFianza los lee el Relationship Mapping, no hace falta column mapping propio salvo que quieras guardarlos.)

### Relationship Mappings (resuelven los lookups por valor de negocio)

| Parent | Child | Lookup Column On Child | Parent Dedup Columns (del CSV) |
|--------|-------|------------------------|--------------------------------|
| Cliente | Direccion (x3) | `sanic_clienteid` | CIF |
| Cliente | Telefono (x2) | `sanic_clienteid` | CIF |
| Cliente | CuentaMora | `sanic_clienteid` | CIF |
| Producto | CuentaMora | `sanic_productoid` | CodigoProducto |
| Agente | CuentaMora | `sanic_agenteid` | Agente |
| Fiador | CuentaMora | `sanic_fiadorid` | NombreFianza |

---

## 3. Advertencias (las que te van a morder si no las cuidás)

1. **TODAS las alt keys deben estar `Active`** antes de importar: `sanic_cif`, `sanic_codigo`,
   `sanic_nombre` (agente y fiador), `sanic_numerocuenta`, y las **compuestas** `sanic_cif+sanic_tipo`
   (dirección y teléfono). Sin ellas → `TargetData` (lo que ya viviste con `contact`).
2. **El truco de la alt key compuesta:** copiamos el `CIF` como texto dentro de Dirección/Teléfono y la
   alt key es `cif+tipo` (texto), NO sobre el lookup. Así el upsert es directo; el lookup al Cliente lo
   setea el Relationship Mapping aparte. (Una alt key sobre un lookup es más frágil — la evitamos.)
3. **Fechas:** `FechaUltimoPago` y `FechaPago` vienen como texto. Si el formato NO es ISO
   (ej. `dd/MM/yyyy`), usá un Transform Expression `DATEPARSE(FechaPago, "dd/MM/yyyy")` en ese column
   mapping. Si fallan, vas a verlo como `TypeError` en el log.
4. **NIO/USD** quedan como **columnas decimales separadas** (no Money) — matchea el CSV sin currency.
5. **Idempotencia:** reprocesar el mismo archivo no duplica nada (todo upsert por su alt key). Un cliente
   con 3 cuentas (3 filas) crea 1 cliente + 3 direcciones + 2 teléfonos (se re-upsertan iguales) + 3 cuentas.

---

## 4. Histórico diario (el archivo se sube cada día)

`sanic_cuentamora` guarda el **estado ACTUAL** (upsert por NumeroCuenta → pisa lo anterior). Para la
**historia** (cuántas veces se cargó, qué agente cada día) se agrega una tabla snapshot diaria.

> **Requiere una columna nueva en el CSV: `FechaCarga`** (la fecha del día, puesta por el sistema origen).
> Sin esa columna no hay forma de fechar el snapshot.

### `sanic_cargadiaria` — Snapshot diario · **Alt key: `sanic_numerocuenta` + `sanic_fechacarga`**
| Columna | Tipo | Del CSV |
|---------|------|---------|
| `sanic_numerocuenta` | Text | NumeroCuenta *(copiado para la alt key)* |
| `sanic_fechacarga` | DateTime (Date Only) | **FechaCarga** *(columna nueva)* |
| `sanic_cuentaid` | Lookup → CuentaMora | (por NumeroCuenta) |
| `sanic_clienteid` | Lookup → Cliente | (por CIF) — opcional, para reportes por cliente |
| `sanic_agenteid` | Lookup → Agente | (por Agente) |
| `sanic_tipoasignacion` | Text | TipoAsignacion |
| `sanic_saldovencidonio` … `sanic_retirousd` | Decimal | snapshot de montos del día |
| `sanic_diasmora` `sanic_nivelmora` | Whole Number | mora del día |

### Cómo encaja en el template
- **Table Mapping #11**: target `sanic_cargadiaria`, Processing Order **4**, dedup `sanic_numerocuenta,sanic_fechacarga`.
- **Column Mappings**: NumeroCuenta→`sanic_numerocuenta`, FechaCarga→`sanic_fechacarga` (DateTime; si el formato no es ISO, `DATEPARSE(FechaCarga,"dd/MM/yyyy")`), Agente→lookup, TipoAsignacion, + los 8 montos (Decimal) y DiasMora/NivelMora (Whole Number).
- **Relationship Mappings**: `CuentaMora → CargaDiaria` (`sanic_cuentaid`, por NumeroCuenta) · `Agente → CargaDiaria` (`sanic_agenteid`, por Agente) · `Cliente → CargaDiaria` (`sanic_clienteid`, por CIF, opcional).

### Por qué NO "guardar solo si cambió"
En cobranza `DiasMora` sube +1 cada día y los saldos varían → casi nunca dos días traen lo mismo, así que
el ahorro de un snapshot "liviano condicional" no se activa. Y detectar el cambio exigiría CDC (hash o
lógica custom en el plugin) que Smelter no tiene. Snapshot completo diario = simple y sin desperdicio real.

### Consultas que habilita
- *"¿Cuántas veces se cargó la cuenta X?"* → `count` de `cargadiaria` por `sanic_cuentaid`.
- *"¿Qué agente la tenía el día X?"* → `cargadiaria` filtrada por cuenta + `sanic_fechacarga` = X.
- *"Evolución de la mora"* → serie de `sanic_diasmora` / saldos por `sanic_fechacarga`.
