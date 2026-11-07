# Conformance files

Files a reader of Open Run Format can test itself against.

- `valid/`: each `*.runlog.jsonl` or `*.runlog` must be read without errors; its `*.expected.json` says what a reader
  must report (see SPEC §9), including `derivedFrom` when the key is present. Files named `*-malformed-ignored` test that a reader ignores a
  malformed optional field: a reader must accept them, but a writer must not produce them (they do not match the schema).
- `invalid/`: each file must be rejected.
- `runmeta/valid/`: each `*.runmeta` must be read without errors; its `*.expected.json` gives, for the keys it has, the
  number of driver intervals (`drivers`; an ignored field counts 0), of parameter descriptions (`parameters`; ignored
  entries do not count) and of keys in the first setup interval (`setupKeys`) a reader must report, see SPEC §8 and §9.
  Files named `*-ignored` hold a malformed optional field or entry that a reader ignores; the others match
  `schema/runmeta.schema.json`.
- `runsetup/valid/`: each `*.runsetup` must be read without errors and match `schema/runsetup.schema.json`; its
  `*.expected.json` gives the `kind` and the number of `parameters`, `values` (0 when absent) and, when present,
  `operations` a reader must report (SPEC §11).
- `runsetup/invalid/`: each `*.runsetup` must be rejected (SPEC §11.3).
- `compose/<case>/`: `base.runsetup` composed with `mod-1.runsetup`, `mod-2.runsetup`... in that order must give the
  parameters (in order, with their positions; `null` means all of the scope), the final damper ways and the number of
  warnings of `expected.json` (SPEC §9 and §11.5). Every file matches `schema/runsetup.schema.json`.
- `circuit-ids.json`: valid and invalid circuit identifiers (SPEC §10).

All the data in these files is invented: coordinates around 40.0, -3.0, round timestamps and made-up device names. Some
files declare `"kind":"real"` only to test that value of the header field; they are not real recordings.
