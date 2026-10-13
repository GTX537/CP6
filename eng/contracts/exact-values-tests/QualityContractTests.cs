using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Entity.Contracts.Quality;
using CP6.Entity.Contracts.Quality.Main;
using CP6.Entity.Contracts.Quality.SourceQA;
using CP6.Entity.Contracts.Wire;
using Xunit;
using Xunit.Abstractions;

namespace CP6.ExactValues.Tests;

public sealed class QualityContractTests(ITestOutputHelper output)
{
    private static string RepositoryRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "eng/contracts/s01-07/csharp/generation-manifest.json")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("S01-07 portable repository inputs not found.");
        }
    }

    private static string Input(string path) => Path.Combine(RepositoryRoot, "eng/contracts/s01-07", path);
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static Type ContractType(string name) => typeof(QapUom).Assembly.GetType(name, throwOnError: true)!;
    private static string Adapt(string original) => original
        .Replace("QaNcrMainR6Declarations.", "CP6.Entity.Contracts.Quality.Main.", StringComparison.Ordinal)
        .Replace("SourceQA.", "CP6.Entity.Contracts.Quality.SourceQA.", StringComparison.Ordinal);

    private static object Decode(string type, string json)
    {
        return NcrJson.Deserialize(ContractType(type), Utf8(json));
    }

    public static IEnumerable<object[]> FullObjects()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Input("csharp/full-object-fixtures.json")));
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [fixture.GetProperty("id").GetString()!, fixture.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(FullObjects))]
    public void Complete_native_object_matches_pinned_schema(string id, string rawFixture)
    {
        using var document = JsonDocument.Parse(rawFixture);
        var fixture = document.RootElement;
        var type = fixture.GetProperty("type").GetString()!;
        var value = fixture.GetProperty("value");
        output.WriteLine($"{id}; source {fixture.GetProperty("source").GetString()}");
        if (!fixture.GetProperty("accepted").GetBoolean())
        {
            Assert.ThrowsAny<JsonException>(() => Decode(type, value.GetRawText()));
            return;
        }
        var native = Decode(type, value.GetRawText());
        Assert.NotNull(native);
        var encoded = JsonSerializer.Serialize(native, ContractType(type));
        using var roundtrip = StrictJson.Parse(Utf8(encoded));
        Assert.True(JsonSemanticEquality.AreEqual(value, roundtrip.RootElement));
        Assert.NotNull(Decode(type, encoded));
    }

    public static IEnumerable<object[]> FixedVectors()
    {
        var bytes = File.ReadAllBytes(Input("inputs/pins/vectors.json"));
        Assert.Equal("B9A63A1512DA2D2374E95D49D9F69C8636D06465F88A47862DC3604605D5BE01", Convert.ToHexString(SHA256.HashData(bytes)));
        using var document = JsonDocument.Parse(bytes);
        var selected = document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(v => v.GetProperty("id").GetString()!.StartsWith("NCR-NULL-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(Enumerable.Range(1, 20).Select(i => $"NCR-NULL-{i:000}"), selected.Select(v => v.GetProperty("id").GetString()));
        foreach (var vector in selected) yield return [vector.GetProperty("id").GetString()!, vector.GetRawText()];
    }

    private static JsonObject CompleteExample(string type)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Input("csharp/full-object-fixtures.json")));
        var fixture = document.RootElement.GetProperty("cases").EnumerateArray().First(f =>
            f.GetProperty("type").GetString() == type && f.GetProperty("id").GetString()!.EndsWith("/complete", StringComparison.Ordinal));
        return JsonNode.Parse(fixture.GetProperty("value").GetRawText())!.AsObject();
    }

    [Theory]
    [MemberData(nameof(FixedVectors))]
    public void Fixed_NCR_NULL_vector_is_executed_on_each_original_native_field(string id, string rawVector)
    {
        using var document = JsonDocument.Parse(rawVector);
        var vector = document.RootElement;
        Assert.Equal("FIELD_SUBASSERTION_ONLY", vector.GetProperty("expected").GetProperty("assertion_scope").GetString());
        var input = vector.GetProperty("input");
        var property = input.GetProperty("required_property").GetString()!;
        var projection = JsonNode.Parse(input.GetProperty("raw_json").GetString()!)!.AsObject();
        foreach (var field in vector.GetProperty("applicable_fields").EnumerateArray())
        {
            var original = field.GetString()!;
            var typeName = Adapt(original[..original.LastIndexOf('.')]);
            var nativeProperty = ContractType(typeName).GetProperties().Single(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == property);
            Assert.Equal(typeof(NullOnly), nativeProperty.PropertyType);
            Assert.NotNull(nativeProperty.GetCustomAttribute<RequiredMemberAttribute>());
            var complete = CompleteExample(typeName);
            if (projection.ContainsKey(property)) complete[property] = projection[property]?.DeepClone();
            else complete.Remove(property);
            output.WriteLine($"{id}: {original}; full native fixture and frozen field assertion");
            if (vector.GetProperty("expected").GetProperty("accepted").GetBoolean())
            {
                var native = Decode(typeName, complete.ToJsonString());
                using var result = JsonDocument.Parse(JsonSerializer.Serialize(native, ContractType(typeName)));
                Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty(property).ValueKind);
            }
            else Assert.ThrowsAny<JsonException>(() => Decode(typeName, complete.ToJsonString()));
        }
    }

    public static IEnumerable<object[]> NullOnlyFields()
    {
        for (var i = 0; i < 3; i++)
            yield return [$"CP6.Entity.Contracts.Quality.Main.Externalf70281e4_C09FailedProblem_Variant{i}_Payload", "originalQuery"];
        yield return ["CP6.Entity.Contracts.Quality.Main.External6d9d3eee__types_IpqcFinalGuardResult_Variant1_Payload", "guardRef"];
        yield return ["CP6.Entity.Contracts.Quality.SourceQA.IpqcFinalGuardResult_Case2", "guardRef"];
        yield return ["CP6.Entity.Contracts.Quality.SourceQA.ObservationInput_Case1", "code"];
        yield return ["CP6.Entity.Contracts.Quality.SourceQA.ObservationInput_Case2", "value"];
    }

    [Theory]
    [MemberData(nameof(NullOnlyFields))]
    public void Seven_required_null_fields_reject_string_null(string type, string property)
    {
        var complete = CompleteExample(type);
        complete[property] = "null";
        Assert.ThrowsAny<JsonException>(() => Decode(type, complete.ToJsonString()));
    }

    [Fact]
    public void Five_field_metadata_rows_retain_exact_required_nullable_and_editable_flags()
    {
        var rows = NcrFieldMetadata.Fields;
        Assert.Equal(5, rows.Count);
        Assert.All(rows, field => { Assert.True(field.Required); Assert.False(field.Editable); });
        Assert.False(rows.Single(f => f.Pointer == "#/$defs/MainResult/oneOf/14/properties/priorHeadRef").Nullable);
        foreach (var name in new[] { "sourceKind", "keyword", "cursor" })
            Assert.True(rows.Single(f => f.Pointer == $"#/$defs/InitialSourceOptionQuery/properties/{name}").Nullable);
        Assert.True(rows.Single(f => f.Pointer == "#/$defs/InitialSourceOptionPage/properties/nextCursor").Nullable);
    }

    [Fact]
    public void Native_query_rejects_undeclared_source_kind()
    {
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<Main_InitialSourceOptionQuery>(
            Encoding.UTF8.GetBytes("""{"sourceKind":"INVENTED","keyword":null,"pageSize":1,"cursor":null}""")));
    }

    [Fact]
    public void Native_qap_uom_rejects_owner_field_borrowed_from_a_reference()
    {
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<QapUom>(
            Encoding.UTF8.GetBytes("""{"id":"EA","version":"1","owner":"QAP"}""")));
    }

    [Theory]
    [InlineData("""{"id":"EA","id":"KG","version":"1"}""")]
    [InlineData("""{"id":"EA","\u0069d":"KG","version":"1"}""")]
    public void Direct_native_converter_rejects_duplicate_decoded_names(string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<QapUom>(json));

    [Fact]
    public void Nested_duplicates_are_rejected_before_branch_selection()
    {
        const string json = """{"value":"1.0","unit":{"kind":"QAP_UOM","value":{"id":"EA","\u0069d":"KG","version":"1"}}}""";
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<Main_Quantity>(json));
    }

    [Fact]
    public void Exact_original_decimal_string_and_nested_union_survive_roundtrip()
    {
        const string json = """{"value":"1.12345678","unit":{"kind":"QAP_UOM","value":{"id":"EA","version":"0"}}}""";
        var value = StrictJson.Deserialize<Main_Quantity>(Utf8(json))!;
        Assert.Equal("1.12345678", value.Value);
        Assert.IsType<Main_UnitIdentity_Variant1>(value.Unit);
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(value));
        Assert.Equal("QAP_UOM", encoded.RootElement.GetProperty("unit").GetProperty("kind").GetString());
        Assert.Equal("EA", encoded.RootElement.GetProperty("unit").GetProperty("value").GetProperty("id").GetString());
        Assert.False(encoded.RootElement.GetProperty("unit").TryGetProperty("Value", out _));
        var observation = CompleteExample("CP6.Entity.Contracts.Quality.SourceQA.ObservationInput_Case1");
        observation["value"] = "1.00000000";
        var originalObservation = NcrJson.Deserialize<ObservationInput>(Utf8(observation.ToJsonString()));
        using var observationEncoded = JsonDocument.Parse(NcrJson.Serialize(originalObservation));
        Assert.Equal("1.00000000", observationEncoded.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void Required_nullable_values_are_not_omitted_by_relaxed_caller_options()
    {
        var value = StrictJson.Deserialize<Main_InitialSourceOptionQuery>(Utf8("""{"sourceKind":null,"keyword":null,"pageSize":1e0,"cursor":null}"""))!;
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(value, options));
        Assert.Equal(JsonValueKind.Null, encoded.RootElement.GetProperty("sourceKind").ValueKind);
        Assert.Equal(JsonValueKind.Null, encoded.RootElement.GetProperty("keyword").ValueKind);
        Assert.Equal(JsonValueKind.Null, encoded.RootElement.GetProperty("cursor").ValueKind);
    }

    [Fact]
    public void Invalid_in_memory_record_cannot_be_serialized_as_native_contract()
    {
        var value = new Main_InitialSourceOptionQuery { SourceKind = "INVENTED", Keyword = null, Cursor = null, PageSize = 1 };
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Serialize(value));
        var unit = new Main_UnitIdentity_Variant1(new Main_UnitIdentity_Variant1_Payload
        {
            Kind = "OWNER_REF", Value = new External3713e7bd_QapUom { Id = "EA", Version = "1" }
        });
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Serialize<Main_UnitIdentity>(unit));
    }

    [Fact]
    public void A_typed_union_branch_cannot_serialize_as_a_different_valid_branch()
    {
        const string type = "CP6.Entity.Contracts.Quality.Main.Externalf70281e4_C09FailedProblem_Variant0_Payload";
        var payload = (Externalf70281e4_C09FailedProblem_Variant0_Payload)Decode(type, CompleteExample(type).ToJsonString());
        var wrong = new Externalf70281e4_C09FailedProblem_Variant0(payload with { Status = 403, Code = "CAPABILITY_DENIED" });
        Assert.ThrowsAny<JsonException>(() => NcrJson.Serialize<Externalf70281e4_C09FailedProblem>(wrong));
    }

    [Fact]
    public void In_memory_unpaired_surrogates_are_rejected_without_replacement()
    {
        var invalid = new QapUom { Id = "\ud800", Version = "1" };
        Assert.ThrowsAny<JsonException>(() => NcrJson.Serialize(invalid));
    }

    [Fact]
    public void Native_version_strings_preserve_the_source_modules_distinct_numeric_ranges()
    {
        var large = new string('9', 200);
        var qap = NcrJson.Deserialize<QapUom>(Utf8("{\"id\":\"EA\",\"version\":\"" + large + "\"}"));
        Assert.Equal(large, qap.Version);
        Assert.ThrowsAny<JsonException>(() => NcrJson.Deserialize<External2ef0f278_OwnerRef>(
            Utf8("{\"owner\":\"QA\",\"id\":\"source\",\"version\":\"" + large + "\"}")));
    }

    [Theory]
    [InlineData("1e99999999999999999999999999999999999999")]
    [InlineData("1e-99999999999999999999999999999999999999")]
    [InlineData("9007199254740993")]
    public void Bounded_json_integer_rejects_extreme_values_without_float_rounding(string number)
    {
        Assert.ThrowsAny<JsonException>(() => NcrJson.Deserialize<Main_InitialSourceOptionQuery>(
            Utf8("{\"sourceKind\":null,\"keyword\":null,\"pageSize\":" + number + ",\"cursor\":null}")));
    }

    [Theory]
    [InlineData("""{"id":"\uD800","version":"1"}""")]
    [InlineData("""{/*comment*/"id":"EA","version":"1"}""")]
    [InlineData("""{"id":"EA","version":"1",}""")]
    public void Native_utf8_entry_rejects_invalid_unicode_and_json_grammar(string json) =>
        Assert.ThrowsAny<JsonException>(() => NcrJson.Deserialize<QapUom>(Utf8(json)));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void Native_ECMAScript_patterns_reject_trailing_line_terminators(string terminator)
    {
        var version = "1" + terminator;
        output.WriteLine($"Runtime {RuntimeInformation.FrameworkDescription}; raw .NET ECMAScript $ match: {Regex.IsMatch(version, "^(0|[1-9][0-9]*)$", RegexOptions.ECMAScript)}; native JS without m rejects this input.");
        var json = JsonSerializer.Serialize(new { id = "EA", version });
        Assert.ThrowsAny<JsonException>(() => NcrJson.Deserialize<QapUom>(Utf8(json)));
        Assert.ThrowsAny<JsonException>(() => NcrJson.Serialize(new QapUom { Id = "EA", Version = version }));
    }

    [Theory]
    [InlineData("0000-01-01T00:00:00Z", true)]
    [InlineData("0000-02-29T23:59:59.123456789Z", true)]
    [InlineData("0000-02-30T23:59:59Z", false)]
    [InlineData("0000-01-01T00:59:60+01:00", true)]
    [InlineData("9999-12-31T23:00:00-01:00", true)]
    [InlineData("2000-03-01T00:59:60+01:00", true)]
    [InlineData("2000-01-31T23:59:60Z", true)]
    [InlineData("2000-02-28T23:59:60Z", false)]
    [InlineData("2000-01-30T23:59:60Z", false)]
    [InlineData("2000-02-01T01:59:60+02:00", true)]
    [InlineData("0000-12-31T23:59:60-00:01", false)]
    [InlineData("2000-01-31T23:59:60+00:01", false)]
    public void Native_time_format_uses_RFC3339_calendar_range_and_UTC_month_end_positions(string time, bool accepted)
    {
        var observation = CompleteExample("CP6.Entity.Contracts.Quality.SourceQA.ObservationInput_Case1");
        observation["observedAt"] = time;
        output.WriteLine("RFC3339 format/calendar/offset-position example only; synthetic second-60 positions do not claim an IERS-announced leap second.");
        if (!accepted)
        {
            Assert.ThrowsAny<JsonException>(() => NcrJson.Deserialize<ObservationInput>(Utf8(observation.ToJsonString())));
            return;
        }
        var value = NcrJson.Deserialize<ObservationInput>(Utf8(observation.ToJsonString()));
        using var encoded = JsonDocument.Parse(NcrJson.Serialize(value));
        Assert.Equal(time, encoded.RootElement.GetProperty("observedAt").GetString());
    }

    [Fact]
    public void Every_adopted_declaration_compiles_and_has_a_complete_positive_and_negative_fixture()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Input("csharp/generation-manifest.json")));
        using var fixtures = JsonDocument.Parse(File.ReadAllBytes(Input("csharp/full-object-fixtures.json")));
        var declarations = manifest.RootElement.GetProperty("declarations").EnumerateArray().ToArray();
        Assert.Equal(manifest.RootElement.GetProperty("declarationCount").GetInt32(), declarations.Length);
        foreach (var declaration in declarations)
        {
            var name = declaration.GetProperty("adapted").GetString()!;
            Assert.NotNull(ContractType(name));
            var cases = fixtures.RootElement.GetProperty("cases").EnumerateArray().Where(f => f.GetProperty("type").GetString() == name).ToArray();
            Assert.Contains(cases, f => f.GetProperty("accepted").GetBoolean());
            Assert.Contains(cases, f => !f.GetProperty("accepted").GetBoolean());
        }
        Assert.Equal(8, Environment.Version.Major);
        output.WriteLine($"Compiled {declarations.Length} native declarations on {RuntimeInformation.FrameworkDescription}; no Owner/storage acceptance claimed.");
    }
}
