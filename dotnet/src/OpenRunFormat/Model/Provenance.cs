namespace OpenRunFormat;

/// <summary>Where the data of a run comes from. Serialized as "real", "sim" or "synthetic".</summary>
public enum SourceKind { Real, Sim, Synthetic }

/// <summary>A tool (converter or writer) and its version.</summary>
public sealed record ToolRef(string Id, string Version);

/// <summary>A source file the run was derived from, with its SHA-256 as lowercase hex.</summary>
public sealed record SourceFileRef(string Name, string Sha256);

/// <summary>Declared precision of the data.</summary>
public sealed record Precision(double? SegmentToleranceMs, double? GpsHz);

/// <summary>Origin and tooling information of a run.</summary>
public sealed record Provenance(
    SourceKind Kind,
    string? Device,
    ToolRef? Converter,
    ToolRef? Writer,
    IReadOnlyList<SourceFileRef> SourceFiles,
    Precision? Precision);
