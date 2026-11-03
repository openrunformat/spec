namespace OpenRunFormat;

/// <summary>A timing gate defined by two geographic points.</summary>
public sealed record Gate(double Lat1, double Lon1, double Lat2, double Lon2);

/// <summary>Start, finish and intermediate gates of a course.</summary>
/// <remarks>
/// <paramref name="CircuitRef"/> and <paramref name="Variant"/> are circuit identifiers (see <see cref="CircuitId"/>);
/// a reader keeps any string it finds, the writer accepts only valid identifiers.
/// <paramref name="Gates"/> says where the gate coordinates came from (see <see cref="CourseGates"/>).
/// </remarks>
public sealed record Course(Gate Start, Gate Finish, IReadOnlyList<Gate> Splits, string? CircuitRef, string? Variant,
    Guid? CircuitUid = null, Guid? VariantUid = null, string? Gates = null);

/// <summary>Values the writer accepts for <c>course.gates</c>.</summary>
public static class CourseGates
{
    /// <summary>The gates were measured or declared by whoever recorded the file.</summary>
    public const string Origin = "origin";
    /// <summary>The gates were taken from a circuit registry entry.</summary>
    public const string Registry = "registry";
}
