using System.Text;
using OpenRunFormat.Json;

namespace OpenRunFormat.Tests;

public class WriterReaderTests
{
    private static readonly RunlogWriterOptions Plain = new(Compress: false);

    private static RunHeader Header(string orf = "1.0") => new(
        orf, 1_700_000_000_000_000_000, null, "r1", null,
        new Provenance(SourceKind.Synthetic, null, null, null, [], null),
        new Dictionary<string, string>());

    private static double[] Seq(int n) => Enumerable.Range(0, n).Select(i => (double)i).ToArray();

    private static string[] Lines(MemoryStream ms) =>
        Encoding.UTF8.GetString(ms.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static RunDocument ReadText(string text) =>
        RunlogReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

    private static string HeaderLine(string orf = "1.0") => RecordCodec.Encode(Header(orf));

    [Fact]
    public void Round_trip_regular_and_explicit_channels()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), Plain))
        {
            w.DefineChannel(new ChannelDef("speed", "km/h", null, 10));
            w.DefineChannel(new ChannelDef("gps", "deg", null, null));
            w.WriteSamples(new SampleBlock("speed", 0, Seq(10), null));
            w.WriteSamples(new SampleBlock("speed", 1_000_000_000, Seq(10), null));
            w.WriteSamples(new SampleBlock("gps", 0, [1, 2, 3], [0, 1_000_000_000, 2_050_000_000]));
            w.Complete();
        }
        ms.Position = 0;
        var doc = RunlogReader.Read(ms);
        Assert.Equal(20, doc.Channels["speed"].Times.Length);
        Assert.Equal(1_000_000_000, doc.Channels["speed"].Times[10]);
        Assert.Equal(2_050_000_000, doc.Channels["gps"].Times[2]);
        Assert.True(doc.Complete);
        Assert.Equal(23, doc.End!.Samples);
        // speed: last time 1.9 s + 0.1 s period = 2.0 s; gps: 2.05 s
        Assert.Equal(2_050_000_000, doc.End.DurationNs);
    }

    [Fact]
    public void Uncompressed_file_starts_with_header_line()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), Plain)) w.Complete();
        Assert.StartsWith("{\"k\":\"h\"", Encoding.UTF8.GetString(ms.ToArray()));
        Assert.NotEqual((byte)0xEF, ms.ToArray()[0]); // no BOM
    }

    [Fact]
    public void Header_orf_is_forced_to_current_version()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header("9.9"), Plain)) w.Complete();
        ms.Position = 0;
        Assert.Equal(RunFormat.Version, RunlogReader.Read(ms).Header.Orf);
        ms.Position = 0;
        Assert.Equal(RunFormat.Version, RunlogReader.ReadHeader(ms).Orf);
    }

    [Fact]
    public void Duplicate_channel_throws()
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        w.DefineChannel(new ChannelDef("a", "u", null, 1));
        Assert.Throws<ArgumentException>(() => w.DefineChannel(new ChannelDef("a", "u", null, 1)));
    }

    [Fact]
    public void Samples_for_undefined_channel_throw_InvalidOperation()
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        Assert.Throws<InvalidOperationException>(() => w.WriteSamples(new SampleBlock("x", 0, [1], null)));
    }

    [Fact]
    public void Regular_block_overlapping_previous_throws()
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        w.DefineChannel(new ChannelDef("s", "u", null, 10));
        w.WriteSamples(new SampleBlock("s", 0, Seq(10), null));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock("s", 500_000_000, Seq(3), null)));
    }

    [Fact]
    public void Regular_block_with_times_throws()
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        w.DefineChannel(new ChannelDef("s", "u", null, 10));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock("s", 0, [1], [0])));
    }

    [Fact]
    public void Explicit_block_without_times_or_with_wrong_length_throws()
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        w.DefineChannel(new ChannelDef("g", "u", null, null));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock("g", 0, [1, 2], null)));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock("g", 0, [1, 2], [0])));
    }

    [Fact]
    public void Explicit_block_with_non_increasing_times_throws()
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        w.DefineChannel(new ChannelDef("g", "u", null, null));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock("g", 0, [1, 2], [5, 5])));
        w.WriteSamples(new SampleBlock("g", 0, [1], [10]));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock("g", 10, [1], [10])));
    }

    [Fact]
    public void Infinite_value_throws()
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        w.DefineChannel(new ChannelDef("s", "u", null, 10));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock("s", 0, [double.PositiveInfinity], null)));
    }


    [Fact]
    public void Reader_rejects_overlapping_blocks_in_foreign_file()
    {
        var text = HeaderLine() + "\n"
            + "{\"k\":\"ch\",\"id\":\"s\",\"unit\":\"u\",\"hz\":10}\n"
            + "{\"k\":\"d\",\"ch\":\"s\",\"t0\":0,\"v\":[1,2,3,4,5,6,7,8,9,10]}\n"
            + "{\"k\":\"d\",\"ch\":\"s\",\"t0\":500000000,\"v\":[1,2]}\n";
        Assert.Throws<FormatException>(() => ReadText(text));
    }

    [Fact]
    public void Reader_rejects_bad_blocks_in_foreign_file()
    {
        var ch = "{\"k\":\"ch\",\"id\":\"g\",\"unit\":\"u\"}\n";
        Assert.Throws<FormatException>(() => ReadText(HeaderLine() + "\n" + ch + "{\"k\":\"d\",\"ch\":\"g\",\"t0\":0,\"v\":[1,2]}\n"));
        Assert.Throws<FormatException>(() => ReadText(HeaderLine() + "\n" + ch + "{\"k\":\"d\",\"ch\":\"g\",\"t0\":0,\"v\":[1,2],\"t\":[0]}\n"));
        Assert.Throws<FormatException>(() => ReadText(HeaderLine() + "\n" + ch + "{\"k\":\"d\",\"ch\":\"g\",\"t0\":0,\"v\":[1,2],\"t\":[3,3]}\n"));
        Assert.Throws<FormatException>(() => ReadText(HeaderLine() + "\n{\"k\":\"d\",\"ch\":\"nope\",\"t0\":0,\"v\":[1]}\n"));
        Assert.Throws<FormatException>(() => ReadText(HeaderLine() + "\n{\"k\":\"ch\",\"id\":\"s\",\"unit\":\"u\",\"hz\":10}\n{\"k\":\"d\",\"ch\":\"s\",\"t0\":0,\"v\":[1],\"t\":[0]}\n"));
    }

    [Fact]
    public void Reader_ignores_unknown_records()
    {
        var doc = ReadText(HeaderLine() + "\n{\"k\":\"zzz\",\"x\":1}\n");
        Assert.Empty(doc.Channels);
    }

    [Fact]
    public void Reader_rejects_major_version_2()
        => Assert.Throws<FormatException>(() => ReadText(HeaderLine("2.0") + "\n"));

    [Fact]
    public void Reader_accepts_newer_minor_version()
        => Assert.Equal("1.7", ReadText(HeaderLine("1.7") + "\n").Header.Orf);

    [Fact]
    public void Reader_rejects_file_without_header_first()
    {
        Assert.Throws<FormatException>(() => ReadText("{\"k\":\"ch\",\"id\":\"s\",\"unit\":\"u\",\"hz\":10}\n" + HeaderLine() + "\n"));
        Assert.Throws<FormatException>(() => ReadText(""));
        Assert.Throws<FormatException>(() => ReadText(HeaderLine() + "\n" + HeaderLine() + "\n"));
    }

    [Fact]
    public void Dispose_without_Complete_reads_as_incomplete()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), Plain))
        {
            w.DefineChannel(new ChannelDef("s", "u", null, 10));
            w.WriteSamples(new SampleBlock("s", 0, Seq(5), null));
        }
        ms.Position = 0;
        var doc = RunlogReader.Read(ms);
        Assert.False(doc.Complete);
        Assert.Equal(5, doc.Channels["s"].Values.Length);
    }

    [Fact]
    public void Complete_with_no_samples_has_zero_duration()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), Plain)) w.Complete();
        ms.Position = 0;
        Assert.Equal(new EndRecord(0, 0), RunlogReader.Read(ms).End);
    }

    [Fact]
    public void Course_segments_events_round_trip()
    {
        var ms = new MemoryStream();
        var course = new Course(new Gate(1, 2, 3, 4), new Gate(5, 6, 7, 8), [], "ZZ-test", null);
        var seg = new Segment("lap", 0, 10, 10, null, [], "gate");
        var ev = new RunEvent("note", 5, null, "x");
        using (var w = new RunlogWriter(ms, Header(), Plain))
        {
            w.WriteCourse(course); w.WriteSegment(seg); w.WriteEvent(ev); w.Complete();
        }
        ms.Position = 0;
        var doc = RunlogReader.Read(ms);
        Assert.Equal(course.Start, doc.Course!.Start);
        var rs = Assert.Single(doc.Segments);
        Assert.Equal((seg.Kind, seg.S, seg.E, seg.OfficialTimeNs, seg.Src), (rs.Kind, rs.S, rs.E, rs.OfficialTimeNs, rs.Src));
        Assert.Equal(ev, Assert.Single(doc.Events));
    }

    [Fact]
    public void Runlog_Write_splits_into_blocks_of_1000()
    {
        var def = new ChannelDef("s", "u", null, 100);
        var vals = Seq(2500);
        var doc = new RunDocument(Header(), new Dictionary<string, ChannelData> { ["s"] = new(def, Enumerable.Range(0, 2500).Select(i => i * 10_000_000L).ToArray(), vals) },
            null, [], [], null);
        var ms = new MemoryStream();
        Runlog.Write(ms, doc, Plain);
        Assert.Equal(3, Lines(ms).Count(l => l.StartsWith("{\"k\":\"d\"")));
        ms.Position = 0;
        var back = RunlogReader.Read(ms);
        Assert.Equal(2500, back.Channels["s"].Values.Length);
        Assert.Equal(vals, back.Channels["s"].Values);
        Assert.True(back.Complete);
    }

    private static RunDocument RoundTrip(RunDocument doc)
    {
        var ms = new MemoryStream();
        Runlog.Write(ms, doc, Plain);
        ms.Position = 0;
        return RunlogReader.Read(ms);
    }

    [Fact]
    public void Runlog_Write_keeps_gaps_of_regular_channels()
    {
        var def = new ChannelDef("s", "u", null, 10);
        // 5 samples, a gap of 1.5 s, then 3 samples that are off the original grid.
        long[] times = [0, 100_000_000, 200_000_000, 300_000_000, 400_000_000, 1_950_000_000, 2_050_000_000, 2_150_000_000];
        var doc = new RunDocument(Header(), new Dictionary<string, ChannelData> { ["s"] = new(def, times, Seq(8)) }, null, [], [], null);
        var back = RoundTrip(doc);
        Assert.Equal(times, back.Channels["s"].Times);
        Assert.Equal(Seq(8), back.Channels["s"].Values);
        // read -> write -> read gives the same arrays
        Assert.Equal(times, RoundTrip(back).Channels["s"].Times);
    }

    [Fact]
    public void Runlog_Write_keeps_times_of_non_integer_period_across_many_samples()
    {
        var def = new ChannelDef("s", "u", null, 3);
        var times = Enumerable.Range(0, 2500).Select(i => (long)Math.Round(i * 1e9 / 3, MidpointRounding.AwayFromZero)).ToArray();
        var doc = new RunDocument(Header(), new Dictionary<string, ChannelData> { ["s"] = new(def, times, Seq(2500)) }, null, [], [], null);
        var back = RoundTrip(doc);
        Assert.Equal(times, back.Channels["s"].Times);
        Assert.Equal(times, RoundTrip(back).Channels["s"].Times);
    }

    [Fact]
    public void Writer_rejects_null_required_strings()
    {
        var ms = new MemoryStream();
        Assert.Throws<ArgumentException>(() => new RunlogWriter(ms, Header() with { Id = null! }, Plain));
        var prov = new Provenance(SourceKind.Synthetic, null, new ToolRef(null!, "1"), null, [], null);
        Assert.Throws<ArgumentException>(() => new RunlogWriter(new MemoryStream(), Header() with { Provenance = prov }, Plain));
        var files = new Provenance(SourceKind.Synthetic, null, null, null, [new SourceFileRef("a", null!)], null);
        Assert.Throws<ArgumentException>(() => new RunlogWriter(new MemoryStream(), Header() with { Provenance = files }, Plain));
        Assert.Throws<ArgumentException>(() => new RunlogWriter(new MemoryStream(),
            Header() with { Meta = new Dictionary<string, string> { ["k"] = null! } }, Plain));

        using var w = new RunlogWriter(new MemoryStream(), Header(), Plain);
        Assert.Throws<ArgumentException>(() => w.DefineChannel(new ChannelDef("a", null!, null, 1)));
        w.DefineChannel(new ChannelDef("a", "u", null, 1)); // the failed definition left nothing behind
        Assert.Throws<ArgumentException>(() => w.WriteSegment(new Segment(null!, 0, 1, null, null, [], "x")));
        Assert.Throws<ArgumentException>(() => w.WriteSegment(new Segment("lap", 0, 1, null, null, [], null!)));
        Assert.Throws<ArgumentException>(() => w.WriteEvent(new RunEvent(null!, 0, null, null)));
        Assert.Throws<ArgumentException>(() => w.WriteSamples(new SampleBlock(null!, 0, [1], null)));
    }

    [Fact]
    public void Reader_reports_unsupported_version_before_other_header_problems()
    {
        var ex = Assert.Throws<FormatException>(() =>
            ReadText("""{"k":"h","orf":"2.0","utc":0,"id":"x","kind":"a-new-kind"}""" + "\n"));
        Assert.Contains("Unsupported format version", ex.Message);
    }

    [Fact]
    public void Runlog_path_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".runlog");
        try
        {
            var def = new ChannelDef("g", "u", null, null);
            var doc = new RunDocument(Header(), new Dictionary<string, ChannelData> { ["g"] = new(def, [1, 5], [2, 3]) }, null, [], [], null);
            Runlog.Write(path, doc, Plain);
            var back = Runlog.Read(path);
            Assert.Equal([1L, 5L], back.Channels["g"].Times);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Rewriting_a_file_with_free_text_circuit_fails_naming_the_field()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "conformance", "valid", "course-free-text-circuit.runlog.jsonl");
        var doc = Runlog.Read(path);
        Assert.Equal("My Track", doc.Course!.CircuitRef);
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".runlog.jsonl");
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => Runlog.Write(tmp, doc));
            Assert.Contains("course.circuit", ex.Message);
        }
        finally { File.Delete(tmp); }
    }
}
