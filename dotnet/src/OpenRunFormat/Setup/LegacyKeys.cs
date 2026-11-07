using System.Globalization;

namespace OpenRunFormat.Setup;

/// <summary>
/// Rewriting of files older than specification 1.3, whose setup keys are free text: before such a file is written as
/// 1.3 or later, every key that collides with the standard vocabulary becomes <c>x-legacy-&lt;key&gt;</c>, keeping its
/// position suffix and <c>.ref</c> companion. Values are never converted.
/// </summary>
internal static class LegacyKeys
{
    public const string Prefix = "x-legacy-";

    /// <summary>True for a major-1 version with minor below 3 (the vocabulary did not govern it).</summary>
    public static bool NeedsRename(string? orf)
    {
        if (orf is null) return false;
        var parts = orf.Split('.');
        return parts.Length >= 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            && major == 1 && minor < 3;
    }

    /// <summary><c>camber.fl</c> becomes <c>x-legacy-camber.fl</c>; a key outside the vocabulary is returned as is.</summary>
    public static string Rename(string key) =>
        SetupVocabulary.IsStandard(SetupVocabulary.BaseKeyOf(key)) ? Prefix + key : key;

    public static IReadOnlyDictionary<string, string> Rename(IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in values) result[Rename(kv.Key)] = kv.Value;
        return result;
    }

    /// <summary>
    /// Renames a parameter key. A colliding key without description (a standard key used with its standard definition)
    /// gets a <c>text</c> description named after the original key, with the scope and positions of the standard key,
    /// because a non-standard key needs a description and the old value was free text.
    /// </summary>
    public static KeyValuePair<string, ParameterDescription?> Rename(KeyValuePair<string, ParameterDescription?> parameter)
    {
        var renamed = Rename(parameter.Key);
        if (renamed == parameter.Key) return parameter;
        if (parameter.Value is { } d) return new(renamed, d);
        var standard = SetupVocabulary.Standard[parameter.Key];
        return new(renamed, new ParameterDescription(new Dictionary<string, string> { ["en"] = parameter.Key }, "text", standard.Scope)
        {
            Positions = standard.Positions,
        });
    }
}
