namespace OpenRunFormat;

/// <summary>
/// A whole .runlog file in memory. <see cref="End"/> is null when the file was not closed properly.
/// <see cref="HasTrailingContent"/> is true when at least one non-empty line or byte followed the first <c>end</c>
/// record; readers ignore that content, and applications SHOULD warn the user about it.
/// </summary>
public sealed record RunDocument(
    RunHeader Header,
    IReadOnlyDictionary<string, ChannelData> Channels,
    Course? Course,
    IReadOnlyList<Segment> Segments,
    IReadOnlyList<RunEvent> Events,
    EndRecord? End,
    bool HasTrailingContent = false)
{
    /// <summary>True when the file ends with an <c>end</c> record.</summary>
    public bool Complete => End is not null;
}
