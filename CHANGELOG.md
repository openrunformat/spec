# Changelog

## 0.5.0 — specification 1.3 (draft), setup vocabulary and .runsetup

- Spec: standard vocabulary of 21 setup keys (section 8.1), with position suffixes `.fl .fr .rl .rr` and `.f .r`, a
  `<key>.ref` link to a public catalog, `x-` for non-standard keys and values in SI; it governs only files with `orf`
  1.3 or later, so keys of 1.0–1.2 files stay free text. `driver_mass` and parameters marked `personal` are personal
  data. The `.runmeta` gains `parameters` (section 8.2), descriptions of setup parameters; a reader ignores a
  malformed entry on its own. New section 11: setup files `.runsetup` (template, modification, setup), their
  operations and the composition rule; damper keys derive only from the final damper ways. The example moves to
  section 12. `expected.json` of `runmeta/valid` accepts `parameters` and `setupKeys`, and `conformance/runsetup/` is
  new (section 9).
- Schema: `parameters` in `runmeta.schema.json`; new `runsetup.schema.json`.
- Library: `OpenRunFormat.Setup` with `SetupKeys` and `SetupVocabulary` (`Applies`, `BaseKeyOf`, `PositionOf`,
  `KeysFor`, `DamperKeys`), `ParameterDescription`, `RunMeta.Parameters`, `RunSetup` and `RunSetupFile` (atomic write)
  and `RunSetupComposer`; the writers emit `orf` 1.3.
- Conformance: `setup-1-3` and `parameters-malformed-entry-ignored` in `conformance/runmeta/valid/`, checked against the
  schema; `conformance/runsetup/valid/` and `conformance/runsetup/invalid/`.

## 0.4.0 — specification 1.2 (draft), derivedFrom

- Spec: the header gains the optional `derivedFrom` (`{id, s, e}`, section 3.1): the file is an excerpt of run `id`,
  interval `[s, e)` in nanoseconds relative to time 0 of that run. A reader ignores a malformed value without rejecting
  the file. `expected.json` accepts `derivedFrom` (section 9).
- Schema: `derivedFrom` in `runlog.schema.json`.
- Library: `DerivedFrom` and `RunHeader.DerivedFrom` (optional last parameter, null when absent or malformed); the writer
  emits it after `precision` and rejects an empty `id` or `e <= s`; the writer emits `orf` 1.2.
- Conformance: `derived-from` and `derived-from-malformed-ignored` in `conformance/valid/`.

## 0.3.0 — specification 1.1 (draft), drivers

- Spec: the companion file `.runmeta` gains `drivers` (section 8): non-overlapping, half-open intervals with `id`,
  `name` and optional `initials`; a reader ignores the whole field when an entry is malformed or intervals overlap.
  `id` is opaque and not derived from personal data; `name` is personal data (include it only with consent).
- Schema: `drivers` in `runmeta.schema.json`.
- Library: `DriverInterval` and `RunMeta.Drivers` (never null); the writer emits `drivers` and `orf` 1.1; the reader
  returns an empty list for a malformed field, including empty `id` or `name`.
- Conformance: `conformance/runmeta/valid/` with `drivers` and `drivers-overlap-ignored`.

## 0.2.0 — specification 1.0 (draft), circuit identifiers

- Spec: new section 10, circuit identifiers (grammar, variants and versions, stable UUIDs, aliases, provisional
  `local-<uuid>` ids, `orf:circuit/` URIs); `course` gains `circuitUid`, `variantUid` and `gates`; readers accept
  `circuit`, `variant` and `gates` values that do not follow the grammar, and reject a `circuitUid` or `variantUid`
  that is not a UUID. The example moves to section 11.
- Library: `CircuitId` (parse, URI form, provisional ids, `TryFromCourse`); `CourseGates`; `Course.CircuitUid`,
  `Course.VariantUid` and `Course.Gates`; the writer validates circuit and variant ids.
- Conformance: `circuit-ids.json`; course files with registry and provisional ids, free-text circuit and a bad UID;
  `expected.json` accepts a `course` object.

## 0.1.1

- Library: reproducible builds; no local machine paths are embedded in the package.
- Includes the changes listed under Unreleased below.

## 0.1.0 — specification 1.0 (draft)

First public draft.

- Specification 1.0 (draft): JSONL records (`h`, `ch`, `d`, `course`, `seg`, `ev`, `end`), integer nanosecond time,
  optional concatenated zstd frames, interrupted-recording rules, initial channel vocabulary, companion file `.runmeta`.
- JSON schemas for records and companion file, and conformance files.
- .NET reference library `OpenRunFormat` 0.1.0 (reader, incremental writer, `.runmeta` support).

### Changes after 0.1.0 (released in 0.1.1)

- Spec: non-normative rationale for the block overlap rule, with a worked example; readers SHOULD warn when content
  follows `end`; axis and sign conventions for the vocabulary (ISO 8855) with converter guidance; new channel `odometer`
  (distinct from `distance`); atomic-write guidance for writers.
- Library: `RunDocument.HasTrailingContent`; `Runlog.Write(path, ...)` and `RunMetaFile.Write(path, ...)` write atomically
  (temporary file, flush to disk, move over the target).
- Conformance: `valid/trailing-after-end.runlog.jsonl`; `expected.json` accepts `"trailingContent"` (default false).

- Specification clarifications: `time` and `dur` are durations; `.runmeta` instants vs. `userSegments.time`; regular-time
  arithmetic; empty blocks; a single course; wrong-type optional fields are rejected; the first damaged zstd frame ends
  the file; writer guidance on flushing; the prose is authoritative over the schemas.
- Library: `Runlog.Write` keeps gaps and off-grid times of regular channels; the writer rejects null required strings;
  the reader checks the major version before decoding the rest of the header.
- Conformance: expected files carry times, values and `end`; truncated-line and truncated-frame files; one invalid file per rule.
