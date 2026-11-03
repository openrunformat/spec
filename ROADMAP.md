# Roadmap

Items planned before the specification is marked 1.0 final:

- Cap the length of lines for regular channels whose period is not a whole number of nanoseconds (for example 60 Hz),
  where the writer currently cannot split blocks.
- Reject JSON `null` in `meta` (header and companion file) in readers and schemas.
- Streaming and per-channel or time-window read API for very long logs. Example: an endurance run of 100 Hz x 50
  channels x 24 h is about 7 GB in memory with the current whole-document reader.
- Allow re-binding a `.runmeta` to a reconverted `.runlog` (today it is tied to the file hash).
- Throw `ArgumentException` for null elements in `SourceFiles` and `Splits`.
- Fix the placement of a paragraph in `SPEC.es.md` and the format line in SPEC section 9.
