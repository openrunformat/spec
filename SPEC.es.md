# Open Run Format (ORF) 1.3 — resumen en español

Este resumen no es normativo: manda [SPEC.md](SPEC.md) (inglés). Versión de la especificación: 1.3 (borrador).

## Qué es

Un `.runlog` guarda la telemetría de **una salida** (una sesión en circuito, un tramo, una ruta): series temporales de
canales, las vueltas o tramos que se detectan en ellas y la procedencia de los datos. Está pensado para escribirse
desde un móvil, resistir cortes (batería, app cerrada) y leerse con cualquier lector JSON y zstd.
Inspirado en MTJ (<https://github.com/tobi/motorsport-telemetry-rs>, MIT); no se copió código.

## Estructura

- Un registro JSON por línea (JSONL, UTF-8 sin BOM, LF; se acepta CRLF y se ignoran líneas vacías). Cada registro
  tiene `k`: `h` (cabecera), `ch` (canal), `d` (bloque de muestras), `course` (recorrido), `seg` (tramo), `ev`
  (evento), `end` (cierre).
- El primer registro es la cabecera, y solo hay una. Un canal se declara antes de sus muestras. Tras el primer `end`
  se ignora todo, pero un lector DEBERÍA avisar al usuario si hay algún byte o línea no vacía después (la biblioteca lo
  expone como `RunDocument.HasTrailingContent`; `Complete` no cambia). Registros y campos desconocidos se ignoran.
- Compresión opcional: una o varias **tramas zstd concatenadas**. El lector detecta la firma `28 B5 2F FD` (o una
  trama saltable). El escritor no parte líneas entre tramas y activa el checksum; el lector acepta líneas que cruzan
  tramas y salta tramas saltables.
- Tiempo: siempre **enteros en nanosegundos**. Los instantes (`t0`, `t`, `s`, `e`, `splits`) son relativos a `t = 0`;
  `time` (tiempo oficial de un tramo) y `dur` (cierre) son **duraciones**, no instantes. La cabecera trae `utc`
  (epoch ns de `t = 0`) y `tz` (solo para mostrar).

## Registros

- **Cabecera**: `orf` (p. ej. `"1.0"`), `utc`, `id`, `kind` (`real`, `sim` o `synthetic`, obligatorio), y opcionales `tz`,
  `discipline` (`car`, `motorbike`, `bike`, `kart`, `other`), `device`, `converter`/`writer` (`{id, version}`),
  `sourceFiles` (`{name, sha256}`), `precision` (`segTolMs`, `gpsHz`), `derivedFrom` (desde 1.2) y `meta` (cadenas).
- `derivedFrom` (`{id, s, e}`, desde 1.2): el fichero es un extracto de otra salida. `id` es el `id` de la cabecera de
  esa salida y `[s, e)` el intervalo, en ns relativos al tiempo 0 **de esa salida** (`0 <= s < e`). El tiempo 0 de este
  fichero es el inicio del extracto (su `utc` es el de origen más `s`) y el fichero tiene su propio `id`. Sirve, por
  ejemplo, para exportar una vuelta o varias seguidas. Un lector que lo encuentra mal formado lo ignora sin rechazar el
  fichero.
- **Canal**: `id`, `unit`, opcionales `src` y `hz`. Con `hz` es **regular** (finito, > 0, ≤ 1e9); sin `hz` es **explícito**.
- **Bloque**: `ch`, `t0`, `v` (números; `null` = muestra sin valor; NaN se escribe como `null`). Regular: la muestra
  *i* está en `t0 + round(i·1e9/hz)` (cociente en doble IEEE-754 `(i×1e9)/hz`, redondeo hacia fuera en .5) y no lleva `t`. Explícito: `t` obligatorio, misma longitud que `v`,
  estrictamente creciente. Entre bloques de un canal, el primer instante de un bloque debe ser **estrictamente mayor**
  que el último del bloque anterior. Motivo (no normativo): cada instante tiene como máximo un valor por canal y el
  lector nunca tiene que elegir. Ejemplo: un móvil escribe `speed` a 10 Hz en bloques de 1 s (0,0–0,9 s y luego 1,0 s);
  si una corrección del reloj GNSS hiciera empezar el siguiente bloque en 0,5 s, ese bloque es inválido. El escritor
  debe resolver los saltos de reloj (descartar o reajustar muestras, o empezar otra grabación), no emitir bloques solapados. Un hueco es ausencia de datos; no se inventan muestras. No se permiten
  números no finitos en ningún sitio.
- **Recorrido** (`course`): `start`, `finish` (puertas `[lat1,lon1,lat2,lon2]`; iguales en circuito), `splits`,
  `circuit`, `variant`, `circuitUid`, `variantUid` y `gates` (identificadores de circuito: sección siguiente). Un
  fichero DEBERÍA tener un solo recorrido; si hay varios, los lectores usan el último.
- **Tramo** (`seg`): `kind` (`lap`, `stage`, `run`, `outlap`, `inlap`), `s`, `e`, `src` (`origin`, `gates`, `user`);
  opcionales `time` (tiempo oficial), `tol` (ms) y `splits`.
- **Evento** (`ev`): `kind` (`stop`, `flag`, `note`), `s`, opcionales `e` y `text`.
- **Cierre** (`end`): `dur` = máximo entre canales de (último instante + un periodo, si es regular; último instante,
  si es explícito) y `n` = total de muestras.

## Canales y unidades

SI con excepciones: velocidad m/s, presión Pa, aceleración m/s², temperatura °C, ángulos y velocidad angular en
grados, porcentajes en %, rpm, lat/lon en grados WGS84, distancia m (`distance`: recorrida en esta grabación; `odometer`: total del vehículo). Lista inicial en SPEC.md, sección 5
(`lat`, `lon`, `alt`, `speed`, `heading`, `rpm`, `throttle`, `brake`, `brake_pressure`, `gear`, `acc_lat`...).
**Ejes y signos (normativo, sección 5.1):** ejes del vehículo según ISO 8855 (x adelante, y a la izquierda, z arriba,
sistema dextrógiro). `acc_lon` > 0 al acelerar, < 0 al frenar; `acc_lat` > 0 cuando la aceleración apunta a la
izquierda (curva a izquierdas), < 0 en curva a derechas; `acc_vert` > 0 hacia arriba; todos los `acc_*` sin gravedad
(en reposo sobre suelo llano: 0, 0, 0). `yaw_rate` > 0 al girar a la izquierda (antihorario visto desde arriba);
`roll_rate` > 0 cuando baja el lado derecho; `pitch_rate` > 0 cuando baja el morro; `steering` y `steering_angle` > 0
al girar el volante a la izquierda. **Conversores:** muchos registradores usan otros convenios (G longitudinal
positivo al frenar, G lateral positivo a la derecha, Z hacia abajo); un conversor DEBE normalizar signos y unidades a
este convenio y DEBERÍA indicar el convenio de origen en `meta` si no puede determinarlo.

`steering` (%) y `steering_angle` (grados) son canales distintos y nunca se convierten uno en otro. Un canal propio usa
`x-{origen}-{nombre}` y declara su unidad.

## Versionado

`orf` = `mayor.menor`. Un lector 1.x lee cualquier 1.y; rechaza un mayor distinto de 1. Los cambios incompatibles
suben el mayor.

## Cortes

Sin `end`, la grabación está cortada pero es legible (se informa como incompleta). También terminan la lectura sin
error: una trama zstd final truncada, dañada o no decodificable, y una última línea sin LF que no se puede interpretar.
La **primera** trama que falle acaba el fichero (las posteriores, aunque estén bien, se descartan), igual que unos
bytes finales que no empiezan una trama zstd. Una línea terminada en LF que no es un registro válido es un error.
Un escritor DEBERÍA vaciar (flush) periódicamente: lo que sigue en su búfer se pierde si la grabadora muere; el flush
no fuerza la escritura a disco (no hace fsync). Quien reescribe un fichero existente DEBERÍA hacerlo de forma atómica
(fichero temporal en el mismo directorio, volcado a disco y movimiento sobre el destino); `Runlog.Write(ruta, ...)` y
`RunMetaFile.Write(ruta, ...)` lo hacen. Sin cabecera completa no se puede leer.

## `.runmeta`

Fichero aparte, opcional, JSON sin comprimir: `orf`, `id`, `runlogSha256` y opcionales `vehicle`, `setup`
(`{s, e, values}`), `drivers` (`{s, e, id, name, initials?}`, desde 1.1), `parameters` (desde 1.3), `notes`
(`{t, text}`) y `userSegments`. Los instantes (`s`, `e`, `t`, `splits`) son **epoch ns absolutos**; `time` de `userSegments` sigue siendo una duración. Solo se
aplica si `id` coincide con el de la cabecera y el hash con el del `.runlog`.

`drivers` (1.1): quién conducía en cada intervalo. `s` y `e` son epoch ns absolutos con `e > s` y el intervalo es
semiabierto (`[s, e)`): dos intervalos contiguos no se solapan. `id` y `name` son cadenas no vacías; `initials` es
opcional. Los intervalos NO DEBEN solaparse. Un lector ignora todo el campo (como si no existiera) si alguna entrada no
es un objeto, le falta `s`, `e`, `id` o `name`, tiene un tipo erróneo, `id` o `name` vacíos, `e <= s` o se solapa con
otra; el resto del fichero se lee igual. Un hueco significa «sin piloto registrado». `id` es un identificador opaco y
estable que elige el escritor: DEBERÍA ser aleatorio y NO DEBE derivarse de datos personales. `name` es un dato
personal: los escritores DEBERÍAN incluirlo solo con el consentimiento del usuario.

Vocabulario de `setup` (1.3, SPEC §8.1): **solo rige si `orf` es 1.3 o posterior**. En un fichero 1.0–1.2 todas las
claves de `setup` son texto libre y NO DEBEN reinterpretarse (un `camber` de un fichero 1.2 no son radianes ni se
convierte). Un escritor que reescribe un fichero con `orf` anterior a 1.3 DEBE, antes de escribirlo como 1.3 o
posterior, convertir al vocabulario las claves cuyo significado conoce y renombrar **toda otra clave cuya clave base no
empiece por `x-`** (choque o no con el vocabulario) a `x-legacy-<clave>`, conservando el sufijo de posición y la
compañera `.ref` (`camber.fl` pasa a `x-legacy-camber.fl` y `front_pressure` a `x-legacy-front_pressure`); las claves
`x-` se conservan. La biblioteca de referencia no convierte nada: renombra toda clave base que no empiece por `x-`. Un escritor NO DEBE
escribir un `orf` menor que el del fichero que reescribe: escribe el mayor de los dos (también en `.runsetup`). Una clave es una clave base con sufijo de posición opcional: por rueda `.fl`, `.fr`, `.rl`, `.rr`; por eje
`.f`, `.r`; sin sufijo si el valor es único. La clave compañera `<clave>.ref` vale `<catálogo>:<id>` y solo enlaza a
catálogos **públicos**, nunca a ids de unidades privadas ni de bibliotecas locales. Una clave fuera del vocabulario DEBE
empezar por `x-`, y un escritor NO DEBE usar una clave estándar con otro significado, unidad o ámbito. Los valores son siempre
cadenas; los numéricos en SI, en notación de ida y vuelta y con punto decimal. Las 21 claves estándar:

| Clave | Ámbito | Unidad | Tipo |
|---|---|---|---|
| `tyre_pressure_cold`, `tyre_pressure_hot` | rueda | Pa (manométrica) | decimal |
| `camber` (negativa = parte superior hacia dentro), `toe` (positiva = convergente) | rueda | rad | decimal |
| `caster` | rueda, solo delanteras | rad | decimal |
| `ride_height` | eje | m | decimal |
| `spring_rate` | rueda | N/m | decimal |
| `damper_stiffness` (1 vía); `damper_bump`, `damper_rebound` (2 vías); `damper_bump_low`, `damper_bump_high` (3–4 vías); `damper_rebound_low`, `damper_rebound_high` (4 vías) | rueda | — (clics) | integer |
| `damper_click_reference` (`from_hard` / `from_soft`) | único | — | choice |
| `wing_angle` | eje | rad | decimal |
| `brake_bias` (fracción delante, 0–1) | único | 1 | decimal |
| `fuel_mass`, `ballast_mass`, `driver_mass` | único | kg | decimal |
| `tyres` (modelo; admite `tyres.ref`) | único | — | text |

`driver_mass`, y cualquier parámetro descrito con `"personal": true`, es un dato personal: los escritores DEBERÍAN
incluirlo solo con el consentimiento del usuario.

`parameters` (1.3, SPEC §8.2): objeto clave base → descripción `{name, type, scope, unit?, choices?, allowOther?,
catalogKind?, min?, max?, precision?, group?, personal?, positions?, clickReference?}` para las claves no estándar (las
estándar no la necesitan). `type`: `text`, `bool`, `choice`, `integer`, `decimal`; `scope`: `single`, `axle`, `wheel`;
`group` es una lista abierta: los ids estándar son `tyres`, `geometry`, `suspension`, `aero`, `brakes`, `electronics`,
`mass` y `other`; un lector DEBE aceptar cualquier cadena y una aplicación que no conoce un grupo DEBERÍA mostrar el
parámetro en `other` (la composición solo mira `suspension`). Un lector ignora **solo la
entrada** mal formada (falta `name`, `type` o `scope`, tipo JSON erróneo, `choice` sin opciones, `min > max`,
`precision` fuera de `decimal`, `clickReference` desconocido...); el resto de entradas y del fichero se lee igual.
Una clave estándar PUEDE tener descripción, pero su `type`, `unit` y `scope` DEBEN ser los del vocabulario y los
lectores usan los del vocabulario (si difieren, ignoran esos tres campos y conservan el resto); se respetan `name`,
`min`/`max`, `precision` (solo si el tipo estándar es `decimal`) y `group`, y `positions` solo restringe las posiciones
del vocabulario (las que quedan fuera se descartan). Rige donde rige el vocabulario (`orf` ≥ 1.3), también en `.runsetup`.

## Ficheros `.runsetup`

Desde 1.3 (SPEC §11): JSON UTF-8 sin comprimir, independiente de un `.runlog`, para compartir una **plantilla**
(`template`, los parámetros de un vehículo), una **modificación** (`modification`, un paquete que añade, cambia o quita
parámetros) o un **reglaje** completo (`setup`, con sus valores). Campos: `orf` (los escritores DEBEN escribir 1.3 o
posterior; se aceptan menores anteriores), `kind`, `id` y `version` (obligatorios en plantilla y modificación),
`appliesTo` (ids de modelo; vacío = genérico), `name` (por idioma), `vehicle`, `parameters` (en orden; `{}` para una
clave estándar con su definición estándar, descripción para las demás), `values` (obligatorio en `setup`),
`damperWays` (`{front, rear}`, 0–4), `operations`, `composition` (`{base, modifications}` con referencias `{id,
version}`; sin `modifications` = ninguna) y `extensions` (un objeto por aplicación, p. ej. `x-myapp`, que se conserva al reescribir; los campos
desconocidos se ignoran y no se conservan).

Se rechaza el fichero si el JSON está roto, falta `orf` o su mayor no es 1, falta o es desconocido `kind`, `id` está vacío, `version` no es un
entero positivo, una plantilla o modificación no tiene `id` o `version`, un `setup` no tiene `values`, `name` no tiene idiomas, o `vehicle`, `values` o
`appliesTo` no son cadenas. Una entrada de `parameters` o una operación mal formadas se ignoran solas; por
compatibilidad hacia delante, `{}`/`null` en `parameters`, o un `add`/`change` sin descripción, para una clave sin `x-`
que el lector no conoce (p. ej. añadida en una versión menor posterior) se conserva como parámetro `text` de ámbito
`single` con la clave por nombre (para una clave `x-` se ignora); un `damperWays`,
`composition` o `extensions` mal formado se ignora entero. Al reescribir un fichero anterior a 1.3 se aplica el
renombrado `x-legacy-` a las claves de `values` y `parameters`.

Operaciones: `{"op":"add"|"change","key","description"?}`, `{"op":"remove","key"}` y
`{"op":"damperWays","ways":{"front","rear"}}`; una clave no estándar necesita `description` (`"description": null` cuenta como ausente). **Composición** (las
aplicaciones componen las capas así): se parte de los parámetros y las vías de la plantilla base (0/0 si no tiene) y se aplican las
modificaciones en orden. `add` de una clave existente actúa como `change` y avisa; `change` o `remove` de una ausente no
hace nada y avisa; `damperWays` sustituye las vías. Al final, las claves de amortiguador **se derivan solo de las vías
finales** (las añadidas o quitadas a mano se sustituyen, sin aviso): las de las vías delanteras para `fl fr`, las de las
traseras para `rl rr` y una común a los dos ejes para las cuatro ruedas, colocadas tras el último parámetro de
suspensión (un parámetro cuya descripción tiene el grupo `suspension`, o la clave `ride_height`, `spring_rate` o
`damper_click_reference`) o al final.
`damper_click_reference` no se deriva y se mantiene aunque las vías sean 0/0.

## Identificadores de circuito

Sección 10 de SPEC.md. Un circuito se nombra con un id del registro abierto
<https://github.com/openrunformat/circuits>: `ES-catalunya` = país ISO 3166-1 alfa-2 en mayúsculas, guion y `name`
(2 a 32 caracteres `a-z`, `0-9` y `-`, sin empezar ni acabar en guion, sin `--`; único en el país). Una variante
(trazado) es `gp` o `gp@2023`, con año o año-mes desde el que rige la geometría (se omite si solo hay una). Id de
circuito = `ES-catalunya`; id de variante = `ES-catalunya/gp@2023`. En `course` se guardan por separado (`circuit`,
`variant`). La forma URI `orf:circuit/ES-catalunya/gp@2023` es para uso fuera de un `.runlog` y no se escribe en
`course`. Los ids distinguen mayúsculas y nunca se normalizan.

- `circuitUid` y `variantUid`: UUID (v7, forma canónica 8-4-4-4-12; se escriben en minúsculas) que no cambia nunca.
  Si se renombra, el id antiguo pasa a `aliases` del registro y los consumidores DEBERÍAN resolverlo y DEBERÍAN
  preferir el `uid` cuando estén ambos.
- Ids provisionales `local-<uuid>` (minúsculas, UUIDv7 recomendado) para circuitos que no están en el registro; los
  genera la aplicación y son estables para ese circuito en ese equipo.
- Los escritores DEBEN escribir ids que sigan la gramática; los lectores NO DEBEN rechazar otros (antes `circuit` era
  texto libre). Una variante o un `circuitUid` sin circuito, o un
  `variantUid` sin variante, no se deben escribir, pero se aceptan al leer; `gates` solo se escribe como `origin` o
  `registry`. Un `circuitUid` o `variantUid`
  que no sea un UUID invalida el fichero.
- `gates`: `origin` = puertas de la fuente de datos; `registry` = puertas tomadas del registro. Las puertas van
  siempre en `course`: el `.runlog` no depende del registro. Clave recomendada de `meta`: `circuit.finish_offset_m`
  (distancia en metros, como cadena decimal, entre la meta del origen y la del registro).

## Conformidad

`conformance/valid` (con `<nombre>.expected.json`, que incluye tiempos, valores y `end` cuando se declaran) debe leerse;
`conformance/invalid` (un fichero por regla de la sección 4 de SPEC.md) debe rechazarse. La prosa de SPEC.md manda: los
esquemas JSON pueden ser más estrictos.
`conformance/runmeta/valid/<nombre>.runmeta` con `<nombre>.expected.json` `{"drivers": n, "parameters": n, "setupKeys": n}`
(claves opcionales): un lector DEBE aceptar el fichero y producir ese número de pilotos (un campo `drivers` ignorado
cuenta 0), de descripciones de parámetros y de claves del primer intervalo de `setup`.
`conformance/runsetup/valid/<nombre>.runsetup` con `<nombre>.expected.json` `{"kind", "parameters": n, "values": n,
"operations": n (opcional)}` debe leerse y cumplir el esquema; `conformance/runsetup/invalid` debe rechazarse.
`conformance/compose/<caso>/` tiene `base.runsetup`, las modificaciones `mod-1.runsetup`, `mod-2.runsetup`... (en ese
orden) y `expected.json` `{"parameters": [{"key", "positions": [...] | null}...], "damperWays": {front, rear},
"warnings": n}`: una implementación de la composición DEBE producir esos parámetros en ese orden con esas posiciones
(`null` = todo su ámbito), esas vías finales y ese número de avisos.
`conformance/circuit-ids.json` lista ids y URI válidos e inválidos; `expected.json` puede llevar un bloque `course`.
Esquemas JSON en `schema/` (un registro por línea). Un campo opcional con tipo JSON incorrecto (p. ej. un valor de `meta`
que no es cadena) invalida el fichero.
