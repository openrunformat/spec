namespace OpenRunFormat.Setup;

/// <summary>An option of a <c>choice</c> parameter: a stable identifier and its name per language.</summary>
public sealed record ParameterChoice(string Id, IReadOnlyDictionary<string, string> Name);

/// <summary>Ids of the standard parameter groups (the optional <c>group</c> of a <see cref="ParameterDescription"/>).</summary>
public static class ParameterGroups
{
    public const string Tyres = "tyres";
    public const string Geometry = "geometry";
    public const string Suspension = "suspension";
    public const string Aero = "aero";
    public const string Brakes = "brakes";
    public const string Electronics = "electronics";
    public const string Mass = "mass";
    public const string Other = "other";
}

/// <summary>
/// Describes a setup parameter that is not part of the standard vocabulary (see the <c>parameters</c> field of the
/// .runmeta). <paramref name="Name"/> maps a language tag to a text; <paramref name="Type"/> is one of
/// <c>text</c>, <c>bool</c>, <c>choice</c>, <c>integer</c> or <c>decimal</c>.
/// </summary>
public sealed record ParameterDescription(IReadOnlyDictionary<string, string> Name, string Type, SetupScope Scope)
{
    /// <summary>Unit of the value (SI for numeric values), when it has one.</summary>
    public string? Unit { get; init; }

    /// <summary>Options of a <c>choice</c> parameter.</summary>
    public IReadOnlyList<ParameterChoice> Choices { get; init; } = [];

    /// <summary>For a <c>choice</c>, whether free text is accepted besides the options.</summary>
    public bool AllowOther { get; init; }

    /// <summary>For a <c>text</c>, the kind of public catalog its value may link to through a <c>.ref</c> companion.</summary>
    public string? CatalogKind { get; init; }

    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>Number of decimals to show for a <c>decimal</c>.</summary>
    public int? Precision { get; init; }

    /// <summary>Group id, normally one of <see cref="ParameterGroups"/>.</summary>
    public string? Group { get; init; }

    /// <summary>True when the value is personal data of the person who set it up.</summary>
    public bool Personal { get; init; }

    /// <summary>Position suffixes the parameter uses (for example <c>fl</c>, <c>fr</c>), when not all of its scope.</summary>
    public IReadOnlyList<string>? Positions { get; init; }

    /// <summary>For dampers: <c>from_hard</c> or <c>from_soft</c>.</summary>
    public string? ClickReference { get; init; }
}
