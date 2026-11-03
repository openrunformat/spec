using System.Text.Json;

namespace OpenRunFormat.Tests;

public class CircuitIdTests
{
    private static JsonElement Data => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "conformance", "circuit-ids.json"))).RootElement;

    public static IEnumerable<TheoryDataRow<string, string, string?, bool>> ValidIds() =>
        Data.GetProperty("valid").EnumerateArray().Select(e => new TheoryDataRow<string, string, string?, bool>(
            e.GetProperty("id").GetString()!, e.GetProperty("circuit").GetString()!,
            e.GetProperty("variant").GetString(), e.GetProperty("provisional").GetBoolean()));

    public static IEnumerable<TheoryDataRow<string>> InvalidIds() =>
        Data.GetProperty("invalid").EnumerateArray().Select(e => new TheoryDataRow<string>(e.GetString()!));

    public static IEnumerable<TheoryDataRow<string>> ValidUris() =>
        Data.GetProperty("uris").GetProperty("valid").EnumerateArray().Select(e => new TheoryDataRow<string>(e.GetString()!));

    public static IEnumerable<TheoryDataRow<string>> InvalidUris() =>
        Data.GetProperty("uris").GetProperty("invalid").EnumerateArray().Select(e => new TheoryDataRow<string>(e.GetString()!));

    [Theory]
    [MemberData(nameof(ValidIds))]
    public void Valid_ids_parse_and_round_trip(string id, string circuit, string? variant, bool provisional)
    {
        Assert.True(CircuitId.TryParse(id, out var c));
        Assert.Equal(id, c.ToString());
        Assert.Equal(circuit, c.Circuit);
        Assert.Equal(variant, c.Variant);
        Assert.Equal(provisional, c.IsProvisional);
        Assert.Equal(id, CircuitId.Parse(id).ToString());
    }

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public void Invalid_ids_are_rejected(string id)
    {
        Assert.False(CircuitId.TryParse(id, out var c));
        Assert.Null(c);
        Assert.Throws<FormatException>(() => CircuitId.Parse(id));
    }

    [Fact]
    public void Null_is_rejected()
    {
        Assert.False(CircuitId.TryParse(null, out _));
        Assert.Throws<FormatException>(() => CircuitId.Parse(null!));
    }

    [Theory]
    [MemberData(nameof(ValidUris))]
    public void Uri_form_round_trips(string uri)
    {
        Assert.True(CircuitId.TryParseUri(uri, out var c));
        Assert.Equal(uri, c.ToUri());
    }

    [Theory]
    [MemberData(nameof(InvalidUris))]
    public void Invalid_uris_are_rejected(string uri)
    {
        Assert.False(CircuitId.TryParseUri(uri, out _));
    }

    [Fact]
    public void Parts_of_a_registry_id_are_exposed()
    {
        Assert.True(CircuitId.TryParse("ES-catalunya/gp@2023-04", out var c));
        Assert.Equal("ES", c.Country);
        Assert.Equal("catalunya", c.Name);
        Assert.Equal("gp", c.Layout);
        Assert.Equal("2023-04", c.LayoutVersion);
        Assert.Equal("gp@2023-04", c.Variant);
        Assert.Null(c.ProvisionalUuid);
    }

    [Fact]
    public void Circuit_and_variant_validators_check_only_their_part()
    {
        Assert.True(CircuitId.IsValidCircuit("ES-catalunya"));
        Assert.False(CircuitId.IsValidCircuit("ES-catalunya/gp"));
        Assert.False(CircuitId.IsValidCircuit(null));
        Assert.True(CircuitId.IsValidVariant("gp"));
        Assert.True(CircuitId.IsValidVariant("gp@2023"));
        Assert.False(CircuitId.IsValidVariant("ES-catalunya"));
        Assert.False(CircuitId.IsValidVariant(null));
    }

    [Fact]
    public void NewProvisional_is_a_lower_case_version_7_uuid()
    {
        var a = CircuitId.NewProvisional();
        var b = CircuitId.NewProvisional();
        Assert.True(a.IsProvisional);
        Assert.Equal(7, a.ProvisionalUuid!.Value.Version);
        Assert.Equal(a.Circuit.ToLowerInvariant(), a.Circuit);
        Assert.StartsWith("local-", a.Circuit);
        Assert.NotEqual(a.Circuit, b.Circuit);
        Assert.True(CircuitId.TryParse(a.ToString(), out _));
    }

    [Fact]
    public void TryFromCourse_combines_circuit_and_variant()
    {
        var g = new Gate(0, 0, 1, 1);
        Assert.True(CircuitId.TryFromCourse(new Course(g, g, [], "ES-jarama", "gp"), out var c));
        Assert.Equal("ES-jarama/gp", c.ToString());
        Assert.False(CircuitId.TryFromCourse(new Course(g, g, [], "My Track", null), out _));
        Assert.False(CircuitId.TryFromCourse(new Course(g, g, [], null, "gp"), out _));
        Assert.False(CircuitId.TryFromCourse(new Course(g, g, [], "ES-jarama", "Bad Variant"), out _));
    }
}
