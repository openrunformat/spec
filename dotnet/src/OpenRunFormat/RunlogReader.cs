using System.Globalization;
using OpenRunFormat.Json;

namespace OpenRunFormat;

/// <summary>
/// Reads .runlog files. Unknown records and fields are ignored; a major version other than 1 is rejected.
/// The first zstd frame that cannot be decoded (truncated, corrupt, failing its checksum, or valid but unsupported,
/// e.g. needing a dictionary) ends reading as a truncation, and so do trailing bytes that do not start a zstd frame:
/// what was read before is kept, later frames are dropped, and <c>End</c> is null.
/// </summary>
public static class RunlogReader
{
    public static RunDocument Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var info = new LineSource.Info();
        using var lines = LineSource.Open(input, info).GetEnumerator();
        var header = ReadHeaderLine(lines, info);

        var defs = new Dictionary<string, ChannelDef>();
        var times = new Dictionary<string, List<long>>();
        var values = new Dictionary<string, List<double>>();
        Course? course = null;
        var segments = new List<Segment>();
        var events = new List<RunEvent>();
        EndRecord? end = null;

        while (lines.MoveNext())
        {
            var (line, terminated) = lines.Current;
            if (line.Length == 0) continue;
            object? record;
            // An unterminated last line that does not parse was cut by a truncation; a terminated one is invalid.
            try { record = RecordCodec.Decode(line); }
            catch (FormatException) when (!terminated) { break; }
            switch (record)
            {
                case RunHeader:
                    throw new FormatException("A second header record was found.");
                case ChannelDef def:
                    if (BlockRules.CheckChannel(def) is { } cerr) throw new FormatException(cerr);
                    if (!defs.TryAdd(def.Id, def)) throw new FormatException($"Channel '{def.Id}' is defined twice.");
                    times[def.Id] = new List<long>();
                    values[def.Id] = new List<double>();
                    break;
                case SampleBlock b:
                    if (!defs.TryGetValue(b.Channel, out var d))
                        throw new FormatException($"Samples for undefined channel '{b.Channel}'.");
                    var t = times[b.Channel];
                    if (BlockRules.Check(d, b, t.Count > 0 ? t[^1] : null) is { } berr) throw new FormatException(berr);
                    if (d.Hz is { } hz)
                        for (int i = 0; i < b.Values.Length; i++) t.Add(BlockRules.RegularTime(b.T0, i, hz));
                    else
                        t.AddRange(b.Times!);
                    values[b.Channel].AddRange(b.Values);
                    break;
                case Course c: course = c; break;
                case Segment s: segments.Add(s); break;
                case RunEvent e: events.Add(e); break;
                case EndRecord er: end = er; break;
            }
            if (end is not null) break; // nothing meaningful follows the end record
        }

        // Surface (without reading as data) whatever follows the first end record.
        bool trailing = false;
        if (end is not null)
        {
            while (lines.MoveNext())
                if (lines.Current.Text.Length > 0) { trailing = true; break; }
            if (!trailing && info.DroppedBytes) trailing = true;
        }

        var channels = new Dictionary<string, ChannelData>();
        foreach (var (id, def) in defs)
            channels[id] = new ChannelData(def, times[id].ToArray(), values[id].ToArray());
        return new RunDocument(header, channels, course, segments, events, end, trailing);
    }

    public static RunHeader ReadHeader(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var info = new LineSource.Info();
        using var lines = LineSource.Open(input, info).GetEnumerator(); // lazy: reads only up to the first line
        return ReadHeaderLine(lines, info);
    }

    private static RunHeader ReadHeaderLine(IEnumerator<(string Text, bool Terminated)> lines, LineSource.Info info)
    {
        string? line = null;
        while (lines.MoveNext())
        {
            if (lines.Current.Text.Length == 0) continue;
            line = lines.Current.Text;
            break;
        }
        if (line is null)
            throw new FormatException(info.HasInput ? "No complete header record could be read." : "The file is empty.");
        // Check the major version first: a 2.x header may use fields or values this reader cannot decode.
        if (RecordCodec.PeekHeaderVersion(line) is { } orf) CheckVersion(orf);
        if (RecordCodec.Decode(line) is not RunHeader h) throw new FormatException("The first record must be the header.");
        CheckVersion(h.Orf);
        return h;
    }

    private static void CheckVersion(string orf)
    {
        var major = orf.Split('.')[0];
        if (!int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m != 1)
            throw new FormatException($"Unsupported format version '{orf}'.");
    }
}
