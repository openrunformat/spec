namespace OpenRunFormat.Setup;

/// <summary>
/// A parameter of a composed setup. <paramref name="Positions"/> lists the positions that apply when not all of the
/// scope does (for dampers, the wheels of the axles that have those ways); otherwise it is the description's positions or,
/// without them, those of the standard vocabulary (for example <c>fl</c>, <c>fr</c> for <c>caster</c>).
/// </summary>
public sealed record ComposedParameter(string Key, ParameterDescription? Description, IReadOnlyList<string>? Positions);

/// <summary>The result of composing a base template with modifications.</summary>
public sealed record ComposedSetup(IReadOnlyList<ComposedParameter> Parameters, DamperWays DamperWays, IReadOnlyList<string> Warnings);

/// <summary>Composes setup layers: a base template followed by modifications, applied in order.</summary>
public static class RunSetupComposer
{
    private static readonly string[] FrontWheels = ["fl", "fr"];
    private static readonly string[] RearWheels = ["rl", "rr"];
    private static readonly string[] AllWheels = ["fl", "fr", "rl", "rr"];

    private static readonly HashSet<string> WaysKeys =
        [.. Enumerable.Range(1, 4).SelectMany(SetupVocabulary.DamperKeys)];

    /// <summary>
    /// Starts from the parameters and damper ways of <paramref name="baseLayer"/> and applies each operation of each
    /// modification. An <c>add</c> of an existing key works as a <c>change</c>; a <c>change</c> or <c>remove</c> of an
    /// absent key has no effect; both add a warning. At the end the damper keys follow the damper ways: the keys of the
    /// front ways for <c>fl fr</c> and those of the rear ways for <c>rl rr</c>, placed after the last suspension
    /// parameter (or at the end), keeping any description they had.
    /// </summary>
    /// <exception cref="ArgumentException">The base is not a template, a modification is not a modification, or damper ways are outside 0–4.</exception>
    public static ComposedSetup Compose(RunSetup baseLayer, IEnumerable<RunSetup> modifications)
    {
        ArgumentNullException.ThrowIfNull(baseLayer);
        ArgumentNullException.ThrowIfNull(modifications);
        if (baseLayer.Kind != RunSetupKind.Template)
            throw new ArgumentException("The base layer must be a template.", nameof(baseLayer));
        var ways = baseLayer.DamperWays ?? new DamperWays(0, 0);
        if (!Valid(ways)) throw new ArgumentException("Damper ways must be between 0 and 4.", nameof(baseLayer));

        var parameters = new List<KeyValuePair<string, ParameterDescription?>>(baseLayer.Parameters);
        var warnings = new List<string>();

        foreach (var mod in modifications)
        {
            if (mod.Kind != RunSetupKind.Modification)
                throw new ArgumentException($"Layer '{mod.Id}' is not a modification.", nameof(modifications));
            var name = mod.Id ?? "?";
            foreach (var op in mod.Operations)
            {
                if (op.Op == SetupOperationKind.DamperWays)
                {
                    if (op.Ways is { } w && Valid(w)) ways = w;
                    else warnings.Add($"{name}: invalid damper ways ignored.");
                    continue;
                }
                if (string.IsNullOrEmpty(op.Key))
                {
                    warnings.Add($"{name}: operation without key ignored.");
                    continue;
                }
                var at = parameters.FindIndex(p => p.Key == op.Key);
                switch (op.Op)
                {
                    case SetupOperationKind.Add when at < 0:
                        parameters.Add(new(op.Key, op.Description));
                        break;
                    case SetupOperationKind.Add:
                        warnings.Add($"{name}: 'add' of existing key '{op.Key}' applied as 'change'.");
                        parameters[at] = new(op.Key, op.Description);
                        break;
                    case SetupOperationKind.Change when at >= 0:
                        parameters[at] = new(op.Key, op.Description);
                        break;
                    case SetupOperationKind.Remove when at >= 0:
                        parameters.RemoveAt(at);
                        break;
                    default:
                        warnings.Add($"{name}: '{op.Op.ToString().ToLowerInvariant()}' of absent key '{op.Key}' ignored.");
                        break;
                }
            }
        }

        var previous = parameters.Where(p => WaysKeys.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        var result = parameters
            .Where(p => !WaysKeys.Contains(p.Key))
            .Select(p => new ComposedParameter(p.Key, p.Value,
                p.Value?.Positions ?? SetupVocabulary.Standard.GetValueOrDefault(p.Key)?.Positions))
            .ToList();

        var front = SetupVocabulary.DamperKeys(ways.Front);
        var rear = SetupVocabulary.DamperKeys(ways.Rear);
        var dampers = front.Concat(rear.Where(k => !front.Contains(k)))
            .Select(k => new ComposedParameter(k, previous.GetValueOrDefault(k),
                front.Contains(k) && rear.Contains(k) ? AllWheels : front.Contains(k) ? FrontWheels : RearWheels));
        var lastSuspension = result.FindLastIndex(IsSuspension);
        result.InsertRange(lastSuspension >= 0 ? lastSuspension + 1 : result.Count, dampers);

        return new ComposedSetup(result, ways, warnings);
    }

    private static bool Valid(DamperWays w) => w.Front is >= 0 and <= 4 && w.Rear is >= 0 and <= 4;

    private static bool IsSuspension(ComposedParameter p) =>
        p.Description?.Group == ParameterGroups.Suspension
        || p.Key is SetupKeys.RideHeight or SetupKeys.SpringRate
        || (SetupVocabulary.IsStandard(p.Key) && p.Key.StartsWith("damper_", StringComparison.Ordinal));
}
