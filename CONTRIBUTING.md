# Contributing to Open Run Format

Thank you for helping. Open Run Format is a young specification (1.0 draft), so feedback from people who write or read
telemetry is the most useful contribution.

## Proposing a change to the specification

1. Open an issue first. Explain the problem (what you cannot record or read today), not only the solution.
2. Once the idea is agreed, send a pull request that changes, together:
   - `SPEC.md` (normative, English) and, if it changes something summarised there, `SPEC.es.md`;
   - the JSON schemas in `schema/`;
   - the conformance files in `conformance/` (at least one valid case and, if the change adds a rule, one invalid case);
   - the .NET reference library and its tests, so that the change is proven by working code;
   - `CHANGELOG.md`.
3. Changes must keep existing files readable. A change that would make valid files invalid needs a new major version
   and a strong reason.

## Code

- .NET 10; `dotnet test dotnet/OpenRunFormat.sln` must pass with no warnings.
- Keep the library small and dependency-free (apart from the zstd library already used).

## Data and privacy

Do not add real recordings to the repository, not even your own: use invented data (see `conformance/README.md`). Do
not add data or identifiers taken from vehicle manufacturers or their applications.

## Licence

By contributing you agree that your contribution is licensed under the Apache License 2.0, like the rest of the
repository (see `LICENSE` and `NOTICE`).
