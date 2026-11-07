using OpenRunFormat.Setup;
using Xunit;

namespace OpenRunFormat.Tests;

public class SetupVocabularyTests
{
    [Fact]
    public void Vocabulario_contiene_la_tabla_del_diseno()
    {
        string[] keys =
        [
            "tyre_pressure_cold", "tyre_pressure_hot", "camber", "toe", "caster", "ride_height", "spring_rate",
            "damper_stiffness", "damper_bump", "damper_rebound", "damper_bump_low", "damper_bump_high",
            "damper_rebound_low", "damper_rebound_high", "damper_click_reference", "wing_angle", "brake_bias",
            "fuel_mass", "ballast_mass", "driver_mass", "tyres",
        ];
        Assert.Equal(21, SetupVocabulary.Standard.Count);
        foreach (var k in keys)
            Assert.True(SetupVocabulary.Standard.ContainsKey(k), k);

        var camber = SetupVocabulary.Standard[SetupKeys.Camber];
        Assert.Equal(SetupScope.Wheel, camber.Scope);
        Assert.Equal("rad", camber.Unit);
        var ride = SetupVocabulary.Standard[SetupKeys.RideHeight];
        Assert.Equal(SetupScope.Axle, ride.Scope);
        Assert.Equal("m", ride.Unit);
        var bias = SetupVocabulary.Standard[SetupKeys.BrakeBias];
        Assert.Equal(SetupScope.Single, bias.Scope);
        Assert.Equal("1", bias.Unit);
        Assert.True(SetupVocabulary.Standard[SetupKeys.DriverMass].Personal);
        Assert.False(SetupVocabulary.Standard[SetupKeys.FuelMass].Personal);
        Assert.Equal(["fl", "fr"], SetupVocabulary.Standard[SetupKeys.Caster].Positions);
        Assert.Equal("text", SetupVocabulary.Standard[SetupKeys.Tyres].Type);
        Assert.Equal("choice", SetupVocabulary.Standard[SetupKeys.DamperClickReference].Type);
        Assert.Equal("integer", SetupVocabulary.Standard[SetupKeys.DamperBump].Type);
        Assert.Null(SetupVocabulary.Standard[SetupKeys.DamperBump].Unit);
        Assert.Equal("decimal", SetupVocabulary.Standard[SetupKeys.TyrePressureCold].Type);
        Assert.Equal("Pa", SetupVocabulary.Standard[SetupKeys.TyrePressureCold].Unit);
        Assert.Equal(".ref", SetupKeys.RefSuffix);
    }

    [Fact]
    public void Vocabulario_no_rige_antes_de_1_3()
    {
        Assert.False(SetupVocabulary.Applies("1.0"));
        Assert.False(SetupVocabulary.Applies("1.2"));
        Assert.True(SetupVocabulary.Applies("1.3"));
        Assert.True(SetupVocabulary.Applies("1.10"));
        Assert.False(SetupVocabulary.Applies("2.0"));
        Assert.False(SetupVocabulary.Applies("basura"));
    }

    [Fact]
    public void BaseKeyOf_quita_posicion_y_ref()
    {
        Assert.Equal("camber", SetupVocabulary.BaseKeyOf("camber.fl"));
        Assert.Equal("tyres", SetupVocabulary.BaseKeyOf("tyres.ref"));
        Assert.Equal("ride_height", SetupVocabulary.BaseKeyOf("ride_height.f"));
        Assert.Equal("x-drive-mode", SetupVocabulary.BaseKeyOf("x-drive-mode"));
        Assert.Equal("x-a.b", SetupVocabulary.BaseKeyOf("x-a.b"));
        Assert.Equal("fl", SetupVocabulary.PositionOf("camber.fl"));
        Assert.Null(SetupVocabulary.PositionOf("camber"));
        Assert.Null(SetupVocabulary.PositionOf("x-a.b"));
        Assert.True(SetupVocabulary.IsStandard("camber"));
        Assert.False(SetupVocabulary.IsStandard("x-camber"));
        Assert.True(SetupVocabulary.IsCustom("x-camber"));
        Assert.False(SetupVocabulary.IsCustom("camber"));
    }

    [Fact]
    public void KeysFor_genera_por_ambito()
    {
        Assert.Equal(["camber.fl", "camber.fr", "camber.rl", "camber.rr"],
            SetupVocabulary.KeysFor("camber", SetupScope.Wheel));
        Assert.Equal(["ride_height.f", "ride_height.r"], SetupVocabulary.KeysFor("ride_height", SetupScope.Axle));
        Assert.Equal(["brake_bias"], SetupVocabulary.KeysFor("brake_bias", SetupScope.Single));
        Assert.Equal(["caster.fl", "caster.fr"], SetupVocabulary.KeysFor("caster", SetupScope.Wheel, ["fl", "fr"]));
    }

    [Fact]
    public void DamperKeys_por_vias()
    {
        Assert.Empty(SetupVocabulary.DamperKeys(0));
        Assert.Equal(["damper_stiffness"], SetupVocabulary.DamperKeys(1));
        Assert.Equal(["damper_bump", "damper_rebound"], SetupVocabulary.DamperKeys(2));
        Assert.Equal(["damper_bump_low", "damper_bump_high", "damper_rebound"], SetupVocabulary.DamperKeys(3));
        Assert.Equal(["damper_bump_low", "damper_bump_high", "damper_rebound_low", "damper_rebound_high"],
            SetupVocabulary.DamperKeys(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => SetupVocabulary.DamperKeys(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => SetupVocabulary.DamperKeys(-1));
    }

    [Fact]
    public void Damper_click_reference_trae_sus_opciones()
    {
        var p = SetupVocabulary.Standard[SetupKeys.DamperClickReference];
        Assert.Equal(["from_hard", "from_soft"], p.Choices!.Select(c => c.Id));
        Assert.All(p.Choices!, c => Assert.False(string.IsNullOrEmpty(c.Name["en"])));
        Assert.All(SetupVocabulary.Standard.Values.Where(x => x.Type != "choice"), x => Assert.Null(x.Choices));
    }
}
