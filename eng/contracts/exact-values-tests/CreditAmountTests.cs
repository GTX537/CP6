using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Entity.Contracts.FinCredit;
using CP6.Entity.Contracts.Wire;
using Xunit;
using Xunit.Abstractions;

namespace CP6.ExactValues.Tests;

public sealed class CreditAmountTests(ITestOutputHelper output)
{
    private const string PackageVersion = "CP6-S01-CONTRACT-INPUTS/1.0.0";
    private const string VectorsSha256 = "b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01";
    private const string ProfileInputsSha256 = "2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e";
    private const string NativeSchema = "https://cp6.example/design/fin-credit-wire-r4.schema.json";
    private const string MaxInteger = "99999999999999999999999999999999999999";
    private const string MaxFraction = "999999999999999999999999999999.99999999";

    // Frozen S01-03 selection: never read the leaf's wave2 Inputs/dispatch.json.
    private static readonly string[] SignedIds =
    [
        "NUM-001", "NUM-002", "NUM-003", "NUM-004", "NUM-005", "NUM-006", "NUM-007",
        "NUM-008", "NUM-010", "CREDIT-005", "CREDIT-006", "CREDIT-JS-001", "CREDIT-JS-002", "CREDIT-JS-003"
    ];
    private static readonly string[] NonNegativeIds =
    [
        "NUM-011", "CREDIT-001", "CREDIT-002", "CREDIT-007", "CREDIT-009", "CREDIT-011"
    ];
    private static readonly string[] PositiveIds =
    [
        "NUM-009", "CREDIT-003", "CREDIT-004", "CREDIT-008", "CREDIT-010"
    ];

    // Exact native pointers, including nullable branches; names alone do not select a profile.
    private static readonly string[] SignedBindings =
    [
        "/$defs/absorptionLink/properties/signedPolicyAmount",
        "/$defs/representationTransfer/properties/deltaPolicyAmount",
        "/$defs/subject/properties/exposureDeltaPolicyAmount",
        "/$defs/moneySigned/properties/amount"
    ];
    private static readonly string[] NonNegativeBindings =
    [
        "/$defs/moneyNonNegative/properties/amount",
        "/$defs/arObligationAggregate/properties/openPolicyAmount",
        "/$defs/absorptionLink/properties/predecessorOpenPolicyAmount",
        "/$defs/absorptionLink/properties/successorOpenPolicyAmount",
        "/$defs/arCut/properties/arBookedExposure",
        "/$defs/orderExposureLineage/properties/committedPolicyAmount",
        "/$defs/representationTransfer/properties/predecessorCommittedPolicyAmount",
        "/$defs/representationTransfer/properties/targetCommittedPolicyAmount",
        "/$defs/reservationSet/properties/activeReservedDelta",
        "/$defs/creditExposureSnapshot/properties/arBookedExposure",
        "/$defs/creditExposureSnapshot/properties/orderUnbilledExposure",
        "/$defs/creditExposureSnapshot/properties/activeReservedDelta",
        "/$defs/creditExposureSnapshot/properties/totalExposure",
        "/$defs/subject/properties/currentCommittedPolicyAmount",
        "/$defs/subject/properties/targetCommittedPolicyAmount",
        "/$defs/creditEvaluation/properties/limitPolicyAmount/oneOf/0",
        "/$defs/creditEvaluation/properties/requestedPolicyAmount",
        "/$defs/creditEvaluation/properties/projectedPolicyAmount/oneOf/0",
        "/$defs/creditReservation/properties/reservedDeltaPolicyAmount",
        "/$defs/exceptionDecisionCommand/properties/maxPolicyAmount/oneOf/0",
        "/$defs/planAtpObservation/properties/promisedQuantity/oneOf/0",
        "/$defs/orderCreditContext/properties/canonicalPolicyAmount/oneOf/0",
        "/$defs/creditExceptionDecision/properties/maxPolicyAmount/oneOf/0",
        "/$defs/salesExposureReductionInput/properties/targetPolicyAmount",
        "/$defs/finArFinalPostInput/properties/expectedCommittedPolicyAmount",
        "/$defs/legacyExposureAdoptionEntry/properties/policyAmount/oneOf/0",
        "/$defs/legacyExposureAdoptionEntry/allOf/0/then/properties/policyAmount",
        "/$defs/postedOpenContribution/properties/policyAmount",
        "/$defs/partiallySettledOpenContribution/properties/policyAmount",
        "/$defs/effectiveCreditContribution/properties/policyAmount",
        "/$defs/appliedReceiptContribution/properties/policyAmount",
        "/$defs/unbilledOrderContribution/properties/policyAmount",
        "/$defs/activeReservationContribution/properties/policyAmount"
    ];
    private static readonly string[] PositiveBindings =
    [
        "/$defs/moneyPositive/properties/amount",
        "/$defs/fxEvidence/properties/rate",
        "/$defs/planAtpObservationInput/properties/requestedQuantity",
        "/$defs/finArReceiptUnapplyCorrectionSource/properties/restoredPolicyAmount",
        "/$defs/finArCreditReversalCorrectionSource/properties/restoredPolicyAmount"
    ];

    public static IEnumerable<object[]> FixedVectors()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Inputs/vectors.json"));
        Assert.Equal(VectorsSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.Equal(PackageVersion, root.GetProperty("package_version").GetString());
        Assert.Equal("EXPECTED_VECTORS_NOT_EXECUTION_RESULTS", root.GetProperty("role").GetString());
        Assert.Equal("NOT_RUN", root.GetProperty("execution").GetString());
        var profilePin = root.GetProperty("source_pins").EnumerateArray()
            .Single(p => p.GetProperty("path").GetString() == "eng/contracts/s01/profile-inputs.json");
        Assert.Equal(ProfileInputsSha256, profilePin.GetProperty("sha256").GetString());
        var ids = SignedIds.Concat(NonNegativeIds).Concat(PositiveIds).ToArray();
        Assert.Equal(14, SignedIds.Length);
        Assert.Equal(6, NonNegativeIds.Length);
        Assert.Equal(5, PositiveIds.Length);
        Assert.Equal(25, ids.Distinct(StringComparer.Ordinal).Count());
        var selected = root.GetProperty("cases").EnumerateArray().Where(v =>
            ids.Contains(v.GetProperty("id").GetString(), StringComparer.Ordinal) ||
            v.GetProperty("profile_id").GetString() is "fin-credit-signed" or "fin-credit-nonnegative" or "fin-credit-positive").ToArray();
        Assert.Equal(ids.Order(StringComparer.Ordinal), selected.Select(v => v.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
        foreach (var vector in selected)
        {
            var id = vector.GetProperty("id").GetString()!;
            var profile = SignedIds.Contains(id) ? "fin-credit-signed" :
                NonNegativeIds.Contains(id) ? "fin-credit-nonnegative" : "fin-credit-positive";
            Assert.Equal(profile, vector.GetProperty("profile_id").GetString());
            Assert.Equal("lexical", vector.GetProperty("operation").GetString());
            Assert.Equal("CONTRACT_UNIT", vector.GetProperty("verification_level").GetString());
            yield return [id, vector.GetRawText()];
        }
    }

    [Theory]
    [MemberData(nameof(FixedVectors))]
    public void Fixed_package_vector(string id, string rawVector)
    {
        using var document = JsonDocument.Parse(rawVector);
        var vector = document.RootElement;
        var profileId = vector.GetProperty("profile_id").GetString();
        var (profile, wireType, bindings) = profileId switch
        {
            "fin-credit-signed" => (CreditAmountProfile.Signed, "signedDecimal", SignedBindings),
            "fin-credit-nonnegative" => (CreditAmountProfile.NonNegative, "nonNegativeDecimal", NonNegativeBindings),
            "fin-credit-positive" => (CreditAmountProfile.Positive, "positiveDecimal", PositiveBindings),
            _ => throw new InvalidOperationException($"Unsupported profile in {id}")
        };
        Assert.Equal(wireType, vector.GetProperty("wire_type").GetString());
        Assert.Equal(bindings.Select(p => NativeSchema + "#" + p).Order(StringComparer.Ordinal),
            vector.GetProperty("applicable_fields").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal));
        var text = vector.GetProperty("input").GetString()!;
        var expected = vector.GetProperty("expected");
        output.WriteLine($"{PackageVersion}; {id}; {profileId}; {RuntimeInformation.FrameworkDescription}");
        if (!expected.GetProperty("accepted").GetBoolean())
        {
            Assert.Equal(JsonValueKind.Null, expected.GetProperty("pair").ValueKind);
            Assert.Throws<FormatException>(() => CreditAmount.Parse(text, profile));
            Assert.ThrowsAny<JsonException>(() => ReadScalar(JsonSerializer.Serialize(text), profile));
            return;
        }
        var amount = CreditAmount.Parse(text, profile);
        var pair = expected.GetProperty("pair");
        Assert.Equal(pair.GetProperty("unscaledValue").GetString(), amount.Coefficient.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(pair.GetProperty("scale").GetInt32(), amount.Scale);
        Assert.Equal(text, amount.ToFieldString(profile));
        Assert.Equal(amount, ReadScalar(JsonSerializer.Serialize(text), profile));
        Assert.Equal(JsonSerializer.Serialize(text), JsonSerializer.Serialize(amount, Options(profile)));
    }

    [Theory]
    [InlineData("0", CreditAmountProfile.Signed, "0", 0)]
    [InlineData("0", CreditAmountProfile.NonNegative, "0", 0)]
    [InlineData("1", CreditAmountProfile.Positive, "1", 0)]
    [InlineData("-1", CreditAmountProfile.Signed, "-1", 0)]
    [InlineData("0.00000001", CreditAmountProfile.Positive, "1", 8)]
    [InlineData("-0.00000001", CreditAmountProfile.Signed, "-1", 8)]
    [InlineData("1.00000001", CreditAmountProfile.NonNegative, "100000001", 8)]
    [InlineData("99999999999999999999999999999999999999", CreditAmountProfile.Positive, "99999999999999999999999999999999999999", 0)]
    [InlineData("-99999999999999999999999999999999999999", CreditAmountProfile.Signed, "-99999999999999999999999999999999999999", 0)]
    [InlineData("-999999999999999999999999999999.12345678", CreditAmountProfile.Signed, "-99999999999999999999999999999912345678", 8)]
    public void Native_sign_and_precision_boundaries_are_exact(string text, CreditAmountProfile profile, string coefficient, int scale)
    {
        var amount = CreditAmount.Parse(text, profile);
        Assert.Equal(BigInteger.Parse(coefficient, CultureInfo.InvariantCulture), amount.Coefficient);
        Assert.Equal(scale, amount.Scale);
        Assert.Equal(text, amount.ToFieldString(profile));
    }

    public static IEnumerable<object[]> InvalidLexicalValues()
    {
        string[] values =
        [
            "", "-", "+1", "+0", "01", "00", "-01", ".1", "1.", "-.1", "1..1", "1,2",
            " 1", "1 ", "\t1", "1\r", "1\n", "1\0", "1e3", "1E-3", "1.0", "0.0", "-0", "-0.0",
            "1.10", "0.00000000", "0.000000010", "0.123456789", "-0.123456789", "1.000000001",
            "999999999999999999999999999999999999999", "9999999999999999999999999999999.12345678",
            "１２", "١", "−1", "NaN", "Infinity"
        ];
        foreach (var profile in new[] { CreditAmountProfile.Signed, CreditAmountProfile.NonNegative, CreditAmountProfile.Positive })
            foreach (var value in values) yield return [value, profile];
    }

    [Theory]
    [MemberData(nameof(InvalidLexicalValues))]
    public void Noncanonical_input_is_never_normalized_into_acceptance(string text, CreditAmountProfile profile) =>
        Assert.Throws<FormatException>(() => CreditAmount.Parse(text, profile));

    [Theory]
    [InlineData("-1", CreditAmountProfile.NonNegative)]
    [InlineData("-0.00000001", CreditAmountProfile.NonNegative)]
    [InlineData("-99999999999999999999999999999999999999", CreditAmountProfile.NonNegative)]
    [InlineData("-1", CreditAmountProfile.Positive)]
    [InlineData("-0.00000001", CreditAmountProfile.Positive)]
    [InlineData("-99999999999999999999999999999999999999", CreditAmountProfile.Positive)]
    [InlineData("0", CreditAmountProfile.Positive)]
    public void Narrower_profile_rejects_signed_values_on_both_boundaries(string text, CreditAmountProfile profile)
    {
        Assert.Throws<FormatException>(() => CreditAmount.Parse(text, profile));
        var signed = CreditAmount.Parse(text, CreditAmountProfile.Signed);
        Assert.Throws<FormatException>(() => signed.ToFieldString(profile));
        Assert.ThrowsAny<JsonException>(() => ReadScalar(JsonSerializer.Serialize(text), profile));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Serialize(signed, Options(profile)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void Unknown_profile_fails_before_text_or_json_token_inspection(int unknown)
    {
        var profile = (CreditAmountProfile)unknown;
        foreach (var text in new[] { "1", "invalid", null })
            Assert.Throws<ArgumentOutOfRangeException>(() => CreditAmount.Parse(text!, profile));
        Assert.Throws<ArgumentOutOfRangeException>(() => default(CreditAmount).ToFieldString(profile));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CreditAmountJsonConverter(profile));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CreditAmountFieldAttribute(profile));
        foreach (var token in new[] { "\"1\"", "null", "1", "{}" })
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadScalar(token, profile));
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonSerializer.Serialize(default(CreditAmount), Options(profile)));
    }

    [Fact]
    public void Null_text_is_not_a_required_amount() =>
        Assert.Throws<ArgumentNullException>(() => CreditAmount.Parse(null!, CreditAmountProfile.Signed));

    [Theory]
    [InlineData("0.9", "0.1", "1", "1", 0)]
    [InlineData("1.99999999", "0.00000001", "2", "2", 0)]
    [InlineData("1.25", "-0.25", "1", "1", 0)]
    [InlineData("-1.25", "0.2", "-1.05", "-105", 2)]
    [InlineData("0", "0.00000001", "0.00000001", "1", 8)]
    [InlineData("-0.00000001", "0.00000001", "0", "0", 0)]
    public void Addition_aligns_scales_and_normalizes_carry_and_cancellation(string left, string right, string result, string coefficient, int scale)
    {
        var sum = Signed(left).Add(Signed(right));
        Assert.Equal(result, sum.ToFieldString(CreditAmountProfile.Signed));
        Assert.Equal(BigInteger.Parse(coefficient, CultureInfo.InvariantCulture), sum.Coefficient);
        Assert.Equal(scale, sum.Scale);
        Assert.Equal(sum, Signed(right).Add(Signed(left)));
    }

    [Theory]
    [InlineData("1", "0.00000001", "0.99999999", "99999999", 8)]
    [InlineData("0.01", "0.009", "0.001", "1", 3)]
    [InlineData("-1", "0.01", "-1.01", "-101", 2)]
    [InlineData("-0.1", "-0.1", "0", "0", 0)]
    [InlineData("0.00000001", "1", "-0.99999999", "-99999999", 8)]
    public void Subtraction_preserves_borrow_and_sign(string left, string right, string result, string coefficient, int scale)
    {
        var difference = Signed(left).Subtract(Signed(right));
        Assert.Equal(result, difference.ToFieldString(CreditAmountProfile.Signed));
        Assert.Equal(BigInteger.Parse(coefficient, CultureInfo.InvariantCulture), difference.Coefficient);
        Assert.Equal(scale, difference.Scale);
        Assert.Equal(Signed(left), difference.Add(Signed(right)));
    }

    [Theory]
    [InlineData("1", "1.00000001", -1)]
    [InlineData("1.2", "1.19", 1)]
    [InlineData("0.00000001", "0", 1)]
    [InlineData("-0.00000001", "0", -1)]
    [InlineData("-1.00000001", "-1", -1)]
    [InlineData("-1.2", "-1.19", -1)]
    [InlineData("-1", "1", -1)]
    [InlineData("9007199254740992", "9007199254740993", -1)]
    [InlineData("99999999999999999999999999999999999999", "99999999999999999999999999999999999998", 1)]
    [InlineData("-99999999999999999999999999999999999999", "-99999999999999999999999999999999999998", -1)]
    [InlineData("1.2", "1.2", 0)]
    public void Comparison_is_exact_for_adjacent_values_and_different_scales(string left, string right, int expectedSign)
    {
        var l = Signed(left);
        var r = Signed(right);
        Assert.Equal(expectedSign, Math.Sign(l.CompareTo(r)));
        Assert.Equal(-expectedSign, Math.Sign(r.CompareTo(l)));
        Assert.Equal(expectedSign == 0, l.Equals(r));
    }

    [Fact]
    public void Intermediate_overflow_can_cancel_before_final_field_validation()
    {
        var max = Signed(MaxInteger);
        var above = max.Add(Signed("1"));
        Assert.Equal(BigInteger.Pow(10, 38), above.Coefficient);
        Assert.Equal(0, above.Scale);
        foreach (var profile in new[] { CreditAmountProfile.Signed, CreditAmountProfile.NonNegative, CreditAmountProfile.Positive })
            Assert.Throws<FormatException>(() => above.ToFieldString(profile));
        Assert.Equal(MaxInteger, above.Subtract(Signed("1")).ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal(MaxInteger, CreditAmount.Sum([max, Signed("1"), Signed("-1")]).ToFieldString(CreditAmountProfile.Positive));
        var below = Signed("-" + MaxInteger).Subtract(Signed("1"));
        Assert.Throws<FormatException>(() => below.ToFieldString(CreditAmountProfile.Signed));
        Assert.Equal("-" + MaxInteger, below.Add(Signed("1")).ToFieldString(CreditAmountProfile.Signed));
    }

    [Fact]
    public void Fractional_carry_and_cancellation_validate_normalized_final_precision()
    {
        Assert.Equal("1000000000000000000000000000000", Signed(MaxFraction).Add(Signed("0.00000001")).ToFieldString(CreditAmountProfile.Positive));
        var maxPlusTiny = Signed(MaxInteger).Add(Signed("0.00000001"));
        Assert.Equal(8, maxPlusTiny.Scale);
        Assert.Throws<FormatException>(() => maxPlusTiny.ToFieldString(CreditAmountProfile.Signed));
        Assert.Equal(MaxInteger, maxPlusTiny.Subtract(Signed("0.00000001")).ToFieldString(CreditAmountProfile.NonNegative));
    }

    [Fact]
    public void Negative_intermediate_is_legal_before_a_positive_final_field()
    {
        var intermediate = CreditAmount.Parse("1", CreditAmountProfile.Positive).Subtract(CreditAmount.Parse("2", CreditAmountProfile.Positive));
        Assert.Equal(new BigInteger(-1), intermediate.Coefficient);
        Assert.Throws<FormatException>(() => intermediate.ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal("2", intermediate.Add(Signed("3")).ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal("2", CreditAmount.Sum([Signed("1"), Signed("-2"), Signed("3")]).ToFieldString(CreditAmountProfile.Positive));
    }

    [Fact]
    public void Intermediate_factory_is_explicit_unbounded_and_never_rounds_scale()
    {
        var huge = BigInteger.Pow(10, 10000) + 17;
        var amount = CreditAmount.FromIntermediate(huge, 9);
        Assert.Equal(huge, amount.Coefficient);
        Assert.Equal(9, amount.Scale);
        Assert.Throws<FormatException>(() => amount.ToFieldString(CreditAmountProfile.Signed));
        var restored = amount.Subtract(CreditAmount.FromIntermediate(huge - 10, 9));
        Assert.Equal("0.00000001", restored.ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal(1, restored.Coefficient);
        Assert.Equal(8, restored.Scale);
        Assert.Throws<FormatException>(() => CreditAmount.FromIntermediate(1, 9).ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal("0.1", CreditAmount.FromIntermediate(1000000000, 10).ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal(0, CreditAmount.FromIntermediate(BigInteger.Zero, int.MaxValue).Scale);
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditAmount.FromIntermediate(1, -1));
    }

    [Fact]
    public void Accumulation_streams_many_terms_and_unbounded_coefficients()
    {
        var unit = Signed("0.00000001");
        Assert.Equal("0.0002", CreditAmount.Sum(Enumerable.Repeat(unit, 20000)).ToFieldString(CreditAmountProfile.Positive));
        var huge = CreditAmount.FromIntermediate(BigInteger.Pow(10, 10000), 0);
        var total = CreditAmount.Sum([huge, huge, Signed("7"), CreditAmount.FromIntermediate(-2 * huge.Coefficient, 0)]);
        Assert.Equal("7", total.ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal("0", CreditAmount.Sum(Array.Empty<CreditAmount>()).ToFieldString(CreditAmountProfile.NonNegative));
        Assert.Throws<ArgumentNullException>(() => CreditAmount.Sum(null!));
    }

    [Fact]
    public void Representation_is_immutable_canonical_and_equal_by_value()
    {
        var normalized = CreditAmount.FromIntermediate(1200, 3);
        Assert.Equal(new BigInteger(12), normalized.Coefficient);
        Assert.Equal(1, normalized.Scale);
        Assert.Equal(Signed("1.2"), normalized);
        Assert.Equal(Signed("1.2").GetHashCode(), normalized.GetHashCode());
        Assert.Equal(0, normalized.CompareTo(Signed("1.2")));
        Assert.False(normalized.Equals((object)"1.2"));
        Assert.Equal(default, CreditAmount.FromIntermediate(0, 8));
        Assert.Equal("0", default(CreditAmount).ToFieldString(CreditAmountProfile.Signed));
        Assert.Throws<FormatException>(() => default(CreditAmount).ToFieldString(CreditAmountProfile.Positive));
        Assert.Empty(typeof(CreditAmount).GetConstructors());
        Assert.False(typeof(CreditAmount).GetProperty(nameof(CreditAmount.Coefficient))!.CanWrite);
        Assert.False(typeof(CreditAmount).GetProperty(nameof(CreditAmount.Scale))!.CanWrite);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"value\":\"1\"}")]
    public void Required_scalar_converter_rejects_every_non_string_token(string json)
    {
        foreach (var profile in new[] { CreditAmountProfile.Signed, CreditAmountProfile.NonNegative, CreditAmountProfile.Positive })
            Assert.ThrowsAny<JsonException>(() => ReadScalar(json, profile));
    }

    [Fact]
    public void Converter_write_revalidates_intermediates_for_its_target_profile()
    {
        var above = Signed(MaxInteger).Add(Signed("1"));
        var excessiveScale = CreditAmount.FromIntermediate(1, 9);
        foreach (var profile in new[] { CreditAmountProfile.Signed, CreditAmountProfile.NonNegative, CreditAmountProfile.Positive })
        {
            Assert.ThrowsAny<JsonException>(() => JsonSerializer.Serialize(above, Options(profile)));
            Assert.ThrowsAny<JsonException>(() => JsonSerializer.Serialize(excessiveScale, Options(profile)));
        }
        var cancelled = above.Subtract(Signed("1"));
        Assert.Equal("\"" + MaxInteger + "\"", JsonSerializer.Serialize(cancelled, Options(CreditAmountProfile.Positive)));
        Assert.Equal("\"0.01\"", JsonSerializer.Serialize(CreditAmount.FromIntermediate(1000, 5), Options(CreditAmountProfile.Positive)));
    }

    [Fact]
    public void Serialization_without_an_explicit_profile_fails_closed()
    {
        var unconfigured = new CreditAmountJsonConverter();
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        Assert.Throws<InvalidOperationException>(() => unconfigured.Write(writer, default, new JsonSerializerOptions()));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(default(CreditAmount)));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(CreditAmount.FromIntermediate(BigInteger.Pow(10, 100), 0)));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<CreditAmount>("\"1\""));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<CreditAmount>("{}"));
    }

    [Fact]
    public void Field_presence_and_nullability_belong_to_the_enclosing_contract()
    {
        var options = Options(CreditAmountProfile.Positive);
        Assert.Equal("1", StrictJson.Deserialize<RequiredAmountCarrier>(Utf8("{\"amount\":\"1\"}"), options)!.Amount.ToFieldString(CreditAmountProfile.Positive));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<RequiredAmountCarrier>(Utf8("{}"), options));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<RequiredAmountCarrier>(Utf8("{\"amount\":null}"), options));
        Assert.Null(StrictJson.Deserialize<OptionalAmountCarrier>(Utf8("{}"), options)!.Amount);
        Assert.Null(StrictJson.Deserialize<OptionalAmountCarrier>(Utf8("{\"amount\":null}"), options)!.Amount);
        Assert.Equal(Signed("1"), StrictJson.Deserialize<OptionalAmountCarrier>(Utf8("{\"amount\":\"1\"}"), options)!.Amount);
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<RequiredNullableAmountCarrier>(Utf8("{}"), options));
        Assert.Null(StrictJson.Deserialize<RequiredNullableAmountCarrier>(Utf8("{\"amount\":null}"), options)!.Amount);
    }

    [Fact]
    public void Mixed_carrier_profiles_are_attached_to_exact_fields()
    {
        const string json = "{\"delta\":\"-1\",\"rate\":\"0.00000001\",\"requestedQuantity\":\"1\",\"promisedQuantity\":\"0\"}";
        var carrier = StrictJson.Deserialize<NativeProfileCarrier>(Utf8(json))!;
        Assert.Equal("-1", carrier.Delta.ToFieldString(CreditAmountProfile.Signed));
        Assert.Equal("0.00000001", carrier.Rate.ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal("1", carrier.RequestedQuantity.ToFieldString(CreditAmountProfile.Positive));
        Assert.Equal("0", carrier.PromisedQuantity!.Value.ToFieldString(CreditAmountProfile.NonNegative));
        Assert.Equal(json, JsonSerializer.Serialize(carrier));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<NativeProfileCarrier>(Utf8(json.Replace("\"rate\":\"0.00000001\"", "\"rate\":\"0\""))));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<NativeProfileCarrier>(Utf8(json.Replace("\"requestedQuantity\":\"1\"", "\"requestedQuantity\":\"0\""))));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<NativeProfileCarrier>(Utf8(json.Replace("\"promisedQuantity\":\"0\"", "\"promisedQuantity\":\"-1\""))));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Serialize(new NativeProfileCarrier
        {
            Delta = Signed("-1"), Rate = Signed("0"), RequestedQuantity = Signed("1"), PromisedQuantity = Signed("0")
        }));
    }

    [Theory]
    [InlineData("{\"amount\":\"1\",\"amount\":\"2\"}")]
    [InlineData("{\"amount\":\"1\",\"\\u0061mount\":\"2\"}")]
    [InlineData("{\"amount\":\"1\",\"ignored\":[{\"a\":1,\"\\u0061\":2}]}")]
    [InlineData("{\"amount\":null,\"ignored\":{\"x\":1,\"x\":2}}")]
    public void Strict_carrier_boundary_rejects_duplicates_before_amount_conversion(string json)
    {
        var error = Assert.ThrowsAny<JsonException>(() => StrictJson.Deserialize<RequiredAmountCarrier>(Utf8(json), Options(CreditAmountProfile.Positive)));
        Assert.Equal("DUPLICATE_PROPERTY_NAME", error.Message);
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse(Utf8(json)));
    }

    [Fact]
    public void Escaped_amount_tokens_preserve_raw_bytes_independently_of_decoded_value()
    {
        var escaped = Utf8(" \r\n{ \"\\u0061mount\" : \"\\u0031.\\u0032\" }\t");
        var plain = Utf8("{\"amount\":\"1.2\"}");
        var callerBuffer = escaped.ToArray();
        using var retained = StrictJson.Parse(callerBuffer);
        Array.Fill(callerBuffer, (byte)'x');
        var decoded = retained.Deserialize<RequiredAmountCarrier>(Options(CreditAmountProfile.Positive))!;
        Assert.Equal(Signed("1.2"), decoded.Amount);
        Assert.Equal(decoded.Amount, StrictJson.Deserialize<RequiredAmountCarrier>(plain, Options(CreditAmountProfile.Positive))!.Amount);
        Assert.Equal("\"\\u0031.\\u0032\"", retained.RootElement.GetProperty("amount").GetRawText());
        Assert.Equal(escaped, retained.GetOriginalUtf8());
        Assert.Equal(SHA256.HashData(escaped), SHA256.HashData(retained.GetOriginalUtf8()));
        Assert.False(SHA256.HashData(plain).SequenceEqual(SHA256.HashData(retained.GetOriginalUtf8())));
        var returned = retained.GetOriginalUtf8();
        Array.Fill(returned, (byte)'y');
        Assert.Equal(escaped, retained.GetOriginalUtf8());
        Assert.Equal("{\"amount\":\"1.2\"}", JsonSerializer.Serialize(decoded, Options(CreditAmountProfile.Positive)));
        Assert.False(escaped.SequenceEqual(Utf8(JsonSerializer.Serialize(decoded, Options(CreditAmountProfile.Positive)))));
        Assert.ThrowsAny<JsonException>(() => ReadScalar("\"\\u0031.\\u0030\"", CreditAmountProfile.Signed));
    }

    [Fact]
    public void Culture_does_not_change_wire_grammar_or_emission()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1234.5", Signed("1234.5").ToFieldString(CreditAmountProfile.Positive));
            Assert.Equal("\"1234.5\"", JsonSerializer.Serialize(Signed("1234.5"), Options(CreditAmountProfile.Positive)));
            Assert.Throws<FormatException>(() => Signed("1234,5"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Tests_execute_on_actual_net8_runtime()
    {
        output.WriteLine(RuntimeInformation.FrameworkDescription);
        output.WriteLine(AppContext.TargetFrameworkName);
        Assert.Equal(8, Environment.Version.Major);
        Assert.Equal(".NETCoreApp,Version=v8.0", AppContext.TargetFrameworkName);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    private static CreditAmount Signed(string text) => CreditAmount.Parse(text, CreditAmountProfile.Signed);
    private static CreditAmount ReadScalar(string json, CreditAmountProfile profile) =>
        JsonSerializer.Deserialize<CreditAmount>(json, Options(profile));
    private static JsonSerializerOptions Options(CreditAmountProfile profile)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new CreditAmountJsonConverter(profile));
        return options;
    }

    public sealed class RequiredAmountCarrier
    {
        [JsonPropertyName("amount")]
        public required CreditAmount Amount { get; init; }
    }

    public sealed class OptionalAmountCarrier
    {
        [JsonPropertyName("amount")]
        public CreditAmount? Amount { get; init; }
    }

    public sealed class RequiredNullableAmountCarrier
    {
        [JsonPropertyName("amount")]
        public required CreditAmount? Amount { get; init; }
    }

    public sealed class NativeProfileCarrier
    {
        [JsonPropertyName("delta"), CreditAmountField(CreditAmountProfile.Signed)]
        public required CreditAmount Delta { get; init; }
        [JsonPropertyName("rate"), CreditAmountField(CreditAmountProfile.Positive)]
        public required CreditAmount Rate { get; init; }
        [JsonPropertyName("requestedQuantity"), CreditAmountField(CreditAmountProfile.Positive)]
        public required CreditAmount RequestedQuantity { get; init; }
        [JsonPropertyName("promisedQuantity"), CreditAmountField(CreditAmountProfile.NonNegative)]
        public required CreditAmount? PromisedQuantity { get; init; }
    }
}
