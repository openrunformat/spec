using System.Diagnostics.CodeAnalysis;

namespace OpenRunFormat;

/// <summary>
/// A circuit identifier, optionally followed by a layout variant: <c>ES-catalunya/gp@2023</c>.
/// Identifiers are never normalised: anything that does not match the grammar exactly is rejected.
/// </summary>
public sealed record CircuitId
{
    private const string UriPrefix = "orf:circuit/";
    private const string ProvisionalPrefix = "local-";

    private CircuitId(string circuit, string? country, string? name, Guid? uuid, string? variant)
    {
        Circuit = circuit;
        Country = country;
        Name = name;
        ProvisionalUuid = uuid;
        Variant = variant;
        if (variant is not null)
        {
            var at = variant.IndexOf('@');
            Layout = at < 0 ? variant : variant[..at];
            LayoutVersion = at < 0 ? null : variant[(at + 1)..];
        }
    }

    /// <summary>The circuit part, e.g. <c>ES-catalunya</c> or <c>local-&lt;uuid&gt;</c>.</summary>
    public string Circuit { get; }
    /// <summary>Two-letter upper-case country code; null for a provisional identifier.</summary>
    public string? Country { get; }
    /// <summary>Circuit name within the country; null for a provisional identifier.</summary>
    public string? Name { get; }
    /// <summary>The UUID of a provisional identifier.</summary>
    public Guid? ProvisionalUuid { get; }
    public bool IsProvisional => ProvisionalUuid is not null;
    /// <summary>Layout name, e.g. <c>gp</c>.</summary>
    public string? Layout { get; }
    /// <summary>Layout version, e.g. <c>2023</c> or <c>2023-04</c>.</summary>
    public string? LayoutVersion { get; }
    /// <summary>The variant as written: <c>gp</c> or <c>gp@2023</c>.</summary>
    public string? Variant { get; }

    public override string ToString() => Variant is null ? Circuit : Circuit + "/" + Variant;

    /// <summary>The URI form, <c>orf:circuit/ES-catalunya/gp@2023</c>.</summary>
    public string ToUri() => UriPrefix + ToString();

    public static bool TryParse(string? text, [NotNullWhen(true)] out CircuitId? id)
    {
        id = null;
        if (string.IsNullOrEmpty(text)) return false;
        var slash = text.IndexOf('/');
        string circuit = text;
        string? variant = null;
        if (slash >= 0)
        {
            circuit = text[..slash];
            variant = text[(slash + 1)..];
            if (!IsValidVariant(variant)) return false;
        }
        if (!TryParseCircuit(circuit, out var country, out var name, out var uuid)) return false;
        id = new CircuitId(circuit, country, name, uuid, variant);
        return true;
    }

    public static CircuitId Parse(string text) =>
        TryParse(text, out var id) ? id : throw new FormatException($"Not a valid circuit identifier: '{text}'.");

    public static bool TryParseUri(string? uri, [NotNullWhen(true)] out CircuitId? id)
    {
        id = null;
        return uri is not null && uri.StartsWith(UriPrefix, StringComparison.Ordinal)
            && TryParse(uri[UriPrefix.Length..], out id);
    }

    /// <summary>True when <paramref name="text"/> is a valid circuit part (no variant).</summary>
    public static bool IsValidCircuit(string? text) => text is not null && TryParseCircuit(text, out _, out _, out _);

    /// <summary>True when <paramref name="text"/> is a valid variant, e.g. <c>gp</c> or <c>gp@2023</c>.</summary>
    public static bool IsValidVariant(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var at = text.IndexOf('@');
        return at < 0 ? IsName(text) : IsName(text.AsSpan(0, at)) && IsVersion(text.AsSpan(at + 1));
    }

    public static bool TryFromCourse(Course course, [NotNullWhen(true)] out CircuitId? id)
    {
        ArgumentNullException.ThrowIfNull(course);
        id = null;
        if (!IsValidCircuit(course.CircuitRef)) return false;
        if (course.Variant is not null && !IsValidVariant(course.Variant)) return false;
        return TryParse(course.Variant is null ? course.CircuitRef : course.CircuitRef + "/" + course.Variant, out id);
    }

    /// <summary>Creates a provisional identifier <c>local-&lt;uuid v7&gt;</c> for a circuit not yet in the registry.</summary>
    public static CircuitId NewProvisional()
    {
        var uuid = Guid.CreateVersion7();
        return new CircuitId(ProvisionalPrefix + uuid.ToString("D"), null, null, uuid, null);
    }

    private static bool TryParseCircuit(string s, out string? country, out string? name, out Guid? uuid)
    {
        country = name = null;
        uuid = null;
        if (s.StartsWith(ProvisionalPrefix, StringComparison.Ordinal))
        {
            var rest = s[ProvisionalPrefix.Length..];
            if (rest.Length != 36 || !Guid.TryParseExact(rest, "D", out var g)) return false;
            if (g.ToString("D") != rest) return false; // canonical lower-case only
            uuid = g;
            return true;
        }
        if (s.Length < 5 || s[0] is < 'A' or > 'Z' || s[1] is < 'A' or > 'Z' || s[2] != '-') return false;
        if (!IsName(s.AsSpan(3))) return false;
        country = s[..2];
        name = s[3..];
        return true;
    }

    // 2..32 of [a-z0-9-], starting and ending with a letter or digit, no "--".
    private static bool IsName(ReadOnlySpan<char> s)
    {
        if (s.Length is < 2 or > 32) return false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var alnum = c is >= 'a' and <= 'z' || c is >= '0' and <= '9';
            if (alnum) continue;
            if (c != '-' || i == 0 || i == s.Length - 1 || s[i - 1] == '-') return false;
        }
        return true;
    }

    // 4DIGIT [ "-" ( "0" 1-9 / "1" 0-2 ) ]
    private static bool IsVersion(ReadOnlySpan<char> s)
    {
        if (s.Length != 4 && s.Length != 7) return false;
        for (var i = 0; i < 4; i++) if (s[i] is < '0' or > '9') return false;
        if (s.Length == 4) return true;
        if (s[4] != '-') return false;
        return (s[5] == '0' && s[6] is >= '1' and <= '9') || (s[5] == '1' && s[6] is >= '0' and <= '2');
    }
}
