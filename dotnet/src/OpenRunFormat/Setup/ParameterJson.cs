using System.Text.Json;

namespace OpenRunFormat.Setup;

/// <summary>Tolerant JSON reading and writing of a single <see cref="ParameterDescription"/>.</summary>
internal static class ParameterJson
{
    private static readonly string[] Types = ["text", "bool", "choice", "integer", "decimal"];

    public static void Write(Utf8JsonWriter w, ParameterDescription p)
    {
        w.WriteStartObject();
        WriteNames(w, "name", p.Name);
        w.WriteString("type", p.Type);
        w.WriteString("scope", p.Scope switch { SetupScope.Axle => "axle", SetupScope.Wheel => "wheel", _ => "single" });
        if (p.Unit is not null) w.WriteString("unit", p.Unit);
        if (p.Choices.Count > 0)
        {
            w.WriteStartArray("choices");
            foreach (var c in p.Choices)
            {
                w.WriteStartObject();
                w.WriteString("id", c.Id);
                WriteNames(w, "name", c.Name);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (p.AllowOther) w.WriteBoolean("allowOther", true);
        if (p.CatalogKind is not null) w.WriteString("catalogKind", p.CatalogKind);
        if (p.Min is { } min) w.WriteNumber("min", min);
        if (p.Max is { } max) w.WriteNumber("max", max);
        if (p.Precision is { } prec) w.WriteNumber("precision", prec);
        if (p.Group is not null) w.WriteString("group", p.Group);
        if (p.Personal) w.WriteBoolean("personal", true);
        if (p.Positions is not null)
        {
            w.WriteStartArray("positions");
            foreach (var pos in p.Positions) w.WriteStringValue(pos);
            w.WriteEndArray();
        }
        if (p.ClickReference is not null) w.WriteString("clickReference", p.ClickReference);
        w.WriteEndObject();
    }

    private static void WriteNames(Utf8JsonWriter w, string property, IReadOnlyDictionary<string, string> names)
    {
        w.WriteStartObject(property);
        foreach (var kv in names) w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
    }

    /// <summary>Reads a description; null when it is malformed (anything wrong makes the whole entry unusable).</summary>
    public static ParameterDescription? TryRead(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty("name", out var nameEl) || ReadNames(nameEl) is not { } name) return null;
        if (!Str(e, "type", out var type) || type is null || Array.IndexOf(Types, type) < 0) return null;
        if (!Str(e, "scope", out var scopeText)) return null;  // absent or null falls to the default below
        SetupScope scope;
        switch (scopeText)
        {
            case "single": scope = SetupScope.Single; break;
            case "axle": scope = SetupScope.Axle; break;
            case "wheel": scope = SetupScope.Wheel; break;
            default: return null;
        }

        var choices = new List<ParameterChoice>();
        if (e.TryGetProperty("choices", out var chEl))
        {
            if (chEl.ValueKind != JsonValueKind.Array) return null;
            foreach (var c in chEl.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object || !Str(c, "id", out var id) || string.IsNullOrEmpty(id)
                    || !c.TryGetProperty("name", out var cn) || ReadNames(cn) is not { } cname) return null;
                choices.Add(new ParameterChoice(id, cname));
            }
        }
        if (type == "choice" && choices.Count == 0) return null;

        if (!Str(e, "unit", out var unit) || !Str(e, "catalogKind", out var catalogKind)
            || !Str(e, "group", out var group) || !Str(e, "clickReference", out var click)
            || !Bool(e, "allowOther", out var allowOther) || !Bool(e, "personal", out var personal)
            || !Num(e, "min", out var min) || !Num(e, "max", out var max)) return null;
        if (min > max) return null;

        int? precision = null;
        if (e.TryGetProperty("precision", out var pEl))
        {
            if (pEl.ValueKind != JsonValueKind.Number || !pEl.TryGetInt32(out var pr) || pr < 0 || type != "decimal") return null;
            precision = pr;
        }

        List<string>? positions = null;
        if (e.TryGetProperty("positions", out var posEl))
        {
            if (posEl.ValueKind != JsonValueKind.Array) return null;
            positions = [];
            foreach (var x in posEl.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(x.GetString())) return null;
                positions.Add(x.GetString()!);
            }
        }

        return new ParameterDescription(name, type, scope)
        {
            Unit = unit, Choices = choices, AllowOther = allowOther, CatalogKind = catalogKind, Min = min, Max = max,
            Precision = precision, Group = group, Personal = personal, Positions = positions, ClickReference = click,
        };
    }

    private static Dictionary<string, string>? ReadNames(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, string>();
        foreach (var p in e.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.String) return null;
            result[p.Name] = p.Value.GetString()!;
        }
        return result.Count == 0 ? null : result;
    }

    // Absent => ok with null; present with the wrong kind => false.
    private static bool Str(JsonElement e, string name, out string? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v)) return true;
        if (v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString();
        return true;
    }

    private static bool Bool(JsonElement e, string name, out bool value)
    {
        value = false;
        if (!e.TryGetProperty(name, out var v)) return true;
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = v.GetBoolean();
        return true;
    }

    private static bool Num(JsonElement e, string name, out double? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v)) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d)) return false;
        value = d;
        return true;
    }
}
