using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenRunFormat.Json;

/// <summary>
/// Encodes and decodes the individual JSON-line records. Written by hand (no reflection serialization) so that
/// field order and names are normative and null/NaN handling is explicit.
/// </summary>
internal static class RecordCodec
{
    /// <summary>Encodes a record as one compact JSON object (no trailing newline).</summary>
    public static string Encode(object record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            switch (record)
            {
                case RunHeader h: WriteHeader(w, h); break;
                case ChannelDef c: WriteChannel(w, c); break;
                case SampleBlock d: WriteBlock(w, d); break;
                case Course c: WriteCourse(w, c); break;
                case Segment s: WriteSegment(w, s); break;
                case RunEvent e: WriteEvent(w, e); break;
                case EndRecord e: WriteEnd(w, e); break;
                default: throw new ArgumentException($"Unsupported record type {record.GetType().Name}.", nameof(record));
            }
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>
    /// If <paramref name="line"/> is a header record with a string <c>orf</c>, returns that version; otherwise null.
    /// Never throws; lets the reader check the major version before decoding the rest of the header.
    /// </summary>
    public static string? PeekHeaderVersion(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            return r.ValueKind == JsonValueKind.Object
                && r.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() == "h"
                && r.TryGetProperty("orf", out var o) && o.ValueKind == JsonValueKind.String
                ? o.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Decodes one line. Returns null for an unknown record kind (forward compatibility).</summary>
    /// <exception cref="FormatException">Broken JSON or a missing/invalid required field.</exception>
    public static object? Decode(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) throw new FormatException("Record is not a JSON object.");
            return Req(r, "k").GetString() switch
            {
                "h" => ReadHeader(r),
                "ch" => ReadChannel(r),
                "d" => ReadBlock(r),
                "course" => ReadCourse(r),
                "seg" => ReadSegment(r),
                "ev" => ReadEvent(r),
                "end" => new EndRecord(Req(r, "dur").GetInt64(), Req(r, "n").GetInt64()),
                _ => null,
            };
        }
        catch (FormatException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw new FormatException("Invalid record: " + ex.Message, ex);
        }
    }

    // ---------- writing ----------

    private static void WriteHeader(Utf8JsonWriter w, RunHeader h)
    {
        w.WriteStartObject();
        w.WriteString("k", "h");
        w.WriteString("orf", Required(h.Orf, "header orf"));
        w.WriteNumber("utc", h.Utc);
        if (h.Tz is not null) w.WriteString("tz", h.Tz);
        w.WriteString("id", Required(h.Id, "header id"));
        if (h.Discipline is not null) w.WriteString("discipline", h.Discipline);
        var p = h.Provenance;
        w.WriteString("kind", KindToString(p.Kind));
        if (p.Device is not null) w.WriteString("device", p.Device);
        if (p.Converter is not null) WriteTool(w, "converter", p.Converter);
        if (p.Writer is not null) WriteTool(w, "writer", p.Writer);
        if (p.SourceFiles is { Count: > 0 })
        {
            w.WriteStartArray("sourceFiles");
            foreach (var f in p.SourceFiles)
            {
                w.WriteStartObject();
                w.WriteString("name", Required(f.Name, "source file name"));
                w.WriteString("sha256", Required(f.Sha256, "source file sha256"));
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (p.Precision is { SegmentToleranceMs: not null } or { GpsHz: not null })
        {
            w.WriteStartObject("precision");
            WriteOptDouble(w, "segTolMs", p.Precision!.SegmentToleranceMs);
            WriteOptDouble(w, "gpsHz", p.Precision.GpsHz);
            w.WriteEndObject();
        }
        if (h.Meta is { Count: > 0 })
        {
            w.WriteStartObject("meta");
            foreach (var kv in h.Meta) w.WriteString(Required(kv.Key, "meta key"), Required(kv.Value, "meta value"));
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    private static void WriteTool(Utf8JsonWriter w, string name, ToolRef t)
    {
        w.WriteStartObject(name);
        w.WriteString("id", Required(t.Id, name + " id"));
        w.WriteString("version", Required(t.Version, name + " version"));
        w.WriteEndObject();
    }

    private static void WriteChannel(Utf8JsonWriter w, ChannelDef c)
    {
        w.WriteStartObject();
        w.WriteString("k", "ch");
        w.WriteString("id", Required(c.Id, "channel id"));
        w.WriteString("unit", Required(c.Unit, "channel unit"));
        if (c.SourceName is not null) w.WriteString("src", c.SourceName);
        WriteOptDouble(w, "hz", c.Hz);
        w.WriteEndObject();
    }

    private static void WriteBlock(Utf8JsonWriter w, SampleBlock d)
    {
        w.WriteStartObject();
        w.WriteString("k", "d");
        w.WriteString("ch", Required(d.Channel, "block channel"));
        w.WriteNumber("t0", d.T0);
        w.WriteStartArray("v");
        foreach (var v in d.Values)
        {
            if (double.IsNaN(v)) w.WriteNullValue();
            else w.WriteNumberValue(Finite(v));
        }
        w.WriteEndArray();
        if (d.Times is not null)
        {
            w.WriteStartArray("t");
            foreach (var t in d.Times) w.WriteNumberValue(t);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static void WriteCourse(Utf8JsonWriter w, Course c)
    {
        if (c.CircuitRef is not null && !CircuitId.IsValidCircuit(c.CircuitRef))
            throw new ArgumentException($"course.circuit is not a valid circuit identifier: '{c.CircuitRef}'.");
        if (c.Variant is not null && c.CircuitRef is null)
            throw new ArgumentException("course.variant requires course.circuit.");
        if (c.Variant is not null && !CircuitId.IsValidVariant(c.Variant))
            throw new ArgumentException($"course.variant is not a valid variant identifier: '{c.Variant}'.");
        if (c.CircuitUid is not null && c.CircuitRef is null)
            throw new ArgumentException("course.circuitUid requires course.circuit.");
        if (c.VariantUid is not null && c.Variant is null)
            throw new ArgumentException("course.variantUid requires course.variant.");
        if (c.Gates is not null && c.Gates is not (CourseGates.Origin or CourseGates.Registry))
            throw new ArgumentException($"course.gates must be '{CourseGates.Origin}' or '{CourseGates.Registry}': '{c.Gates}'.");
        w.WriteStartObject();
        w.WriteString("k", "course");
        WriteGate(w, "start", c.Start);
        WriteGate(w, "finish", c.Finish);
        if (c.Splits is { Count: > 0 })
        {
            w.WriteStartArray("splits");
            foreach (var g in c.Splits) WriteGateArray(w, g);
            w.WriteEndArray();
        }
        if (c.CircuitRef is not null) w.WriteString("circuit", c.CircuitRef);
        if (c.Variant is not null) w.WriteString("variant", c.Variant);
        if (c.CircuitUid is { } cu) w.WriteString("circuitUid", cu.ToString("D"));
        if (c.VariantUid is { } vu) w.WriteString("variantUid", vu.ToString("D"));
        if (c.Gates is not null) w.WriteString("gates", c.Gates);
        w.WriteEndObject();
    }

    private static void WriteGate(Utf8JsonWriter w, string name, Gate g)
    {
        w.WritePropertyName(name);
        WriteGateArray(w, g);
    }

    private static void WriteGateArray(Utf8JsonWriter w, Gate g)
    {
        w.WriteStartArray();
        w.WriteNumberValue(Finite(g.Lat1));
        w.WriteNumberValue(Finite(g.Lon1));
        w.WriteNumberValue(Finite(g.Lat2));
        w.WriteNumberValue(Finite(g.Lon2));
        w.WriteEndArray();
    }

    private static void WriteSegment(Utf8JsonWriter w, Segment s)
    {
        w.WriteStartObject();
        w.WriteString("k", "seg");
        WriteSegmentBody(w, s);
        w.WriteEndObject();
    }

    /// <summary>Writes the fields of a segment (everything but the record kind <c>k</c>) into the current object.</summary>
    internal static void WriteSegmentBody(Utf8JsonWriter w, Segment s)
    {
        w.WriteString("kind", Required(s.Kind, "segment kind"));
        w.WriteNumber("s", s.S);
        w.WriteNumber("e", s.E);
        if (s.OfficialTimeNs is { } t) w.WriteNumber("time", t);
        WriteOptDouble(w, "tol", s.ToleranceMs);
        if (s.Splits is { Count: > 0 })
        {
            w.WriteStartArray("splits");
            foreach (var x in s.Splits) w.WriteNumberValue(x);
            w.WriteEndArray();
        }
        w.WriteString("src", Required(s.Src, "segment src"));
    }

    private static void WriteEvent(Utf8JsonWriter w, RunEvent e)
    {
        w.WriteStartObject();
        w.WriteString("k", "ev");
        w.WriteString("kind", Required(e.Kind, "event kind"));
        w.WriteNumber("s", e.S);
        if (e.E is { } end) w.WriteNumber("e", end);
        if (e.Text is not null) w.WriteString("text", e.Text);
        w.WriteEndObject();
    }

    private static void WriteEnd(Utf8JsonWriter w, EndRecord e)
    {
        w.WriteStartObject();
        w.WriteString("k", "end");
        w.WriteNumber("dur", e.DurationNs);
        w.WriteNumber("n", e.Samples);
        w.WriteEndObject();
    }

    private static void WriteOptDouble(Utf8JsonWriter w, string name, double? v)
    {
        if (v is { } d) w.WriteNumber(name, Finite(d));
    }

    /// <summary>A required string must not be null (it would be written as JSON null, which a reader rejects).</summary>
    private static string Required(string? v, string what)
        => v ?? throw new ArgumentException($"{char.ToUpperInvariant(what[0])}{what[1..]} must not be null.");

    internal static double Finite(double v)
        => double.IsFinite(v) ? v : throw new ArgumentException($"Non-finite number ({v}) cannot be written.");

    private static string KindToString(SourceKind k) => k switch
    {
        SourceKind.Real => "real",
        SourceKind.Sim => "sim",
        SourceKind.Synthetic => "synthetic",
        _ => throw new ArgumentException($"Unknown SourceKind {k}."),
    };

    // ---------- reading ----------

    internal static JsonElement Req(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? v
            : throw new FormatException($"Missing required field '{name}'.");

    internal static JsonElement? Opt(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static string? OptString(JsonElement o, string name) => Opt(o, name)?.GetString();
    private static double? OptDouble(JsonElement o, string name) => Opt(o, name)?.GetDouble();
    private static long? OptLong(JsonElement o, string name) => Opt(o, name)?.GetInt64();

    private static RunHeader ReadHeader(JsonElement r)
    {
        var kind = Req(r, "kind").GetString() switch
        {
            "real" => SourceKind.Real,
            "sim" => SourceKind.Sim,
            "synthetic" => SourceKind.Synthetic,
            var other => throw new FormatException($"Unknown source kind '{other}'."),
        };
        var files = new List<SourceFileRef>();
        if (Opt(r, "sourceFiles") is { } sf)
            foreach (var f in sf.EnumerateArray())
                files.Add(new SourceFileRef(Req(f, "name").GetString()!, Req(f, "sha256").GetString()!));
        Precision? precision = null;
        if (Opt(r, "precision") is { } pr)
            precision = new Precision(OptDouble(pr, "segTolMs"), OptDouble(pr, "gpsHz"));
        var meta = new Dictionary<string, string>();
        if (Opt(r, "meta") is { } m)
            foreach (var p in m.EnumerateObject()) meta[p.Name] = p.Value.GetString() ?? "";
        var prov = new Provenance(kind, OptString(r, "device"), ReadTool(r, "converter"), ReadTool(r, "writer"), files, precision);
        return new RunHeader(Req(r, "orf").GetString()!, Req(r, "utc").GetInt64(), OptString(r, "tz"),
            Req(r, "id").GetString()!, OptString(r, "discipline"), prov, meta);
    }

    private static ToolRef? ReadTool(JsonElement r, string name)
        => Opt(r, name) is { } t ? new ToolRef(Req(t, "id").GetString()!, Req(t, "version").GetString()!) : null;

    private static ChannelDef ReadChannel(JsonElement r)
        => new(Req(r, "id").GetString()!, Req(r, "unit").GetString()!, OptString(r, "src"), OptDouble(r, "hz"));

    private static SampleBlock ReadBlock(JsonElement r)
    {
        var v = Req(r, "v");
        var values = new double[v.GetArrayLength()];
        int i = 0;
        foreach (var x in v.EnumerateArray())
            values[i++] = x.ValueKind == JsonValueKind.Null ? double.NaN : x.GetDouble();
        long[]? times = null;
        if (Opt(r, "t") is { } t)
        {
            times = new long[t.GetArrayLength()];
            i = 0;
            foreach (var x in t.EnumerateArray()) times[i++] = x.GetInt64();
        }
        return new SampleBlock(Req(r, "ch").GetString()!, Req(r, "t0").GetInt64(), values, times);
    }

    private static Gate ReadGate(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 4)
            throw new FormatException("A gate must be an array of 4 numbers.");
        return new Gate(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble());
    }

    private static Course ReadCourse(JsonElement r)
    {
        var splits = new List<Gate>();
        if (Opt(r, "splits") is { } sp)
            foreach (var g in sp.EnumerateArray()) splits.Add(ReadGate(g));
        return new Course(ReadGate(Req(r, "start")), ReadGate(Req(r, "finish")), splits,
            OptString(r, "circuit"), OptString(r, "variant"),
            OptUuid(r, "circuitUid"), OptUuid(r, "variantUid"), OptString(r, "gates"));
    }

    private static Guid? OptUuid(JsonElement o, string name)
    {
        if (Opt(o, name) is not { } e) return null;
        if (e.ValueKind != JsonValueKind.String || !Guid.TryParseExact(e.GetString(), "D", out var g))
            throw new FormatException($"Field '{name}' is not a UUID.");
        return g;
    }

    private static Segment ReadSegment(JsonElement r) => ReadSegmentBody(r);

    /// <summary>Reads the fields of a segment from a JSON object (the <c>k</c> field is not needed).</summary>
    internal static Segment ReadSegmentBody(JsonElement r)
    {
        var splits = new List<long>();
        if (Opt(r, "splits") is { } sp)
            foreach (var x in sp.EnumerateArray()) splits.Add(x.GetInt64());
        return new Segment(Req(r, "kind").GetString()!, Req(r, "s").GetInt64(), Req(r, "e").GetInt64(),
            OptLong(r, "time"), OptDouble(r, "tol"), splits, Req(r, "src").GetString()!);
    }

    private static RunEvent ReadEvent(JsonElement r)
        => new(Req(r, "kind").GetString()!, Req(r, "s").GetInt64(), OptLong(r, "e"), OptString(r, "text"));
}
