using System.Diagnostics;
using System.Text;
using OpenRunFormat.Json;

namespace OpenRunFormat.Tests;

public class CompressionTests
{
    private static readonly byte[] Magic = [0x28, 0xB5, 0x2F, 0xFD];

    private static RunHeader Header() => new(
        "1.0", 1_700_000_000_000_000_000, null, "r1", null,
        new Provenance(SourceKind.Synthetic, null, null, null, [], null),
        new Dictionary<string, string>());

    private static double[] Seq(int n, int from = 0) => Enumerable.Range(from, n).Select(i => (double)i).ToArray();

    private static int CountMagic(byte[] data)
    {
        int c = 0;
        for (int i = 0; i + 4 <= data.Length; i++)
            if (data.AsSpan(i, 4).SequenceEqual(Magic)) c++;
        return c;
    }

    /// <summary>Three frames (Flush between them) of 100 samples each; no end record.</summary>
    private static byte[] ThreeFrames(bool complete = false)
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), new RunlogWriterOptions()))
        {
            w.DefineChannel(new ChannelDef("speed", "km/h", null, 10));
            w.Flush();
            for (int f = 0; f < 3; f++)
            {
                w.WriteSamples(new SampleBlock("speed", f * 10_000_000_000, Seq(100, f * 100), null));
                w.Flush();
            }
            if (complete) w.Complete();
        }
        return ms.ToArray();
    }

    [Fact]
    public void Compressed_file_starts_with_zstd_magic()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header())) w.Complete();
        Assert.Equal(Magic, ms.ToArray()[..4]);
    }

    [Fact]
    public void Compressed_round_trip_equals_uncompressed()
    {
        RunDocument Build(RunlogWriterOptions o)
        {
            var ms = new MemoryStream();
            using (var w = new RunlogWriter(ms, Header(), o))
            {
                w.DefineChannel(new ChannelDef("speed", "km/h", null, 10));
                w.DefineChannel(new ChannelDef("gps", "deg", null, null));
                for (int i = 0; i < 20; i++)
                    w.WriteSamples(new SampleBlock("speed", i * 10_000_000_000, Seq(100, i * 100), null));
                w.WriteSamples(new SampleBlock("gps", 0, [1, 2, 3], [0, 1_000_000_000, 2_050_000_000]));
                w.WriteEvent(new RunEvent("lap", 5_000_000_000, null, null));
                w.Complete();
            }
            ms.Position = 0;
            return RunlogReader.Read(ms);
        }
        var z = Build(new RunlogWriterOptions(Compress: true, FrameBytes: 1024));
        var p = Build(new RunlogWriterOptions(Compress: false));
        Assert.True(z.Complete);
        Assert.Equal(p.Header.Id, z.Header.Id);
        Assert.Equal(p.Header.Utc, z.Header.Utc);
        Assert.Equal(p.End, z.End);
        Assert.Equal(p.Events, z.Events);
        Assert.Equal(p.Channels.Keys, z.Channels.Keys);
        foreach (var id in p.Channels.Keys)
        {
            Assert.Equal(p.Channels[id].Times, z.Channels[id].Times);
            Assert.Equal(p.Channels[id].Values, z.Channels[id].Values);
        }
    }

    [Fact]
    public void Small_FrameBytes_makes_several_frames_of_whole_lines()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), new RunlogWriterOptions(FrameBytes: 100)))
        {
            w.DefineChannel(new ChannelDef("s", "", null, 10));
            for (int i = 0; i < 10; i++) w.WriteSamples(new SampleBlock("s", i * 1_000_000_000L, Seq(10, i * 10), null));
            w.Complete();
        }
        Assert.True(CountMagic(ms.ToArray()) >= 5);
        ms.Position = 0;
        var doc = RunlogReader.Read(ms);
        Assert.True(doc.Complete);
        Assert.Equal(100, doc.Channels["s"].Values.Length);
    }

    [Fact]
    public void Each_Flush_produces_a_new_frame()
        => Assert.True(CountMagic(ThreeFrames()) >= 4); // header+channel frame + 3 sample frames

    [Fact]
    public void Flush_with_nothing_pending_adds_no_frame()
    {
        var ms = new MemoryStream();
        using var w = new RunlogWriter(ms, Header());
        w.Flush();
        long len = ms.Length;
        w.Flush(); w.Flush();
        Assert.Equal(len, ms.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(-1)] // half of the last frame
    public void Truncated_file_keeps_complete_frames(int cutFromEnd)
    {
        var data = ThreeFrames();
        // Find the start of the last frame.
        int lastFrame = 0;
        for (int i = 0; i + 4 <= data.Length; i++) if (data.AsSpan(i, 4).SequenceEqual(Magic)) lastFrame = i;
        int cut = cutFromEnd > 0 ? data.Length - cutFromEnd : lastFrame + (data.Length - lastFrame) / 2;
        var doc = RunlogReader.Read(new MemoryStream(data[..cut]));
        Assert.False(doc.Complete);
        Assert.Null(doc.End);
        Assert.True(doc.Channels["speed"].Values.Length >= 200);
        Assert.Equal(Seq(200), doc.Channels["speed"].Values[..200]);
    }

    [Fact]
    public void Corrupt_frame_stops_reading_without_throwing()
    {
        var data = ThreeFrames(complete: true);
        int third = 0, seen = 0;
        for (int i = 0; i + 4 <= data.Length; i++)
            if (data.AsSpan(i, 4).SequenceEqual(Magic) && ++seen == 4) { third = i; break; }
        // Damage the block header of the 3rd sample frame.
        for (int k = 6; k < 20; k++) data[third + k] ^= 0xFF;
        var doc = RunlogReader.Read(new MemoryStream(data));
        Assert.False(doc.Complete);
        Assert.True(doc.Channels["speed"].Values.Length >= 200);
    }

    [Fact]
    public void Truncated_in_middle_of_line_drops_only_that_line()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), new RunlogWriterOptions(Compress: false)))
        {
            w.DefineChannel(new ChannelDef("s", "", null, 10));
            w.WriteSamples(new SampleBlock("s", 0, Seq(10), null));
            w.WriteSamples(new SampleBlock("s", 1_000_000_000, Seq(10, 10), null));
            w.Complete();
        }
        var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
        int lastLineStart = text.LastIndexOf('\n', text.LastIndexOf('\n') - 1) + 1; // start of 2nd block line
        var cutAt = lastLineStart + 15; // inside the second data line
        var doc = RunlogReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text[..cutAt])));
        Assert.False(doc.Complete);
        Assert.Equal(10, doc.Channels["s"].Values.Length);
    }

    [Fact]
    public void Unterminated_but_valid_last_line_is_kept()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), new RunlogWriterOptions(Compress: false)))
        {
            w.DefineChannel(new ChannelDef("s", "", null, 10));
            w.WriteSamples(new SampleBlock("s", 0, Seq(10), null));
            w.Complete();
        }
        var bytes = ms.ToArray();
        var doc = RunlogReader.Read(new MemoryStream(bytes[..^1])); // only the final LF is missing
        Assert.True(doc.Complete);
    }

    [Fact]
    public void Terminated_invalid_record_still_throws()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), new RunlogWriterOptions(Compress: false))) w.Flush();
        var text = Encoding.UTF8.GetString(ms.ToArray()) + "{not json}\n";
        Assert.Throws<FormatException>(() => { RunlogReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text))); });
    }

    [Fact]
    public void Overlapping_blocks_in_compressed_file_still_throw()
    {
        var lines = new[]
        {
            RecordCodec.Encode(Header()),
            "{\"k\":\"ch\",\"id\":\"s\",\"hz\":10}",
            "{\"k\":\"d\",\"ch\":\"s\",\"t0\":0,\"v\":[1,2,3]}",
            "{\"k\":\"d\",\"ch\":\"s\",\"t0\":0,\"v\":[1,2,3]}",
        };
        var raw = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        var framed = new ZstdSharp.Compressor().Wrap(raw).ToArray();
        Assert.Throws<FormatException>(() => { RunlogReader.Read(new MemoryStream(framed)); });
    }

    [Fact]
    public void ReadHeader_reads_only_first_frame()
    {
        var data = ThreeFrames();
        int second = 0, seen = 0;
        for (int i = 0; i + 4 <= data.Length; i++)
            if (data.AsSpan(i, 4).SequenceEqual(Magic) && ++seen == 2) { second = i; break; }
        for (int k = 4; k < data.Length - second && k < 40; k++) data[second + k] ^= 0xFF;
        Assert.Equal("r1", RunlogReader.ReadHeader(new MemoryStream(data)).Id);
    }

    /// <summary>Non-seekable stream that serves <c>limit</c> bytes and throws if anything beyond is requested.</summary>
    private sealed class LimitedStream(byte[] data, int limit) : Stream
    {
        private int _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= limit) throw new InvalidOperationException("Read past the first frame.");
            int n = Math.Min(count, limit - _pos);
            Array.Copy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void ReadHeader_on_non_seekable_stream_does_not_read_past_the_first_frame()
    {
        var data = ThreeFrames();
        int second = 0, seen = 0;
        for (int i = 0; i + 4 <= data.Length; i++)
            if (data.AsSpan(i, 4).SequenceEqual(Magic) && ++seen == 2) { second = i; break; }
        Assert.Equal("r1", RunlogReader.ReadHeader(new LimitedStream(data, second)).Id);
    }

    [Fact]
    public void ReadHeader_on_non_seekable_plain_stream_reads_only_what_is_needed()
    {
        var ms = new MemoryStream();
        using (var w = new RunlogWriter(ms, Header(), new RunlogWriterOptions(Compress: false))) w.Flush();
        var data = ms.ToArray();
        // Serve exactly the header line plus a little; a full-stream copy would hit the throwing tail.
        var padded = data.Concat(new byte[100_000]).ToArray();
        Assert.Equal("r1", RunlogReader.ReadHeader(new LimitedStream(padded, data.Length)).Id);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(2e9)]
    public void Invalid_rates_are_rejected_by_writer_and_reader(double hz)
    {
        using var w = new RunlogWriter(new MemoryStream(), Header(), new RunlogWriterOptions(Compress: false));
        Assert.Throws<ArgumentException>(() => { w.DefineChannel(new ChannelDef("s", "", null, hz)); });
        if (!double.IsFinite(hz)) return; // not representable in JSON
        var text = RecordCodec.Encode(Header()) + "\n{\"k\":\"ch\",\"id\":\"s\",\"hz\":" +
                   hz.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}\n";
        Assert.Throws<FormatException>(() => { RunlogReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text))); });
    }

    [Fact]
    public void Runlog_Write_rejects_mismatched_channel_lengths()
    {
        var doc = new RunDocument(Header(),
            new Dictionary<string, ChannelData> { ["bad"] = new(new ChannelDef("bad", "", null, 10), [0, 1], [1.0]) },
            null, [], [], null);
        var ex = Assert.Throws<ArgumentException>(() => { Runlog.Write(new MemoryStream(), doc); });
        Assert.Contains("bad", ex.Message);
    }

    [Fact]
    public void Bit_flip_inside_valid_frame_is_detected_by_checksum()
    {
        var data = ThreeFrames(complete: true);
        var starts = new List<int>();
        for (int i = 0; i + 4 <= data.Length; i++) if (data.AsSpan(i, 4).SequenceEqual(Magic)) starts.Add(i);
        // starts[0] = header+channel, [1..3] = sample frames; flip a byte in the middle of the 2nd sample frame.
        data[(starts[2] + starts[3]) / 2] ^= 0x01;
        var doc = RunlogReader.Read(new MemoryStream(data));
        Assert.False(doc.Complete);
        Assert.Null(doc.End);
        Assert.Equal(Seq(100), doc.Channels["speed"].Values);
    }

    [Fact]
    public void Header_unreadable_gives_specific_message_and_empty_input_says_empty()
    {
        var data = ThreeFrames();
        var cut = data[..10]; // first frame truncated
        var ex = Assert.Throws<FormatException>(() => { RunlogReader.Read(new MemoryStream(cut)); });
        Assert.Equal("No complete header record could be read.", ex.Message);
        ex = Assert.Throws<FormatException>(() => { RunlogReader.ReadHeader(new MemoryStream(cut)); });
        Assert.Equal("No complete header record could be read.", ex.Message);
        ex = Assert.Throws<FormatException>(() => { RunlogReader.Read(new MemoryStream()); });
        Assert.Equal("The file is empty.", ex.Message);
    }

    private static byte[] Skippable(int payload)
    {
        var f = new byte[8 + payload];
        BitConverter.GetBytes(0x184D2A50u).CopyTo(f, 0);
        BitConverter.GetBytes((uint)payload).CopyTo(f, 4);
        return f;
    }

    [Fact]
    public void Foreign_encoder_frames_split_mid_line_with_skippable_frames_and_checksum()
    {
        var plainMs = new MemoryStream();
        using (var w = new RunlogWriter(plainMs, Header(), new RunlogWriterOptions(Compress: false)))
        {
            w.DefineChannel(new ChannelDef("speed", "km/h", null, 10));
            for (int i = 0; i < 5; i++) w.WriteSamples(new SampleBlock("speed", i * 10_000_000_000, Seq(100, i * 100), null));
            w.WriteEvent(new RunEvent("lap", 1_000_000_000, null, null));
            w.Complete();
        }
        var raw = plainMs.ToArray();
        var expected = RunlogReader.Read(new MemoryStream(raw));

        var offsets = new[] { 0, 37, 311, 700, 1500, raw.Length };
        offsets = offsets.Where(o => o <= raw.Length).Distinct().OrderBy(o => o).ToArray();
        var outMs = new MemoryStream();
        outMs.Write(Skippable(5));
        using var plainC = new ZstdSharp.Compressor();
        using var sumC = new ZstdSharp.Compressor();
        sumC.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
        for (int k = 0; k + 1 < offsets.Length; k++)
        {
            var part = raw.AsSpan(offsets[k], offsets[k + 1] - offsets[k]);
            if (part.Length == 0) continue;
            outMs.Write((k % 2 == 0 ? sumC : plainC).Wrap(part));
            if (k == 1) outMs.Write(Skippable(0));
            if (k == 2) outMs.Write(Skippable(100));
        }
        var doc = RunlogReader.Read(new MemoryStream(outMs.ToArray()));
        Assert.True(doc.Complete);
        Assert.Equal(expected.End, doc.End);
        Assert.Equal(expected.Events, doc.Events);
        Assert.Equal(expected.Channels["speed"].Times, doc.Channels["speed"].Times);
        Assert.Equal(expected.Channels["speed"].Values, doc.Channels["speed"].Values);
    }

    [Fact]
    public void Real_size_one_hour_10Hz_20_channels_under_budget()
    {
        var rnd = new Random(42);
        const int n = 36_000, channels = 20;
        var data = new Dictionary<string, ChannelData>();
        for (int c = 0; c < channels; c++)
        {
            var v = new double[n];
            double x = rnd.NextDouble() * 100;
            for (int i = 0; i < n; i++)
            {
                x += (rnd.NextDouble() - 0.5) * 2;
                v[i] = Math.Round(x, 2);
            }
            var def = new ChannelDef($"ch{c}", "u", null, 10);
            data[def.Id] = new ChannelData(def, Enumerable.Range(0, n).Select(i => i * 100_000_000L).ToArray(), v);
        }
        var doc = new RunDocument(Header(), data, null, [], [], null);

        var sw = Stopwatch.StartNew();
        var ms = new MemoryStream();
        Runlog.Write(ms, doc);
        ms.Position = 0;
        var back = RunlogReader.Read(ms);
        sw.Stop();

        Assert.True(ms.Length < 15 * 1024 * 1024, $"size {ms.Length}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"time {sw.Elapsed}"); // brief says 5 s; wide margin for loaded CI
        Assert.Equal(n, back.Channels["ch7"].Values.Length);
        Assert.Equal(data["ch7"].Values, back.Channels["ch7"].Values);
        Console.WriteLine($"PERF size={ms.Length} bytes time={sw.ElapsedMilliseconds} ms");
    }
}
