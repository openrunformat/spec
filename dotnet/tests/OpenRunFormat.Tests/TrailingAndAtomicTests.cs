using System.Text;
using OpenRunFormat.Json;
using OpenRunFormat.Meta;

namespace OpenRunFormat.Tests;

public class TrailingAndAtomicTests
{
    private static RunHeader Header() => new(
        "1.0", 1_700_000_000_000_000_000, null, "r1", null,
        new Provenance(SourceKind.Synthetic, null, null, null, [], null),
        new Dictionary<string, string>());

    private static byte[] Build(bool compress, bool complete = true)
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), new RunlogWriterOptions(Compress: compress)))
        {
            w.DefineChannel(new ChannelDef("speed", "m/s", null, 10));
            w.WriteSamples(new SampleBlock("speed", 0, [1, 2, 3], null));
            if (complete) w.Complete();
        }
        return ms.ToArray();
    }

    private static byte[] Cat(byte[] a, byte[] b) => [.. a, .. b];
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static RunDocument Read(byte[] data) => RunlogReader.Read(new MemoryStream(data));

    [Fact]
    public void Plain_without_trailing_content()
    {
        var doc = Read(Build(false));
        Assert.True(doc.Complete);
        Assert.False(doc.HasTrailingContent);
    }

    [Fact]
    public void Plain_with_only_empty_lines_after_end_has_no_trailing_content()
    {
        var doc = Read(Cat(Build(false), Bytes("\n\r\n\n")));
        Assert.False(doc.HasTrailingContent);
    }

    [Fact]
    public void Plain_with_a_record_after_end()
    {
        var doc = Read(Cat(Build(false), Bytes("{\"k\":\"ev\",\"kind\":\"note\",\"s\":0}\n")));
        Assert.True(doc.Complete);
        Assert.True(doc.HasTrailingContent);
        Assert.Equal(3, doc.Channels["speed"].Values.Length);
        Assert.Empty(doc.Events);
    }

    [Fact]
    public void Plain_with_garbage_without_lf_after_end()
    {
        var doc = Read(Cat(Build(false), Bytes("xx")));
        Assert.True(doc.HasTrailingContent);
        Assert.True(doc.Complete);
    }

    [Fact]
    public void Compressed_without_trailing_content()
    {
        Assert.False(Read(Build(true)).HasTrailingContent);
    }

    [Fact]
    public void Compressed_with_a_frame_after_end()
    {
        // A second file's bytes appended: its frame(s) follow the first end.
        var doc = Read(Cat(Build(true), Build(true)));
        Assert.True(doc.Complete);
        Assert.True(doc.HasTrailingContent);
        Assert.Equal(3, doc.Channels["speed"].Values.Length);
    }

    [Fact]
    public void Compressed_with_non_frame_bytes_after_end()
    {
        var doc = Read(Cat(Build(true), Bytes("junk")));
        Assert.True(doc.Complete);
        Assert.True(doc.HasTrailingContent);
    }

    [Fact]
    public void Compressed_with_trailing_lines_in_same_frame()
    {
        // Hand-built: end record followed by another line inside one frame.
        var plain = Build(false);
        var text = Encoding.UTF8.GetString(plain) + "{\"k\":\"ev\",\"kind\":\"note\",\"s\":0}\n";
        var ms = new MemoryStream();
        using (var z = new ZstdSharp.CompressionStream(ms, 1, leaveOpen: true)) z.Write(Bytes(text));
        var doc = Read(ms.ToArray());
        Assert.True(doc.Complete);
        Assert.True(doc.HasTrailingContent);
        Assert.Equal(3, doc.Channels["speed"].Values.Length);
    }

    [Fact]
    public void Incomplete_file_has_no_trailing_content()
    {
        var doc = Read(Build(false, complete: false));
        Assert.False(doc.Complete);
        Assert.False(doc.HasTrailingContent);
    }

    // ---- atomic write ----

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "orf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static RunDocument GoodDoc()
    {
        var def = new ChannelDef("speed", "m/s", null, 10);
        return new RunDocument(Header(), new Dictionary<string, ChannelData> { ["speed"] = new(def, [0, 100_000_000], [1, 2]) },
            null, [], [], null);
    }

    private static RunDocument BadDoc()
    {
        var def = new ChannelDef("speed", "m/s", null, 10);
        return new RunDocument(Header(), new Dictionary<string, ChannelData> { ["speed"] = new(def, [0, 100_000_000], [1]) },
            null, [], [], null);
    }

    [Fact]
    public void Failed_runlog_write_keeps_existing_file_and_leaves_no_temp()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "a.runlog");
            File.WriteAllText(path, "original");
            Assert.Throws<ArgumentException>(() => Runlog.Write(path, BadDoc()));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Failed_runlog_write_creates_no_file_when_none_existed()
    {
        var dir = TempDir();
        try
        {
            Assert.Throws<ArgumentException>(() => Runlog.Write(Path.Combine(dir, "a.runlog"), BadDoc()));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Successful_runlog_write_replaces_and_leaves_no_temp()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "a.runlog");
            File.WriteAllText(path, "original");
            Runlog.Write(path, GoodDoc());
            Assert.Equal(2, Runlog.Read(path).Channels["speed"].Values.Length);
            Assert.Equal([path], Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Failed_runmeta_write_keeps_existing_file_and_leaves_no_temp()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "a.runmeta");
            File.WriteAllText(path, "original");
            var bad = new RunMeta(RunFormat.Version, "r1", "00", null!, [], [], []);
            Assert.ThrowsAny<Exception>(() => RunMetaFile.Write(path, bad));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
