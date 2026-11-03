# Changelog

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
