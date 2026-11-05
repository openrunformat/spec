namespace OpenRunFormat;

/// <summary>
/// The file is an excerpt of the run <paramref name="Id"/>: interval <c>[S, E)</c> in nanoseconds relative to time 0 of
/// that run (spec 1.2). <paramref name="Id"/> is non-empty, <c>0 &lt;= S &lt; E</c>.
/// </summary>
public sealed record DerivedFrom(string Id, long S, long E);
