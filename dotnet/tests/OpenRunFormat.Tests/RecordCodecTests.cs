using OpenRunFormat.Json;

namespace OpenRunFormat.Tests;

public class RecordCodecTests
{
    private static RunHeader FullHeader() => new(
        "1.0", 1_700_000_000_000_000_000, "Europe/Madrid", "s1", "car",
        new Provenance(SourceKind.Real, "Phone", new ToolRef("conv", "1.2.0"), null, [], new Precision(500, 1)),
        new Dictionary<string, string>());

    [Fact]
    public void Header_encodes_exact_compact_json()
    {
        Assert.Equal(
            """{"k":"h","orf":"1.0","utc":1700000000000000000,"tz":"Europe/Madrid","id":"s1","discipline":"car","kind":"real","device":"Phone","converter":{"id":"conv","version":"1.2.0"},"precision":{"segTolMs":500,"gpsHz":1}}""",
            RecordCodec.Encode(FullHeader()));
    }

    public static TheoryData<object> AllRecords() => new()
    {
        new RunHeader("1.0", 5, "UTC", "id9", "kart",
            new Provenance(SourceKind.Sim, "dev", new ToolRef("c", "1"), new ToolRef("w", "2"),
                [new SourceFileRef("a.csv", "ab12")], new Precision(250.5, 10)),
            new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" }),
        new RunHeader("1.0", 5, null, "id", null,
            new Provenance(SourceKind.Synthetic, null, null, null, [], null), new Dictionary<string, string>()),
        new ChannelDef("speed", "m/s", "Speed", 10),
        new ChannelDef("gps", "deg", null, null),
        new SampleBlock("speed", 1000, [1.5, double.NaN, 3], null),
        new SampleBlock("gps", 1000, [1, 2], [1000L, 2500L]),
        new Course(new Gate(1, 2, 3, 4), new Gate(5, 6, 7, 8), [new Gate(9, 10, 11, 12)], "ZZ-circuit-x", "short",
            Guid.Parse("0192f0c4-8b7a-7c3e-9a1d-3f5e6b7c8d9e"), Guid.Parse("0192f0c4-8b7a-7c3e-9a1d-3f5e6b7c8d9f"), CourseGates.Origin),
        new Course(new Gate(1, 2, 3, 4), new Gate(5, 6, 7, 8), [], null, null),
        new Segment("lap", 100, 200, 99, 0.5, [150L, 175L], "calc"),
        new Segment("out", 100, 200, null, null, [], "official"),
        new RunEvent("note", 10, 20, "hi"),
        new RunEvent("mark", 10, null, null),
        new EndRecord(123456789, 42),
    };

    [Theory]
    [MemberData(nameof(AllRecords))]
    public void Every_record_type_round_trips(object record)
    {
        var json = RecordCodec.Encode(record);
        var back = RecordCodec.Decode(json);
        Assert.NotNull(back);
        Assert.Equal(record.GetType(), back!.GetType());
        // Re-encoding must be identical (covers arrays/lists/dictionaries without reference equality).
        Assert.Equal(json, RecordCodec.Encode(back));
        AssertEquivalent(record, back);
    }

    private static void AssertEquivalent(object a, object b)
    {
        switch (a)
        {
            case SampleBlock sa:
                var sb = (SampleBlock)b;
                Assert.Equal(sa.Channel, sb.Channel);
                Assert.Equal(sa.T0, sb.T0);
                Assert.Equal(sa.Values.Length, sb.Values.Length);
                for (int i = 0; i < sa.Values.Length; i++)
                    Assert.True(sa.Values[i].Equals(sb.Values[i]));
                Assert.Equal(sa.Times is null, sb.Times is null);
                if (sa.Times is not null) Assert.True(sa.Times.SequenceEqual(sb.Times!));
                break;
            case RunHeader ha:
                var hb = (RunHeader)b;
                Assert.Equal(ha.Orf, hb.Orf);
                Assert.Equal(ha.Utc, hb.Utc);
                Assert.Equal(ha.Tz, hb.Tz);
                Assert.Equal(ha.Id, hb.Id);
                Assert.Equal(ha.Discipline, hb.Discipline);
                Assert.Equal(ha.Provenance.Kind, hb.Provenance.Kind);
                Assert.Equal(ha.Provenance.Device, hb.Provenance.Device);
                Assert.Equal(ha.Provenance.Converter, hb.Provenance.Converter);
                Assert.Equal(ha.Provenance.Writer, hb.Provenance.Writer);
                Assert.True(ha.Provenance.SourceFiles.SequenceEqual(hb.Provenance.SourceFiles));
                Assert.Equal(ha.Provenance.Precision, hb.Provenance.Precision);
                Assert.True(ha.Meta.OrderBy(p => p.Key).SequenceEqual(hb.Meta.OrderBy(p => p.Key)));
                break;
            case Course ca:
                var cb = (Course)b;
                Assert.Equal(ca.Start, cb.Start);
                Assert.Equal(ca.Finish, cb.Finish);
                Assert.True(ca.Splits.SequenceEqual(cb.Splits));
                Assert.Equal(ca.CircuitRef, cb.CircuitRef);
                Assert.Equal(ca.Variant, cb.Variant);
                Assert.Equal(ca.CircuitUid, cb.CircuitUid);
                Assert.Equal(ca.VariantUid, cb.VariantUid);
                Assert.Equal(ca.Gates, cb.Gates);
                break;
            case Segment ga:
                var gb = (Segment)b;
                Assert.Equal(ga.Kind, gb.Kind);
                Assert.Equal(ga.S, gb.S);
                Assert.Equal(ga.E, gb.E);
                Assert.Equal(ga.OfficialTimeNs, gb.OfficialTimeNs);
                Assert.Equal(ga.ToleranceMs, gb.ToleranceMs);
                Assert.True(ga.Splits.SequenceEqual(gb.Splits));
                Assert.Equal(ga.Src, gb.Src);
                break;
            default:
                Assert.Equal(a, b); // ChannelDef, RunEvent, EndRecord: value equality
                break;
        }
    }

    private const string CourseHead = """{"k":"course","start":[1,2,3,4],"finish":[1,2,3,4]""";

    [Fact]
    public void Course_with_registry_ids_encodes_exact_json()
    {
        var c = new Course(new Gate(1, 2, 3, 4), new Gate(1, 2, 3, 4), [], "ES-jarama", "gp@2023",
            Guid.Parse("0192f0c4-8b7a-7c3e-9a1d-3f5e6b7c8d9e"), Guid.Parse("0192f0c4-8b7a-7c3e-9a1d-3f5e6b7c8d9f"), CourseGates.Registry);
        Assert.Equal(
            """{"k":"course","start":[1,2,3,4],"finish":[1,2,3,4],"circuit":"ES-jarama","variant":"gp@2023","circuitUid":"0192f0c4-8b7a-7c3e-9a1d-3f5e6b7c8d9e","variantUid":"0192f0c4-8b7a-7c3e-9a1d-3f5e6b7c8d9f","gates":"registry"}""",
            RecordCodec.Encode(c));
    }

    [Theory]
    [InlineData("My Track", null, null, false, "course.circuit")]
    [InlineData("ES-jarama", "GP", null, false, "course.variant")]
    [InlineData(null, "gp", null, false, "course.variant")]
    [InlineData("ES-jarama", null, null, true, "course.variantUid")]
    [InlineData("ES-jarama", "gp", "official", false, "course.gates")]
    public void Writer_rejects_invalid_course_ids(string? circuit, string? variant, string? gates, bool variantUid, string field)
    {
        var uid = Guid.Parse("0192f0c4-8b7a-7c3e-9a1d-3f5e6b7c8d9f");
        var c = new Course(new Gate(1, 2, 3, 4), new Gate(1, 2, 3, 4), [], circuit, variant,
            null, variantUid ? uid : null, gates);
        var ex = Assert.Throws<ArgumentException>(() => RecordCodec.Encode(c));
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Writer_rejects_circuit_uid_without_circuit()
    {
        var c = new Course(new Gate(1, 2, 3, 4), new Gate(1, 2, 3, 4), [], null, null, Guid.NewGuid());
        var ex = Assert.Throws<ArgumentException>(() => RecordCodec.Encode(c));
        Assert.Contains("course.circuitUid", ex.Message);
    }

    [Fact]
    public void Reader_keeps_free_text_circuit_as_opaque_string()
    {
        var c = Assert.IsType<Course>(RecordCodec.Decode(CourseHead + ""","circuit":"My Track"}"""));
        Assert.Equal("My Track", c.CircuitRef);
        Assert.False(CircuitId.TryFromCourse(c, out _));
    }

    [Fact]
    public void Reader_accepts_unknown_gates_value()
    {
        var c = Assert.IsType<Course>(RecordCodec.Decode(CourseHead + ""","gates":"survey"}"""));
        Assert.Equal("survey", c.Gates);
    }

    [Theory]
    [InlineData("\"circuitUid\":\"x\"")]
    [InlineData("\"circuitUid\":5")]
    [InlineData("\"variantUid\":\"x\"")]
    public void Reader_rejects_non_uuid_course_uid(string field)
        => Assert.Throws<FormatException>(() => RecordCodec.Decode(CourseHead + "," + field + "}"));

    [Fact]
    public void NaN_value_is_written_as_null_and_read_back_as_NaN()
    {
        var json = RecordCodec.Encode(new SampleBlock("c", 0, [1, double.NaN, 3], null));
        Assert.Equal("""{"k":"d","ch":"c","t0":0,"v":[1,null,3]}""", json);
        var back = (SampleBlock)RecordCodec.Decode(json)!;
        Assert.Equal(1, back.Values[0]);
        Assert.True(double.IsNaN(back.Values[1]));
        Assert.Equal(3, back.Values[2]);
    }

    [Fact]
    public void Infinity_value_throws_ArgumentException_on_encode()
    {
        Assert.Throws<ArgumentException>(() => RecordCodec.Encode(new SampleBlock("c", 0, [1, double.PositiveInfinity], null)));
        Assert.Throws<ArgumentException>(() => RecordCodec.Encode(new SampleBlock("c", 0, [double.NegativeInfinity], null)));
        Assert.Throws<ArgumentException>(() => RecordCodec.Encode(new ChannelDef("c", "u", null, double.PositiveInfinity)));
    }

    [Fact]
    public void Unknown_record_kind_decodes_to_null()
        => Assert.Null(RecordCodec.Decode("""{"k":"zz","x":1}"""));

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var ch = Assert.IsType<ChannelDef>(RecordCodec.Decode("""{"k":"ch","id":"a","unit":"m/s","foo":1}"""));
        Assert.Equal(new ChannelDef("a", "m/s", null, null), ch);
    }

    [Fact]
    public void Missing_required_field_throws_FormatException()
    {
        Assert.Throws<FormatException>(() => RecordCodec.Decode("""{"k":"ch","unit":"m/s"}"""));
        Assert.Throws<FormatException>(() => RecordCodec.Decode("""{"k":"end","dur":1}"""));
    }

    [Fact]
    public void Broken_json_throws_FormatException()
        => Assert.Throws<FormatException>(() => RecordCodec.Decode("{not json"));

    [Fact]
    public void SourceKind_rejects_unknown_value()
        => Assert.Throws<FormatException>(() =>
            RecordCodec.Decode("""{"k":"h","orf":"1.0","utc":1,"id":"x","kind":"fake"}"""));

    [Fact]
    public void Integral_doubles_are_written_without_decimal_point()
        => Assert.Contains("\"hz\":500", RecordCodec.Encode(new ChannelDef("a", "u", null, 500.0)));

    [Fact]
    public void Non_ascii_text_is_written_as_raw_utf8()
    {
        var h = FullHeader() with { Meta = new Dictionary<string, string> { ["circuit"] = "ジャラマ — Jarama ñ" } };
        var json = RecordCodec.Encode(h);
        Assert.Contains("ジャラマ — Jarama ñ", json);
        Assert.DoesNotContain("\\u", json);
        var back = (RunHeader)RecordCodec.Decode(json)!;
        Assert.Equal("ジャラマ — Jarama ñ", back.Meta["circuit"]);
    }

    [Fact]
    public void Precision_with_both_fields_null_is_not_written()
    {
        var h = FullHeader();
        h = h with { Provenance = h.Provenance with { Precision = new Precision(null, null) } };
        Assert.DoesNotContain("precision", RecordCodec.Encode(h));
    }
}
