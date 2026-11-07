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
        var setup = new RunSetup("1.0", RunSetupKind.Template, new Dictionary<string, string> { ["en"] = "Street", ["es"] = "Calle" })
        {
            Id = "generic-street",
            Version = 2,
            AppliesTo = ["honda/civic/type-r@fl5"],
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
        Assert.Equal(["honda/civic/type-r@fl5"], read.AppliesTo);
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
    public void Write_fuerza_orf_actual()
    {
        var path = Path.Combine(_dir, "s.runsetup");
        RunSetupFile.Write(path, new RunSetup("1.0", RunSetupKind.Setup, En("S")) { Values = new Dictionary<string, string>() });
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(RunFormat.Version, doc.RootElement.GetProperty("orf").GetString());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));
    }
}
