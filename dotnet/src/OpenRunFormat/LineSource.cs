using System.Buffers.Binary;
using System.Text;
using ZstdSharp;

namespace OpenRunFormat;

/// <summary>
/// Splits a .runlog stream (plain or zstd-framed) into lines, lazily. A zstd frame is read with exactly the bytes it
/// needs (the frame structure is walked header by header, block by block), so nothing past the frame being decoded
/// is consumed. A truncated or corrupt frame ends the sequence quietly; a last line without LF is reported as unterminated.
/// </summary>
internal static class LineSource
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private const int MaxBlock = 128 * 1024; // zstd block size limit

    /// <summary>Set by the source: whether the input had any byte at all.</summary>
    public sealed class Info
    {
        public bool HasInput;
        /// <summary>Set when the sequence ended early because bytes remained that could not be decoded or are not a frame.</summary>
        public bool DroppedBytes;
    }

    public static IEnumerable<(string Text, bool Terminated)> Open(Stream input, Info? info = null)
    {
        var pending = new MemoryStream();
        foreach (var chunk in Chunks(input, info))
        {
            int start = 0;
            while (true)
            {
                int idx = chunk.Span[start..].IndexOf((byte)'\n');
                if (idx < 0) break;
                pending.Write(chunk.Span.Slice(start, idx));
                yield return (Take(pending), true);
                start += idx + 1;
            }
            pending.Write(chunk.Span[start..]);
        }
        if (pending.Length > 0) yield return (Take(pending), false);
    }

    private static string Take(MemoryStream pending)
    {
        int len = (int)pending.Length;
        if (len > 0 && pending.GetBuffer()[len - 1] == (byte)'\r') len--;
        var text = Utf8.GetString(pending.GetBuffer(), 0, len);
        pending.SetLength(0);
        return text;
    }

    private static IEnumerable<ReadOnlyMemory<byte>> Chunks(Stream input, Info? info)
    {
        var magic = new byte[4];
        int n = Fill(input, magic);
        if (n > 0 && info is not null) info.HasInput = true;
        if (n < 4)
        {
            if (n > 0) yield return magic.AsMemory(0, n);
            yield break;
        }
        if (!IsZstdMagic(magic))
        {
            yield return magic;
            var buf = new byte[16 * 1024];
            int r;
            while ((r = input.Read(buf, 0, buf.Length)) > 0) yield return buf.AsMemory(0, r);
            yield break;
        }

        while (true)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(magic) != 0xFD2FB528u)
            {
                // Skippable frame (0x184D2A50..5F): 4-byte size, then that many bytes to ignore.
                var size = new byte[4];
                if (Fill(input, size) < 4) yield break;
                long skip = BinaryPrimitives.ReadUInt32LittleEndian(size);
                var sink = new byte[4096];
                while (skip > 0)
                {
                    int got = input.Read(sink, 0, (int)Math.Min(skip, sink.Length));
                    if (got <= 0) yield break;
                    skip -= got;
                }
            }
            else
            {
                var frame = ReadFrame(input, magic);
                if (frame is null) { Dropped(info); yield break; }
                byte[]? plain = Decode(frame);
                if (plain is null) { Dropped(info); yield break; }
                yield return plain;
            }
            int got4 = Fill(input, magic);
            if (got4 < 4) { if (got4 > 0) Dropped(info); yield break; }
            if (!IsZstdMagic(magic)) { Dropped(info); yield break; }
        }
    }

    private static void Dropped(Info? info) { if (info is not null) info.DroppedBytes = true; }

    private static bool IsZstdMagic(byte[] m)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(m);
        return v == 0xFD2FB528u || (v & 0xFFFFFFF0u) == 0x184D2A50u;
    }

    private static byte[]? Decode(byte[] frame)
    {
        try
        {
            using var src = new MemoryStream(frame);
            using var ds = new DecompressionStream(src);
            var dst = new MemoryStream();
            ds.CopyTo(dst);
            return dst.ToArray();
        }
        catch (Exception ex) when (ex is ZstdException or IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads one complete zstd frame whose magic was already read. Null when it is truncated or malformed.</summary>
    private static byte[]? ReadFrame(Stream input, byte[] magic)
    {
        var frame = new MemoryStream();
        frame.Write(magic);
        var fhd = new byte[1];
        if (Fill(input, fhd) < 1) return null;
        frame.Write(fhd);
        int d = fhd[0];
        bool single = (d & 0x20) != 0;
        bool checksum = (d & 0x04) != 0;
        int dictBytes = (d & 3) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
        int fcsBytes = (d >> 6) switch { 0 => single ? 1 : 0, 1 => 2, 2 => 4, _ => 8 };
        int rest = (single ? 0 : 1) + dictBytes + fcsBytes;
        if (rest > 0)
        {
            var hdr = new byte[rest];
            if (Fill(input, hdr) < rest) return null;
            frame.Write(hdr);
        }
        var bh = new byte[3];
        var body = new byte[MaxBlock];
        while (true)
        {
            if (Fill(input, bh) < 3) return null;
            frame.Write(bh);
            uint h = (uint)(bh[0] | bh[1] << 8 | bh[2] << 16);
            bool last = (h & 1) != 0;
            int type = (int)(h >> 1) & 3;
            int size = (int)(h >> 3);
            if (type == 3 || size > MaxBlock) return null;
            int content = type == 1 ? 1 : size;
            if (content > 0)
            {
                if (Fill(input, body.AsSpan(0, content)) < content) return null;
                frame.Write(body, 0, content);
            }
            if (last) break;
        }
        if (checksum)
        {
            var cs = new byte[4];
            if (Fill(input, cs) < 4) return null;
            frame.Write(cs);
        }
        return frame.ToArray();
    }

    /// <summary>Reads up to buffer.Length bytes; fewer only at the end of the stream.</summary>
    private static int Fill(Stream s, Span<byte> buffer) => s.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
}
