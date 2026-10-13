using System.Numerics;
using System.Text;
using System.Text.Json;
using CP6.Entity.Contracts.FinCredit;
using CP6.Entity.Contracts.FinInventory;
using CP6.Entity.Contracts.PlanAtp;
using CP6.Persistence.PostgreSql.Contracts;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace CP6.ExactValues.PostgreSqlTests;

public partial class ExactValueStorageTests
{
    public static IEnumerable<object[]> CreditFields => TestInputs.Mapping.GetProperty("credit").GetProperty("field_bindings")
        .EnumerateArray().SelectMany(group => group.GetProperty("pointers").EnumerateArray()
            .Select(pointer => new object[] { pointer.GetString()!, group.GetProperty("profile").GetString()! }));

    [Theory]
    [MemberData(nameof(CreditFields))]
    public async Task Credit_all_42_native_fields_use_their_exact_destination_profile(string pointer, string profileName)
    {
        var profile = profileName switch { "fin-credit-signed" => CreditAmountProfile.Signed,
            "fin-credit-nonnegative" => CreditAmountProfile.NonNegative, "fin-credit-positive" => CreditAmountProfile.Positive,
            _ => throw new InvalidOperationException("UNKNOWN_NATIVE_FIELD_PROFILE") };
        var value = CreditAmount.Parse(profile == CreditAmountProfile.Signed ? "-0.00000001" : profile == CreditAmountProfile.Positive ? "0.00000001" : "0", profile);
        await using var connection = await database.OpenWriterAsync();
        await InsertCredit(connection, pointer, value, profile);
        await using var command = new NpgsqlCommand("SELECT coefficient,scale FROM exact_fixture.credit WHERE id=@id", connection);
        command.Parameters.Add(Text("id", pointer));
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(value, ExactValueStorage.ReadCredit(reader, 0, 1, profile));
        if (profile != CreditAmountProfile.Signed)
            Assert.Throws<FormatException>(() => ExactValueStorage.CreditParameters(CreditAmount.FromIntermediate(-1, 0), profile));
        if (profile == CreditAmountProfile.Positive)
            Assert.Throws<FormatException>(() => ExactValueStorage.CreditParameters(default(CreditAmount), profile));
    }

    [Fact]
    public async Task Credit_rejects_invalid_pairs_and_guards_raw_integer_input_before_numeric_rounding()
    {
        foreach (var pair in new[] { (BigInteger.Zero, (short)1), (new BigInteger(10), (short)1), (BigInteger.One, (short)-1),
                     (BigInteger.One, (short)9), (BigInteger.Pow(10, 38), (short)0) })
            Assert.Throws<FormatException>(() => ExactValueStorage.RestoreCredit(pair.Item1, pair.Item2, CreditAmountProfile.Signed));
        Assert.Throws<FormatException>(() => ExactValueStorage.CreditParameters(CreditAmount.FromIntermediate(BigInteger.Pow(10, 38), 0), CreditAmountProfile.Signed));
        Assert.Throws<FormatException>(() => ExactValueStorage.CreditParameters(CreditAmount.FromIntermediate(1, 9), CreditAmountProfile.Signed));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactValueStorage.CreditParameters(null, (CreditAmountProfile)0, true));
        Assert.Throws<FormatException>(() => ExactValueStorage.RestoreCredit(null, 0, CreditAmountProfile.Signed, true));
        Assert.Throws<FormatException>(() => ExactValueStorage.CreditParameters(null, CreditAmountProfile.Signed));
        await using var connection = await database.OpenWriterAsync();
        await using (var coercion = new NpgsqlCommand("SELECT 1.5::numeric(38,0)", connection))
        await using (var reader = await coercion.ExecuteReaderAsync())
        { Assert.True(await reader.ReadAsync()); Assert.Equal(new BigInteger(2), reader.GetFieldValue<BigInteger>(0)); }
        foreach (var raw in new[] { "1.5", "NaN", "Infinity", "1e3", "01", "-0", "1\n", "1 ", "１", new string('9', 39) })
            await Reject(connection, "SELECT exact_fixture.credit_integer_input(@raw)", "23514", Text("raw", raw));
        foreach (var tuple in new[] { "'Signed',0,1,false", "'Signed',10,1,false", "'Signed',1,9,false", "'NonNegative',-1,0,false",
                     "'Positive',0,0,false", "'Signed','NaN',0,false", "'Signed',NULL,0,true", "'Signed',1,NULL,true", "'Unknown',1,0,false" })
            await Reject(connection, $"INSERT INTO exact_fixture.credit VALUES (@id,{tuple})", "23514", Text("id", Guid.NewGuid().ToString()));
        await Reject(connection, "INSERT INTO exact_fixture.credit VALUES ('credit-overflow','Signed',100000000000000000000000000000000000000,0,false)", "22003");
        await InsertCredit(connection, "credit-null", null, CreditAmountProfile.NonNegative, nullable: true);
        await using var readNull = new NpgsqlCommand("SELECT coefficient,scale FROM exact_fixture.credit WHERE id='credit-null'", connection);
        await using var nullReader = await readNull.ExecuteReaderAsync(); Assert.True(await nullReader.ReadAsync());
        Assert.Null(ExactValueStorage.ReadCredit(nullReader, 0, 1, CreditAmountProfile.NonNegative, true));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Wrong_JSON_tokens_fail_native_contracts_before_storage_binding(string json)
    {
        var credit = new JsonSerializerOptions(); credit.Converters.Add(new CreditAmountJsonConverter(CreditAmountProfile.Signed));
        var inventory = new JsonSerializerOptions(); inventory.Converters.Add(new Decimal18JsonConverter());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreditAmount>(json, credit));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InventoryDecimal>(json, inventory));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StableId>(json));
    }

    [Fact]
    public async Task Committed_values_and_original_UTF8_survive_new_physical_connections_and_repeated_persistence()
    {
        var wire = new string('9', 90) + ".123450000000000000";
        var bytes = Encoding.UTF8.GetBytes("{ \"amount\" : \"-0.000\", \"escaped\" : \"\\u0041\" }\r\n");
        var amount = CreditAmount.Parse("999999999999999999999999999999.12345678", CreditAmountProfile.Positive);
        var pids = new HashSet<int>();
        Guid atpId;
        await using (var connection = await database.OpenWriterAsync())
        {
            pids.Add(connection.ProcessID);
            await using var transaction = await connection.BeginTransactionAsync();
            await InsertCredit(connection, "reconnect-credit", amount, CreditAmountProfile.Positive);
            await InsertInventoryScalar(connection, "reconnect-inventory", wire, InventoryStorageProfile.Decimal18);
            atpId = await InsertAtpRows(connection, new string('U', 128));
            await Execute(connection, "INSERT INTO exact_fixture.original_document VALUES ('reconnect-document',@bytes)", new NpgsqlParameter("bytes", NpgsqlDbType.Bytea) { Value = bytes });
            await transaction.CommitAsync();
        }
        for (var iteration = 0; iteration < 2; iteration++)
        {
            await using var connection = await database.OpenWriterAsync(); Assert.True(pids.Add(connection.ProcessID));
            Assert.Equal(Enumerable.Repeat(new string('U', 128), 5), await ReadAtpUoms(connection, atpId));
            await using (var command = new NpgsqlCommand("SELECT coefficient,scale FROM exact_fixture.credit WHERE id='reconnect-credit'", connection))
            await using (var reader = await command.ExecuteReaderAsync())
            { Assert.True(await reader.ReadAsync()); Assert.Equal(amount, ExactValueStorage.ReadCredit(reader, 0, 1, CreditAmountProfile.Positive)); }
            InventoryStorageValue restored;
            await using (var command = new NpgsqlCommand("SELECT digits,scale,wire FROM exact_fixture.inventory_scalar WHERE id='reconnect-inventory'", connection))
            await using (var reader = await command.ExecuteReaderAsync())
            { Assert.True(await reader.ReadAsync()); restored = ExactValueStorage.ReadInventory(reader, 0, 1, 2, InventoryStorageProfile.Decimal18)!; Assert.Equal(wire, restored.OriginalWire); }
            await InsertInventoryScalar(connection, "reconnect-repeat-" + iteration, restored.OriginalWire, InventoryStorageProfile.Decimal18);
            await using var raw = new NpgsqlCommand("SELECT original_utf8 FROM exact_fixture.original_document WHERE id='reconnect-document'", connection);
            Assert.Equal(bytes, Assert.IsType<byte[]>(await raw.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task Constraint_failure_aborts_and_rolls_back_all_writes_on_the_same_transaction()
    {
        Guid atpId;
        await using (var connection = await database.OpenWriterAsync())
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await InsertCredit(connection, "rollback-credit", CreditAmount.Parse("0.00000001", CreditAmountProfile.Positive), CreditAmountProfile.Positive);
            await InsertInventoryScalar(connection, "rollback-inventory", "-0.000", InventoryStorageProfile.Decimal18);
            await Execute(connection, "INSERT INTO exact_fixture.original_document VALUES ('rollback-document',@bytes)", new NpgsqlParameter("bytes", NpgsqlDbType.Bytea) { Value = new byte[] { 1, 2, 3 } });
            atpId = await InsertAtpRows(connection, "EA");
            await Reject(connection, "UPDATE exact_fixture.evaluation SET result_uom=exact_fixture.uom_input(@bad) WHERE evaluation_id=@id", "23514",
                Text("bad", new string('A', 128) + " "), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = atpId });
            await Reject(connection, "SELECT 1", "25P02");
            await transaction.RollbackAsync();
        }
        await using var reopened = await database.OpenWriterAsync();
        await using var count = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM exact_fixture.credit WHERE id='rollback-credit')+
                   (SELECT count(*) FROM exact_fixture.inventory_scalar WHERE id='rollback-inventory')+
                   (SELECT count(*) FROM exact_fixture.original_document WHERE id='rollback-document')+
                   (SELECT count(*) FROM exact_fixture.evaluation WHERE evaluation_id=@id)+
                   (SELECT count(*) FROM exact_fixture.segment WHERE evaluation_id=@id)+
                   (SELECT count(*) FROM exact_fixture.basis WHERE evaluation_id=@id)+
                   (SELECT count(*) FROM exact_fixture.reservation_ledger WHERE evaluation_id=@id)
            """, reopened);
        count.Parameters.AddWithValue("id", atpId); Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Owned_resource_rejects_collisions_identity_drift_sessions_and_writer_DDL()
    {
        var collision = await Assert.ThrowsAsync<InvalidOperationException>(() => database.AssertExistingNameRejectedAsync());
        Assert.Equal("DATABASE_ALREADY_EXISTS", collision.Message);
        var marker = "CP6-S01-10:" + new string('a', 32);
        foreach (var drift in new[] { "oid", "marker", "owner", "login", "privileges" })
            Assert.Throws<InvalidOperationException>(() => OwnedPostgres.ValidateDatabaseSnapshot(10, drift == "oid" ? 11u : 10u,
                marker, drift == "marker" ? marker + "other" : marker, drift == "owner" ? "unowned" : "s0110_owner", drift == "login", drift == "privileges"));
        Assert.Throws<InvalidOperationException>(() => OwnedPostgres.AssertPath("D:/CP6-local-test/S01-10/../other"));
        await using (var connection = await database.OpenWriterAsync())
        {
            var busy = await Assert.ThrowsAsync<InvalidOperationException>(() => database.AssertNoOtherSessionsAsync());
            Assert.Equal("OWNED_CLUSTER_HAS_OTHER_SESSIONS", busy.Message);
            await Reject(connection, "CREATE SCHEMA writer_must_not_create", "42501");
            await Reject(connection, "CREATE TABLE exact_fixture.writer_must_not_create(id integer)", "42501");
            await Reject(connection, "SET ROLE s0110_owner", "42501");
        }
        await database.AssertNoOtherSessionsAsync();
    }
}
