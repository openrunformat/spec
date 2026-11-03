# Conformance files

Files a reader of Open Run Format can test itself against.

- `valid/`: each `*.runlog.jsonl` or `*.runlog` must be read without errors; its `*.expected.json` says what a reader
  must report (see SPEC §9).
- `invalid/`: each file must be rejected.
- `circuit-ids.json`: valid and invalid circuit identifiers (SPEC §10).

All the data in these files is invented: coordinates around 40.0, -3.0, round timestamps and made-up device names. Some
files declare `"kind":"real"` only to test that value of the header field; they are not real recordings.
