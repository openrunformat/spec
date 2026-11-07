using System.Globalization;

namespace OpenRunFormat.Setup;

/// <summary>Scope of a setup parameter: one value, one per axle, or one per wheel.</summary>
public enum SetupScope { Single, Axle, Wheel }

/// <summary>A parameter of the standard setup vocabulary (specification 1.3).</summary>
/// <param name="Key">Base key, without position or <c>.ref</c> suffix.</param>
/// <param name="Scope">Scope of the parameter.</param>
/// <param name="Unit">SI unit symbol, or null when it has none.</param>
/// <param name="Type">One of <c>text|bool|choice|integer|decimal</c>.</param>
/// <param name="Positions">Restricts the positions that apply (for example front wheels only).</param>
/// <param name="Personal">True for personal data, to be written only with consent.</param>
/// <param name="Choices">Options of a <c>choice</c> key, with English names; null for other types.</param>
public sealed record StandardParameter(
    string Key,
    SetupScope Scope,
    string? Unit,
    string Type,
    IReadOnlyList<string>? Positions = null,
    bool Personal = false,
    IReadOnlyList<ParameterChoice>? Choices = null);

/// <summary>Base keys of the standard setup vocabulary.</summary>
public static class SetupKeys
{
    public const string TyrePressureCold = "tyre_pressure_cold";
    public const string TyrePressureHot = "tyre_pressure_hot";
    public const string Camber = "camber";
    public const string Toe = "toe";
    public const string Caster = "caster";
    public const string RideHeight = "ride_height";
    public const string SpringRate = "spring_rate";
    public const string DamperStiffness = "damper_stiffness";
    public const string DamperBump = "damper_bump";
    public const string DamperRebound = "damper_rebound";
    public const string DamperBumpLow = "damper_bump_low";
    public const string DamperBumpHigh = "damper_bump_high";
    public const string DamperReboundLow = "damper_rebound_low";
    public const string DamperReboundHigh = "damper_rebound_high";
    public const string DamperClickReference = "damper_click_reference";
    public const string WingAngle = "wing_angle";
    public const string BrakeBias = "brake_bias";
    public const string FuelMass = "fuel_mass";
    public const string BallastMass = "ballast_mass";
    public const string DriverMass = "driver_mass";
    public const string Tyres = "tyres";

    /// <summary>Suffix of the companion key that links a value to a public catalog (<c>catalog:id</c>).</summary>
    public const string RefSuffix = ".ref";
}

/// <summary>The standard setup vocabulary, which applies to files with <c>orf</c> 1.3 or later.</summary>
public static class SetupVocabulary
{
    private static readonly string[] WheelPositions = ["fl", "fr", "rl", "rr"];
    private static readonly string[] AxlePositions = ["f", "r"];
    private static readonly string[] FrontWheels = ["fl", "fr"];

    public static IReadOnlyDictionary<string, StandardParameter> Standard { get; } = Build();

    private static Dictionary<string, StandardParameter> Build()
    {
        StandardParameter[] all =
        [
            new(SetupKeys.TyrePressureCold, SetupScope.Wheel, "Pa", "decimal"),
            new(SetupKeys.TyrePressureHot, SetupScope.Wheel, "Pa", "decimal"),
            new(SetupKeys.Camber, SetupScope.Wheel, "rad", "decimal"),
            new(SetupKeys.Toe, SetupScope.Wheel, "rad", "decimal"),
            new(SetupKeys.Caster, SetupScope.Wheel, "rad", "decimal", FrontWheels),
            new(SetupKeys.RideHeight, SetupScope.Axle, "m", "decimal"),
            new(SetupKeys.SpringRate, SetupScope.Wheel, "N/m", "decimal"),
            new(SetupKeys.DamperStiffness, SetupScope.Wheel, null, "integer"),
            new(SetupKeys.DamperBump, SetupScope.Wheel, null, "integer"),
            new(SetupKeys.DamperRebound, SetupScope.Wheel, null, "integer"),
            new(SetupKeys.DamperBumpLow, SetupScope.Wheel, null, "integer"),
            new(SetupKeys.DamperBumpHigh, SetupScope.Wheel, null, "integer"),
            new(SetupKeys.DamperReboundLow, SetupScope.Wheel, null, "integer"),
            new(SetupKeys.DamperReboundHigh, SetupScope.Wheel, null, "integer"),
            new(SetupKeys.DamperClickReference, SetupScope.Single, null, "choice", Choices:
            [
                new ParameterChoice("from_hard", new Dictionary<string, string> { ["en"] = "From hard" }),
                new ParameterChoice("from_soft", new Dictionary<string, string> { ["en"] = "From soft" }),
            ]),
            new(SetupKeys.WingAngle, SetupScope.Axle, "rad", "decimal"),
            new(SetupKeys.BrakeBias, SetupScope.Single, "1", "decimal"),
            new(SetupKeys.FuelMass, SetupScope.Single, "kg", "decimal"),
            new(SetupKeys.BallastMass, SetupScope.Single, "kg", "decimal"),
            new(SetupKeys.DriverMass, SetupScope.Single, "kg", "decimal", Personal: true),
            new(SetupKeys.Tyres, SetupScope.Single, null, "text"),
        ];
        return all.ToDictionary(p => p.Key, StringComparer.Ordinal);
    }

    /// <summary>True when the standard vocabulary governs a file with this <c>orf</c> version (1.3 or later, same major).</summary>
    public static bool Applies(string orfVersion)
    {
        var parts = orfVersion.Split('.');
        return parts.Length >= 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            && major == 1 && minor >= 3;
    }

    public static bool IsStandard(string baseKey) => Standard.ContainsKey(baseKey);

    public static bool IsCustom(string baseKey) => baseKey.StartsWith("x-", StringComparison.Ordinal);

    /// <summary>Removes a trailing <c>.ref</c> and then a valid position suffix.</summary>
    public static string BaseKeyOf(string key)
    {
        if (key.EndsWith(SetupKeys.RefSuffix, StringComparison.Ordinal))
            key = key[..^SetupKeys.RefSuffix.Length];
        var position = PositionOf(key);
        return position is null ? key : key[..^(position.Length + 1)];
    }

    /// <summary>The position suffix (<c>fl fr rl rr f r</c>) of a key, or null.</summary>
    public static string? PositionOf(string key)
    {
        if (key.EndsWith(SetupKeys.RefSuffix, StringComparison.Ordinal))
            key = key[..^SetupKeys.RefSuffix.Length];
        var dot = key.LastIndexOf('.');
        if (dot <= 0) return null;
        var suffix = key[(dot + 1)..];
        return Array.IndexOf(WheelPositions, suffix) >= 0 || Array.IndexOf(AxlePositions, suffix) >= 0 ? suffix : null;
    }

    /// <summary>Position suffixes of a scope: the four wheels, the two axles, or none.</summary>
    internal static IReadOnlyList<string> PositionsOf(SetupScope scope) => scope switch
    {
        SetupScope.Wheel => WheelPositions,
        SetupScope.Axle => AxlePositions,
        _ => [],
    };

    /// <summary>Concrete keys for a base key in a scope, optionally limited to some positions.</summary>
    public static IEnumerable<string> KeysFor(string baseKey, SetupScope scope, IReadOnlyList<string>? positions = null)
    {
        if (scope == SetupScope.Single)
        {
            yield return baseKey;
            yield break;
        }
        foreach (var p in scope == SetupScope.Wheel ? WheelPositions : AxlePositions)
            if (positions is null || positions.Contains(p))
                yield return $"{baseKey}.{p}";
    }

    /// <summary>Damper keys for a number of damping adjustment ways (0 to 4).</summary>
    public static IReadOnlyList<string> DamperKeys(int ways) => ways switch
    {
        0 => [],
        1 => [SetupKeys.DamperStiffness],
        2 => [SetupKeys.DamperBump, SetupKeys.DamperRebound],
        3 => [SetupKeys.DamperBumpLow, SetupKeys.DamperBumpHigh, SetupKeys.DamperRebound],
        4 => [SetupKeys.DamperBumpLow, SetupKeys.DamperBumpHigh, SetupKeys.DamperReboundLow, SetupKeys.DamperReboundHigh],
        _ => throw new ArgumentOutOfRangeException(nameof(ways), ways, "Damper ways must be between 0 and 4."),
    };
}
