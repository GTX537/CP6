using System.Globalization;
using System.Numerics;
using CP6.Persistence.PostgreSql.Contracts;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace CP6.ExactValues.PostgreSqlTests;

public partial class ExactValueStorageTests
{
    public static IEnumerable<object[]> InventoryFields => TestInputs.Inventory.Select(x => new object[] { x.Key });

    [Theory]
    [MemberData(nameof(InventoryFields))]
    public async Task Inventory_all_66_mappings_roundtrip_full_scale_and_enforce_null_coupling(string key)
    {
        var mapping = TestInputs.Inventory.Single(x => x.Key == key);
        var wire = new string('9', 90) + (mapping.MaximumScale == 0 ? "" : "." + new string('1', mapping.MaximumScale));
        var id = "mapping-" + key;
        await using var connection = await database.OpenWriterAsync();
        await InsertInventoryRow(connection, mapping.Table, id, new() { [mapping.Column] = wire });
        var read = await ReadInventoryField(connection, mapping, id);
        Assert.Equal(wire, read!.OriginalWire);
        Assert.Equal((short)mapping.MaximumScale, read.Scale);
        Assert.Equal(wire.Replace(".", "", StringComparison.Ordinal), read.Digits);
        var table = "exact_fixture." + FixtureSql.Quote(mapping.Table);
        foreach (var column in new[] { mapping.Digits, mapping.Scale, mapping.Wire })
            await Reject(connection, $"UPDATE {table} SET {FixtureSql.Quote(column)}=NULL WHERE id=@id",
                mapping.Nullable ? "23514" : "23502", Text("id", id));
        var nullSql = $"UPDATE {table} SET {FixtureSql.Quote(mapping.Digits)}=NULL,{FixtureSql.Quote(mapping.Scale)}=NULL,{FixtureSql.Quote(mapping.Wire)}=NULL WHERE id=@id";
        if (!mapping.Nullable) await Reject(connection, nullSql, "23502", Text("id", id));
        else if (mapping.Table == "MovingAveragePoolRevision")
        {
            // Individually null triples still violate the ACTIVE five-field relationship.
            await Reject(connection, nullSql, "23514", Text("id", id));
        }
        else
        {
            await Execute(connection, nullSql, Text("id", id));
            Assert.Null(await ReadInventoryField(connection, mapping, id));
        }
        Assert.Equal(0, ExactValueStorage.InventoryParameters(wire, mapping.Profile, mapping.Nullable, mapping.Rule)[0].Size);
    }

    [Theory]
    [InlineData("-0.000", "0", 0)]
    [InlineData("123.45000", "12345", 2)]
    [InlineData("-123.45000", "-12345", 2)]
    public async Task Inventory_original_lexeme_is_independent_from_normalized_storage(string wire, string digits, short scale)
    {
        await using var connection = await database.OpenWriterAsync();
        var id = "raw-" + wire;
        await InsertInventoryScalar(connection, id, wire, InventoryStorageProfile.Decimal18);
        await using var command = new NpgsqlCommand("SELECT digits,scale,wire FROM exact_fixture.inventory_scalar WHERE id=@id", connection);
        command.Parameters.Add(Text("id", id));
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        var value = ExactValueStorage.ReadInventory(reader, 0, 1, 2, InventoryStorageProfile.Decimal18)!;
        Assert.Equal(digits, value.Digits); Assert.Equal(scale, value.Scale); Assert.Equal(wire, value.OriginalWire);
    }

    [Theory]
    [InlineData("00", 0, "0")]
    [InlineData("-0", 0, "0")]
    [InlineData("+1", 0, "1")]
    [InlineData("1e2", 0, "100")]
    [InlineData("1\n", 0, "1")]
    [InlineData("10", 1, "1.0")]
    [InlineData("0", 1, "-0.0")]
    [InlineData("12", 1, "1.3")]
    [InlineData("1", 19, "0.0000000000000000001")]
    public async Task Inventory_rejects_noncanonical_or_mismatched_stored_pairs(string digits, short scale, string wire)
    {
        Assert.Throws<FormatException>(() => ExactValueStorage.RestoreInventory(digits, scale, wire, InventoryStorageProfile.Decimal18));
        await using var connection = await database.OpenWriterAsync();
        await Reject(connection, "INSERT INTO exact_fixture.inventory_scalar VALUES (@id,'Decimal18',@digits,@scale,@wire)", "23514",
            Text("id", Guid.NewGuid().ToString()), Text("digits", digits), new NpgsqlParameter("scale", NpgsqlDbType.Smallint) { Value = scale }, Text("wire", wire));
    }

    [Fact]
    public async Task Inventory_coefficient_beyond_PostgreSQL_numeric_capacity_remains_text()
    {
        // PostgreSQL numeric has at most 131072 digits before the decimal point. No CAST is attempted.
        var wire = new string('8', 131073) + ".123456789012345678";
        await using var connection = await database.OpenWriterAsync();
        await InsertInventoryScalar(connection, "beyond-numeric", wire, InventoryStorageProfile.Decimal18);
        await using var command = new NpgsqlCommand("SELECT digits,scale,wire FROM exact_fixture.inventory_scalar WHERE id='beyond-numeric'", connection);
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        var value = ExactValueStorage.ReadInventory(reader, 0, 1, 2, InventoryStorageProfile.Decimal18)!;
        Assert.Equal(wire, value.OriginalWire); Assert.Equal(131091, value.Digits.Length); Assert.Equal(18, value.Scale);
    }

    [Fact]
    public async Task Mavg_preserves_unreduced_numerators_denominators_and_remainder_raw_strings()
    {
        await using var connection = await database.OpenWriterAsync();
        foreach (var numerator in new[] { "00", "-0", "6", "-6" })
        {
            ExactValueStorage.ValidateMovingAverage("ACTIVE", "2", "0", numerator, "12", "0.000", "-0", "12");
            var id = "mavg-raw-" + numerator;
            await InsertInventoryRow(connection, "MovingAveragePoolRevision", id, new()
            {
                ["ExactAverageNumerator"] = numerator, ["ExactAverageDenominator"] = "12",
                ["DisplayAverage18"] = "0.000", ["DiscardedRemainderNumerator"] = "-0", ["DiscardedRemainderDenominator"] = "12"
            });
            foreach (var field in new[] { "ExactAverageNumerator", "ExactAverageDenominator", "DiscardedRemainderNumerator", "DiscardedRemainderDenominator" })
            {
                var value = await ReadInventoryField(connection, TestInputs.Inventory.Single(x => x.Table == "MovingAveragePoolRevision" && x.Column == field), id);
                Assert.Equal(field switch { "ExactAverageNumerator" => numerator, "DiscardedRemainderNumerator" => "-0", _ => "12" }, value!.OriginalWire);
            }
        }
    }

    [Fact]
    public async Task Mavg_active_empty_state_constraints_and_positive_denominators_are_enforced()
    {
        await using var connection = await database.OpenWriterAsync();
        const string table = "MovingAveragePoolRevision";
        var empty = TestInputs.Inventory.Where(x => x.Table == table && x.Nullable).ToDictionary(x => x.Column, _ => (string?)null);
        empty["AfterQty"] = "0.000"; empty["AfterUnroundedValue"] = "-0.000";
        await InsertInventoryRow(connection, table, "mavg-empty", empty, "EMPTY");
        ExactValueStorage.ValidateMovingAverage("EMPTY", "0.000", "-0.000", null, null, null, null, null);
        foreach (var field in TestInputs.Inventory.Where(x => x.Table == table && x.Nullable))
            Assert.Null(await ReadInventoryField(connection, field, "mavg-empty"));
        foreach (var state in new[] { "ACTIVE", "UNKNOWN" })
            await Reject(connection, "UPDATE exact_fixture.\"MovingAveragePoolRevision\" SET state=@state WHERE id='mavg-empty'", "23514", Text("state", state));
        await Reject(connection, "UPDATE exact_fixture.\"MovingAveragePoolRevision\" SET \"AfterQtyOriginalWire\"='-0.000' WHERE id='mavg-empty'", "23514");
        await InsertInventoryRow(connection, table, "mavg-active", []);
        await Reject(connection, "UPDATE exact_fixture.\"MovingAveragePoolRevision\" SET state='EMPTY' WHERE id='mavg-active'", "23514");
        foreach (var bad in new[] { "0", "00", "-1", "1.0", "+1", "1\n" })
        {
            Assert.Throws<FormatException>(() => ExactValueStorage.ValidateMovingAverage("ACTIVE", "2", "1", "00", bad, "0", "-0", "2"));
            foreach (var name in new[] { "ExactAverageDenominator", "DiscardedRemainderDenominator" })
                await Reject(connection, $"UPDATE exact_fixture.\"MovingAveragePoolRevision\" SET {FixtureSql.Quote(name + "OriginalWire")}=@wire WHERE id='mavg-active'", "23514", Text("wire", bad));
        }
        Assert.Throws<FormatException>(() => ExactValueStorage.ValidateMovingAverage("EMPTY", "-0", "0", null, null, null, null, null));
        Assert.Throws<FormatException>(() => ExactValueStorage.ValidateMovingAverage("EMPTY", "0", "1", null, null, null, null, null));
        Assert.Throws<FormatException>(() => ExactValueStorage.ValidateMovingAverage("ACTIVE", "0", "1", "1", "2", "0.5", "0", "2"));
    }

    private static async Task InsertInventoryRow(NpgsqlConnection connection, string table, string id,
        Dictionary<string, string?> overrides, string state = "ACTIVE")
    {
        var columns = new List<string> { "id" }; var values = new List<string> { "@id" };
        await using var command = new NpgsqlCommand { Connection = connection };
        command.Parameters.Add(Text("id", id));
        var index = 0;
        foreach (var field in TestInputs.Inventory.Where(x => x.Table == table))
        {
            var wire = overrides.TryGetValue(field.Column, out var replacement) ? replacement :
                field.Profile == InventoryStorageProfile.MavgNumerator ? "00" : "2";
            var parameters = ExactValueStorage.InventoryParameters(wire, field.Profile, field.Nullable, field.Rule);
            foreach (var (column, parameter) in new[] { (field.Digits, parameters[0]), (field.Scale, parameters[1]), (field.Wire, parameters[2]) })
            {
                parameter.ParameterName = "p" + index.ToString(CultureInfo.InvariantCulture);
                index++;
                columns.Add(FixtureSql.Quote(column)); values.Add("@" + parameter.ParameterName); command.Parameters.Add(parameter);
            }
        }
        if (table == "MovingAveragePoolRevision") { columns.Add("state"); values.Add("@state"); command.Parameters.Add(Text("state", state)); }
        command.CommandText = $"INSERT INTO exact_fixture.{FixtureSql.Quote(table)} ({string.Join(',', columns)}) VALUES ({string.Join(',', values)})";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<InventoryStorageValue?> ReadInventoryField(NpgsqlConnection connection, InventoryMapping mapping, string id)
    {
        await using var command = new NpgsqlCommand($"SELECT {FixtureSql.Quote(mapping.Digits)},{FixtureSql.Quote(mapping.Scale)},{FixtureSql.Quote(mapping.Wire)} FROM exact_fixture.{FixtureSql.Quote(mapping.Table)} WHERE id=@id", connection);
        command.Parameters.Add(Text("id", id));
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        return ExactValueStorage.ReadInventory(reader, 0, 1, 2, mapping.Profile, mapping.Nullable, mapping.Rule);
    }
}
