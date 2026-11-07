# Open Run Format (ORF) 1.3 — draft

File extension: `.runlog`. Companion file: `.runmeta`. Setup file: `.runsetup`. Specification version: `1.3` (draft, see
`CHANGELOG.md`).

The key words MUST, MUST NOT, SHOULD and MAY are used as in RFC 2119.

Open Run Format is a file format for the telemetry of one run (a session on a track, a road stage, a ride) from a
vehicle or a person: time series of channels (speed, position, engine data...), the laps or stages found in them,
and where the data came from. It is meant to be easy to write on a phone, safe against a sudden stop of the
recorder (battery, app killed), compact, and readable by anyone with a JSON parser and a zstd decoder.

The format is inspired by the Motorsport Telemetry JSON (MTJ) project,
<https://github.com/tobi/motorsport-telemetry-rs> (MIT licence). Only ideas were taken from it; no code or text was copied.

## 1. File structure

### 1.1 Records

A `.runlog` is a sequence of **records**. Each record is one JSON object (RFC 8259) on its own line.

- Encoding is UTF-8 without BOM. Strings are written as raw UTF-8; writers MAY use JSON escapes but need not.
- A line ends with LF (`0x0A`). Readers MUST also accept CRLF and MUST ignore empty lines.
- Every record has a field `k` (a string) that tells its kind. The kinds are: `h`, `ch`, `d`, `course`, `seg`,
  `ev`, `end`.
- Readers MUST ignore records whose `k` they do not know, and MUST ignore fields they do not know, in every record.
  This is how the format grows inside a major version.

### 1.2 Order

1. The **first** record MUST be the header (`h`). There MUST be exactly one header. A file whose first record is
   not a header, or that contains a second header, is invalid.
2. A channel (`ch`) MUST be declared before any sample block (`d`) refers to it.
3. After that, records may appear in any order, except that nothing after the first `end` record is meaningful:
   readers MUST ignore everything after the first `end` record.
4. The `end` record is optional (see section 7).
5. Content after the first `end` record is ignored, but a reader SHOULD warn the user when any non-empty line or
   any byte follows it (in a plain or a compressed file), because it may be data that was appended by mistake or lost.
   The reference library reports this as `RunDocument.HasTrailingContent`; `Complete` is not affected.

### 1.3 Compression

A file is either plain JSONL or **zstd-compressed JSONL**. Readers MUST detect which by the first four bytes: the
zstd magic number `28 B5 2F FD` (little-endian `0xFD2FB528`) or a skippable-frame magic (`0x184D2A50` to
`0x184D2A5F`, little-endian) means compressed; anything else is plain JSONL.

A compressed file is **one or more zstd frames concatenated**. This is what makes incremental, crash-safe
writing possible: the writer buffers whole lines, closes a frame every few seconds or every N kilobytes, and
appends the next frame. If the recorder dies, only the frame that was still open is lost.

- A writer MUST NOT split a line between frames (it keeps lines whole). It SHOULD enable the zstd content
  checksum so that damaged frames are detected.
- A reader MUST accept a line that spans several frames (frames are decoded in order and their content is joined
  before splitting into lines) and MUST skip skippable frames.
- The reader reads a frame with only the bytes the frame needs, so appending is always safe.

Files SHOULD use the extension `.runlog` in both cases; the content decides.

## 2. Time

- Time is always an **integer number of nanoseconds** (a JSON number without fraction or exponent, in the signed
  64-bit range).
- The only civil time in the file is the header: `utc` (nanoseconds since the Unix epoch, UTC) is the instant that
  corresponds to file time `t = 0`, and `tz` is an IANA time zone name used only for display.
- The instants `t0`, `t`, `s`, `e` and `splits` are **relative** to `t = 0` ("file times").
- `time` (the official time of a segment) and `dur` (the duration of the data in the `end` record) are **durations**:
  lengths of time in nanoseconds, not instants relative to `t = 0`.
- Absolute time of any file time `t` is `utc + t`.

## 3. Records

Field tables use these markers: **R** required, O optional. Numbers listed as `ns` are integer nanoseconds.

### 3.1 Header — `k: "h"`

| Field | Type | | Meaning |
|---|---|---|---|
| `k` | string | R | `"h"` |
| `orf` | string | R | Specification version `major.minor`, e.g. `"1.0"` |
| `utc` | integer | R | Unix epoch nanoseconds of file time 0 |
| `tz` | string | O | IANA time zone, display only |
| `id` | string | R | Stable identifier of the run |
| `discipline` | string | O | Free text. Recommended: `car`, `motorbike`, `bike`, `kart`, `other` |
| `kind` | string | R | Provenance of the data: `real`, `sim` or `synthetic`. Any other value is invalid |
| `device` | string | O | Free text, e.g. `"phone + external GNSS receiver"` |
| `converter` | object | O | `{ "id": string, "version": string }`: the module that converted the data into this file |
| `writer` | object | O | `{ "id": string, "version": string }`: the program that wrote the file |
| `sourceFiles` | array | O | Originals the file was made from: objects `{ "name": string, "sha256": string }`. Absent or empty if the `.runlog` is itself the original |
| `precision` | object | O | `{ "segTolMs": number, "gpsHz": number }`: declared tolerance of segment times (ms) and real position-fix rate (Hz). Both optional |
| `derivedFrom` | object | O | `{ "id": string, "s": ns, "e": ns }`: the file is an excerpt of another run (added in 1.2, see below) |
| `meta` | object | O | Free dictionary, string keys and **string** values |

`converter.version` is the version of the converting module and is unrelated to `orf`; it exists so that tools can
decide when a file should be converted again.

`derivedFrom` (added in 1.2) says the file holds an interval cut from another run: `id` is the header `id` of that run
(non-empty) and `[s, e)` is the interval, in nanoseconds relative to time 0 of **that** run, with `0 <= s < e`. Time 0 of
this file is the start of the excerpt, so its `utc` is the source `utc` plus `s`. The file has its own `id`. A writer
uses it, for example, when it exports a single lap or several consecutive laps; other uses (a composition of pieces of
several runs) may extend it in later versions. A reader that finds `derivedFrom` malformed (not an object, missing or
wrongly typed fields, empty `id`, `s < 0` or `e <= s`) ignores the field without rejecting the file.

### 3.2 Channel — `k: "ch"`

| Field | Type | | Meaning |
|---|---|---|---|
| `k` | string | R | `"ch"` |
| `id` | string | R | Channel id, non-empty. A name from section 5, or `x-{origin}-{name}` |
| `unit` | string | R | Unit of the values (may be empty for a unitless channel) |
| `src` | string | O | Name of the channel in the origin |
| `hz` | number | O | Sampling rate. Present → **regular** channel. Absent → **explicit** channel |

- A channel id MUST be declared once; declaring it twice is invalid.
- `hz`, when present, MUST be finite, greater than 0 and at most 1 000 000 000.
- A channel with a standard id (section 5) SHOULD use that id's canonical unit. A custom channel declares its own.

### 3.3 Sample block — `k: "d"`

| Field | Type | | Meaning |
|---|---|---|---|
| `k` | string | R | `"d"` |
| `ch` | string | R | Id of a declared channel |
| `t0` | ns | R | Time of the first sample |
| `v` | array | R | Values. A number, or `null` = a sample that was acquired but has no value |
| `t` | array of ns | explicit channels only | Time of every sample |

**Regular channel** (has `hz`): the block MUST NOT contain `t`. The time of sample *i* (from 0) is
`t0 + round(i · 1e9 / hz)`, computed on the exact `i`, not by accumulating a rounded period (so no drift builds up
within a block). The quotient is computed in IEEE-754 double precision as `(i × 1e9) / hz` and rounded to an
integer half away from zero.

**Explicit channel** (no `hz`): the block MUST contain `t`, with the same length as `v`; `t` MUST be strictly
increasing; `t[0]` is the time of the first sample (`t0` is kept for uniformity and SHOULD equal `t[0]`).

**Across blocks of one channel:** the first sample time of a block MUST be strictly greater than the time of the
last sample of the previous block of that channel (no overlap, no duplicates). A gap is represented by the
absence of data; writers MUST NOT invent samples to fill gaps.

> *Rationale (non-normative).* The rule guarantees that every instant has at most one value per channel, so readers
> never have to choose between two samples. Example: a phone writes a 10 Hz `speed` channel in blocks of one second
> (the first block covers 0.0 to 0.9 s, the next starts at 1.0 s). If a GNSS clock correction would make the next
> block start at 0.5 s, that block is invalid. Writers must handle clock jumps themselves, for example by dropping or
> re-timing the affected samples, or by starting a new recording, instead of emitting overlapping blocks.

An empty block (`v` empty) is allowed. It adds no samples and is not compared with the previous block, but it
must still follow the rules for its channel kind: a block of an explicit channel needs `t` (empty), a block of a
regular channel must not carry `t`.

**Numbers:** `NaN` and the infinities do not exist in JSON. A value that is not available is written as `null`
(a writer given `NaN` writes `null`; a reader returns it as NaN). Non-finite numbers MUST NOT be written anywhere
else in the file (gate coordinates, tolerances, `hz`...) and a writer MUST refuse them.

### 3.4 Course — `k: "course"`

Geometry of the gates used to cut the run into segments.

| Field | Type | | Meaning |
|---|---|---|---|
| `k` | string | R | `"course"` |
| `start` | gate | R | Start gate |
| `finish` | gate | R | Finish gate. Equal to `start` on a circuit; different on a point-to-point course |
| `splits` | array of gate | O | Intermediate gates |
| `circuit` | string | O | Circuit id (section 10): a registry id such as `ES-catalunya` or a provisional id `local-<uuid>` |
| `variant` | string | O | Layout id with optional version, e.g. `gp@2023` |
| `circuitUid` | string | O | UUID of the circuit (section 10), canonical `8-4-4-4-12` hex form |
| `variantUid` | string | O | UUID of the layout (section 10), canonical `8-4-4-4-12` hex form |
| `gates` | string | O | Where the gates come from: `origin` = the data source, `registry` = the circuit registry |

A **gate** is an array of exactly four numbers `[lat1, lon1, lat2, lon2]`: the two end points of the gate line,
in degrees (WGS84). A gate with any other shape is invalid. A file SHOULD contain at most one course; if there are
several, readers use the last one.

### 3.5 Segment — `k: "seg"`

A lap, stage, run or other interval of the run.

| Field | Type | | Meaning |
|---|---|---|---|
| `k` | string | R | `"seg"` |
| `kind` | string | R | Recommended: `lap`, `stage`, `run`, `outlap`, `inlap`. Other values allowed |
| `s` | ns | R | Start |
| `e` | ns | R | End |
| `time` | ns | O | Official time as given by the origin, when it has one. A **duration**, not a file time |
| `tol` | number | O | Tolerance of the segment time in milliseconds |
| `splits` | array of ns | O | Intermediate times (file times) |
| `src` | string | R | Who determined the segment. Recommended: `origin` (the data source), `gates` (crossing of the course gates), `user` |

`time` is the official figure; `e - s` is what the samples give. They can differ, and consumers decide which to show.

### 3.6 Event — `k: "ev"`

A point or an interval worth marking.

| Field | Type | | Meaning |
|---|---|---|---|
| `k` | string | R | `"ev"` |
| `kind` | string | R | Recommended: `stop`, `flag`, `note`. Other values allowed |
| `s` | ns | R | Time (start of the interval) |
| `e` | ns | O | End of the interval; absent for a point event |
| `text` | string | O | Free text |

### 3.7 End — `k: "end"`

| Field | Type | | Meaning |
|---|---|---|---|
| `k` | string | R | `"end"` |
| `dur` | ns | R | Duration of the data (a **duration**, not a file time) |
| `n` | integer | R | Total number of samples in all channels |

Writers compute the values like this: `n` is the total number of samples written (null samples count);
`dur` is the maximum over all channels of the channel's end, where the end of a **regular** channel is
`last sample time + one period` (the period is `round(1e9 / hz)`), and the end of an **explicit** channel is its
last sample time. A file without samples has `dur = 0` and `n = 0`.

## 4. Validation summary

A reader MUST reject the file (as invalid) when:

- the first record is not a header, or there is more than one header;
- the major version in `orf` (the number before the first dot) is not 1, or is not a number;
- a required field is missing, has the wrong JSON type, or the header `kind` is not `real`, `sim` or `synthetic`;
- an optional field is present with the wrong JSON type (for example a number where a string is expected, or a
  non-string value in the header `meta` dictionary);
- a gate is not an array of four numbers;
- `circuitUid` or `variantUid` is present and is not a UUID in canonical `8-4-4-4-12` hex form;
- a channel is declared twice, or has an invalid `hz`;
- a block refers to an undeclared channel;
- a block of an explicit channel lacks `t`, `t` has a different length from `v`, or `t` is not strictly increasing;
- a block of a regular channel carries `t`;
- a block overlaps the previous block of its channel (3.3);
- a line that is terminated by LF cannot be parsed as a JSON object.

A reader MUST NOT reject the file because of unknown record kinds, unknown fields, an unknown `discipline`,
`seg.kind`, `seg.src` or `ev.kind`, a custom channel id, a minor version higher than it knows, or a `circuit`,
`variant` or `gates` value that does not follow section 10.

## 5. Channel vocabulary (initial)

Channel ids are lower-case ASCII with underscores. Units follow SI with practical exceptions:

- speed in **m/s**; pressure in **Pa**; acceleration in **m/s²**; temperature in **°C**;
- angles and angular rates in **degrees** (and degrees per second);
- percentages in **%**; engine speed in **rpm**;
- position as decimal degrees, **WGS84**; distance in **m**.

| Id | Unit | Meaning |
|---|---|---|
| `lat`, `lon` | deg | Position, WGS84 |
| `alt` | m | Altitude |
| `speed` | m/s | Speed over ground (or vehicle speed) |
| `heading` | deg | Course over ground, 0–360 clockwise from north |
| `distance` | m | Distance travelled in this recording (starts at 0) |
| `odometer` | m | Total distance of the vehicle (its odometer reading), independent of the recording |
| `rpm` | rpm | Engine speed |
| `throttle` | % | Throttle / accelerator position |
| `brake` | % | Brake pedal position |
| `brake_pressure` | Pa | Brake line pressure |
| `clutch` | % | Clutch pedal position |
| `gear` | 1 | Gear number (0 = neutral, -1 = reverse) |
| `steering` | % | Steering wheel position as a percentage of full lock; > 0 steering to the left |
| `steering_angle` | deg | Steering wheel angle; > 0 steering to the left |
| `acc_lat`, `acc_lon`, `acc_vert` | m/s² | Acceleration along lateral, longitudinal, vertical axes of the vehicle (signs below) |
| `yaw_rate`, `pitch_rate`, `roll_rate` | deg/s | Angular rate (signs below) |
| `boost` | Pa | Intake manifold pressure (gauge) |
| `coolant_temp`, `oil_temp`, `intake_temp`, `ambient_temp` | °C | Temperatures |
| `oil_pressure`, `ambient_pressure` | Pa | Pressures |
| `fuel_level` | % | Fuel level |

### 5.1 Axes and signs (normative)

Vehicle axes follow ISO 8855: **x points forward, y to the left, z up**, a right-handed system. The signs below are
part of the channel definitions:

- `acc_lon` > 0 when the vehicle speeds up; < 0 when it brakes.
- `acc_lat` > 0 when the acceleration points to the vehicle's left (for example in a left-hand corner, where the
  vehicle is pulled towards the inside of the bend); < 0 in a right-hand corner.
- `acc_vert` > 0 upwards.
- All `acc_*` channels are the acceleration of the vehicle with gravity removed: a vehicle at rest on level ground
  reads 0, 0, 0.
- `yaw_rate` > 0 when the vehicle turns left (counter-clockwise seen from above).
- `roll_rate` > 0 when the right side of the vehicle goes down (right-hand rule about x, pointing forward).
- `pitch_rate` > 0 when the nose goes down (right-hand rule about y, pointing left).
- `steering` and `steering_angle` > 0 when the driver steers to the left.

**Converter guidance.** Many loggers and apps use other conventions (for example longitudinal G positive under
braking, lateral G positive to the right, or Z pointing down, or accelerations that include gravity). A converter MUST
normalise signs and units to the convention above, and SHOULD state the source convention in the header `meta` when it
cannot determine it.

### 5.2 Other rules

- `steering` (in %) and `steering_angle` (in degrees) are **different channels**. A writer MUST NOT convert one into
  the other: the conversion needs the steering ratio, which the file does not carry.
- A channel specific to an origin, or not in this list, uses the id **`x-{origin}-{name}`** (for example
  `x-acme-turbo_target`) and declares its own unit. Readers MUST accept any id.
- The list grows with minor versions; adding an id is not a breaking change.

## 6. Versioning

- `orf` is `major.minor`.
- A reader for 1.x reads any 1.y, ignoring unknown records and fields. A change that makes older readers
  misread or reject a valid file bumps the major version. A reader MUST reject a file whose major version it does
  not support.
- Adding optional fields, new record kinds that can be ignored, and new recommended values or channel ids are minor changes.
- The writer's `orf` is the specification version it implements (the reference library always writes the version it
  implements, ignoring the value in a header it is given).

## 7. Interrupted recordings

A file with no `end` record is a recording that was cut (battery, crash, app killed). It is valid and readable;
a reader reports it as *incomplete*. The data before the cut are kept.

When reading, the following also end the file early without an error (the file is reported as incomplete):

- a zstd frame that is truncated, damaged, fails its checksum, or cannot be decoded: **the first such frame ends
  the file**, and any later frame, even a good one, is dropped;
- bytes after a complete frame that do not start a zstd frame (neither the zstd magic nor a skippable-frame magic),
  including fewer than four trailing bytes;
- a last line without LF that cannot be parsed (a line cut in half).

Everything read before that point is kept. By contrast, an LF-terminated line that is not a valid record is an error
(it cannot be the result of a cut). A file with no complete header cannot be read at all.

A writer closes the zstd frame when asked to flush and when its buffer is full, and appends the `end` record only
when the recording is completed.

Writer guidance: data that is still in the writer's buffer is lost if the recorder dies, so a recorder SHOULD flush
periodically (every few seconds, for example), not only when its buffer fills. A flush hands the data to the output
stream and flushes that stream; it does not force the operating system to write to disk (no fsync), which the
application does itself if it needs it. The reference library's writer is not thread-safe.

Writers that rewrite an existing file SHOULD do it atomically: write to a temporary file in the same directory, flush
it to disk, then move it over the target, and delete the temporary file if anything fails, so a crash never leaves a
half-written file in place of a good one. `Runlog.Write(path, ...)` and `RunMetaFile.Write(path, ...)` do this.

## 8. Companion file `.runmeta`

Setup information and notes are **optional** and live in a separate file, never inside the `.runlog`. A
`.runlog` without `.runmeta` is complete and valid.

The file is a single UTF-8 JSON object (not compressed, so it can be read and edited by hand), named like the
run with the extension `.runmeta`. The instants in a `.runmeta` (`s`, `e`, `t` and `splits`, wherever they appear)
are **absolute Unix epoch nanoseconds**, never relative file times and never sample indices. The only exception is
`time` in `userSegments`, which, as in a `seg` record, is a duration.

| Field | Type | | Meaning |
|---|---|---|---|
| `orf` | string | R | Specification version `major.minor`. Major ≠ 1 → rejected |
| `id` | string | R | `id` of the run it belongs to (equal to the header `id`) |
| `runlogSha256` | string | R | SHA-256 of the whole `.runlog` file, lower-case hex (readers compare case-insensitively) |
| `vehicle` | object | O | Free dictionary of strings |
| `setup` | array | O | Objects `{ "s": ns, "e": ns, "values": { string: string } }` — a configuration (pressures, modes, tyres...) valid for an interval |
| `drivers` | array | O | Objects `{ "s": ns, "e": ns, "id": string, "name": string, "initials": string (O) }` — who was driving during the interval. Intervals MUST NOT overlap; a reader that finds overlapping or malformed entries ignores the whole field without rejecting the file. A gap means "no driver recorded". |
| `parameters` | object | O | Descriptions of setup parameters, by base key (added in 1.3, see section 8.2). A malformed entry is ignored on its own |
| `notes` | array | O | Objects `{ "t": ns, "text": string }` |
| `userSegments` | array | O | Segments defined by the user, with the same fields as a `seg` record without `k` (`kind`, `s`, `e`, `src` required) |

A `.runmeta` belongs to a `.runlog` only if its `id` equals the header `id` exactly and its `runlogSha256` equals the
hash of the file; otherwise a consumer MUST NOT apply it. Unknown fields are ignored. The vocabulary of `vehicle` keys
is free. The keys of `setup` follow the standard vocabulary of section 8.1 in files with `orf` 1.3 or later; in files
with `orf` 1.0 to 1.2 they are free text.

`drivers` (added in 1.1): `s` and `e` are absolute epoch nanoseconds with `e > s`, and each interval is half-open
(`[s, e)`), so adjacent intervals (`e` of one equal to `s` of the next) do not overlap. `id` and `name` are non-empty
strings; `initials`, if present, is a string. A reader ignores the whole field (as if absent) when any entry is
not an object, lacks `s`, `e`, `id` or `name`, has a value of the wrong type, has an empty `id` or `name`, has
`e <= s`, or overlaps another entry; the rest of the file is still read. Entries may appear in any order.

`id` is an opaque, stable identifier chosen by the writer: it SHOULD be random (for example a UUID) and MUST NOT be
derived from personal data (name, e-mail, account). Consumers use it to tell whether two intervals are the same
person. `name` is personal data: writers SHOULD only include it with the user's consent, and anyone sharing a
`.runmeta` should remember it contains that name.

### 8.1 Setup vocabulary (added in 1.3)

The standard vocabulary governs the keys of `setup` values **only in files whose `orf` is 1.3 or later** (same major).
In a file with `orf` 1.0, 1.1 or 1.2 every `setup` key is free text, even one that looks like a standard key: a reader
MUST NOT reinterpret it (for example, `camber` in a 1.2 file is not an angle in radians, and its value is not converted).
The same rules apply to the `values` of a `.runsetup` file (section 11).

**Keys.** A key is a base key, optionally followed by a position suffix:

- per wheel: `.fl`, `.fr`, `.rl`, `.rr` (front left, front right, rear left, rear right);
- per axle: `.f`, `.r` (front, rear);
- no suffix for a parameter with a single value.

For example `camber.fl`, `ride_height.r`, `brake_bias`. The scope of each standard key in the table below says which
form it takes.

**Catalog link.** A value MAY have a companion key `<key>.ref` (after the position suffix, if any) whose value is
`<catalog>:<id>`, an entry of a **public** catalog the value refers to (for example `"tyres.ref":
"tyre:example-tyre-245-30r20"`). A writer MUST NOT use `.ref` for identifiers that only make sense to one person or one
application (a private unit, a local library).

**Non-standard keys.** A key that is not in the vocabulary MUST start with `x-` (for example `x-drive-mode`), like custom
channels. A writer MUST NOT use a standard key with a different meaning, unit or scope; such a parameter needs its own
`x-` key. Readers accept any key.

**Values** are always strings. Numeric values are in the SI unit of the table, written in round-trip notation (the
shortest text that reads back to the same IEEE-754 double) with a dot as decimal separator and no thousands separator,
for example `"-0.05235987755982989"` or `"220000"`.

| Key | Scope | Unit | Type | Meaning |
|---|---|---|---|---|
| `tyre_pressure_cold` | wheel | Pa | decimal | Tyre pressure, cold, gauge (above atmospheric) |
| `tyre_pressure_hot` | wheel | Pa | decimal | Tyre pressure, hot, gauge |
| `camber` | wheel | rad | decimal | Camber; negative = top of the wheel inwards |
| `toe` | wheel | rad | decimal | Toe; positive = toe-in |
| `caster` | wheel, front wheels only (`.fl`, `.fr`) | rad | decimal | Caster |
| `ride_height` | axle | m | decimal | Ride height (the measuring point is the vehicle's own) |
| `spring_rate` | wheel | N/m | decimal | Spring rate |
| `damper_stiffness` | wheel | — | integer | Clicks of a 1-way damper |
| `damper_bump`, `damper_rebound` | wheel | — | integer | Clicks of a 2-way damper |
| `damper_bump_low`, `damper_bump_high` | wheel | — | integer | Clicks of a 3- or 4-way damper, bump |
| `damper_rebound_low`, `damper_rebound_high` | wheel | — | integer | Clicks of a 4-way damper, rebound (a 3-way damper uses `damper_rebound`) |
| `damper_click_reference` | single | — | choice | Where damper clicks are counted from: `from_hard` or `from_soft` |
| `wing_angle` | axle | rad | decimal | Wing angle |
| `brake_bias` | single | 1 | decimal | Fraction of braking on the front axle, 0 to 1 |
| `fuel_mass` | single | kg | decimal | Fuel on board |
| `ballast_mass` | single | kg | decimal | Ballast |
| `driver_mass` | single | kg | decimal | Mass of the driver (**personal data**, see below) |
| `tyres` | single | — | text | Tyre model; MAY have a `tyres.ref` link |

That is 21 keys. Damper keys follow the number of adjustment ways of each axle: 1 way `damper_stiffness`; 2 ways
`damper_bump` and `damper_rebound`; 3 ways `damper_bump_low`, `damper_bump_high` and `damper_rebound`; 4 ways
`damper_bump_low`, `damper_bump_high`, `damper_rebound_low` and `damper_rebound_high`. What depends on the vehicle
(maps, modes, anti-roll bars, preload, electronics) is not standard: it uses `x-` keys with a description (section 8.2).

`driver_mass`, and any parameter whose description has `"personal": true`, is personal data: writers SHOULD only
include its value with the user's consent, and anyone sharing a `.runmeta` or `.runsetup` should remember it contains
that value.

The list grows with minor versions; adding a key is not a breaking change.

### 8.2 Parameter descriptions (added in 1.3)

`parameters` maps a **base key** (without position or `.ref` suffix) to a description that tells a consumer how to show
and edit a parameter that is not in the standard vocabulary. Keys of the standard vocabulary need no description.

| Field | Type | | Meaning |
|---|---|---|---|
| `name` | object | R | Name per language tag (for example `{"en": "Drive mode", "es": "Modo de conducción"}`); string values, at least one |
| `type` | string | R | `text`, `bool`, `choice`, `integer` or `decimal` |
| `scope` | string | R | `single`, `axle` or `wheel`: which position suffixes the values use (section 8.1) |
| `unit` | string | O | Unit of the value; SI for numeric values |
| `choices` | array | O | Options of a `choice`: objects `{ "id": string, "name": { tag: string } }`, `id` non-empty. A `choice` MUST have at least one |
| `allowOther` | bool | O | For a `choice`: free text is accepted besides the options. Default `false` |
| `catalogKind` | string | O | For a `text`: the kind of public catalog the value may link to through `<key>.ref` |
| `min`, `max` | number | O | Limits of a numeric value, in its unit; `min <= max` |
| `precision` | integer | O | Only for a `decimal`: number of decimals to show, `>= 0` |
| `group` | string | O | Group id: `tyres`, `geometry`, `suspension`, `aero`, `brakes`, `electronics`, `mass` or `other` |
| `personal` | bool | O | The value is personal data (see section 8.1). Default `false` |
| `positions` | array | O | Position suffixes that apply when not all of the scope does (for example `["fl", "fr"]`); non-empty strings |
| `clickReference` | string | O | For dampers: `from_hard` or `from_soft` |

A reader ignores **an entry on its own** (as if absent) when it is not an object, lacks `name`, `type` or `scope`, has a
field of the wrong JSON type, an empty `name`, an unknown `type` or `scope`, a `choice` without `choices`, a choice
without `id` or `name`, `min > max`, a `precision` that is negative, not an integer or on a type other than `decimal`,
or a `clickReference` other than `from_hard` and `from_soft`. The other entries and the rest of the file are still read.
Unknown fields of a description are ignored.

## 9. Conformance

The directory `conformance/` contains synthetic files that every implementation can use:

- `conformance/valid/<name>.runlog[.jsonl]` with `<name>.expected.json`:
  `{"complete": bool, "trailingContent": bool (optional, default false), "channels": {"<id>": <number of samples>}, "segments": n, "kind": "real|sim|synthetic", "derivedFrom": {"id", "s", "e"} | null (optional)}`. A reader
  MUST accept the file and produce those values (`"derivedFrom": null` means the reader reports no `derivedFrom`).
- `conformance/invalid/*`: a reader MUST reject each file.
- `conformance/runmeta/valid/<name>.runmeta` with `<name>.expected.json` `{"drivers": n, "parameters": n, "setupKeys": n}`
  (each key optional): a reader MUST accept the file and produce that number of drivers (an ignored `drivers` field
  counts as 0), of parameter descriptions (ignored entries do not count) and of keys in the `values` of the first
  `setup` interval. Files named `*-ignored` hold a malformed optional field or entry that a reader ignores; a writer
  MUST NOT produce them (they do not match the schema). The others match `schema/runmeta.schema.json`.
- `conformance/runsetup/valid/<name>.runsetup` with `<name>.expected.json`
  `{"kind": "template|modification|setup", "parameters": n, "values": n, "operations": n (optional)}`: a reader MUST
  accept the file and produce that kind and those numbers of `parameters` entries, `values` (0 when absent) and
  `operations`. Each file matches `schema/runsetup.schema.json`.
- `conformance/runsetup/invalid/*.runsetup`: a reader MUST reject each file (section 11.3).
- `conformance/circuit-ids.json`: lists of valid and invalid circuit identifiers (section 10) and of valid and invalid
  `orf:circuit/` URIs. For each valid entry it gives the circuit part, the variant (or `null`) and whether the id is
  provisional. An implementation of the grammar MUST accept every valid entry with those parts and reject every
  invalid one.

An `expected.json` may also have a `course` object with the expected `circuit`, `variant`, `circuitUid`, `variantUid`
and `gates` of the course (an absent key means the field is absent) and `circuitIdValid`: whether `circuit` and
`variant` together follow the grammar of section 10.

In `expected.json`, a channel entry is either a sample count or an object `{"n": count, "times": [ns...], "values":
[number|null...]}` (`times` and `values` optional; `null` stands for a sample without value), and an optional
`"end": {"dur": ns, "n": count}` gives the values of the `end` record. A reader MUST produce the declared arrays
exactly. Besides well-formed files the directory has files cut in the middle of a line and in the middle of a zstd
frame (reported as incomplete), and one invalid file for each rule of section 4.

Plain `.runlog.jsonl` files are the uncompressed form (for reading and for the JSON schema); `valid/compressed.runlog` is
a real zstd file made of several frames.

`schema/runlog.schema.json` validates **one record** (one line). `schema/runmeta.schema.json` validates a companion file and
`schema/runsetup.schema.json` a setup file. Rules that span records (section 4) are not expressible in the schema.

The prose of this specification is authoritative. The JSON schemas may be stricter than the text (for example the
`orf` pattern, `minLength` on some strings, `minimum` on `n` and `dur`). JSON Schema's `integer` also accepts a number
such as `1.0`, which readers reject for time fields.

## 10. Circuit identifiers

A course can name the circuit it was recorded on, so that files from different people and applications can be grouped
and compared. The names come from an open registry of circuits, <https://github.com/openrunformat/circuits>; its
index is `https://raw.githubusercontent.com/openrunformat/circuits/main/dist/index.json` (available once that
repository is public). A `.runlog` never depends on the registry: it stays self-sufficient (see below).

### 10.1 Grammar

```abnf
circuit-id   = registry-id / provisional-id
registry-id  = country "-" name
country      = 2%x41-5A                      ; ISO 3166-1 alpha-2, upper case
provisional-id = "local-" uuid               ; lower-case hex, canonical 8-4-4-4-12
name         = alnum-lc *30( alnum-lc / "-" ) alnum-lc
                                             ; alnum-lc = %x61-7A / %x30-39
                                             ; 2 to 32 characters of a-z, 0-9 and "-";
                                             ; starts and ends with a letter or digit; no "--"
variant      = layout [ "@" version ]
layout       = name
version      = 4DIGIT [ "-" ( "0" %x31-39 / "1" %x30-32 ) ]    ; year or year-month
uuid         = 8hex-lc "-" 4hex-lc "-" 4hex-lc "-" 4hex-lc "-" 12hex-lc
hex-lc       = %x30-39 / %x61-66                ; 0-9 a-f (lower case only)
variant-id   = circuit-id "/" variant
uri          = "orf:circuit/" ( circuit-id / variant-id )
```

Identifiers are case-sensitive and are never normalised: spaces, accents, upper-case letters in a `name` and
upper-case hex letters in a provisional UUID make an identifier invalid.

- `country` is the ISO 3166-1 alpha-2 code of the country of the circuit.
- `name` is unique within the country.
- `layout` is unique within the circuit.
- `version` is the year (`2023`) or year and month (`2023-04`) from which the geometry of that layout applies. It is
  omitted when the layout has only one geometry.

A **circuit id** (`ES-catalunya`) names the place; a **variant id** (`ES-catalunya/gp@2023`) names one layout of it.
In a `course` record the two parts are stored separately, in `circuit` and `variant`.

### 10.2 Forms

The URI form `orf:circuit/ES-catalunya/gp@2023` is for use outside a `.runlog` (links, other file formats). It is not
written in `course`.

### 10.3 Stable identifiers and renames

Every circuit and every layout in the registry has a `uid`, a UUID (version 7) that never changes. A writer that
knows it writes it in `circuitUid` and `variantUid`. A `variantUid` is only meaningful together with a `variant`. Writers write UUIDs in lower case; readers accept
upper-case hex digits in `circuitUid` and `variantUid` and do not check the UUID version.

Names can change. When a circuit or layout is renamed, the registry keeps the old id as an alias of the new one.
Consumers SHOULD resolve an unknown id through the aliases of the registry, and SHOULD prefer the `uid` when
both are present.

### 10.4 Provisional identifiers

An application that records on a circuit that is not in the registry generates a provisional id, `local-<uuid>`
(a UUIDv7 is recommended). It stays the same for that circuit on that machine. A provisional id may carry a variant.

### 10.5 Writers and readers

A writer MUST write `circuit` and `variant` values that follow the grammar. A reader MUST NOT reject a file
because `circuit`, `variant` or `gates` do not (before this section `circuit` was free text). A `variant` without a
`circuit`, a `circuitUid` without a `circuit` and a `variantUid` without a `variant` are not meaningful: writers MUST
NOT produce them and readers accept them. Writers MUST write `gates` only as `origin` or `registry`. A `circuitUid` or `variantUid` that is not a UUID is invalid (section 4).

### 10.6 Gates

`gates` tells where the gate geometry of the course comes from:

- `origin`: the gates come from the data source (for example the device or application that recorded the file);
- `registry`: the gates were taken from the circuit registry.

A reader that finds any other value treats the origin of the gates as unknown. The gates are always written in `course`
itself, whichever the origin; a `.runlog` must not need the registry to be understood.

When the finish line of the data source and the one of the registry differ, a writer SHOULD record the distance
between them, in metres, as a decimal string in the header `meta` key `circuit.finish_offset_m`.

## 11. Setup files `.runsetup`

A `.runsetup` file (added in 1.3) holds a setup definition that does not belong to any `.runlog`: a **template** (the
parameters a vehicle has), a **modification** (a package that adds, changes or removes parameters, for example
coilovers) or a complete **setup** (values someone used). It is the common form to share, import and export them. The
file is a single UTF-8 JSON object without BOM, not compressed.

### 11.1 Fields

| Field | Type | | Meaning |
|---|---|---|---|
| `orf` | string | R | Specification version `major.minor`. Writers MUST write 1.3 or later. Major ≠ 1 → rejected |
| `kind` | string | R | `template`, `modification` or `setup` |
| `id` | string | R/O | Non-empty identifier. Required in a template or modification |
| `version` | integer | R/O | Revision of a template or modification, `>= 1`. Required in a template or modification |
| `appliesTo` | array | O | Identifiers (non-empty strings) of the vehicle models the file is meant for; absent or empty for a generic file |
| `name` | object | R | Name per language tag; string values, at least one |
| `vehicle` | object | O | Free dictionary of strings. Without personal data unless the person agreed |
| `parameters` | object | O | Parameters by base key, **in order** (section 11.2) |
| `values` | object | R/O | Setup values, string to string, with the keys and rules of section 8.1. Required in a setup |
| `damperWays` | object | O | `{ "front": integer, "rear": integer }`: damping adjustment ways (0 to 4) of each axle |
| `operations` | array | O | In a modification: operations applied in order (section 11.4) |
| `composition` | object | O | In a setup: `{ "base": ref, "modifications": [ref...] }`, the layers it was built from, where `ref` is `{ "id": string, "version": integer }` |
| `extensions` | object | O | Data of applications, one key per application (for example `x-myapp`), any JSON value |

A **template** has `id`, `version`, its `parameters` and, when the vehicle has adjustable dampers, `damperWays`. A
**modification** has `id`, `version` and `operations`. A **setup** has `values` and MAY have `parameters` (for example
the descriptions of its `x-` keys), `damperWays` and `composition`. As in a `.runmeta`, the setup vocabulary of
section 8.1 governs the keys only when `orf` is 1.3 or later.

### 11.2 Parameters

`parameters` maps a base key to its description (section 8.2). A key of the standard vocabulary used with its standard
definition has the value `{}` (readers also accept `null`); any other key needs a description, and a standard key MAY
have one (for example a translated name or limits). The order of the entries is the order in which a consumer shows the
parameters and the order the composition starts from.

### 11.3 Reading

A reader MUST reject the file when the JSON is broken or not an object; `orf` is missing, not a string or has a major
version other than 1; `kind` is missing, not a string or unknown; `id` is present and not a non-empty string; `version`
is present and not a positive integer; a template or modification lacks `id` or `version`; a setup lacks `values`;
`name` is missing, not an object, has a value that is not a string or has no language; `vehicle` or `values` is not an
object of strings; `appliesTo` is not an array of non-empty strings.

Everything else is tolerated:

- An entry of `parameters` is ignored on its own when its key is empty, its description is malformed (section 8.2), or
  it is `{}` or `null` for a key that is not in the standard vocabulary. If a key appears twice, it keeps the position of
  the first and the description of the last.
- An operation is ignored on its own when it is malformed (section 11.4).
- A malformed `damperWays` (missing `front` or `rear`, not an integer, outside 0 to 4), `composition` (missing or
  malformed `base`, `modifications` not an array of refs, a ref without a non-empty `id` or a positive `version`) or
  `extensions` (not an object) is ignored as a whole; so are `parameters` and `operations` of the wrong JSON type.

Readers accept any `orf` 1.y, also lower than 1.3. Unknown fields are ignored and are not kept when a file is rewritten;
an application keeps its own data in `extensions`, which a writer that rewrites a file SHOULD keep as it is.

### 11.4 Operations

| Operation | Shape |
|---|---|
| add | `{ "op": "add", "key": base key, "description": description (O) }` |
| change | `{ "op": "change", "key": base key, "description": description (O) }` |
| remove | `{ "op": "remove", "key": base key }` |
| damperWays | `{ "op": "damperWays", "ways": { "front": integer, "rear": integer } }` |

`description` is required for a key that is not in the standard vocabulary. A reader ignores an operation on its own
when it is not an object, `op` is missing or unknown, an `add`, `change` or `remove` lacks a non-empty `key`, an `add`
or `change` has a malformed `description` or a non-standard key without one, or a `damperWays` lacks a valid `ways`. A
`description` on a `remove` is ignored.

### 11.5 Composition

Applications compose layers as follows to build the parameters of a vehicle from a base template and modifications.
The base MUST be a template and each layer a modification.

1. Start with the `parameters` of the base, in order, and its `damperWays` (`{front: 0, rear: 0}` when absent).
2. Apply the operations of each modification, modifications in the given order and operations in file order:
   - `add` of an absent key appends it at the end with its description;
   - `add` of a key that is already present works as a `change` and adds a warning;
   - `change` replaces the description of a present key and keeps its position;
   - `remove` removes a present key;
   - `change` or `remove` of an absent key has no effect and adds a warning;
   - `damperWays` replaces the damper ways of both axles.
3. Damper keys (`damper_stiffness`, `damper_bump`, `damper_rebound`, `damper_bump_low`, `damper_bump_high`,
   `damper_rebound_low`, `damper_rebound_high`) are **derived only from the final damper ways**: any of them that is in
   the list, from the base or from an explicit `add`, is taken out, and any explicit `remove` of one is overridden,
   without a warning. Then the keys of the front ways (section 8.1) apply to `fl` and `fr` and the keys of the rear ways
   to `rl` and `rr`; a key of both axles applies to all four wheels. The front keys come first, then the rear keys not
   already listed. A derived key keeps the description it had in the list, if any.
4. The derived keys are placed right after the last suspension parameter: one whose description has the group
   `suspension`, `ride_height`, `spring_rate` or `damper_click_reference`. If there is none, they go at the end.

`damper_click_reference` is not derived: it stays like any other parameter, also with ways `{0, 0}`. The result is a
list of parameters, the positions of the damper keys and the final damper ways, plus the warnings, which a consumer
shows to the user; an operation never makes the composition fail. Values are not composed: a setup holds its own
`values` and MAY record in `composition` the layers it was built from.

For example, with front ways 2 and rear ways 1 the derived keys are `damper_bump` and `damper_rebound` for `fl` and
`fr` and `damper_stiffness` for `rl` and `rr`, so a setup has values such as `damper_bump.fl` and
`damper_stiffness.rr`.

A modification for 2-way coilovers:

```json
{
  "orf": "1.3",
  "kind": "modification",
  "id": "mod-coilover-2way",
  "version": 1,
  "name": { "en": "Coilovers, 2-way dampers" },
  "operations": [
    { "op": "remove", "key": "x-suspension" },
    { "op": "add", "key": "ride_height" },
    { "op": "add", "key": "spring_rate" },
    { "op": "add", "key": "damper_click_reference" },
    { "op": "damperWays", "ways": { "front": 2, "rear": 2 } }
  ]
}
```

## 12. Example

```jsonl
{"k":"h","orf":"1.0","utc":1767225600000000000,"id":"demo-1","kind":"synthetic"}
{"k":"ch","id":"speed","unit":"m/s","hz":10}
{"k":"d","ch":"speed","t0":0,"v":[0.0,1.5,3.25,null]}
{"k":"seg","kind":"lap","s":0,"e":400000000,"src":"origin"}
{"k":"end","dur":400000000,"n":4}
```
