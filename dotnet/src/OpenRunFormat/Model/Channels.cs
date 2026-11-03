namespace OpenRunFormat;

/// <summary>Channel definition. <paramref name="Hz"/> null means explicit timestamps per sample.</summary>
public sealed record ChannelDef(string Id, string Unit, string? SourceName, double? Hz);

/// <summary>A block of consecutive samples. NaN marks a sample without value.
/// <paramref name="Times"/> is only used by explicit-timestamp channels.</summary>
public sealed record SampleBlock(string Channel, long T0, double[] Values, long[]? Times);

/// <summary>All samples of a channel, blocks merged; timestamps are always explicit (nanoseconds).</summary>
public sealed record ChannelData(ChannelDef Def, long[] Times, double[] Values);
