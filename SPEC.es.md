# Open Run Format (ORF) 1.0 — resumen en español

Este resumen no es normativo: manda [SPEC.md](SPEC.md) (inglés). Versión de la especificación: 1.0 (borrador).

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
  `sourceFiles` (`{name, sha256}`), `precision` (`segTolMs`, `gpsHz`) y `meta` (cadenas).
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
(`{s, e, values}`), `notes` (`{t, text}`) y `userSegments`. Los instantes (`s`, `e`, `t`, `splits`) son **epoch ns absolutos**; `time` de `userSegments` sigue siendo una duración. Solo se
aplica si `id` coincide con el de la cabecera y el hash con el del `.runlog`.

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
`conformance/circuit-ids.json` lista ids y URI válidos e inválidos; `expected.json` puede llevar un bloque `course`.
Esquemas JSON en `schema/` (un registro por línea). Un campo opcional con tipo JSON incorrecto (p. ej. un valor de `meta`
que no es cadena) invalida el fichero.
