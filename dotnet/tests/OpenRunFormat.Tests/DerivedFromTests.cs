using OpenRunFormat.Json;

namespace OpenRunFormat.Tests;

public class DerivedFromTests
{
    private static RunHeader Header(DerivedFrom? d) => new(
        "1.2", 1_700_000_000_000_000_000, null, "run-1@100-900", "car",
        new Provenance(SourceKind.Real, null, null, null, [], null), new Dictionary<string, string>(), d);

    [Fact]
    public void Writes_and_reads_derivedFrom()
    {
        var json = RecordCodec.Encode(Header(new DerivedFrom("run-1", 100, 900)));
        Assert.Contains("\"derivedFrom\":{\"id\":\"run-1\",\"s\":100,\"e\":900}", json);
        var back = Assert.IsType<RunHeader>(RecordCodec.Decode(json));
        Assert.Equal(new DerivedFrom("run-1", 100, 900), back.DerivedFrom);
    }

    [Fact]
    public void Without_derivedFrom_the_key_is_not_written()
    {
        var json = RecordCodec.Encode(Header(null));
        Assert.DoesNotContain("derivedFrom", json);
        Assert.Null(Assert.IsType<RunHeader>(RecordCodec.Decode(json)).DerivedFrom);
    }

    [Theory]
    [InlineData("""{"id":"","s":0,"e":10}""")]
    [InlineData("""{"id":5,"s":0,"e":10}""")]
    [InlineData("""{"id":"a","s":-1,"e":10}""")]
    [InlineData("""{"id":"a","s":10,"e":10}""")]
    [InlineData("""{"id":"a","s":20,"e":10}""")]
    [InlineData("""{"id":"a","e":10}""")]
    [InlineData("""{"id":"a","s":0}""")]
    [InlineData("""{"id":"a","s":1.5,"e":10}""")]
    [InlineData("""{"id":"a","s":"0","e":10}""")]
    [InlineData("\"run-1\"")]
    [InlineData("[]")]
    public void Malformed_derivedFrom_is_ignored(string value)
    {
        var json = $$"""{"k":"h","orf":"1.2","utc":0,"id":"x","kind":"real","derivedFrom":{{value}}}""";
        var back = Assert.IsType<RunHeader>(RecordCodec.Decode(json));
        Assert.Null(back.DerivedFrom);
        Assert.Equal("x", back.Id);
    }

    [Fact]
    public void Writer_rejects_a_malformed_derivedFrom()
    {
        Assert.ThrowsAny<Exception>(() => RecordCodec.Encode(Header(new DerivedFrom("run-1", 900, 100))));
        Assert.ThrowsAny<Exception>(() => RecordCodec.Encode(Header(new DerivedFrom("", 0, 100))));
    }
}
