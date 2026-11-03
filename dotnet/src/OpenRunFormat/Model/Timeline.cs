namespace OpenRunFormat;

/// <summary>A time interval of the run (lap, out-lap, ...). All times are nanoseconds.</summary>
public sealed record Segment(
    string Kind,
    long S,
    long E,
    long? OfficialTimeNs,
    double? ToleranceMs,
    IReadOnlyList<long> Splits,
    string Src);

/// <summary>A point or interval event. <paramref name="E"/> null means a point event.</summary>
public sealed record RunEvent(string Kind, long S, long? E, string? Text);

/// <summary>Last record of a .runlog file.</summary>
public sealed record EndRecord(long DurationNs, long Samples);
