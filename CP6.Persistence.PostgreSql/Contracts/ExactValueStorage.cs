using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CP6.Entity.Contracts.FinCredit;
using CP6.Entity.Contracts.FinInventory;
using CP6.Entity.Contracts.PlanAtp;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Persistence.PostgreSql.Contracts;

/// <summary>Validates final field profiles before constructing exact PostgreSQL parameters.</summary>
public static class ExactValueStorage
{
    public const string MappingVersion = "CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0";

    // Never set Size, Precision or Scale on these parameters: truncation/rounding is not validation.
    public static NpgsqlParameter[] CreditParameters(CreditAmount? value, CreditAmountProfile profile, bool nullable = false)
    {
        ValidateProfile(profile);
        if (value is null && !nullable) throw new FormatException("CREDIT_NULL_NOT_ALLOWED");
        if (value is { } amount) _ = amount.ToFieldString(profile);
        return [Parameter("coefficient", NpgsqlDbType.Numeric, value?.Coefficient),
                Parameter("scale", NpgsqlDbType.Smallint, value is { } v ? checked((short)v.Scale) : null)];
    }

    /// <summary>Rejects corrupt/noncanonical pairs before normalization could conceal them.</summary>
    public static CreditAmount? RestoreCredit(BigInteger? coefficient, short? scale, CreditAmountProfile profile, bool nullable = false)
    {
        ValidateProfile(profile);
        if (coefficient is null && scale is null && nullable) return null;
        if (coefficient is null || scale is null || scale < 0 || scale > 8)
            throw new FormatException("CREDIT_STORAGE_PAIR_INVALID");
        var value = CreditAmount.FromIntermediate(coefficient.Value, scale.Value);
        if (value.Coefficient != coefficient || value.Scale != scale)
            throw new FormatException("CREDIT_STORAGE_PAIR_NOT_CANONICAL");
        _ = value.ToFieldString(profile);
        return value;
    }

    public static CreditAmount? ReadCredit(NpgsqlDataReader reader, int coefficientOrdinal, int scaleOrdinal,
        CreditAmountProfile profile, bool nullable = false) => RestoreCredit(
            reader.IsDBNull(coefficientOrdinal) ? null : reader.GetFieldValue<BigInteger>(coefficientOrdinal),
            reader.IsDBNull(scaleOrdinal) ? null : reader.GetInt16(scaleOrdinal), profile, nullable);

    /// <summary>Uses the delivered scalar contracts; integer lexemes remain independent of reduced ratios.</summary>
    public static InventoryStorageValue? ParseInventory(string? wire, InventoryStorageProfile profile, bool nullable = false,
        InventoryNumericRule numericRule = InventoryNumericRule.Any)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        if (!Enum.IsDefined(numericRule)) throw new ArgumentOutOfRangeException(nameof(numericRule));
        if (wire is null)
            return nullable ? null : throw new FormatException("INVENTORY_NULL_NOT_ALLOWED");
        if (profile is InventoryStorageProfile.PositiveInteger or InventoryStorageProfile.MavgNumerator)
        {
            var pattern = profile == InventoryStorageProfile.PositiveInteger ? PositiveIntegerPattern : MavgNumeratorPattern;
            if (!pattern.IsMatch(wire)) throw new FormatException("INVENTORY_INTEGER_WIRE_INVALID");
            var integer = BigInteger.Parse(wire, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            ValidateSign(integer.Sign, numericRule);
            return new InventoryStorageValue(integer.ToString(CultureInfo.InvariantCulture), 0, wire);
        }
        var native = profile switch
        {
            InventoryStorageProfile.Decimal18 => InventoryDecimalProfile.Decimal18,
            InventoryStorageProfile.Qty12 => InventoryDecimalProfile.Qty12,
            InventoryStorageProfile.NonNegativeQty12 => InventoryDecimalProfile.NonNegativeQty12,
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
        var parsed = InventoryDecimal.Parse(wire, native);
        ValidateSign(parsed.Value.Coefficient.Sign, numericRule);
        return new InventoryStorageValue(parsed.Value.Coefficient.ToString(CultureInfo.InvariantCulture),
            checked((short)parsed.Value.Scale), parsed.OriginalWire);
    }

    public static NpgsqlParameter[] InventoryParameters(string? originalWire, InventoryStorageProfile profile, bool nullable = false,
        InventoryNumericRule numericRule = InventoryNumericRule.Any)
    {
        var value = ParseInventory(originalWire, profile, nullable, numericRule);
        return [Parameter("digits", NpgsqlDbType.Text, value?.Digits),
                Parameter("scale", NpgsqlDbType.Smallint, value?.Scale),
                Parameter("wire", NpgsqlDbType.Text, value?.OriginalWire)];
    }

    public static InventoryStorageValue? RestoreInventory(string? digits, short? scale, string? originalWire,
        InventoryStorageProfile profile, bool nullable = false, InventoryNumericRule numericRule = InventoryNumericRule.Any)
    {
        var value = ParseInventory(originalWire, profile, nullable, numericRule);
        if (value is null && digits is null && scale is null) return null;
        if (value is null || digits != value.Digits || scale != value.Scale)
            throw new FormatException("INVENTORY_STORAGE_WIRE_MISMATCH");
        return value;
    }

    public static InventoryStorageValue? ReadInventory(NpgsqlDataReader reader, int digitsOrdinal, int scaleOrdinal,
        int wireOrdinal, InventoryStorageProfile profile, bool nullable = false, InventoryNumericRule numericRule = InventoryNumericRule.Any) => RestoreInventory(
            reader.IsDBNull(digitsOrdinal) ? null : reader.GetString(digitsOrdinal),
            reader.IsDBNull(scaleOrdinal) ? null : reader.GetInt16(scaleOrdinal),
            reader.IsDBNull(wireOrdinal) ? null : reader.GetString(wireOrdinal), profile, nullable, numericRule);

    /// <summary>Preserves the native R10 ACTIVE/EMPTY scalar and null relations, without inventing a full object codec.</summary>
    public static void ValidateMovingAverage(string state, string afterQty, string afterUnroundedValue,
        string? exactNumerator, string? exactDenominator, string? displayAverage18,
        string? remainderNumerator, string? remainderDenominator)
    {
        // Both states require a nonnegative lexical afterQty; EMPTY disallows native signed negative zero.
        var qty = InventoryDecimal.Parse(afterQty, InventoryDecimalProfile.NonNegativeQty12).Value;
        var amount = InventoryDecimal.Parse(afterUnroundedValue, InventoryDecimalProfile.Decimal18).Value;
        string?[] average = [exactNumerator, exactDenominator, displayAverage18, remainderNumerator, remainderDenominator];
        if (state == "EMPTY" && qty.Coefficient.IsZero && amount.Coefficient.IsZero && average.All(x => x is null)) return;
        if (state != "ACTIVE" || qty.Coefficient.Sign <= 0 || average.Any(x => x is null))
            throw new FormatException("MAVG_STATE_INVALID");
        _ = ParseInventory(exactNumerator, InventoryStorageProfile.MavgNumerator);
        _ = ParseInventory(exactDenominator, InventoryStorageProfile.PositiveInteger);
        _ = ParseInventory(displayAverage18, InventoryStorageProfile.Decimal18);
        _ = ParseInventory(remainderNumerator, InventoryStorageProfile.MavgNumerator);
        _ = ParseInventory(remainderDenominator, InventoryStorageProfile.PositiveInteger);
    }

    public static NpgsqlParameter AtpUomParameter(string name, string? wire, bool nullable = false)
    {
        if (wire is null && !nullable) throw new FormatException("ATP_UOM_NULL_NOT_ALLOWED");
        var validated = wire is null ? null : StableId.Parse(wire).Value;
        return Parameter(name, NpgsqlDbType.Text, validated);
    }

    private static void ValidateProfile(CreditAmountProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
    }

    private static void ValidateSign(int sign, InventoryNumericRule rule)
    {
        if ((rule == InventoryNumericRule.NonNegative && sign < 0) || (rule == InventoryNumericRule.Positive && sign <= 0))
            throw new FormatException("INVENTORY_NUMERIC_SIGN_RULE_FAILED");
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) => new(name, type) { Value = value ?? DBNull.Value };
    private static readonly Regex PositiveIntegerPattern = new(@"\A[1-9][0-9]*\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex MavgNumeratorPattern = new(@"\A-?[0-9]+\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}

public enum InventoryStorageProfile { Decimal18, Qty12, NonNegativeQty12, PositiveInteger, MavgNumerator }
/// <summary>Dictionary scalar checks, independent of the native field's lexical profile (including signed zero).</summary>
public enum InventoryNumericRule { Any, NonNegative, Positive }

/// <summary>Validated normalized value plus the unchanged decoded field string; not original JSON document bytes.</summary>
public sealed class InventoryStorageValue
{
    public string Digits { get; }
    public short Scale { get; }
    public string OriginalWire { get; }
    internal InventoryStorageValue(string digits, short scale, string originalWire) =>
        (Digits, Scale, OriginalWire) = (digits, scale, originalWire);
}
