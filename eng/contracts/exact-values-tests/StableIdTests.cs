using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Entity.Contracts.PlanAtp;
using CP6.Entity.Contracts.Wire;
using Xunit;
using Xunit.Abstractions;

namespace CP6.ExactValues.Tests;

public sealed class StableIdTests(ITestOutputHelper output)
{
    private static readonly string[] AssignedIds =
    [
        "NUM-017", "NUM-018", "NUM-019", "NUM-020", "NUM-021", "NUM-022", "NUM-023",
        "ATP-R1-001", "ATP-R1-002", "ATP-R1-003", "ATP-R1-004", "ATP-R1-005", "ATP-R1-006",
        "ATP-R1-007", "ATP-R1-008", "ATP-R1-009", "ATP-R1-010", "ATP-LEADING-001", "ATP-LEADING-002",
        "ATP-LEADING-003", "ATP-LEADING-004", "ATP-LEADING-005"
    ];

    public static IEnumerable<object[]> FixedVectors()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Inputs/vectors.json")));
        Assert.Equal("CP6-S01-CONTRACT-INPUTS/1.0.0", document.RootElement.GetProperty("package_version").GetString());
        var selected = document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("profile_id").GetString() == "atp-stableid-r1").ToArray();
        Assert.Equal(22, selected.Length);
        Assert.Equal(AssignedIds.Order(StringComparer.Ordinal), selected.Select(item => item.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
        foreach (var item in selected)
        {
            Assert.Equal("lexical", item.GetProperty("operation").GetString());
            yield return [item.GetProperty("id").GetString()!, item.GetProperty("input").GetString()!, item.GetProperty("expected").GetProperty("accepted").GetBoolean()];
        }
    }

    [Theory]
    [MemberData(nameof(FixedVectors))]
    public void Fixed_vector(string id, string input, bool accepted)
    {
        output.WriteLine($"{id}; C# lexical and string JSON; accepted={accepted}");
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input));
        if (!accepted)
        {
            Assert.ThrowsAny<ArgumentException>(() => StableId.Parse(input));
            Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<StableId>(json));
            return;
        }
        var value = StableId.Parse(input);
        Assert.Equal(input, value.Value);
        Assert.Equal(input, value.ToString());
        Assert.Equal(value, StableId.Parse(input));
        Assert.Equal(value, StrictJson.Deserialize<StableId>(json));
        Assert.Equal(JsonSerializer.Serialize(input), JsonSerializer.Serialize(value));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Explicit_length_boundary(int length, bool accepted)
    {
        var wire = new string('U', length);
        if (accepted) Assert.Equal(wire, StableId.Parse(wire).Value);
        else Assert.ThrowsAny<ArgumentException>(() => StableId.Parse(wire));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EA\n")]
    [InlineData("EA\r\n")]
    [InlineData("EA\r")]
    [InlineData("EA\u2028")]
    [InlineData("EA\u2029")]
    [InlineData("EA\0")]
    [InlineData(" EA")]
    [InlineData("EA ")]
    [InlineData("EA\t")]
    [InlineData(".EA")]
    [InlineData("_EA")]
    [InlineData(":EA")]
    [InlineData("/EA")]
    [InlineData("-EA")]
    [InlineData("公斤")]
    [InlineData("ＥＡ")]
    [InlineData("A😀")]
    [InlineData("é")]
    public void Invalid_lexical_value_is_not_normalized(string? input) =>
        Assert.ThrowsAny<ArgumentException>(() => StableId.Parse(input));

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Wrong_json_token_rejects(string json) =>
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<StableId>(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void String_roundtrip_preserves_case_punctuation_and_full_length()
    {
        var wire = "Az09._:/-" + new string('X', 119);
        Assert.Equal(128, wire.Length);
        Assert.Equal(wire, StableId.Parse(wire).Value);
        Assert.NotEqual(StableId.Parse("EA"), StableId.Parse("ea"));
        Assert.Equal("ATP-STABLEID-LEXICAL/20261011-R1", StableId.ProfileVersion);
        Assert.Equal("EA", StrictJson.Deserialize<StableId>(Encoding.UTF8.GetBytes("\"\\u0045A\""))!.Value);
    }

    [Theory]
    [InlineData("{\"id\":\"EA\",\"\\u0069d\":\"KG\"}")]
    [InlineData("{\"id\":\"EA\",\"extra\":[{\"x\":0,\"x\":0}]}")]
    [InlineData("{\"id\":null}")]
    [InlineData("{}")]
    public void Required_carrier_uses_production_strict_boundary(string json) =>
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<Carrier>(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Typed_carrier_keeps_original_bytes_separate_from_string_emission()
    {
        var original = Encoding.UTF8.GetBytes(" { \"id\": \"\\u0045A\" }\r\n");
        using var parsed = StrictJson.Parse(original);
        Assert.Equal("EA", parsed.Deserialize<Carrier>()!.Id.Value);
        Assert.Equal(original, parsed.GetOriginalUtf8());
        Assert.Equal("{\"id\":\"EA\"}", JsonSerializer.Serialize(parsed.Deserialize<Carrier>()));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Serialize<StableId>(null!));
    }

    private sealed class Carrier
    {
        [JsonPropertyName("id")]
        public required StableId Id { get; init; }
    }
}
