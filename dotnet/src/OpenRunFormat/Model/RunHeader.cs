namespace OpenRunFormat;

/// <summary>
/// First record of a .runlog file. <paramref name="Utc"/> is the run start in Unix nanoseconds. <paramref name="DerivedFrom"/>
/// (spec 1.2) says the file is an excerpt of another run; null when absent or malformed.
/// </summary>
public sealed record RunHeader(
    string Orf,
    long Utc,
    string? Tz,
    string Id,
    string? Discipline,
    Provenance Provenance,
    IReadOnlyDictionary<string, string> Meta,
    DerivedFrom? DerivedFrom = null);
