using System.Globalization;

namespace OpenRunFormat;

/// <summary>Constants of the Open Run Format specification implemented by this library.</summary>
public static class RunFormat
{
    /// <summary>Specification version written to the header's <c>orf</c> field.</summary>
    public const string Version = "1.3";

    /// <summary>
    /// The version a writer emits when it rewrites a companion or setup file of version <paramref name="orf"/>: the
    /// higher of the two, so a rewrite never lowers the version. A version that is not <c>1.y</c> gives <see cref="Version"/>.
    /// </summary>
    internal static string RewriteVersion(string? orf) =>
        Minor(orf) is { } minor && minor > Minor(Version) ? orf! : Version;

    private static int? Minor(string? orf)
    {
        var parts = orf?.Split('.');
        return parts is { Length: 2 }
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) && major == 1
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            ? minor
            : null;
    }
}
