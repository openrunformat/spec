using System.Text.Json;
using OpenRunFormat.Setup;

namespace OpenRunFormat.Tests;

public class RunSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orf-runsetup-" + Guid.NewGuid().ToString("N"));

    public RunSetupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Dictionary<string, string> En(string text) => new() { ["en"] = text };

    private static ParameterDescription Choice(string name, params string[] ids) =>
        new(En(name), "choice", SetupScope.Single)
        {
            Choices = ids.Select(i => new ParameterChoice(i, En(i))).ToList(),
            AllowOther = true,
            Group = ParameterGroups.Suspension,
        };

    [Fact]
    public void Plantilla_ida_y_vuelta_conserva_orden_de_parametros()
    {
        var setup = new RunSetup(RunFormat.Version, RunSetupKind.Template, new Dictionary<string, string> { ["en"] = "Street", ["es"] = "Calle" })
        {
            Id = "generic-street",
            Version = 2,
            AppliesTo = ["example-make/example-model@gen1"],
            Vehicle = new Dictionary<string, string> { ["model"] = "demo" },
            Parameters =
            [
                new("x-suspension", Choice("Suspension", "stock")),
                new(SetupKeys.TyrePressureCold, null),
                new("x-drive-mode", Choice("Drive mode", "comfort", "sport")),
                new(SetupKeys.Camber, null),
                new(SetupKeys.Tyres, new ParameterDescription(En("Tyres"), "text", SetupScope.Single) { CatalogKind = "tyre" }),
            ],
            DamperWays = new DamperWays(0, 0),
            Extensions = new Dictionary<string, JsonElement> { ["x-demo"] = JsonDocument.Parse("""{"a":1}""").RootElement },
        };
        var path = Path.Combine(_dir, "t" + RunSetupFile.Extension);

        RunSetupFile.Write(path, setup);
        var read = RunSetupFile.Read(path);

        Assert.Equal(RunFormat.Version, read.Orf);
        Assert.Equal(RunSetupKind.Template, read.Kind);
        Assert.Equal("generic-street", read.Id);
        Assert.Equal(2, read.Version);
        Assert.Equal(["example-make/example-model@gen1"], read.AppliesTo);
        Assert.Equal("Calle", read.Name["es"]);
        Assert.Equal("demo", read.Vehicle["model"]);
        Assert.Equal(["x-suspension", "tyre_pressure_cold", "x-drive-mode", "camber", "tyres"], read.Parameters.Select(p => p.Key));
        Assert.Null(read.Parameters[1].Value);
        Assert.Null(read.Parameters[3].Value);
        Assert.Equal(["comfort", "sport"], read.Parameters[2].Value!.Choices.Select(c => c.Id));
        Assert.Equal("tyre", read.Parameters[4].Value!.CatalogKind);
        Assert.Null(read.Values);
        Assert.Equal(new DamperWays(0, 0), read.DamperWays);
        Assert.Equal(1, read.Extensions["x-demo"].GetProperty("a").GetInt32());
        Assert.Contains("\"tyre_pressure_cold\": {}", File.ReadAllText(path));
    }

    [Fact]
    public void Setup_ida_y_vuelta_con_values_y_composicion()
    {
        var setup = new RunSetup(RunFormat.Version, RunSetupKind.Setup, En("Mine"))
        {
            Values = new Dictionary<string, string> { ["camber.fl"] = "-0.03", ["x-note"] = "Ñ" },
            Composition = new SetupComposition(new SetupLayerRef("generic-street", 1), [new SetupLayerRef("mod-coilover-2way", 3)]),
        };
        var read = RunSetupFile.Parse(RunSetupFile.Serialize(setup));

        Assert.Equal(RunSetupKind.Setup, read.Kind);
        Assert.Null(read.Id);
        Assert.Equal("-0.03", read.Values!["camber.fl"]);
        Assert.Equal("Ñ", read.Values["x-note"]);
        Assert.Equal(new SetupLayerRef("generic-street", 1), read.Composition!.Base);
        Assert.Equal([new SetupLayerRef("mod-coilover-2way", 3)], read.Composition.Modifications);
    }

    [Fact]
    public void Modificacion_ida_y_vuelta_conserva_operaciones()
    {
        var setup = new RunSetup(RunFormat.Version, RunSetupKind.Modification, En("Coilover"))
        {
            Id = "mod-coilover-2way",
            Version = 1,
            Operations =
            [
                new(SetupOperationKind.Remove, "x-suspension"),
                new(SetupOperationKind.Add, SetupKeys.RideHeight),
                new(SetupOperationKind.Change, "x-map", new ParameterDescription(En("Map"), "text", SetupScope.Single)),
                new(SetupOperationKind.DamperWays, Ways: new DamperWays(2, 1)),
            ],
        };
        var read = RunSetupFile.Parse(RunSetupFile.Serialize(setup));

        Assert.Equal(4, read.Operations.Count);
        Assert.Equal(new SetupOperation(SetupOperationKind.Remove, "x-suspension"), read.Operations[0]);
        Assert.Equal(new SetupOperation(SetupOperationKind.Add, SetupKeys.RideHeight), read.Operations[1]);
        Assert.Equal("x-map", read.Operations[2].Key);
        Assert.Equal("text", read.Operations[2].Description!.Type);
        Assert.Equal(new DamperWays(2, 1), read.Operations[3].Ways);
    }

    [Fact]
    public void Setup_sin_values_se_rechaza()
    {
        Assert.Throws<FormatException>(() => RunSetupFile.Parse("""{"orf":"1.3","kind":"setup","name":{"en":"A"}}"""));
    }

    [Theory]
    [InlineData("""{"orf":"1.3","kind":"template","version":1,"name":{"en":"A"}}""")]
    [InlineData("""{"orf":"1.3","kind":"template","id":"","version":1,"name":{"en":"A"}}""")]
    [InlineData("""{"orf":"1.3","kind":"template","id":"a","name":{"en":"A"}}""")]
    [InlineData("""{"orf":"1.3","kind":"modification","version":1,"name":{"en":"A"}}""")]
    public void Plantilla_sin_id_se_rechaza(string json)
    {
        Assert.Throws<FormatException>(() => RunSetupFile.Parse(json));
    }

    [Theory]
    [InlineData("""{"orf":"1.3","kind":"profile","name":{"en":"A"},"values":{}}""")]
    [InlineData("""{"orf":"1.3","name":{"en":"A"},"values":{}}""")]
    public void Kind_desconocido_se_rechaza(string json)
    {
        Assert.Throws<FormatException>(() => RunSetupFile.Parse(json));
    }

    [Theory]
    [InlineData("""{"kind":"setup","name":{"en":"A"},"values":{}}""")]
    [InlineData("""{"orf":"2.0","kind":"setup","name":{"en":"A"},"values":{}}""")]
    [InlineData("""{"orf":"1.3","kind":"setup","name":{},"values":{}}""")]
    [InlineData("""{"orf":"1.3","kind":"setup","values":{}}""")]
    [InlineData("""{"orf":"1.3","kind":"setup","name":{"en":"A"},"values":{"a":1}}""")]
    [InlineData("""{"orf":"1.3","kind":"setup",""")]
    [InlineData("""[]""")]
    public void Cabecera_mal_formada_se_rechaza(string json)
    {
        Assert.Throws<FormatException>(() => RunSetupFile.Parse(json));
    }

    [Fact]
    public void Operacion_mal_formada_se_ignora_sola()
    {
        var read = RunSetupFile.Parse("""
            {
              "orf": "1.3", "kind": "modification", "id": "m", "version": 1, "name": {"en": "M"},
              "operations": [
                {"op": "remove", "key": "x-a"},
                {"op": "rename", "key": "x-b"},
                {"op": "add"},
                {"op": "add", "key": "x-c"},
                {"op": "add", "key": "x-d", "description": {"name": {"en": "D"}, "type": "color", "scope": "single"}},
                {"op": "damperWays", "ways": {"front": 5, "rear": 1}},
                {"op": "damperWays"},
                5,
                {"op": "add", "key": "camber"},
                {"op": "damperWays", "ways": {"front": 3, "rear": 1}}
              ]
            }
            """);

        Assert.Equal(
            [
                new SetupOperation(SetupOperationKind.Remove, "x-a"),
                new SetupOperation(SetupOperationKind.Add, "camber"),
                new SetupOperation(SetupOperationKind.DamperWays, Ways: new DamperWays(3, 1)),
            ],
            read.Operations);
    }

    [Fact]
    public void Parametro_mal_formado_se_ignora_solo()
    {
        var read = RunSetupFile.Parse("""
            {
              "orf": "1.3", "kind": "template", "id": "t", "version": 1, "name": {"en": "T"},
              "parameters": {
                "camber": {},
                "x-a": {},
                "toe": null,
                "x-b": {"name": {"en": "B"}, "type": "decimal", "scope": "diagonal"},
                "x-c": {"name": {"en": "C"}, "type": "integer", "scope": "axle"},
                "caster": 3
              }
            }
            """);

        Assert.Equal(["camber", "toe", "x-c"], read.Parameters.Select(p => p.Key));
    }

    [Fact]
    public void Clave_estandar_desconocida_se_conserva_como_texto()
    {
        // A key added to the vocabulary by a later minor version is unknown here: it is kept as single-valued text.
        var template = RunSetupFile.Parse("""
            {
              "orf": "1.4", "kind": "template", "id": "t", "version": 1, "name": {"en": "T"},
              "parameters": { "camber": {}, "anti_dive": {}, "rake": null, "x-a": {} }
            }
            """);

        Assert.Equal(["camber", "anti_dive", "rake"], template.Parameters.Select(p => p.Key));
        Assert.Null(template.Parameters[0].Value);
        foreach (var (key, d) in template.Parameters.Skip(1))
        {
            Assert.Equal("text", d!.Type);
            Assert.Equal(SetupScope.Single, d.Scope);
            Assert.Equal(key, d.Name["en"]);
            Assert.Null(d.Positions);
        }

        var mod = RunSetupFile.Parse("""
            {
              "orf": "1.4", "kind": "modification", "id": "m", "version": 1, "name": {"en": "M"},
              "operations": [
                {"op": "add", "key": "anti_dive"},
                {"op": "change", "key": "rake", "description": null},
                {"op": "add", "key": "x-a"},
                {"op": "add", "key": "camber"}
              ]
            }
            """);

        Assert.Equal(["anti_dive", "rake", "camber"], mod.Operations.Select(o => o.Key));
        Assert.Equal("text", mod.Operations[0].Description!.Type);
        Assert.Equal(SetupScope.Single, mod.Operations[0].Description!.Scope);
        Assert.Equal("rake", mod.Operations[1].Description!.Name["en"]);
        Assert.Null(mod.Operations[2].Description);
    }

    [Fact]
    public void Descripcion_de_clave_estandar_usa_tipo_unidad_y_ambito_del_vocabulario()
    {
        const string Parameters = """
            "parameters": {
              "camber": {"name": {"es": "Caída"}, "type": "text", "unit": "deg", "scope": "single", "min": -0.1,
                         "group": "geometry", "positions": ["fl", "fr", "f"]},
              "caster": {"name": {"en": "Caster"}, "type": "decimal", "scope": "wheel", "positions": ["rl"], "precision": 3},
              "damper_bump": {"name": {"en": "Bump"}, "type": "decimal", "scope": "wheel", "precision": 1}
            }
            """;
        var read = RunSetupFile.Parse($$"""{"orf":"1.3","kind":"template","id":"t","version":1,"name":{"en":"T"},{{Parameters}}}""");

        var camber = read.Parameters[0].Value!;
        Assert.Equal(("decimal", "rad", SetupScope.Wheel), (camber.Type, camber.Unit, camber.Scope));
        Assert.Equal("Caída", camber.Name["es"]);
        Assert.Equal(-0.1, camber.Min);
        Assert.Equal("geometry", camber.Group);
        Assert.Equal(["fl", "fr"], camber.Positions);
        var caster = read.Parameters[1].Value!;
        Assert.Equal(["fl", "fr"], caster.Positions);
        Assert.Equal(3, caster.Precision);
        var bump = read.Parameters[2].Value!;
        Assert.Equal(("integer", (string?)null), (bump.Type, bump.Unit));
        Assert.Null(bump.Precision);

        var op = RunSetupFile.Parse("""
            {"orf":"1.3","kind":"modification","id":"m","version":1,"name":{"en":"M"},
             "operations":[{"op":"add","key":"camber","description":{"name":{"en":"C"},"type":"text","scope":"axle"}}]}
            """).Operations[0].Description!;
        Assert.Equal(("decimal", "rad", SetupScope.Wheel), (op.Type, op.Unit, op.Scope));

        // Before 1.3 the vocabulary does not govern: the description is read as written.
        var old = RunSetupFile.Parse($$"""{"orf":"1.2","kind":"template","id":"t","version":1,"name":{"en":"T"},{{Parameters}}}""");
        Assert.Equal(("text", "deg", SetupScope.Single), (old.Parameters[0].Value!.Type, old.Parameters[0].Value!.Unit, old.Parameters[0].Value!.Scope));
    }

    [Fact]
    public void Damper_click_reference_conserva_sus_opciones_tras_dos_reescrituras()
    {
        var text = """
            {"orf":"1.3","kind":"template","id":"t","version":1,"name":{"en":"T"},"parameters":{
              "damper_click_reference": {"name":{"es":"Referencia"},"type":"text","scope":"wheel",
                "choices":[{"id":"from_soft","name":{"es":"Desde blando"}},{"id":"middle","name":{"en":"Middle"}}],
                "allowOther":true},
              "camber": {"name":{"en":"Camber"},"type":"decimal","scope":"wheel","choices":[{"id":"a","name":{"en":"A"}}],"allowOther":true}
            }}
            """;
        var read = RunSetupFile.Parse(RunSetupFile.Serialize(RunSetupFile.Parse(RunSetupFile.Serialize(RunSetupFile.Parse(text)))));

        var d = Assert.Single(read.Parameters, p => p.Key == SetupKeys.DamperClickReference).Value!;
        Assert.Equal(("choice", SetupScope.Single), (d.Type, d.Scope));
        Assert.Equal(["from_hard", "from_soft"], d.Choices.Select(c => c.Id));
        Assert.Equal("Desde blando", d.Choices[1].Name["es"]);
        Assert.Contains("en", d.Choices[0].Name.Keys);
        Assert.False(d.AllowOther);
        Assert.Equal("Referencia", d.Name["es"]);
        var camber = read.Parameters.Single(p => p.Key == SetupKeys.Camber).Value!;
        Assert.Empty(camber.Choices);
        Assert.False(camber.AllowOther);
    }

    [Fact]
    public void Claves_con_sufijo_sin_descripcion_se_ignoran()
    {
        var template = RunSetupFile.Parse("""
            {"orf":"1.4","kind":"template","id":"t","version":1,"name":{"en":"T"},
             "parameters":{"camber.fl":{},"tyres.ref":null,"anti_dive":{}}}
            """);
        Assert.Equal(["anti_dive"], template.Parameters.Select(p => p.Key));

        var mod = RunSetupFile.Parse("""
            {"orf":"1.4","kind":"modification","id":"m","version":1,"name":{"en":"M"},
             "operations":[{"op":"add","key":"camber.fl"},{"op":"change","key":"tyres.ref"},{"op":"add","key":"anti_dive"}]}
            """);
        Assert.Equal(["anti_dive"], mod.Operations.Select(o => o.Key));
    }

    [Fact]
    public void Campos_opcionales_mal_formados_se_ignoran()
    {
        var read = RunSetupFile.Parse("""
            {
              "orf": "1.3", "kind": "setup", "name": {"en": "S"}, "values": {},
              "damperWays": {"front": 9, "rear": 1},
              "composition": {"base": {"id": ""}},
              "extensions": 4,
              "unknown": true
            }
            """);

        Assert.Null(read.DamperWays);
        Assert.Null(read.Composition);
        Assert.Empty(read.Extensions);
    }

    [Fact]
    public void Lee_versiones_menores_anteriores_y_no_conserva_campos_desconocidos()
    {
        var read = RunSetupFile.Parse("""
            {
              "orf": "1.0", "kind": "setup", "name": {"en": "S"}, "values": {"camber.fl": "-0.03"},
              "unknown": {"a": 1},
              "extensions": {"x-myapp": {"levels": ["base"]}}
            }
            """);
        Assert.Equal("1.0", read.Orf);

        var text = RunSetupFile.Serialize(read);

        Assert.DoesNotContain("unknown", text);
        // A key of a 1.0 file is free text: it is renamed, not reinterpreted, when written as 1.3.
        Assert.Equal("-0.03", RunSetupFile.Parse(text).Values!["x-legacy-camber.fl"]);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(RunFormat.Version, doc.RootElement.GetProperty("orf").GetString());
        Assert.Equal("base", doc.RootElement.GetProperty("extensions").GetProperty("x-myapp").GetProperty("levels")[0].GetString());
    }

    [Fact]
    public void Reescribir_runsetup_1_2_renombra_claves_del_vocabulario()
    {
        var old = new RunSetup("1.2", RunSetupKind.Setup, En("S"))
        {
            Parameters =
            [
                new(SetupKeys.Camber, null),
                new(SetupKeys.Tyres, new ParameterDescription(En("Tyres"), "text", SetupScope.Single)),
                new("x-foo", new ParameterDescription(En("Foo"), "text", SetupScope.Single)),
                new("front_pressure", null),
                new("anti_roll_bar", new ParameterDescription(En("ARB"), "integer", SetupScope.Axle)),
            ],
            Values = new Dictionary<string, string>
            {
                ["camber.fl"] = "-2", ["tyres"] = "Example", ["tyres.ref"] = "tyre:example", ["x-foo"] = "a", ["my_key"] = "b",
                ["front_pressure"] = "2.1", ["anti_roll_bar.f"] = "3",
            },
        };
        var path = Path.Combine(_dir, "old.runsetup");

        RunSetupFile.Write(path, old);
        var read = RunSetupFile.Read(path);

        Assert.Equal(RunFormat.Version, read.Orf);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["x-legacy-camber.fl"] = "-2", ["x-legacy-tyres"] = "Example", ["x-legacy-tyres.ref"] = "tyre:example",
                ["x-foo"] = "a", ["x-legacy-my_key"] = "b", ["x-legacy-front_pressure"] = "2.1",
                ["x-legacy-anti_roll_bar.f"] = "3",
            },
            read.Values);
        Assert.Equal(
            ["x-legacy-camber", "x-legacy-tyres", "x-foo", "x-legacy-front_pressure", "x-legacy-anti_roll_bar"],
            read.Parameters.Select(p => p.Key));
        Assert.Equal(SetupScope.Wheel, read.Parameters[0].Value!.Scope);
        Assert.Equal("Tyres", read.Parameters[1].Value!.Name["en"]);
        // A free key without description gets a single-valued text description named after the key.
        Assert.Equal("text", read.Parameters[3].Value!.Type);
        Assert.Equal(SetupScope.Single, read.Parameters[3].Value!.Scope);
        Assert.Null(read.Parameters[3].Value!.Positions);
        Assert.Equal("front_pressure", read.Parameters[3].Value!.Name["en"]);
        Assert.Equal(SetupScope.Axle, read.Parameters[4].Value!.Scope);
    }

    [Fact]
    public void Reescribir_runsetup_1_4_conserva_la_version()
    {
        var future = RunSetupFile.Parse("""{"orf":"1.4","kind":"setup","name":{"en":"S"},"values":{"camber.fl":"-0.03"}}""");
        var path = Path.Combine(_dir, "future.runsetup");

        RunSetupFile.Write(path, future);
        var read = RunSetupFile.Read(path);

        Assert.Equal("1.4", read.Orf);
        Assert.Equal("-0.03", read.Values!["camber.fl"]);
        Assert.Equal("1.4", RunSetupFile.Parse(RunSetupFile.Serialize(future)).Orf);
        Assert.Equal(RunFormat.Version, RunSetupFile.Parse(RunSetupFile.Serialize(future with { Orf = "2.1" })).Orf);
    }

    public static TheoryData<string> Rechazables() => ["sin-id", "sin-version", "version-cero", "id-vacio", "mod-sin-id", "setup-sin-values", "sin-nombre", "applies-to-vacio"];

    [Theory]
    [MemberData(nameof(Rechazables))]
    public void Write_rechaza_lo_que_su_lector_rechazaria(string caso)
    {
        var template = new RunSetup(RunFormat.Version, RunSetupKind.Template, En("T")) { Id = "t", Version = 1 };
        var bad = caso switch
        {
            "sin-id" => template with { Id = null },
            "sin-version" => template with { Version = null },
            "version-cero" => template with { Version = 0 },
            "id-vacio" => template with { Id = "" },
            "mod-sin-id" => template with { Kind = RunSetupKind.Modification, Id = null },
            "setup-sin-values" => new RunSetup(RunFormat.Version, RunSetupKind.Setup, En("S")),
            "sin-nombre" => template with { Name = new Dictionary<string, string>() },
            _ => template with { AppliesTo = [""] },
        };
        var path = Path.Combine(_dir, "bad.runsetup");

        Assert.Throws<ArgumentException>(() => RunSetupFile.Serialize(bad));
        Assert.Throws<ArgumentException>(() => RunSetupFile.Write(path, bad));
        Assert.False(File.Exists(path));
        RunSetupFile.Serialize(template);
    }

    [Fact]
    public void Write_fuerza_orf_actual()
    {
        var path = Path.Combine(_dir, "s.runsetup");
        RunSetupFile.Write(path, new RunSetup("1.0", RunSetupKind.Setup, En("S")) { Values = new Dictionary<string, string>() });
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(RunFormat.Version, doc.RootElement.GetProperty("orf").GetString());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));
    }
}
