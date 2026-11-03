using System.Text;
using ZstdSharp;
using OpenRunFormat.Json;

namespace OpenRunFormat;

/// <summary>Options of <see cref="RunlogWriter"/>.</summary>
public sealed record RunlogWriterOptions(bool Compress = true, int FrameBytes = 64 * 1024, int CompressionLevel = 11);

/// <summary>
/// Streaming writer of .runlog files. Does not close the output stream.
/// <para>
/// Crash tolerance: records are buffered and only reach the output when the buffer is full (a zstd frame is closed
/// every <see cref="RunlogWriterOptions.FrameBytes"/>) or when <see cref="Flush"/> is called. A recorder that must lose
/// little data when it dies has to call <see cref="Flush"/> periodically (for example every few seconds); whatever is
/// still in the buffer is lost. <see cref="Flush"/> flushes the output stream but does not force the operating system
/// to write to disk (no fsync); do that on the stream yourself if you need it.
/// </para>
/// <para>This class is not thread-safe: use it from one thread at a time, or serialize the calls.</para>
/// </summary>
public sealed class RunlogWriter : IDisposable
{
    private readonly Stream _output;
    private readonly MemoryStream _buf = new();
    private readonly Compressor? _compressor;
    private readonly int _frameBytes;
    private readonly Dictionary<string, ChannelDef> _channels = new();
    private readonly Dictionary<string, long> _last = new();
    private long _duration;
    private long _samples;
    private bool _completed;
    private bool _disposed;

    /// <summary>Writes the header; <c>Orf</c> is forced to <see cref="RunFormat.Version"/>.</summary>
    public RunlogWriter(Stream output, RunHeader header, RunlogWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(header.Provenance, nameof(header));
        options ??= new RunlogWriterOptions();
        if (options.FrameBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options), "FrameBytes must be positive.");
        _output = output;
        _frameBytes = options.Compress ? options.FrameBytes : 16 * 1024;
        if (options.Compress)
        {
            _compressor = new Compressor();
            try
            {
                if (options.CompressionLevel < Compressor.MinCompressionLevel || options.CompressionLevel > Compressor.MaxCompressionLevel)
                    throw new ArgumentOutOfRangeException(nameof(options), "CompressionLevel is out of range.");
                _compressor.Level = options.CompressionLevel;
                _compressor.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_checksumFlag, 1); // detect bit flips
                Emit(header with { Orf = RunFormat.Version });
            }
            catch
            {
                _compressor.Dispose(); // the constructor failed: nobody will get the instance to dispose it
                throw;
            }
            return;
        }
        Emit(header with { Orf = RunFormat.Version });
    }

    public void DefineChannel(ChannelDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        Guard();
        if (BlockRules.CheckChannel(def) is { } err) throw new ArgumentException(err, nameof(def));
        if (_channels.ContainsKey(def.Id)) throw new ArgumentException($"Channel '{def.Id}' is already defined.", nameof(def));
        _channels[def.Id] = def;
        Emit(def);
    }

    public void WriteSamples(SampleBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        Guard();
        if (block.Channel is null) throw new ArgumentException("Block channel must not be null.", nameof(block));
        if (!_channels.TryGetValue(block.Channel, out var def))
            throw new InvalidOperationException($"Channel '{block.Channel}' is not defined.");
        _last.TryGetValue(block.Channel, out var lastRaw);
        long? last = _last.ContainsKey(block.Channel) ? lastRaw : null;
        if (BlockRules.Check(def, block, last) is { } err) throw new ArgumentException(err, nameof(block));
        if (block.Values.Length == 0) return;
        var lastT = BlockRules.LastTime(def, block);
        _last[block.Channel] = lastT;
        _samples += block.Values.Length;
        // Regular channels cover one more period after their last sample.
        _duration = Math.Max(_duration, def.Hz is { } hz ? lastT + BlockRules.Period(hz) : lastT);
        Emit(block);
    }

    public void WriteCourse(Course course) { ArgumentNullException.ThrowIfNull(course); Guard(); Emit(course); }
    public void WriteSegment(Segment segment) { ArgumentNullException.ThrowIfNull(segment); Guard(); Emit(segment); }
    public void WriteEvent(RunEvent ev) { ArgumentNullException.ThrowIfNull(ev); Guard(); Emit(ev); }

    /// <summary>Writes pending data to the output stream and flushes it. When compressing, this closes the current zstd frame.</summary>
    public void Flush()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RunlogWriter));
        WriteBuffer();
        _output.Flush();
    }

    /// <summary>Writes the <c>end</c> record and flushes. Further writes are rejected.</summary>
    public void Complete()
    {
        Guard();
        Emit(new EndRecord(_duration, _samples));
        _completed = true;
        Flush();
    }

    /// <summary>Flushes without an <c>end</c> record if <see cref="Complete"/> was not called. Does not close the stream.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        try { WriteBuffer(); _output.Flush(); }
        finally
        {
            _disposed = true;
            _compressor?.Dispose(); // the underlying stream stays open
        }
    }

    private void Guard()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RunlogWriter));
        if (_completed) throw new InvalidOperationException("The run is already completed.");
    }

    private void Emit(object record)
    {
        var bytes = Utf8.GetBytes(RecordCodec.Encode(record));
        _buf.Write(bytes);
        _buf.WriteByte((byte)'\n');
        if (_buf.Length >= _frameBytes) WriteBuffer();
    }

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Writes the buffered lines (always whole lines) as one zstd frame, or raw when not compressing.</summary>
    private void WriteBuffer()
    {
        if (_buf.Length == 0) return;
        var data = new ReadOnlySpan<byte>(_buf.GetBuffer(), 0, (int)_buf.Length);
        if (_compressor is null) _output.Write(data);
        else _output.Write(_compressor.Wrap(data)); // one-shot: the frame header carries the content size
        _buf.SetLength(0);
    }
}
