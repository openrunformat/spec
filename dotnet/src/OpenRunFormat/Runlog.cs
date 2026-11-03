namespace OpenRunFormat;

/// <summary>Convenience functions to write and read whole documents.</summary>
public static class Runlog
{
    private const int BlockSize = 1000;

    public static void Write(Stream output, RunDocument doc, RunlogWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        foreach (var (id, data) in doc.Channels)
            if (data.Times.Length != data.Values.Length)
                throw new ArgumentException($"Channel '{id}': times and values differ in length.", nameof(doc));
        using var w = new RunlogWriter(output, doc.Header, options);
        foreach (var cd in doc.Channels.Values) w.DefineChannel(cd.Def);
        if (doc.Course is { } course) w.WriteCourse(course);
        foreach (var cd in doc.Channels.Values)
        {
            if (cd.Def.Hz is { } hz) WriteRegular(w, cd, hz);
            else WriteExplicit(w, cd);
        }
        foreach (var s in doc.Segments) w.WriteSegment(s);
        foreach (var e in doc.Events) w.WriteEvent(e);
        w.Complete();
    }

    private static void WriteExplicit(RunlogWriter w, ChannelData cd)
    {
        for (int start = 0; start < cd.Values.Length; start += BlockSize)
        {
            int n = Math.Min(BlockSize, cd.Values.Length - start);
            w.WriteSamples(new SampleBlock(cd.Def.Id, cd.Times[start], cd.Values.AsSpan(start, n).ToArray(),
                cd.Times.AsSpan(start, n).ToArray()));
        }
    }

    /// <summary>
    /// A regular block rebuilds its times from t0 and the rate, so a new block starts wherever a sample is not on the
    /// grid of the current one (a gap, an off-grid time). The 1000-sample cap only applies when the period is a whole
    /// number of nanoseconds; otherwise splitting would shift the rounding, so a block runs to the next gap.
    /// </summary>
    private static void WriteRegular(RunlogWriter w, ChannelData cd, double hz)
    {
        var period = 1e9 / hz;
        bool capped = period == Math.Floor(period);
        int start = 0;
        for (int i = 1; i <= cd.Values.Length; i++)
        {
            bool end = i == cd.Values.Length
                || cd.Times[i] != BlockRules.RegularTime(cd.Times[start], i - start, hz)
                || (capped && i - start >= BlockSize);
            if (!end) continue;
            w.WriteSamples(new SampleBlock(cd.Def.Id, cd.Times[start], cd.Values.AsSpan(start, i - start).ToArray(), null));
            start = i;
        }
    }

    public static RunDocument Read(string path)
    {
        using var fs = File.OpenRead(path);
        return RunlogReader.Read(fs);
    }

    /// <summary>
    /// Writes the document to <paramref name="path"/> atomically: the data goes to a temporary file in the same
    /// directory (<c>&lt;path&gt;.tmp-&lt;random&gt;</c>), is flushed to disk, and is then moved over the target. If
    /// anything fails, an existing target is left untouched and the temporary file is deleted.
    /// </summary>
    public static void Write(string path, RunDocument doc, RunlogWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(doc);
        AtomicFile.Write(path, fs => Write(fs, doc, options));
    }
}
