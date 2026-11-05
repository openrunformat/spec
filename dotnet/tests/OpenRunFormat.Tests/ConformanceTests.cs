using System.Text.Json;
using OpenRunFormat.Meta;
using Json.Schema;

namespace OpenRunFormat.Tests;

public class ConformanceTests
{
    private static readonly Lazy<JsonSchema> RunlogSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema", "runlog.schema.json"))));

    private static string Root => Path.Combine(AppContext.BaseDirectory, "conformance");

    public static IEnumerable<TheoryDataRow<string>> ValidFiles() => Files("valid", f => f.Contains(".runlog"));
    public static IEnumerable<TheoryDataRow<string>> InvalidFiles() => Files("invalid", f => f.Contains(".runlog"));
    // Files named "*-malformed-ignored" test that a reader ignores a malformed optional field: they are valid for a reader
    // but are not what a writer may produce, so they are not checked against the schema.
    public static IEnumerable<TheoryDataRow<string>> ValidJsonlFiles() =>
        Files("valid", f => f.EndsWith(".runlog.jsonl") && !f.Contains("-malformed-ignored"));

    private static IEnumerable<TheoryDataRow<string>> Files(string dir, Func<string, bool> filter)
    {
        var path = Path.Combine(Root, dir);
        var files = Directory.Exists(path) ? Directory.GetFiles(path).Where(f => !f.EndsWith(".expected.json") && filter(f)) : [];
        var any = false;
        foreach (var f in files.OrderBy(f => f, StringComparer.Ordinal)) { any = true; yield return new TheoryDataRow<string>(Path.GetFileName(f)); }
        if (!any) yield return new TheoryDataRow<string>("<no conformance files found>");
    }

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void Valid_file_reads_as_expected(string name)
    {
        var path = Path.Combine(Root, "valid", name);
        var expected = JsonDocument.Parse(File.ReadAllText(path[..path.IndexOf(".runlog", StringComparison.Ordinal)] + ".expected.json")).RootElement;

        var doc = Runlog.Read(path);

        Assert.Equal(expected.GetProperty("complete").GetBoolean(), doc.Complete);
        var expectTrailing = expected.TryGetProperty("trailingContent", out var tc) && tc.GetBoolean();
        Assert.Equal(expectTrailing, doc.HasTrailingContent);
        Assert.Equal(expected.GetProperty("segments").GetInt32(), doc.Segments.Count);
        Assert.Equal(expected.GetProperty("kind").GetString(), doc.Header.Provenance.Kind.ToString().ToLowerInvariant());
        var channels = expected.GetProperty("channels");
        Assert.Equal(channels.EnumerateObject().Count(), doc.Channels.Count);
        foreach (var c in channels.EnumerateObject())
        {
            var data = doc.Channels[c.Name];
            // A channel entry is a sample count, or an object { "n", "times"?, "values"? }.
            if (c.Value.ValueKind == JsonValueKind.Number)
            {
                Assert.Equal(c.Value.GetInt32(), data.Values.Length);
                continue;
            }
            Assert.Equal(c.Value.GetProperty("n").GetInt32(), data.Values.Length);
            Assert.Equal(data.Times.Length, data.Values.Length);
            if (c.Value.TryGetProperty("times", out var times))
                Assert.Equal(times.EnumerateArray().Select(x => x.GetInt64()).ToArray(), data.Times);
            if (c.Value.TryGetProperty("values", out var values))
            {
                var exp = values.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? double.NaN : x.GetDouble()).ToArray();
                Assert.Equal(exp.Length, data.Values.Length);
                for (int i = 0; i < exp.Length; i++)
                    Assert.True(exp[i].Equals(data.Values[i]), $"{name} channel {c.Name} value {i}: expected {exp[i]}, got {data.Values[i]}.");
            }
        }
        if (expected.TryGetProperty("end", out var end))
        {
            Assert.NotNull(doc.End);
            Assert.Equal(end.GetProperty("dur").GetInt64(), doc.End!.DurationNs);
            Assert.Equal(end.GetProperty("n").GetInt64(), doc.End.Samples);
        }
        else Assert.Null(doc.End);
        if (expected.TryGetProperty("derivedFrom", out var df))
        {
            if (df.ValueKind == JsonValueKind.Null) Assert.Null(doc.Header.DerivedFrom);
            else
            {
                Assert.NotNull(doc.Header.DerivedFrom);
                Assert.Equal(df.GetProperty("id").GetString(), doc.Header.DerivedFrom!.Id);
                Assert.Equal(df.GetProperty("s").GetInt64(), doc.Header.DerivedFrom.S);
                Assert.Equal(df.GetProperty("e").GetInt64(), doc.Header.DerivedFrom.E);
            }
        }
        if (expected.TryGetProperty("course", out var ec))
        {
            Assert.NotNull(doc.Course);
            static string? S(JsonElement e, string n) => e.GetProperty(n).GetString();
            Assert.Equal(S(ec, "circuit"), doc.Course!.CircuitRef);
            Assert.Equal(S(ec, "variant"), doc.Course.Variant);
            Assert.Equal(S(ec, "circuitUid"), doc.Course.CircuitUid?.ToString("D"));
            Assert.Equal(S(ec, "variantUid"), doc.Course.VariantUid?.ToString("D"));
            Assert.Equal(S(ec, "gates"), doc.Course.Gates);
            Assert.Equal(ec.GetProperty("circuitIdValid").GetBoolean(), CircuitId.TryFromCourse(doc.Course, out _));
        }
    }

    public static IEnumerable<TheoryDataRow<string>> ValidRunmetaFiles()
    {
        var path = Path.Combine(Root, "runmeta", "valid");
        var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.runmeta") : [];
        var any = false;
        foreach (var f in files.OrderBy(f => f, StringComparer.Ordinal)) { any = true; yield return new TheoryDataRow<string>(Path.GetFileName(f)); }
        if (!any) yield return new TheoryDataRow<string>("<no conformance files found>");
    }

    [Theory]
    [MemberData(nameof(ValidRunmetaFiles))]
    public void Runmeta_valid_cumple_expected(string name)
    {
        var path = Path.Combine(Root, "runmeta", "valid", name);
        var expected = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path, ".expected.json"))).RootElement;

        var meta = RunMetaFile.Read(path);

        Assert.Equal(expected.GetProperty("drivers").GetInt32(), meta.Drivers.Count);
    }

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void Invalid_file_is_rejected(string name)
    {
        var path = Path.Combine(Root, "invalid", name);
        Assert.Throws<FormatException>(() => Runlog.Read(path));
    }

    [Theory]
    [MemberData(nameof(ValidJsonlFiles))]
    public void Valid_files_match_json_schema(string name)
    {
        var schema = RunlogSchema.Value;
        var text = File.ReadAllText(Path.Combine(Root, "valid", name));
        var all = text.Split('\n');
        // A file cut in the middle of a line (no final LF) has a last line that is not JSON by design.
        var lines = (text.EndsWith('\n') ? all : all[..^1]).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        Assert.NotEmpty(lines);
        for (int i = 0; i < lines.Length; i++)
        {
            using var json = JsonDocument.Parse(lines[i]);
            var result = schema.Evaluate(json.RootElement);
            Assert.True(result.IsValid, $"{name} line {i + 1} does not match the schema.");
        }
    }

    [Fact]
    public void Conformance_files_are_present()
    {
        Assert.True(Directory.Exists(Path.Combine(Root, "valid")));
        Assert.True(Directory.GetFiles(Path.Combine(Root, "valid"), "*.runlog*").Length >= 13);
        Assert.True(Directory.GetFiles(Path.Combine(Root, "invalid")).Length >= 19);
    }

    [Theory]
    [InlineData("""{"k":"h","orf":"1.0","utc":0,"id":"x","kind":"imaginary"}""")]
    [InlineData("""{"k":"h","orf":"1.2","utc":0,"id":"x","kind":"real","derivedFrom":{"id":"","s":0,"e":1}}""")]
    [InlineData("""{"k":"h","orf":"1.2","utc":0,"id":"x","kind":"real","derivedFrom":{"id":"a","s":0}}""")]
    [InlineData("""{"k":"d","ch":"speed","t0":0}""")]
    [InlineData("""{"k":"ch","id":"speed","unit":"m/s","hz":0}""")]
    [InlineData("""{"k":"seg","kind":"lap","s":0,"e":1}""")]
    public void Schema_rejects_malformed_records(string line)
    {
        using var json = JsonDocument.Parse(line);
        Assert.False(RunlogSchema.Value.Evaluate(json.RootElement).IsValid);
    }
}
