using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenRunFormat.Setup;

/// <summary>
/// Reads and writes .runsetup files: indented UTF-8 JSON without BOM, with non-ASCII text written raw. Unknown fields
/// are ignored. A malformed entry of <c>parameters</c> or <c>operations</c> is ignored on its own; a malformed
/// <c>damperWays</c>, <c>composition</c> or <c>extensions</c> is ignored as a whole.
/// </summary>
public static class RunSetupFile
{
    public const string Extension = ".runsetup";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Writes the file. <c>Orf</c> is forced to <see cref="RunFormat.Version"/>; when the file is older than 1.3, keys of
    /// <c>values</c> and <c>parameters</c> that collide with the standard vocabulary are written as
    /// <c>x-legacy-&lt;key&gt;</c> (specification §8.1), without converting values. The write is atomic: a temporary file in
    /// the same directory is flushed to disk and moved over the target; on failure an existing target is untouched.
    /// </summary>
    public static void Write(string path, RunSetup setup)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(setup);
        var bytes = SerializeToUtf8(setup);
        AtomicFile.Write(path, fs => fs.Write(bytes));
    }

    /// <summary>The JSON text of a file, with <c>orf</c> forced to <see cref="RunFormat.Version"/> (keys renamed as in <see cref="Write"/>).</summary>
    public static string Serialize(RunSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return Encoding.UTF8.GetString(SerializeToUtf8(setup));
    }

    private static byte[] SerializeToUtf8(RunSetup s)
    {
        if (LegacyKeys.NeedsRename(s.Orf))
            s = s with
            {
                Parameters = s.Parameters.Select(LegacyKeys.Rename).ToList(),
                Values = s.Values is null ? null : LegacyKeys.Rename(s.Values),
            };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("orf", RunFormat.Version);
            w.WriteString("kind", KindText(s.Kind));
            if (s.Id is not null) w.WriteString("id", s.Id);
            if (s.Version is { } version) w.WriteNumber("version", version);
            if (s.AppliesTo.Count > 0)
            {
                w.WriteStartArray("appliesTo");
                foreach (var a in s.AppliesTo) w.WriteStringValue(a);
                w.WriteEndArray();
            }
            WriteStrings(w, "name", s.Name);
            if (s.Vehicle.Count > 0) WriteStrings(w, "vehicle", s.Vehicle);
            if (s.Parameters.Count > 0)
            {
                w.WriteStartObject("parameters");
                foreach (var kv in s.Parameters)
                {
                    w.WritePropertyName(kv.Key);
                    if (kv.Value is null) { w.WriteStartObject(); w.WriteEndObject(); }
                    else ParameterJson.Write(w, kv.Value);
                }
                w.WriteEndObject();
            }
            if (s.Values is not null) WriteStrings(w, "values", s.Values);
            if (s.DamperWays is not null)
            {
                w.WritePropertyName("damperWays");
                WriteWays(w, s.DamperWays);
            }
            if (s.Operations.Count > 0)
            {
                w.WriteStartArray("operations");
                foreach (var op in s.Operations) WriteOperation(w, op);
                w.WriteEndArray();
            }
            if (s.Composition is not null)
            {
                w.WriteStartObject("composition");
                w.WritePropertyName("base");
                WriteRef(w, s.Composition.Base);
                w.WriteStartArray("modifications");
                foreach (var m in s.Composition.Modifications) WriteRef(w, m);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            if (s.Extensions.Count > 0)
            {
                w.WriteStartObject("extensions");
                foreach (var kv in s.Extensions)
                {
                    w.WritePropertyName(kv.Key);
                    kv.Value.WriteTo(w);
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static string KindText(RunSetupKind kind) => kind switch
    {
        RunSetupKind.Template => "template",
        RunSetupKind.Modification => "modification",
        _ => "setup",
    };

    private static void WriteStrings(Utf8JsonWriter w, string property, IReadOnlyDictionary<string, string> values)
    {
        w.WriteStartObject(property);
        foreach (var kv in values) w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
    }

    private static void WriteWays(Utf8JsonWriter w, DamperWays ways)
    {
        w.WriteStartObject();
        w.WriteNumber("front", ways.Front);
        w.WriteNumber("rear", ways.Rear);
        w.WriteEndObject();
    }

    private static void WriteRef(Utf8JsonWriter w, SetupLayerRef r)
    {
        w.WriteStartObject();
        w.WriteString("id", r.Id);
        w.WriteNumber("version", r.Version);
        w.WriteEndObject();
    }

    private static void WriteOperation(Utf8JsonWriter w, SetupOperation op)
    {
        w.WriteStartObject();
        w.WriteString("op", op.Op switch
        {
            SetupOperationKind.Add => "add",
            SetupOperationKind.Change => "change",
            SetupOperationKind.Remove => "remove",
            _ => "damperWays",
        });
        if (op.Key is not null) w.WriteString("key", op.Key);
        if (op.Description is not null && op.Op is SetupOperationKind.Add or SetupOperationKind.Change)
        {
            w.WritePropertyName("description");
            ParameterJson.Write(w, op.Description);
        }
        if (op.Ways is not null && op.Op == SetupOperationKind.DamperWays)
        {
            w.WritePropertyName("ways");
            WriteWays(w, op.Ways);
        }
        w.WriteEndObject();
    }

    /// <summary>Reads a file.</summary>
    /// <exception cref="FormatException">See <see cref="Parse"/>.</exception>
    public static RunSetup Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>Parses the JSON text of a file.</summary>
    /// <exception cref="FormatException">
    /// Broken JSON; <c>orf</c> missing or with a major version other than 1; <c>kind</c> missing or unknown; a template or
    /// modification without <c>id</c> or <c>version</c>; a setup without <c>values</c>; <c>name</c> without any language;
    /// <c>vehicle</c>, <c>values</c> or <c>appliesTo</c> with something other than strings.
    /// </exception>
    public static RunSetup Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    private static RunSetup Parse(byte[] utf8)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8); }
        catch (JsonException ex) { throw new FormatException("Invalid setup file: " + ex.Message, ex); }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) throw new FormatException("The setup file is not a JSON object.");

            if (!r.TryGetProperty("orf", out var orfEl) || orfEl.ValueKind != JsonValueKind.String)
                throw new FormatException("The setup file has no 'orf' version.");
            var orf = orfEl.GetString()!;
            if (!int.TryParse(orf.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) || major != 1)
                throw new FormatException($"Unsupported format version '{orf}'.");

            if (!r.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
                throw new FormatException("The setup file has no 'kind'.");
            var kind = kindEl.GetString() switch
            {
                "template" => RunSetupKind.Template,
                "modification" => RunSetupKind.Modification,
                "setup" => RunSetupKind.Setup,
                var k => throw new FormatException($"Unknown setup file kind '{k}'."),
            };

            string? id = null;
            if (r.TryGetProperty("id", out var idEl))
            {
                if (idEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(idEl.GetString()))
                    throw new FormatException("'id' must be a non-empty string.");
                id = idEl.GetString();
            }
            int? version = null;
            if (r.TryGetProperty("version", out var vEl))
            {
                if (vEl.ValueKind != JsonValueKind.Number || !vEl.TryGetInt32(out var v) || v < 1)
                    throw new FormatException("'version' must be a positive integer.");
                version = v;
            }
            if (kind != RunSetupKind.Setup && (id is null || version is null))
                throw new FormatException("A template or modification needs 'id' and 'version'.");

            if (!r.TryGetProperty("name", out var nameEl) || ReadStrings(nameEl, "name") is not { Count: > 0 } name)
                throw new FormatException("'name' needs at least one language.");

            IReadOnlyDictionary<string, string>? values = null;
            if (r.TryGetProperty("values", out var valuesEl)) values = ReadStrings(valuesEl, "values");
            if (kind == RunSetupKind.Setup && values is null) throw new FormatException("A setup needs 'values'.");

            var vehicle = r.TryGetProperty("vehicle", out var vehEl)
                ? ReadStrings(vehEl, "vehicle")
                : new Dictionary<string, string>();

            var appliesTo = new List<string>();
            if (r.TryGetProperty("appliesTo", out var atEl))
            {
                if (atEl.ValueKind != JsonValueKind.Array) throw new FormatException("'appliesTo' must be an array.");
                foreach (var a in atEl.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(a.GetString()))
                        throw new FormatException("'appliesTo' must hold non-empty strings.");
                    appliesTo.Add(a.GetString()!);
                }
            }

            return new RunSetup(orf, kind, name)
            {
                Id = id,
                Version = version,
                AppliesTo = appliesTo,
                Vehicle = vehicle,
                Parameters = ReadParameters(r),
                Values = values,
                DamperWays = r.TryGetProperty("damperWays", out var dwEl) ? ReadWays(dwEl) : null,
                Operations = ReadOperations(r),
                Composition = r.TryGetProperty("composition", out var cEl) ? ReadComposition(cEl) : null,
                Extensions = ReadExtensions(r),
            };
        }
    }

    private static Dictionary<string, string> ReadStrings(JsonElement e, string property)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new FormatException($"'{property}' must be an object.");
        var result = new Dictionary<string, string>();
        foreach (var p in e.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.String) throw new FormatException($"'{property}' must hold strings.");
            result[p.Name] = p.Value.GetString()!;
        }
        return result;
    }

    // `{}` or null is a standard key without description; anything else must be a valid description. A key outside the
    // standard vocabulary needs a description. Repeated keys keep the first position and the last description.
    private static List<KeyValuePair<string, ParameterDescription?>> ReadParameters(JsonElement root)
    {
        var result = new List<KeyValuePair<string, ParameterDescription?>>();
        if (!root.TryGetProperty("parameters", out var obj) || obj.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Name.Length == 0) continue;
            ParameterDescription? description;
            if (p.Value.ValueKind == JsonValueKind.Null
                || (p.Value.ValueKind == JsonValueKind.Object && !p.Value.EnumerateObject().Any()))
            {
                if (!SetupVocabulary.IsStandard(p.Name)) continue;
                description = null;
            }
            else if (ParameterJson.TryRead(p.Value) is { } d) description = d;
            else continue;

            var at = result.FindIndex(x => x.Key == p.Name);
            if (at >= 0) result[at] = new(p.Name, description);
            else result.Add(new(p.Name, description));
        }
        return result;
    }

    private static List<SetupOperation> ReadOperations(JsonElement root)
    {
        var result = new List<SetupOperation>();
        if (!root.TryGetProperty("operations", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        foreach (var e in arr.EnumerateArray())
            if (ReadOperation(e) is { } op) result.Add(op);
        return result;
    }

    private static SetupOperation? ReadOperation(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("op", out var opEl) || opEl.ValueKind != JsonValueKind.String)
            return null;
        SetupOperationKind kind;
        switch (opEl.GetString())
        {
            case "add": kind = SetupOperationKind.Add; break;
            case "change": kind = SetupOperationKind.Change; break;
            case "remove": kind = SetupOperationKind.Remove; break;
            case "damperWays":
                return e.TryGetProperty("ways", out var wEl) && ReadWays(wEl) is { } ways
                    ? new SetupOperation(SetupOperationKind.DamperWays, Ways: ways)
                    : null;
            default: return null;
        }

        if (!e.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(keyEl.GetString()))
            return null;
        var key = keyEl.GetString()!;
        if (kind == SetupOperationKind.Remove) return new SetupOperation(kind, key);

        ParameterDescription? description = null;
        if (e.TryGetProperty("description", out var dEl) && dEl.ValueKind != JsonValueKind.Null)
        {
            description = ParameterJson.TryRead(dEl);
            if (description is null) return null;
        }
        if (description is null && !SetupVocabulary.IsStandard(key)) return null;
        return new SetupOperation(kind, key, description);
    }

    private static DamperWays? ReadWays(JsonElement e)
    {
        static bool Ways(JsonElement o, string name, out int value)
        {
            value = 0;
            return o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                && v.TryGetInt32(out value) && value is >= 0 and <= 4;
        }
        return e.ValueKind == JsonValueKind.Object && Ways(e, "front", out var front) && Ways(e, "rear", out var rear)
            ? new DamperWays(front, rear)
            : null;
    }

    private static SetupComposition? ReadComposition(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("base", out var bEl) || ReadRef(bEl) is not { } baseRef)
            return null;
        var mods = new List<SetupLayerRef>();
        if (e.TryGetProperty("modifications", out var mEl))
        {
            if (mEl.ValueKind != JsonValueKind.Array) return null;
            foreach (var m in mEl.EnumerateArray())
            {
                if (ReadRef(m) is not { } r) return null;
                mods.Add(r);
            }
        }
        return new SetupComposition(baseRef, mods);
    }

    private static SetupLayerRef? ReadRef(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(idEl.GetString())
        && e.TryGetProperty("version", out var vEl) && vEl.ValueKind == JsonValueKind.Number && vEl.TryGetInt32(out var v) && v >= 1
            ? new SetupLayerRef(idEl.GetString()!, v)
            : null;

    private static Dictionary<string, JsonElement> ReadExtensions(JsonElement root)
    {
        var result = new Dictionary<string, JsonElement>();
        if (!root.TryGetProperty("extensions", out var obj) || obj.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in obj.EnumerateObject()) result[p.Name] = p.Value.Clone();
        return result;
    }
}
