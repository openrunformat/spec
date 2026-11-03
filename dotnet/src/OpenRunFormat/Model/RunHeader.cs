namespace OpenRunFormat;

/// <summary>First record of a .runlog file. <paramref name="Utc"/> is the run start in Unix nanoseconds.</summary>
public sealed record RunHeader(
    string Orf,
    long Utc,
    string? Tz,
    string Id,
    string? Discipline,
    Provenance Provenance,
    IReadOnlyDictionary<string, string> Meta);
