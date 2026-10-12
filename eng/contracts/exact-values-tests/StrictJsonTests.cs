using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Entity.Contracts.Wire;
using Xunit;
using Xunit.Abstractions;

namespace CP6.ExactValues.Tests;

public sealed class StrictJsonTests(ITestOutputHelper output)
{
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    public static IEnumerable<object[]> FixedVectors()
    {
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Inputs/vectors.json")));
        using var dispatch = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Inputs/dispatch.json")));
        var task = dispatch.RootElement.GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "S01-02");
        var ids = task.GetProperty("case_ids").EnumerateArray().Select(id => id.GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(21, ids.Count);
        var selected = vectors.RootElement.GetProperty("cases").EnumerateArray()
            .Where(v => ids.Contains(v.GetProperty("id").GetString()!)).ToArray();
        Assert.Equal(ids.Order(), selected.Select(v => v.GetProperty("id").GetString()!).Order());
        foreach (var vector in selected)
            yield return [vector.GetProperty("id").GetString()!, vector.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(FixedVectors))]
    public void Fixed_package_vector(string id, string rawVector)
    {
        using var vectorDocument = JsonDocument.Parse(rawVector);
        var vector = vectorDocument.RootElement;
        var input = vector.GetProperty("input");
        var expected = vector.GetProperty("expected");
        output.WriteLine($"Case {id}; runtime {RuntimeInformation.FrameworkDescription}; TFM {AppContext.TargetFrameworkName}");
        switch (vector.GetProperty("operation").GetString())
        {
            case "strict_json_parse":
                AssertAcceptance(expected.GetProperty("accepted").GetBoolean(), () =>
                {
                    using var parsed = StrictJson.Parse(Utf8(input.GetString()!));
                });
                break;
            case "required_null_property":
                Assert.Equal("fixedNull", input.GetProperty("required_property").GetString());
                AssertAcceptance(expected.GetProperty("accepted").GetBoolean(), () =>
                    StrictJson.Deserialize<NullCarrier>(Utf8(input.GetProperty("raw_json").GetString()!)));
                break;
            case "json_semantic_equality":
                Assert.Equal(expected.GetProperty("equal").GetBoolean(), Equal(
                    input.GetProperty("left_raw_json").GetString()!, input.GetProperty("right_raw_json").GetString()!));
                break;
            default:
                Assert.Fail($"Unsupported operation in {id}");
                break;
        }
    }

    private static void AssertAcceptance(bool accepted, Action action)
    {
        if (accepted) action();
        else Assert.ThrowsAny<JsonException>(action);
    }

    private static bool Equal(string left, string right)
    {
        using var l = StrictJson.Parse(Utf8(left));
        using var r = StrictJson.Parse(Utf8(right));
        var result = JsonSemanticEquality.AreEqual(l.RootElement, r.RootElement);
        Assert.Equal(result, JsonSemanticEquality.Key(l.RootElement) == JsonSemanticEquality.Key(r.RootElement));
        return result;
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/*comment*/{}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1,]")]
    [InlineData("{}{}")]
    [InlineData("null true")]
    [InlineData("{\"a\":01}")]
    [InlineData("{\"a\":NaN}")]
    [InlineData("{\"a\":1")]
    [InlineData("\"\\x20\"")]
    [InlineData("\"line\nbreak\"")]
    public void Invalid_json_is_rejected(string json) => Assert.ThrowsAny<JsonException>(() => StrictJson.Parse(Utf8(json)));

    [Theory]
    [InlineData("[[],{\"x\":[{\"a\":null,\"\\u0061\":true}]}]")]
    [InlineData("{\"😀\":1,\"\\ud83d\\ude00\":2}")]
    [InlineData("{\"\\uD800\":1,\"\\ud800\":2}")]
    [InlineData("{\"a/b\":1,\"a\\/b\":2}")]
    [InlineData("{\"a\\u0000b\":1,\"a\\u0000b\":2}")]
    public void Decoded_duplicate_names_are_rejected(string json) => Assert.ThrowsAny<JsonException>(() => StrictJson.Parse(Utf8(json)));

    [Fact]
    public void Duplicate_scan_finishes_before_any_converter_runs()
    {
        var converter = new TrackingConverter();
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<Tracked>(Utf8("{\"ignored\":0,\"nested\":[{\"a\":1,\"a\":2}]}"), options));
        Assert.Equal(0, converter.Calls);
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<NullCarrier>(Utf8("{\"fixedNull\":0,\"x\":1,\"x\":2}")));
    }

    [Fact]
    public void Serializer_options_cannot_relax_the_strict_boundary()
    {
        var options = new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<object>(Utf8("{/*x*/\"a\":1,}"), options));
        Assert.True(options.AllowTrailingCommas);
        Assert.Equal(JsonCommentHandling.Skip, options.ReadCommentHandling);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    [InlineData("[{\"x\":1},{\"x\":2}]")]
    [InlineData("{\"é\":1,\"e\\u0301\":2,\"A\":3,\"a\":4}")]
    public void Valid_values_and_distinct_names_are_accepted(string json)
    {
        using var parsed = StrictJson.Parse(Utf8(json));
        Assert.Equal(Utf8(json), parsed.GetOriginalUtf8());
    }

    [Fact]
    public void Original_bytes_are_owned_and_returned_without_reencoding()
    {
        var original = Utf8(" \r\n{ \"b\": 1.00, \"a\":\"\\u0061\" }\t");
        var callerBuffer = original.ToArray();
        using var parsed = StrictJson.Parse(callerBuffer);
        Array.Fill(callerBuffer, (byte)'x');
        var returned = parsed.GetOriginalUtf8();
        Assert.Equal(original, returned);
        Array.Fill(returned, (byte)'y');
        Assert.Equal(original, parsed.GetOriginalUtf8());
        Assert.Equal("1.00", parsed.RootElement.GetProperty("b").GetRawText());
        using var reordered = StrictJson.Parse(Utf8("{\"a\":\"a\",\"b\":1}"));
        Assert.True(JsonSemanticEquality.AreEqual(parsed.RootElement, reordered.RootElement));
        Assert.False(parsed.GetOriginalUtf8().SequenceEqual(reordered.GetOriginalUtf8()));
    }

    [Fact]
    public void Required_null_uses_metadata_and_round_trips()
    {
        var carrier = StrictJson.Deserialize<NullCarrier>(Utf8("{\"fixedNull\":null}"));
        Assert.Equal("{\"fixedNull\":null}", JsonSerializer.Serialize(carrier));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<NullCarrier>(Utf8("{}")));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<NullCarrier>(Utf8("{\"fixedNull\":[]}")));
        Assert.NotNull(StrictJson.Deserialize<OptionalNullCarrier>(Utf8("{}")));
        Assert.Equal(default, JsonSerializer.Deserialize<NullOnly>("null"));
        Assert.Equal("null", JsonSerializer.Serialize(default(NullOnly)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("\"null\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Null_only_converter_itself_rejects_other_tokens(string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<NullOnly>(json));

    [Fact]
    public void Depth_above_default64_is_supported_without_recursive_comparison()
    {
        const int depth = 2048;
        var left = new string('[', depth) + "{\"a\":1,\"b\":2}" + new string(']', depth);
        var right = new string('[', depth) + "{\"b\":2.0,\"a\":1e0}" + new string(']', depth);
        Assert.True(Equal(left, right));
        Assert.NotNull(StrictJson.Deserialize<object>(Utf8(left)));
        var duplicate = new string('[', depth) + "{\"a\":1,\"a\":2}" + new string(']', depth);
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse(Utf8(duplicate)));
    }

    [Theory]
    [InlineData("1e99999999999999999999999999999999999999", "10e99999999999999999999999999999999999998", true)]
    [InlineData("1e-99999999999999999999999999999999999999", "0.1e-99999999999999999999999999999999999998", true)]
    [InlineData("1e99999999999999999999999999999999999999", "1e99999999999999999999999999999999999998", false)]
    [InlineData("9007199254740992", "9007199254740993", false)]
    [InlineData("-1.20E+2", "-120", true)]
    [InlineData("1", "-1", false)]
    [InlineData("0.000001", "1e-6", true)]
    [InlineData("[]", "{}", false)]
    [InlineData("[1]", "[1,1]", false)]
    [InlineData("true", "false", false)]
    [InlineData("null", "false", false)]
    [InlineData("\"é\"", "\"e\\u0301\"", false)]
    [InlineData("\"😀\"", "\"\\ud83d\\ude00\"", true)]
    [InlineData("\"\\uD800\"", "\"\\ud800\"", true)]
    [InlineData("\"\\uD800\"", "\"\\uD801\"", false)]
    [InlineData("{\"\\uD800\":1}", "{\"\\ud800\":1.0}", true)]
    [InlineData("{\"\\uD800\":1}", "{\"\\uD801\":1}", false)]
    [InlineData("{\"a\":\"b:c\"}", "{\"a:b\":\"c\"}", false)]
    [InlineData("[\"a,b\",\"c\"]", "[\"a\",\"b,c\"]", false)]
    public void Exact_semantics_cover_extreme_exponents_types_and_unicode(string left, string right, bool equal) =>
        Assert.Equal(equal, Equal(left, right));

    [Fact]
    public void Invalid_utf8_is_not_replaced_with_a_different_string()
    {
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse(new byte[] { 34, 0xff, 34 }));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse(new byte[] { 34, 0xed, 0xa0, 0x80, 34 }));
    }

    [Fact]
    public void Semantic_key_rejects_ambiguous_external_documents()
    {
        using var external = JsonDocument.Parse("{\"a\":1,\"a\":2}");
        Assert.ThrowsAny<JsonException>(() => JsonSemanticEquality.Key(external.RootElement));
        Assert.ThrowsAny<JsonException>(() => JsonSemanticEquality.Key(default));
    }

    [Fact]
    public void Tests_execute_on_actual_net8_runtime()
    {
        output.WriteLine(RuntimeInformation.FrameworkDescription);
        output.WriteLine(AppContext.TargetFrameworkName);
        Assert.Equal(8, Environment.Version.Major);
        Assert.Equal(".NETCoreApp,Version=v8.0", AppContext.TargetFrameworkName);
    }

    public sealed class NullCarrier
    {
        [JsonPropertyName("fixedNull")]
        public required NullOnly FixedNull { get; init; }
    }

    public sealed class OptionalNullCarrier
    {
        public NullOnly Value { get; init; }
    }

    public sealed class Tracked;
    private sealed class TrackingConverter : JsonConverter<Tracked>
    {
        public int Calls { get; private set; }
        public override Tracked Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Calls++;
            reader.Skip();
            return new Tracked();
        }
        public override void Write(Utf8JsonWriter writer, Tracked value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
}
