namespace OpenRunFormat;

/// <summary>Shared validation of sample blocks, used by the writer (ArgumentException) and the reader (FormatException).</summary>
internal static class BlockRules
{
    /// <summary>Time of sample <paramref name="i"/> of a regular block starting at <paramref name="t0"/>.</summary>
    public static long RegularTime(long t0, int i, double hz)
        => t0 + (long)Math.Round(i * 1e9 / hz, MidpointRounding.AwayFromZero);

    /// <summary>One sampling period in ns (rounded).</summary>
    public static long Period(double hz) => (long)Math.Round(1e9 / hz, MidpointRounding.AwayFromZero);

    public static string? CheckChannel(ChannelDef def)
    {
        if (string.IsNullOrEmpty(def.Id)) return "Channel id must not be empty.";
        if (def.Unit is null) return $"Channel '{def.Id}' has no unit (use an empty string for a unitless channel).";
        if (def.Hz is { } hz && !(double.IsFinite(hz) && hz > 0 && hz <= 1e9)) return $"Channel '{def.Id}' has an invalid rate.";
        return null;
    }

    /// <summary>Returns an error message or null. <paramref name="lastTime"/> is the last sample time already stored for the channel.</summary>
    public static string? Check(ChannelDef def, SampleBlock b, long? lastTime)
    {
        if (b.Values is null) return "Block has no values.";
        foreach (var v in b.Values)
            if (double.IsInfinity(v)) return $"Block of '{def.Id}' contains an infinite value.";
        if (def.Hz is null)
        {
            if (b.Times is null) return $"Block of explicit channel '{def.Id}' has no times.";
            if (b.Times.Length != b.Values.Length) return $"Block of '{def.Id}': times and values differ in length.";
            for (int i = 1; i < b.Times.Length; i++)
                if (b.Times[i] <= b.Times[i - 1]) return $"Block of '{def.Id}': times are not strictly increasing.";
            if (b.Times.Length > 0 && lastTime is { } l && b.Times[0] <= l)
                return $"Block of '{def.Id}' starts at or before the end of the previous block.";
        }
        else
        {
            if (b.Times is not null) return $"Block of regular channel '{def.Id}' must not carry times.";
            if (b.Values.Length > 0 && lastTime is { } l && b.T0 <= l)
                return $"Block of '{def.Id}' starts at or before the end of the previous block.";
        }
        return null;
    }

    /// <summary>Time of the last sample of a non-empty valid block.</summary>
    public static long LastTime(ChannelDef def, SampleBlock b)
        => def.Hz is { } hz ? RegularTime(b.T0, b.Values.Length - 1, hz) : b.Times![^1];
}
