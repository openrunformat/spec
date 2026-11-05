# Conformance files

Files a reader of Open Run Format can test itself against.

- `valid/`: each `*.runlog.jsonl` or `*.runlog` must be read without errors; its `*.expected.json` says what a reader
  must report (see SPEC §9), including `derivedFrom` when the key is present. Files named `*-malformed-ignored` test that a reader ignores a
  malformed optional field: a reader must accept them, but a writer must not produce them (they do not match the schema).
- `invalid/`: each file must be rejected.
- `runmeta/valid/`: each `*.runmeta` must be read without errors; its `*.expected.json` gives the number of driver
  intervals a reader must report (`{"drivers": n}`; an ignored field counts 0), see SPEC §8.
- `circuit-ids.json`: valid and invalid circuit identifiers (SPEC §10).

All the data in these files is invented: coordinates around 40.0, -3.0, round timestamps and made-up device names. Some
files declare `"kind":"real"` only to test that value of the header field; they are not real recordings.
