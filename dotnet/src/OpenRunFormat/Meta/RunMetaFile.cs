using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenRunFormat.Json;

namespace OpenRunFormat.Meta;

/// <summary>
/// Reads and writes .runmeta companion files: indented UTF-8 JSON without BOM, with non-ASCII text written raw.
/// Unknown fields are ignored. Instants are absolute Unix epoch nanoseconds (see <see cref="RunMeta"/>).
/// </summary>
public static class RunMetaFile
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
/// Writes the companion. <c>Orf</c> is forced to <see cref="RunFormat.Version"/>. The write is atomic: a temporary
/// file in the same directory is flushed to disk and moved over the target; on failure an existing target is untouched.
/// </summary>
    public static void Write(string path, RunMeta meta)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(meta);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("orf", RunFormat.Version);
            w.WriteString("id", meta.Id);
            w.WriteString("runlogSha256", meta.RunlogSha256);
            w.WriteStartObject("vehicle");
            foreach (var kv in meta.Vehicle) w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();
            w.WriteStartArray("setup");
            foreach (var s in meta.Setup)
            {
                w.WriteStartObject();
                w.WriteNumber("s", s.S);
                w.WriteNumber("e", s.E);
                w.WriteStartObject("values");
                foreach (var kv in s.Values) w.WriteString(kv.Key, kv.Value);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("notes");
            foreach (var n in meta.Notes)
            {
                w.WriteStartObject();
                w.WriteNumber("t", n.T);
                w.WriteString("text", n.Text);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("userSegments");
            foreach (var seg in meta.UserSegments)
            {
                w.WriteStartObject();
                RecordCodec.WriteSegmentBody(w, seg);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        AtomicFile.Write(path, fs => fs.Write(ms.GetBuffer(), 0, (int)ms.Length));
    }

    /// <summary>Reads a companion file.</summary>
    /// <exception cref="FormatException">Broken JSON, a major version other than 1, or a missing orf/id/runlogSha256.</exception>
    public static RunMeta Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) throw new FormatException("The companion file is not a JSON object.");

            var orf = RecordCodec.Req(r, "orf").GetString()!;
            var major = orf.Split('.')[0];
            if (!int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m != 1)
                throw new FormatException($"Unsupported format version '{orf}'.");

            var vehicle = new Dictionary<string, string>();
            if (RecordCodec.Opt(r, "vehicle") is { } v)
                foreach (var p in v.EnumerateObject()) vehicle[p.Name] = p.Value.GetString() ?? "";

            var setup = new List<SetupInterval>();
            if (RecordCodec.Opt(r, "setup") is { } setupArr)
                foreach (var s in setupArr.EnumerateArray())
                {
                    var values = new Dictionary<string, string>();
                    if (RecordCodec.Opt(s, "values") is { } sv)
                        foreach (var p in sv.EnumerateObject()) values[p.Name] = p.Value.GetString() ?? "";
                    setup.Add(new SetupInterval(RecordCodec.Req(s, "s").GetInt64(), RecordCodec.Req(s, "e").GetInt64(), values));
                }

            var notes = new List<Note>();
            if (RecordCodec.Opt(r, "notes") is { } notesArr)
                foreach (var n in notesArr.EnumerateArray())
                    notes.Add(new Note(RecordCodec.Req(n, "t").GetInt64(), RecordCodec.Req(n, "text").GetString()!));

            var segments = new List<Segment>();
            if (RecordCodec.Opt(r, "userSegments") is { } segArr)
                foreach (var s in segArr.EnumerateArray())
                {
                    segments.Add(RecordCodec.ReadSegmentBody(s));
                }

            return new RunMeta(orf, RecordCodec.Req(r, "id").GetString()!, RecordCodec.Req(r, "runlogSha256").GetString()!,
                vehicle, setup, notes, segments);
        }
        catch (FormatException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw new FormatException("Invalid companion file: " + ex.Message, ex);
        }
    }

    /// <summary>SHA-256 of the whole .runlog file, as lowercase hex.</summary>
    public static string Sha256Of(string runlogPath)
    {
        ArgumentNullException.ThrowIfNull(runlogPath);
        using var fs = File.OpenRead(runlogPath);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>The companion path for a .runlog path: <c>x.runlog</c> becomes <c>x.runmeta</c>.</summary>
    public static string PathFor(string runlogPath)
    {
        ArgumentNullException.ThrowIfNull(runlogPath);
        return Path.ChangeExtension(runlogPath, ".runmeta");
    }
}
