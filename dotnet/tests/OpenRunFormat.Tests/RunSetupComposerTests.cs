using OpenRunFormat.Setup;

namespace OpenRunFormat.Tests;

public class RunSetupComposerTests
{
    private static Dictionary<string, string> En(string text) => new() { ["en"] = text };

    private static ParameterDescription Text(string name, string? group = null) =>
        new(En(name), "text", SetupScope.Single) { Group = group };

    private static RunSetup GenericStreet() => new(RunFormat.Version, RunSetupKind.Template, En("Street"))
    {
        Id = "generic-street",
        Version = 1,
        Parameters =
        [
            new(SetupKeys.TyrePressureCold, null),
            new(SetupKeys.Camber, null),
            new("x-suspension", Text("Suspension", ParameterGroups.Suspension)),
            new(SetupKeys.Tyres, null),
            new("x-note", Text("Note")),
        ],
    };

    private static RunSetup Mod(string id, params SetupOperation[] ops) =>
        new(RunFormat.Version, RunSetupKind.Modification, En(id)) { Id = id, Version = 1, Operations = ops };

    [Fact]
    public void Base_mas_roscado_2_vias()
    {
        var coilover = Mod("mod-coilover-2way",
            new SetupOperation(SetupOperationKind.Remove, "x-suspension"),
            new SetupOperation(SetupOperationKind.Add, SetupKeys.RideHeight),
            new SetupOperation(SetupOperationKind.Add, SetupKeys.SpringRate),
            new SetupOperation(SetupOperationKind.DamperWays, Ways: new DamperWays(2, 2)));

        var composed = RunSetupComposer.Compose(GenericStreet(), [coilover]);

        var keys = composed.Parameters.Select(p => p.Key).ToList();
        Assert.Contains(SetupKeys.RideHeight, keys);
        Assert.Contains(SetupKeys.SpringRate, keys);
        Assert.Contains(SetupKeys.DamperBump, keys);
        Assert.Contains(SetupKeys.DamperRebound, keys);
        Assert.DoesNotContain("x-suspension", keys);
        Assert.Equal(new DamperWays(2, 2), composed.DamperWays);
        Assert.Empty(composed.Warnings);
        // Dampers go right after the last suspension parameter (spring_rate), not at the end.
        Assert.Equal(
            ["tyre_pressure_cold", "camber", "tyres", "x-note", "ride_height", "spring_rate", "damper_bump", "damper_rebound"],
            keys);
        Assert.Equal(["fl", "fr", "rl", "rr"], composed.Parameters.Single(p => p.Key == SetupKeys.DamperBump).Positions);
    }

    [Fact]
    public void Composicion_operaciones_sobre_claves_ausentes_o_repetidas()
    {
        var mod = Mod("mod-x",
            new SetupOperation(SetupOperationKind.Remove, "x-nada"),
            new SetupOperation(SetupOperationKind.Add, SetupKeys.Camber, Text("Adjustable camber", ParameterGroups.Geometry)));

        var composed = RunSetupComposer.Compose(GenericStreet(), [mod]);

        Assert.Equal(2, composed.Warnings.Count);
        var camber = Assert.Single(composed.Parameters, p => p.Key == SetupKeys.Camber);
        Assert.Equal("Adjustable camber", camber.Description!.Name["en"]);
        Assert.Equal(1, composed.Parameters.ToList().FindIndex(p => p.Key == SetupKeys.Camber));
    }

    [Fact]
    public void Change_de_clave_ausente_avisa_sin_efecto()
    {
        var composed = RunSetupComposer.Compose(GenericStreet(),
            [Mod("m", new SetupOperation(SetupOperationKind.Change, "x-nada", Text("Nothing")))]);

        Assert.Single(composed.Warnings);
        Assert.DoesNotContain(composed.Parameters, p => p.Key == "x-nada");
        Assert.Equal(5, composed.Parameters.Count);
    }

    [Fact]
    public void Vias_distintas_por_eje_generan_claves_por_posicion()
    {
        var composed = RunSetupComposer.Compose(GenericStreet(),
            [Mod("m", new SetupOperation(SetupOperationKind.DamperWays, Ways: new DamperWays(2, 1)))]);

        Assert.Equal(new DamperWays(2, 1), composed.DamperWays);
        Assert.Equal(["fl", "fr"], composed.Parameters.Single(p => p.Key == SetupKeys.DamperBump).Positions);
        Assert.Equal(["fl", "fr"], composed.Parameters.Single(p => p.Key == SetupKeys.DamperRebound).Positions);
        Assert.Equal(["rl", "rr"], composed.Parameters.Single(p => p.Key == SetupKeys.DamperStiffness).Positions);
        // After x-suspension, the last parameter of the suspension group.
        Assert.Equal(
            ["tyre_pressure_cold", "camber", "x-suspension", "damper_bump", "damper_rebound", "damper_stiffness", "tyres", "x-note"],
            composed.Parameters.Select(p => p.Key));
    }

    [Fact]
    public void Clave_comun_a_ambos_ejes_lleva_las_cuatro_posiciones()
    {
        var composed = RunSetupComposer.Compose(GenericStreet(),
            [Mod("m", new SetupOperation(SetupOperationKind.DamperWays, Ways: new DamperWays(3, 2)))]);

        Assert.Equal(["fl", "fr", "rl", "rr"], composed.Parameters.Single(p => p.Key == SetupKeys.DamperRebound).Positions);
        Assert.Equal(["fl", "fr"], composed.Parameters.Single(p => p.Key == SetupKeys.DamperBumpLow).Positions);
        Assert.Equal(["rl", "rr"], composed.Parameters.Single(p => p.Key == SetupKeys.DamperBump).Positions);
    }

    [Fact]
    public void Sin_grupo_suspension_los_amortiguadores_van_al_final_y_conservan_descripcion()
    {
        var baseLayer = new RunSetup(RunFormat.Version, RunSetupKind.Template, En("B"))
        {
            Id = "b",
            Version = 1,
            Parameters =
            [
                new(SetupKeys.DamperStiffness, Text("Stiffness knob")),
                new(SetupKeys.Camber, null),
                new("x-map", Text("Map", ParameterGroups.Electronics)),
            ],
            DamperWays = new DamperWays(1, 1),
        };

        var composed = RunSetupComposer.Compose(baseLayer, []);

        Assert.Equal(["camber", "x-map", "damper_stiffness"], composed.Parameters.Select(p => p.Key));
        Assert.Equal("Stiffness knob", composed.Parameters[2].Description!.Name["en"]);
        Assert.Equal(new DamperWays(1, 1), composed.DamperWays);
    }

    [Fact]
    public void Sin_vias_quita_claves_de_amortiguador_de_la_base()
    {
        var baseLayer = GenericStreet() with
        {
            Parameters = [new(SetupKeys.DamperBump, null), new(SetupKeys.Camber, null)],
        };

        var composed = RunSetupComposer.Compose(baseLayer, []);

        Assert.Equal(new DamperWays(0, 0), composed.DamperWays);
        Assert.Equal(["camber"], composed.Parameters.Select(p => p.Key));
    }

    [Fact]
    public void Claves_de_amortiguador_solo_salen_de_las_vias_finales()
    {
        var baseLayer = GenericStreet() with
        {
            Parameters = [new(SetupKeys.DamperClickReference, null), new(SetupKeys.DamperStiffness, null), new(SetupKeys.Camber, null)],
            DamperWays = new DamperWays(1, 1),
        };
        var mod = Mod("m",
            new SetupOperation(SetupOperationKind.Remove, SetupKeys.DamperStiffness),
            new SetupOperation(SetupOperationKind.Add, SetupKeys.DamperBump));

        var composed = RunSetupComposer.Compose(baseLayer, [mod]);

        // The explicit remove and add are overridden by the ways (1/1), without a warning.
        Assert.Empty(composed.Warnings);
        Assert.Equal(["damper_click_reference", "damper_stiffness", "camber"], composed.Parameters.Select(p => p.Key));

        var noWays = RunSetupComposer.Compose(baseLayer,
            [Mod("n", new SetupOperation(SetupOperationKind.DamperWays, Ways: new DamperWays(0, 0)))]);

        // damper_click_reference is not derived from the ways: it stays with 0/0.
        Assert.Equal(["damper_click_reference", "camber"], noWays.Parameters.Select(p => p.Key));
    }

    [Fact]
    public void Orden_de_modificaciones_importa()
    {
        var add = Mod("add", new SetupOperation(SetupOperationKind.Add, "x-a", Text("A")));
        var remove = Mod("remove", new SetupOperation(SetupOperationKind.Remove, "x-a"));

        var addThenRemove = RunSetupComposer.Compose(GenericStreet(), [add, remove]);
        Assert.DoesNotContain(addThenRemove.Parameters, p => p.Key == "x-a");
        Assert.Empty(addThenRemove.Warnings);

        var removeThenAdd = RunSetupComposer.Compose(GenericStreet(), [remove, add]);
        Assert.Contains(removeThenAdd.Parameters, p => p.Key == "x-a");
        var warning = Assert.Single(removeThenAdd.Warnings);
        Assert.Contains("x-a", warning);
    }

    [Fact]
    public void Capas_de_tipo_incorrecto_lanzan_ArgumentException()
    {
        var setup = new RunSetup(RunFormat.Version, RunSetupKind.Setup, En("S")) { Values = new Dictionary<string, string>() };
        Assert.Throws<ArgumentException>(() => RunSetupComposer.Compose(setup, []));
        Assert.Throws<ArgumentException>(() => RunSetupComposer.Compose(GenericStreet(), [GenericStreet()]));
    }
}
