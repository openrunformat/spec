using System.Text.Json;

namespace OpenRunFormat.Setup;

/// <summary>Kind of a .runsetup file (JSON <c>template</c>, <c>modification</c> or <c>setup</c>).</summary>
public enum RunSetupKind { Template, Modification, Setup }

/// <summary>Number of damping adjustment ways (0 to 4) on the front and rear axles.</summary>
public sealed record DamperWays(int Front, int Rear);

/// <summary>Reference to a published template or modification: its <c>id</c> and <c>version</c>.</summary>
public sealed record SetupLayerRef(string Id, int Version);

/// <summary>The layers a <c>setup</c> was built from: one base template and the modifications applied in order.</summary>
public sealed record SetupComposition(SetupLayerRef Base, IReadOnlyList<SetupLayerRef> Modifications);

/// <summary>Kind of an operation of a <c>modification</c>.</summary>
public enum SetupOperationKind { Add, Change, Remove, DamperWays }

/// <summary>
/// An operation of a <c>modification</c>. <c>Add</c>, <c>Change</c> and <c>Remove</c> act on the base key
/// <paramref name="Key"/>; <paramref name="Description"/> (for <c>Add</c> and <c>Change</c>) is required for a key that is
/// not in the standard vocabulary. <c>DamperWays</c> replaces the damper ways with <paramref name="Ways"/>.
/// </summary>
public sealed record SetupOperation(
    SetupOperationKind Op,
    string? Key = null,
    ParameterDescription? Description = null,
    DamperWays? Ways = null);

/// <summary>
/// A .runsetup file: a template, a modification or a complete setup, independent of any .runlog.
/// <para>
/// <see cref="Id"/> and <see cref="Version"/> are required for templates and modifications; <see cref="Values"/> is
/// required for setups. <see cref="Parameters"/> keeps the order of the file; a null description means a standard key
/// used with its standard definition.
/// </para>
/// </summary>
/// <param name="Orf">Specification version of the file.</param>
/// <param name="Kind">Kind of file.</param>
/// <param name="Name">Name per language tag; at least one.</param>
public sealed record RunSetup(string Orf, RunSetupKind Kind, IReadOnlyDictionary<string, string> Name)
{
    public string? Id { get; init; }

    public int? Version { get; init; }

    /// <summary>Registry model ids the file is meant for; empty for a generic one.</summary>
    public IReadOnlyList<string> AppliesTo { get; init; } = [];

    /// <summary>Free-form vehicle description, without personal data unless the person agreed.</summary>
    public IReadOnlyDictionary<string, string> Vehicle { get; init; } = new Dictionary<string, string>();

    /// <summary>Parameters by base key, in order; a null description is a standard key without description.</summary>
    public IReadOnlyList<KeyValuePair<string, ParameterDescription?>> Parameters { get; init; } = [];

    /// <summary>Setup values (same keys and rules as the <c>setup</c> values of a .runmeta). Required in a <c>setup</c>.</summary>
    public IReadOnlyDictionary<string, string>? Values { get; init; }

    public DamperWays? DamperWays { get; init; }

    /// <summary>Operations of a <c>modification</c>, applied in order.</summary>
    public IReadOnlyList<SetupOperation> Operations { get; init; } = [];

    /// <summary>For a <c>setup</c>, the layers it was built from.</summary>
    public SetupComposition? Composition { get; init; }

    /// <summary>Application data, by application key (for example <c>x-example</c>); kept as is.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } = new Dictionary<string, JsonElement>();
}
