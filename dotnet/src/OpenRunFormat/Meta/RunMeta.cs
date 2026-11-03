namespace OpenRunFormat.Meta;

/// <summary>
/// A setup (vehicle or equipment configuration) that applies from <paramref name="S"/> to <paramref name="E"/>.
/// The instants are absolute Unix epoch nanoseconds (unlike the relative times inside a .runlog file).
/// </summary>
public sealed record SetupInterval(long S, long E, IReadOnlyDictionary<string, string> Values);

/// <summary>A free-text note attached to an absolute instant. <paramref name="T"/> is Unix epoch nanoseconds.</summary>
public sealed record Note(long T, string Text);

/// <summary>
/// The driver at the wheel from <paramref name="S"/> to <paramref name="E"/> (absolute Unix epoch nanoseconds,
/// half-open). <paramref name="Id"/> is an opaque identifier chosen by the writer; <paramref name="Initials"/> is optional.
/// </summary>
public sealed record DriverInterval(long S, long E, string Id, string Name, string? Initials = null);

/// <summary>
/// The companion file (.runmeta) of a .runlog: user-owned data that is not part of the telemetry itself.
/// <para>
/// The instants are absolute Unix epoch nanoseconds, including <c>S</c>, <c>E</c> and <c>Splits</c> of
/// <see cref="UserSegments"/>, which differ from the relative file times used by the <c>seg</c> records of the .runlog
/// file. The official time (<c>OfficialTimeNs</c>) of a user segment stays a duration, as in a .runlog.
/// </para>
/// <para>
/// <see cref="RunlogSha256"/> is the lowercase hex SHA-256 of the .runlog file the companion was made for; use
/// <see cref="Matches"/> to check that the two still belong together.
/// </para>
/// </summary>
/// <param name="Orf">Specification version of the companion file.</param>
/// <param name="Id">Run identifier, equal to the <c>id</c> of the header of the .runlog file.</param>
/// <param name="RunlogSha256">SHA-256 of the whole .runlog file, as lowercase hex.</param>
/// <param name="Vehicle">Free-form vehicle description.</param>
/// <param name="Setup">Setup intervals in absolute epoch nanoseconds.</param>
/// <param name="Notes">Notes in absolute epoch nanoseconds.</param>
/// <param name="UserSegments">User-defined segments; their instants are absolute epoch nanoseconds, their official time a duration.</param>
/// <param name="Drivers">Driver intervals in absolute epoch nanoseconds; non-overlapping. Never null (empty when absent).</param>
public sealed record RunMeta(string Orf, string Id, string RunlogSha256,
    IReadOnlyDictionary<string, string> Vehicle, IReadOnlyList<SetupInterval> Setup,
    IReadOnlyList<Note> Notes, IReadOnlyList<Segment> UserSegments, IReadOnlyList<DriverInterval>? Drivers = null)
{
    /// <summary>Driver intervals; empty when the file has none.</summary>
    public IReadOnlyList<DriverInterval> Drivers { get; init; } = Drivers ?? [];

    /// <summary>
    /// True when this companion was made for the given .runlog: the run <c>Id</c> must match exactly and the
    /// hash must match case-insensitively.
    /// </summary>
    public bool Matches(RunHeader header, string runlogSha256)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(runlogSha256);
        return string.Equals(Id, header.Id, StringComparison.Ordinal)
            && string.Equals(RunlogSha256, runlogSha256, StringComparison.OrdinalIgnoreCase);
    }
}
