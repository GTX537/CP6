using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Entity.Contracts.FinInventory;
using CP6.Entity.Contracts.Wire;
using Xunit;
using Xunit.Abstractions;

namespace CP6.ExactValues.Tests;

public sealed class InventoryDecimalTests(ITestOutputHelper output)
{
    private static readonly string[] AssignedIds =
        ["NUM-012", "NUM-013", "NUM-014", "NUM-015", "NUM-016", "INV-001", "INV-002", "INV-003", "INV-004"];

    private static InventoryDecimalProfile Profile(string id) => id switch
    {
        "fin-inv-decimal18" => InventoryDecimalProfile.Decimal18,
        "fin-inv-qty12" => InventoryDecimalProfile.Qty12,
        _ => throw new InvalidOperationException($"Unsupported inventory profile {id}")
    };

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static JsonSerializerOptions Options(InventoryDecimalProfile profile)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new InventoryDecimalJsonConverter(profile));
        return options;
    }

    public static IEnumerable<object[]> FixedVectors()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Inputs/vectors.json")));
        var selected = document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(v => v.GetProperty("profile_id").GetString() is "fin-inv-decimal18" or "fin-inv-qty12").ToArray();
        Assert.Equal(9, selected.Length);
        Assert.Equal(AssignedIds.Order(), selected.Select(v => v.GetProperty("id").GetString()!).Order());
        Assert.Equal(9, selected.Select(v => v.GetProperty("id").GetString()).Distinct().Count());
        foreach (var vector in selected)
            yield return [vector.GetProperty("id").GetString()!, vector.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(FixedVectors))]
    public void Fixed_package_vector(string id, string rawVector)
    {
        using var document = JsonDocument.Parse(rawVector);
        var vector = document.RootElement;
        Assert.Equal("lexical", vector.GetProperty("operation").GetString());
        var profile = Profile(vector.GetProperty("profile_id").GetString()!);
        var wire = vector.GetProperty("input").GetString()!;
        var expected = vector.GetProperty("expected");
        output.WriteLine($"Case {id}; runtime {RuntimeInformation.FrameworkDescription}; TFM {AppContext.TargetFrameworkName}");
        if (!expected.GetProperty("accepted").GetBoolean())
        {
            Assert.Throws<FormatException>(() => InventoryDecimal.Parse(wire, profile));
            Assert.Throws<JsonException>(() => StrictJson.Deserialize<InventoryDecimal>(Utf8(JsonSerializer.Serialize(wire)), Options(profile)));
            return;
        }
        var value = InventoryDecimal.Parse(wire, profile);
        var pair = expected.GetProperty("pair");
        Assert.Equal(pair.GetProperty("unscaledDigits").GetString(), value.Value.Coefficient.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(pair.GetProperty("scale").GetInt32(), value.Value.Scale);
        Assert.Equal(pair.GetProperty("originalWire").GetString(), value.OriginalWire);
        Assert.Equal(wire, value.ToWire());
        Assert.Equal(Utf8(wire), Utf8(value.OriginalWire));
        var encoded = JsonSerializer.Serialize(value, Options(profile));
        Assert.Equal(JsonSerializer.Serialize(wire), encoded);
        var decoded = StrictJson.Deserialize<InventoryDecimal>(Utf8(encoded), Options(profile))!;
        Assert.Equal(wire, decoded.OriginalWire);
        Assert.Equal(0, value.Value.CompareTo(decoded.Value));
    }

    public static IEnumerable<object[]> InvalidWires()
    {
        string[] wires = ["", " 1", "1 ", "1\n", "1\r\n", "1\u2028", "1\u2029", "+1", "01", "-01", ".1", "1.", "1e3", "1E-3", "NaN", "Infinity", "１", "١", "--1"];
        foreach (var profile in Enum.GetValues<InventoryDecimalProfile>())
            foreach (var wire in wires) yield return [profile, wire];
    }

    [Theory]
    [MemberData(nameof(InvalidWires))]
    public void Invalid_lexical_form_rejects(InventoryDecimalProfile profile, string wire) =>
        Assert.Throws<FormatException>(() => InventoryDecimal.Parse(wire, profile));

    public static IEnumerable<object[]> WrongTokens()
    {
        string[] tokens = ["null", "0", "1.25", "9007199254740993", "true", "false", "{}", "[]"];
        foreach (var profile in Enum.GetValues<InventoryDecimalProfile>())
            foreach (var token in tokens) yield return [profile, token];
    }

    [Theory]
    [MemberData(nameof(WrongTokens))]
    public void Json_scalar_requires_string(InventoryDecimalProfile profile, string token) =>
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<InventoryDecimal>(Utf8(token), Options(profile)));

    [Theory]
    [InlineData(InventoryDecimalProfile.Decimal18, "1.111111111111111111", "1111111111111111111", 18)]
    [InlineData(InventoryDecimalProfile.Qty12, "1.111111111111", "1111111111111", 12)]
    [InlineData(InventoryDecimalProfile.NonNegativeQty12, "0.000000000001", "1", 12)]
    [InlineData(InventoryDecimalProfile.Qty12, "-0.000", "0", 0)]
    [InlineData(InventoryDecimalProfile.Decimal18, "-0", "0", 0)]
    [InlineData(InventoryDecimalProfile.Qty12, "1.2300", "123", 2)]
    [InlineData(InventoryDecimalProfile.Decimal18, "-100.000", "-100", 0)]
    [InlineData(InventoryDecimalProfile.NonNegativeQty12, "0.000", "0", 0)]
    public void Native_wire_and_normalized_value_are_separate(InventoryDecimalProfile profile, string wire, string coefficient, int scale)
    {
        var value = InventoryDecimal.Parse(wire, profile);
        Assert.Equal(coefficient, value.Value.Coefficient.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(scale, value.Value.Scale);
        Assert.Equal(wire, value.OriginalWire);
        Assert.Equal(wire, value.ToString());
        _ = value.Value.Add(InventoryDecimalValue.FromParts(1, 0));
        Assert.Equal(wire, value.ToWire());
    }

    [Theory]
    [InlineData(InventoryDecimalProfile.Decimal18, "1.1111111111111111111")]
    [InlineData(InventoryDecimalProfile.Qty12, "1.1111111111111")]
    [InlineData(InventoryDecimalProfile.Qty12, "1.0000000000000")]
    [InlineData(InventoryDecimalProfile.Decimal18, "-0.0000000000000000000")]
    [InlineData(InventoryDecimalProfile.NonNegativeQty12, "-1")]
    [InlineData(InventoryDecimalProfile.NonNegativeQty12, "-0")]
    [InlineData(InventoryDecimalProfile.NonNegativeQty12, "-0.000")]
    public void Field_constraints_apply_before_normalization(InventoryDecimalProfile profile, string wire) =>
        Assert.Throws<FormatException>(() => InventoryDecimal.Parse(wire, profile));

    [Fact]
    public void Integer_digits_have_no_new_finite_contract_cap()
    {
        var wire = new string('9', 200) + ".123456789012345678";
        var value = InventoryDecimal.Parse(wire, InventoryDecimalProfile.Decimal18);
        Assert.Equal(new string('9', 200) + "123456789012345678", value.Value.Coefficient.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(wire, value.ToWire());
        Assert.Equal(JsonSerializer.Serialize(wire), JsonSerializer.Serialize(value, Options(InventoryDecimalProfile.Decimal18)));
    }

    [Fact]
    public void Unknown_profile_and_null_string_reject()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryDecimal.Parse("0", (InventoryDecimalProfile)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryDecimalJsonConverter((InventoryDecimalProfile)99));
        Assert.Throws<ArgumentNullException>(() => InventoryDecimal.Parse(null!, InventoryDecimalProfile.Qty12));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryDecimalValue.FromParts(1, -1));
    }

    [Fact]
    public void Destination_field_rechecks_raw_wire_and_computed_value()
    {
        var original = InventoryDecimal.Parse("1.0000000000000", InventoryDecimalProfile.Decimal18);
        Assert.Throws<FormatException>(() => original.ToWire(InventoryDecimalProfile.Qty12));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(original, Options(InventoryDecimalProfile.Qty12)));
        Assert.Equal("1", InventoryDecimal.FromValue(original.Value, InventoryDecimalProfile.Qty12).ToWire());
        Assert.Equal("1.0000000000000", original.OriginalWire);
        var negative = InventoryDecimal.Parse("-1", InventoryDecimalProfile.Qty12);
        Assert.Throws<FormatException>(() => negative.ToWire(InventoryDecimalProfile.NonNegativeQty12));
        Assert.Throws<FormatException>(() => InventoryDecimal.FromValue(negative.Value, InventoryDecimalProfile.NonNegativeQty12));
    }

    private static InventoryDecimalValue Value(string wire) => InventoryDecimal.Parse(wire, InventoryDecimalProfile.Decimal18).Value;

    [Fact]
    public void Exact_arithmetic_preserves_units_above_JS_and_decimal_precision()
    {
        Assert.Equal("9007199254740993", Value("9007199254740992").Add(Value("1")).ToString());
        Assert.Equal("1", Value("9007199254740993").Subtract(Value("9007199254740992")).ToString());
        var original = InventoryDecimal.Parse(new string('9', 120) + ".999999999999", InventoryDecimalProfile.Qty12);
        var sum = original.Value.Add(Value("0.000000000001"));
        Assert.Equal("1" + new string('0', 120), sum.ToString());
        Assert.Equal(original.OriginalWire, sum.Subtract(Value("0.000000000001")).ToString());
        Assert.Equal(new string('9', 120) + ".999999999999", original.ToWire());
    }

    [Fact]
    public void Unequal_scales_signs_and_cancellation_are_exact()
    {
        Assert.Equal("0.3", Value("0.1").Add(Value("0.2")).ToString());
        Assert.Equal("-0.77", Value("1.23").Subtract(Value("2")).ToString());
        var zero = Value("-0.000").Add(Value("0.000"));
        Assert.Equal(BigInteger.Zero, zero.Coefficient);
        Assert.Equal(0, zero.Scale);
        Assert.Equal("0", zero.ToString());
        Assert.Equal("-1.2", InventoryDecimalValue.FromParts(-1200, 3).ToString());
        Assert.Equal("0.000001", InventoryDecimalValue.FromParts(1, 6).ToString());
        Assert.True(Value("9007199254740993").CompareTo(Value("9007199254740992")) > 0);
        Assert.True(Value("-1").CompareTo(Value("-0.1")) < 0);
        Assert.Equal(0, Value("1.00").CompareTo(Value("1")));
    }

    [Fact]
    public void Intermediate_fractional_overflow_is_exact_but_final_field_rejects()
    {
        var intermediate = Value("0.000000000000000001").Multiply(Value("0.1"));
        Assert.Equal("0.0000000000000000001", intermediate.ToString());
        Assert.Equal(19, intermediate.Scale);
        Assert.Throws<FormatException>(() => InventoryDecimal.FromValue(intermediate, InventoryDecimalProfile.Decimal18));
        Assert.Equal("0.000000000000000001", InventoryDecimal.FromValue(intermediate.Multiply(Value("10")), InventoryDecimalProfile.Decimal18).ToWire());
        Assert.Throws<FormatException>(() => InventoryDecimal.FromValue(Value("0.0000000000001"), InventoryDecimalProfile.Qty12));
    }

    [Fact]
    public void Mixed_fields_use_separate_converter_profiles()
    {
        var source = Utf8("{\"quantity\":\"-0.000\",\"amount\":\"1.111111111111111111\",\"nonnegative\":\"0.000\"}");
        var carrier = StrictJson.Deserialize<Carrier>(source)!;
        Assert.Equal("-0.000", carrier.Quantity.OriginalWire);
        Assert.Equal(18, carrier.Amount.Value.Scale);
        Assert.Equal(InventoryDecimalProfile.NonNegativeQty12, carrier.Nonnegative.Profile);
        var decoded = StrictJson.Deserialize<Carrier>(Utf8(JsonSerializer.Serialize(carrier)))!;
        Assert.Equal(carrier.Quantity.OriginalWire, decoded.Quantity.OriginalWire);
        Assert.Equal(carrier.Amount.OriginalWire, decoded.Amount.OriginalWire);
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<Carrier>(Utf8("{\"quantity\":\"1.1111111111111\",\"amount\":\"1\",\"nonnegative\":\"0\"}")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"quantity\":null,\"amount\":\"1\",\"nonnegative\":\"0\"}")]
    [InlineData("{\"quantity\":\"1\",\"amount\":null,\"nonnegative\":\"0\"}")]
    [InlineData("{\"quantity\":\"1\",\"amount\":\"1\",\"nonnegative\":\"-0\"}")]
    [InlineData("{\"quantity\":\"1\",\"quanti\\u0074y\":\"2\",\"amount\":\"1\",\"nonnegative\":\"0\"}")]
    [InlineData("{\"quantity\":\"1\",\"amount\":\"1\",\"nonnegative\":\"0\",\"extra\":{\"x\":1,\"\\u0078\":2}}")]
    public void Strict_carrier_rejects_missing_null_domain_and_duplicate_fields(string json) =>
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<Carrier>(Utf8(json)));

    [Fact]
    public void Original_UTF8_bytes_are_preserved_independently_of_decoded_wire()
    {
        var bytes = Utf8(" \n{\"quantity\":\"\\u002d0.000\", \"amount\":\"1.2300\",\"nonnegative\":\"0\"}\r\n");
        using var document = StrictJson.Parse(bytes);
        var carrier = document.Deserialize<Carrier>()!;
        Assert.Equal("-0.000", carrier.Quantity.OriginalWire);
        Assert.Equal("1.2300", carrier.Amount.OriginalWire);
        Assert.Equal(bytes, document.GetOriginalUtf8());
        Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(document.GetOriginalUtf8()));
        _ = carrier.Quantity.Value.Add(Value("1"));
        Assert.Equal(bytes, document.GetOriginalUtf8());
        Assert.False(bytes.SequenceEqual(Utf8(JsonSerializer.Serialize(carrier))));
    }

    [Theory]
    [InlineData(7, 3, 2, 1)]
    [InlineData(-7, 3, -2, -1)]
    [InlineData(6, 3, 2, 0)]
    [InlineData(-6, 3, -2, 0)]
    [InlineData(0, 3, 0, 0)]
    [InlineData(1, 3, 0, 1)]
    [InlineData(-1, 3, 0, -1)]
    public void Ratio_division_truncates_toward_zero_with_dividend_signed_remainder(int numerator, int denominator, int quotient, int remainder)
    {
        var ratio = InventoryRatio.Create(numerator, denominator);
        var actual = ratio.DivRem();
        Assert.Equal(new BigInteger(quotient), actual.Quotient);
        // Reduction changes the remainder's unit; unreduced expected numerator is restored here.
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Assert.Equal(new BigInteger(remainder), actual.Remainder * gcd);
        Assert.Equal(ratio.Numerator, actual.Quotient * ratio.Denominator + actual.Remainder);
        Assert.True(BigInteger.Abs(actual.Remainder) < ratio.Denominator);
        Assert.True(actual.Remainder.IsZero || actual.Remainder.Sign == ratio.Numerator.Sign);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3)]
    public void Ratio_requires_positive_denominator(int denominator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryRatio.Create(7, denominator));

    [Fact]
    public void Ratio_reduces_signed_parts_and_canonicalizes_zero()
    {
        var ratio = InventoryRatio.Create(-14, 6);
        Assert.Equal(new BigInteger(-7), ratio.Numerator);
        Assert.Equal(new BigInteger(3), ratio.Denominator);
        var zero = InventoryRatio.Create(0, BigInteger.Pow(10, 200));
        Assert.Equal(BigInteger.Zero, zero.Numerator);
        Assert.Equal(BigInteger.One, zero.Denominator);
        Assert.Equal((BigInteger.Zero, BigInteger.Zero), zero.DivRem());
    }

    [Fact]
    public void Ratio_parts_beyond_90_digits_keep_exact_quotient_remainder_and_gcd()
    {
        var denominator = BigInteger.Pow(10, 120) + 3;
        var numerator = denominator * (BigInteger.Pow(10, 110) + 7) + 2;
        var ratio = InventoryRatio.Create(numerator, denominator);
        var result = ratio.DivRem();
        Assert.Equal(BigInteger.Pow(10, 110) + 7, result.Quotient);
        Assert.Equal(new BigInteger(2), result.Remainder);
        Assert.Equal(numerator, result.Quotient * denominator + result.Remainder);
        var reduced = InventoryRatio.Create(-numerator * 11, denominator * 11);
        Assert.Equal(-numerator, reduced.Numerator);
        Assert.Equal(denominator, reduced.Denominator);
        Assert.Equal(-result.Quotient, reduced.DivRem().Quotient);
        Assert.Equal(-result.Remainder, reduced.DivRem().Remainder);
    }

    [Fact]
    public void Ratio_arithmetic_and_comparison_use_integer_cross_products()
    {
        var third = InventoryRatio.Create(1, 3);
        var sixth = InventoryRatio.Create(1, 6);
        var sum = third.Add(sixth);
        Assert.Equal(BigInteger.One, sum.Numerator);
        Assert.Equal(new BigInteger(2), sum.Denominator);
        Assert.Equal(new BigInteger(-1), sixth.Subtract(third).Numerator);
        Assert.Equal(new BigInteger(6), sixth.Subtract(third).Denominator);
        Assert.Equal(new BigInteger(-7), third.Multiply(InventoryRatio.Create(-7, 2)).Numerator);
        Assert.Equal(new BigInteger(6), third.Multiply(InventoryRatio.Create(-7, 2)).Denominator);
        Assert.True(third.CompareTo(sixth) > 0);
        Assert.True(InventoryRatio.Create(-7, 3).CompareTo(InventoryRatio.Create(-2, 1)) < 0);
        Assert.Equal(0, third.CompareTo(InventoryRatio.Create(2, 6)));
    }

    public sealed class Carrier
    {
        [JsonPropertyName("quantity"), JsonConverter(typeof(Qty12JsonConverter))]
        public required InventoryDecimal Quantity { get; init; }
        [JsonPropertyName("amount"), JsonConverter(typeof(Decimal18JsonConverter))]
        public required InventoryDecimal Amount { get; init; }
        [JsonPropertyName("nonnegative"), JsonConverter(typeof(NonNegativeQty12JsonConverter))]
        public required InventoryDecimal Nonnegative { get; init; }
    }
}
