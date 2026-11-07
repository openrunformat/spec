# Open Run Format (.runlog)

An open file format and .NET library for the telemetry of a run: a session on a track, a road stage, a ride.
Time is always integer nanoseconds; records are compact JSON lines (UTF-8, no BOM), optionally zstd-compressed in
independent frames so that a recording survives a sudden stop.

- **Repository:** <https://github.com/openrunformat/spec>
- **Specification:** [SPEC.md](SPEC.md) (normative, English) · [SPEC.es.md](SPEC.es.md) (summary, Spanish) · status: 1.3 draft.
- **JSON schemas:** [schema/](schema/). **Conformance files:** [conformance/](conformance/).
- **Circuit identifiers:** SPEC §10; registry: <https://github.com/openrunformat/circuits>
- Inspired by [Motorsport Telemetry JSON (MTJ)](https://github.com/tobi/motorsport-telemetry-rs) (MIT); no code was copied.

## A minimal file

```jsonl
{"k":"h","orf":"1.0","utc":1767225600000000000,"id":"demo-1","kind":"synthetic"}
{"k":"ch","id":"speed","unit":"m/s","hz":10}
{"k":"d","ch":"speed","t0":0,"v":[0.0,1.5,3.25,null]}
{"k":"seg","kind":"lap","s":0,"e":400000000,"src":"origin"}
{"k":"end","dur":400000000,"n":4}
```

## .NET library

```csharp
using OpenRunFormat;

var header = new RunHeader(RunFormat.Version, utcNs, "Europe/Madrid", "run-1", "car",
    new Provenance(SourceKind.Real, "phone + external GNSS receiver", null, null, [], null),
    new Dictionary<string, string>());

using (var fs = File.Create("run.runlog"))
using (var w = new RunlogWriter(fs, header))
{
    w.DefineChannel(new ChannelDef("speed", "m/s", null, 10));
    w.WriteSamples(new SampleBlock("speed", 0, new[] { 0.0, 1.5, 3.25 }, null));
    w.Flush();     // closes a zstd frame: data so far is safe
    w.Complete();  // writes the end record
}

RunDocument doc = Runlog.Read("run.runlog");   // doc.Complete, doc.Channels["speed"].Values ...
RunHeader onlyHeader = RunlogReader.ReadHeader(File.OpenRead("run.runlog"));
```

Companion files (`.runmeta`, setup and notes) are handled by `OpenRunFormat.Meta.RunMetaFile`. Setup files (`.runsetup`:
templates, modifications and setups, SPEC §12) by `OpenRunFormat.Setup.RunSetupFile`; `RunSetupComposer` composes
them and `SetupVocabulary` holds the standard setup keys (SPEC §8.1).

```
dotnet test dotnet/OpenRunFormat.sln
dotnet pack dotnet/src/OpenRunFormat -c Release -o artifacts
```

See [ROADMAP.md](ROADMAP.md) for what is planned before the specification is final.

## Licence

Apache-2.0. See `LICENSE` and `NOTICE`.

## Logo

The logo and the rules for using it to show compatibility are in [`brand/`](brand/README.md).
