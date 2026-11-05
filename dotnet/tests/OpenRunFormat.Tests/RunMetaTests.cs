using System.Text;
using OpenRunFormat.Meta;

namespace OpenRunFormat.Tests;

public class RunMetaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orf-runmeta-" + Guid.NewGuid().ToString("N"));

    public RunMetaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static RunHeader Header(string id = "run-1") => new(
        RunFormat.Version, 1_700_000_000_000_000_000, null, id, null,
        new Provenance(SourceKind.Synthetic, null, null, null, [], null),
        new Dictionary<string, string>());

    private static RunDocument Document(string eventText, string id = "run-1") => new(
        Header(id),
        new Dictionary<string, ChannelData>(),
        null,
        [],
        [new RunEvent("note", 0, null, eventText)],
        null);

    private static RunMeta Sample(string runlogSha256 = "ABCDEF0123456789") => new(
        RunFormat.Version, "run-1", runlogSha256,
        new Dictionary<string, string> { ["model"] = "demo hatchback", ["tyres"] = "Ñ 245/40" },
        [new SetupInterval(100, 200, new Dictionary<string, string> { ["pressure.fl"] = "2.1" })],
        [new Note(150, "Café, frenada tardía")],
        [new Segment("lap", 100, 200, 5_000_000_000, 15.5, [150L], "user")]);

    [Fact]
    public void Round_trip_all_sections()
    {
        var path = Path.Combine(_dir, "run.runmeta");
        RunMetaFile.Write(path, Sample());

        var read = RunMetaFile.Read(path);

        Assert.Equal(RunFormat.Version, read.Orf);
        Assert.Equal("run-1", read.Id);
        Assert.Equal("ABCDEF0123456789", read.RunlogSha256);
        Assert.Equivalent(new Dictionary<string, string> { ["model"] = "demo hatchback", ["tyres"] = "Ñ 245/40" }, read.Vehicle);
        var interval = Assert.Single(read.Setup);
        Assert.Equal(100, interval.S);
        Assert.Equal(200, interval.E);
        Assert.Equal("2.1", interval.Values["pressure.fl"]);
        var note = Assert.Single(read.Notes);
        Assert.Equal(150, note.T);
        Assert.Equal("Café, frenada tardía", note.Text);
        var seg = Assert.Single(read.UserSegments);
        Assert.Equal("lap", seg.Kind);
        Assert.Equal(100, seg.S);
        Assert.Equal(200, seg.E);
        Assert.Equal(5_000_000_000, seg.OfficialTimeNs);
        Assert.Equal(15.5, seg.ToleranceMs);
        Assert.Equal([150L], seg.Splits);
        Assert.Equal("user", seg.Src);

        // Indented JSON, UTF-8 without BOM, non-ASCII written raw.
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\n  \"", text);
        Assert.Contains("Café", text);
        Assert.Contains("Ñ", text);
    }

    [Fact]
    public void Matches_true_for_same_id_and_hash()
    {
        var meta = Sample("abcdef0123456789");
        Assert.True(meta.Matches(Header("run-1"), "ABCDEF0123456789"));
        Assert.True(meta.Matches(Header("run-1"), "abcdef0123456789"));
    }

    [Fact]
    public void Matches_false_for_other_id()
    {
        var meta = Sample("abcdef0123456789");
        Assert.False(meta.Matches(Header("run-2"), "abcdef0123456789"));
    }

    [Fact]
    public void Matches_false_when_runlog_changed()
    {
        var runlog = Path.Combine(_dir, "run.runlog");
        Runlog.Write(runlog, Document("first"));
        var hashBefore = RunMetaFile.Sha256Of(runlog);
        var header = Document("first").Header;
        var meta = new RunMeta(RunFormat.Version, header.Id, hashBefore,
            new Dictionary<string, string>(), [], [], []);
        Assert.True(meta.Matches(header, RunMetaFile.Sha256Of(runlog)));

        Runlog.Write(runlog, Document("second"));

        Assert.False(meta.Matches(header, RunMetaFile.Sha256Of(runlog)));
    }

    [Fact]
    public void Empty_sections_are_valid()
    {
        var path = Path.Combine(_dir, "empty.runmeta");
        var meta = new RunMeta(RunFormat.Version, "run-1", "00", new Dictionary<string, string>(), [], [], []);
        RunMetaFile.Write(path, meta);

        var read = RunMetaFile.Read(path);

        Assert.Empty(read.Vehicle);
        Assert.Empty(read.Setup);
        Assert.Empty(read.Notes);
        Assert.Empty(read.UserSegments);
    }

    [Fact]
    public void PathFor_replaces_extension()
    {
        Assert.Equal(Path.Combine("a", "b.runmeta"), RunMetaFile.PathFor(Path.Combine("a", "b.runlog")));
    }

    [Fact]
    public void Read_rejects_major_version_2()
    {
        var path = Path.Combine(_dir, "v2.runmeta");
        File.WriteAllText(path, """{"orf":"2.0","id":"run-1","runlogSha256":"00"}""");

        Assert.Throws<FormatException>(() => RunMetaFile.Read(path));
    }

    [Fact]
    public void Read_rejects_missing_required_id()
    {
        var path = Path.Combine(_dir, "noid.runmeta");
        File.WriteAllText(path, """{"orf":"1.0","runlogSha256":"00"}""");

        Assert.Throws<FormatException>(() => RunMetaFile.Read(path));
    }

    [Fact]
    public void Read_ignores_unknown_top_level_and_nested_fields()
    {
        var path = Path.Combine(_dir, "unknown.runmeta");
        File.WriteAllText(path, """
            {"orf":"1.3","id":"run-1","runlogSha256":"00","future":{"a":1},
             "setup":[{"s":1,"e":2,"values":{"k":"v"},"extra":true}],
             "notes":[{"t":5,"text":"hi","mood":"x"}],
             "userSegments":[{"kind":"lap","s":1,"e":2,"src":"user","colour":"red"}]}
            """);

        var read = RunMetaFile.Read(path);

        Assert.Equal("run-1", read.Id);
        Assert.Equal("v", Assert.Single(read.Setup).Values["k"]);
        Assert.Equal("hi", Assert.Single(read.Notes).Text);
        Assert.Equal("lap", Assert.Single(read.UserSegments).Kind);
    }

    private static string WriteRaw(string dir, string json)
    {
        var path = Path.Combine(dir, "raw.runmeta");
        File.WriteAllText(path, json);
        return path;
    }

    private static string RawWithDrivers(string orf, string driversJson) =>
        $$"""{"orf":"{{orf}}","id":"run-1","runlogSha256":"00","vehicle":{},"setup":[],"drivers":{{driversJson}},"notes":[],"userSegments":[]}""";

    [Fact]
    public void Drivers_ida_y_vuelta()
    {
        var path = Path.Combine(_dir, "drv.runmeta");
        var meta = Sample() with
        {
            Drivers = [new DriverInterval(0, 10, "id-a", "Fer", "FZ"), new DriverInterval(10, 20, "id-b", "Juan")],
        };
        RunMetaFile.Write(path, meta);

        var read = RunMetaFile.Read(path);

        Assert.Equal(meta.Drivers, read.Drivers);
        var json = File.ReadAllText(path);
        Assert.Contains("\"drivers\"", json);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var second = doc.RootElement.GetProperty("drivers")[1];
        Assert.False(second.TryGetProperty("initials", out _));
        Assert.True(doc.RootElement.GetProperty("drivers")[0].TryGetProperty("initials", out _));
    }

    [Fact]
    public void Sin_drivers_lee_lista_vacia()
    {
        var path = WriteRaw(_dir, """{"orf":"1.0","id":"run-1","runlogSha256":"00"}""");
        Assert.Empty(RunMetaFile.Read(path).Drivers);
    }

    [Fact]
    public void Drivers_solapados_se_ignoran()
    {
        var path = WriteRaw(_dir, RawWithDrivers("1.1",
            """[{"s":0,"e":10,"id":"a","name":"A"},{"s":5,"e":15,"id":"b","name":"B"}]"""));
        Assert.Empty(RunMetaFile.Read(path).Drivers);
    }

    [Fact]
    public void Driver_sin_id_o_name_se_ignora_el_campo()
    {
        var path = WriteRaw(_dir, RawWithDrivers("1.1", """[{"s":0,"e":10,"name":"A"}]"""));
        Assert.Empty(RunMetaFile.Read(path).Drivers);
    }

    [Theory]
    [InlineData("""[{"s":0,"e":10,"id":"","name":"A"}]""")]
    [InlineData("""[{"s":0,"e":10,"id":"a","name":""}]""")]
    public void Driver_con_id_o_name_vacio_se_ignora_el_campo(string drivers)
    {
        var path = WriteRaw(_dir, RawWithDrivers("1.1", drivers));
        Assert.Empty(RunMetaFile.Read(path).Drivers);
    }

    [Fact]
    public void Driver_con_initials_no_cadena_se_ignora_el_campo()
    {
        var path = WriteRaw(_dir, RawWithDrivers("1.1", """[{"s":0,"e":10,"id":"a","name":"A","initials":5}]"""));
        Assert.Empty(RunMetaFile.Read(path).Drivers);
    }

    [Fact]
    public void Escribe_orf_1_2()
    {
        var path = Path.Combine(_dir, "v.runmeta");
        RunMetaFile.Write(path, Sample());
        Assert.Contains("\"orf\": \"1.2\"", File.ReadAllText(path));

        var future = WriteRaw(_dir, RawWithDrivers("1.7", "[]"));
        Assert.Equal("1.7", RunMetaFile.Read(future).Orf);
    }
}
