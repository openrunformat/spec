using OpenRunFormat.Setup;

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

/// <summary>State of the track surface (specification 1.4). <see cref="Damp"/> lies between dry and wet: wet patches, or a track drying or getting wet.</summary>
public enum TrackState { Dry, Damp, Wet }

/// <summary>Weather in the sky (specification 1.4), independent of the light.</summary>
public enum Weather { Clear, Cloudy, Rain, Snow, Fog }

/// <summary>Natural light (specification 1.4), independent of the weather.</summary>
public enum Light { Day, Dusk, Night }

/// <summary>
/// Where the conditions of an interval come from (specification 1.4): <see cref="Declared"/> by the driver or user,
/// <see cref="Measured"/> by a sensor, <see cref="Estimated"/> by a weather service or model.
/// </summary>
public enum ConditionSource { Declared, Measured, Estimated }

/// <summary>
/// The conditions from <paramref name="S"/> to <paramref name="E"/> (absolute Unix epoch nanoseconds, half-open).
/// <paramref name="AirTemp"/> and <paramref name="TrackTemp"/> are in °C. <paramref name="Source"/> is required, and
/// at least one of <paramref name="Track"/>, <paramref name="Weather"/>, <paramref name="Light"/>,
/// <paramref name="AirTemp"/> and <paramref name="TrackTemp"/> must be set.
/// </summary>
public sealed record ConditionInterval(long S, long E, ConditionSource Source, TrackState? Track = null,
    Weather? Weather = null, Light? Light = null, double? AirTemp = null, double? TrackTemp = null)
{
    /// <summary>True when at least one condition (track, weather, light or a temperature) is set.</summary>
    public bool HasValues => Track is not null || Weather is not null || Light is not null || AirTemp is not null || TrackTemp is not null;
}

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
    /// Descriptions of the setup parameters outside the standard vocabulary, by base key (specification 1.3).
    /// Empty when the file has none.
    /// </summary>
    public IReadOnlyDictionary<string, ParameterDescription> Parameters { get; init; } = new Dictionary<string, ParameterDescription>();

    /// <summary>
    /// Condition intervals (track, weather, light, temperatures) in absolute epoch nanoseconds; non-overlapping
    /// (specification 1.4). Empty when the file has none or the field is malformed.
    /// </summary>
    public IReadOnlyList<ConditionInterval> Conditions { get; init; } = [];

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
