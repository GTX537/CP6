using System.Numerics;
using System.Text.Json;
using CP6.Entity.Contracts.FinCredit;
using CP6.Persistence.PostgreSql.Contracts;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace CP6.ExactValues.PostgreSqlTests;

public partial class ExactValueStorageTests(OwnedPostgres database) : IClassFixture<OwnedPostgres>
{
    public static IEnumerable<object[]> FixedVectorIds => TestInputs.Vectors.Keys.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(FixedVectorIds))]
    public async Task Fixed_vector_matches_pinned_expectation_and_real_storage(string id)
    {
        var vector = TestInputs.Vectors[id];
        var wire = vector.GetProperty("input").GetString()!;
        var expected = vector.GetProperty("expected");
        var accepted = expected.GetProperty("accepted").GetBoolean();
        var profile = vector.GetProperty("profile_id").GetString()!;
        await using var connection = await database.OpenWriterAsync();
        if (profile.StartsWith("fin-credit-", StringComparison.Ordinal))
        {
            var native = profile switch { "fin-credit-signed" => CreditAmountProfile.Signed,
                "fin-credit-nonnegative" => CreditAmountProfile.NonNegative, "fin-credit-positive" => CreditAmountProfile.Positive,
                _ => throw new InvalidOperationException("UNKNOWN_CREDIT_PROFILE") };
            if (!accepted) { Assert.Throws<FormatException>(() => CreditAmount.Parse(wire, native)); return; }
            var value = CreditAmount.Parse(wire, native);
            var pair = expected.GetProperty("pair");
            Assert.Equal(pair.GetProperty("unscaledValue").GetString(), value.Coefficient.ToString());
            Assert.Equal(pair.GetProperty("scale").GetInt32(), value.Scale);
            await InsertCredit(connection, id, value, native);
            await using var read = new NpgsqlCommand("SELECT coefficient,scale FROM exact_fixture.credit WHERE id=@id", connection);
            read.Parameters.AddWithValue("id", id);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(wire, ExactValueStorage.ReadCredit(reader, 0, 1, native)!.Value.ToFieldString(native));
        }
        else if (profile.StartsWith("fin-inv-", StringComparison.Ordinal))
        {
            var native = profile switch { "fin-inv-decimal18" => InventoryStorageProfile.Decimal18,
                "fin-inv-qty12" => InventoryStorageProfile.Qty12, _ => throw new InvalidOperationException("UNKNOWN_INVENTORY_PROFILE") };
            if (!accepted)
            {
                Assert.Throws<FormatException>(() => ExactValueStorage.InventoryParameters(wire, native));
                await Reject(connection, "INSERT INTO exact_fixture.inventory_scalar VALUES (@id,@profile,'0',0,@wire)", "23514",
                    Text("id", id), Text("profile", native.ToString()), Text("wire", wire));
                return;
            }
            var value = ExactValueStorage.ParseInventory(wire, native)!;
            var pair = expected.GetProperty("pair");
            Assert.Equal(pair.GetProperty("unscaledDigits").GetString(), value.Digits);
            Assert.Equal(pair.GetProperty("scale").GetInt32(), value.Scale);
            Assert.Equal(pair.GetProperty("originalWire").GetString(), value.OriginalWire);
            await InsertInventoryScalar(connection, id, wire, native);
            await using var read = new NpgsqlCommand("SELECT digits,scale,wire FROM exact_fixture.inventory_scalar WHERE id=@id", connection);
            read.Parameters.AddWithValue("id", id);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var restored = ExactValueStorage.ReadInventory(reader, 0, 1, 2, native)!;
            Assert.Equal(value.Digits, restored.Digits); Assert.Equal(value.Scale, restored.Scale); Assert.Equal(wire, restored.OriginalWire);
        }
        else if (profile == "atp-stableid-r1")
        {
            if (!accepted)
            {
                Assert.ThrowsAny<ArgumentException>(() => ExactValueStorage.AtpUomParameter("uom", wire));
                await Reject(connection, "SELECT exact_fixture.uom_input(@wire)", "23514", Text("wire", wire));
                return;
            }
            var key = await InsertAtpRows(connection, wire);
            Assert.Equal(Enumerable.Repeat(wire, 5), await ReadAtpUoms(connection, key));
        }
        else throw new InvalidOperationException("UNKNOWN_ASSIGNED_PROFILE:" + profile);
    }

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };
    private static async Task Execute(NpgsqlConnection connection, string sql, params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task Reject(NpgsqlConnection connection, string sql, string state, params NpgsqlParameter[] parameters)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, sql, parameters));
        Assert.Equal(state, exception.SqlState);
    }
    private static async Task InsertCredit(NpgsqlConnection connection, string id, CreditAmount? value, CreditAmountProfile profile, bool nullable = false)
    {
        await using var command = new NpgsqlCommand("INSERT INTO exact_fixture.credit(id,profile,coefficient,scale,nullable_value) VALUES(@id,@profile,@coefficient,@scale,@nullable)", connection);
        command.Parameters.AddRange(ExactValueStorage.CreditParameters(value, profile, nullable));
        command.Parameters.Add(Text("id", id)); command.Parameters.Add(Text("profile", profile.ToString()));
        command.Parameters.AddWithValue("nullable", nullable);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task InsertInventoryScalar(NpgsqlConnection connection, string id, string wire, InventoryStorageProfile profile)
    {
        await using var command = new NpgsqlCommand("INSERT INTO exact_fixture.inventory_scalar VALUES (@id,@profile,@digits,@scale,@wire)", connection);
        command.Parameters.AddRange(ExactValueStorage.InventoryParameters(wire, profile));
        command.Parameters.Add(Text("id", id)); command.Parameters.Add(Text("profile", profile.ToString()));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertAtpRows(NpgsqlConnection connection, string uom)
    {
        var key = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO exact_fixture.evaluation (organization_id,evaluation_id,actor_id,site_id,item_id,revision_id,usage_code,
              request_key,request_body_digest,replay_envelope_digest,requested_quantity,result_uom,created_at)
            VALUES ('OrgA',@id,'ActorA','Site','Item','R1','SALE','Request',repeat('a',64),repeat('b',64),1,exact_fixture.uom_input(@uom),'2026-10-13T00:00:00Z');
            INSERT INTO exact_fixture.segment VALUES ('OrgA',@id,1,'2026-10-13',exact_fixture.uom_input(@uom),0,1,1,1,0,0,0,0,'ELIGIBLE','NONE',repeat('a',64),repeat('b',64));
            INSERT INTO exact_fixture.basis VALUES ('OrgA',@id,'Basis','Lineage','ONHAND','INV','Owner','V1','Rep',1,NULL,NULL,
              '2026-10-13T00:00:00Z','2026-10-13',1,exact_fixture.uom_input(@uom),1,exact_fixture.uom_input(@uom),0,0,0,1,repeat('a',64),repeat('b',64),repeat('c',64));
            INSERT INTO exact_fixture.reservation_ledger VALUES ('OrgA',@id,'Reservation',repeat('a',64),'ORDER','Order','V1','Line','Schedule',
              'Site','Item','R1','SALE',exact_fixture.uom_input(@uom),'Lineage',1,false,1,repeat('b',64));
            """, connection);
        command.Parameters.AddWithValue("id", key);
        command.Parameters.Add(ExactValueStorage.AtpUomParameter("uom", uom));
        await command.ExecuteNonQueryAsync();
        return key;
    }

    private static async Task<string[]> ReadAtpUoms(NpgsqlConnection connection, Guid key)
    {
        await using var command = new NpgsqlCommand("""
            SELECT result_uom FROM exact_fixture.evaluation WHERE organization_id='OrgA' AND evaluation_id=@id
            UNION ALL SELECT uom FROM exact_fixture.segment WHERE organization_id='OrgA' AND evaluation_id=@id
            UNION ALL SELECT native_uom FROM exact_fixture.basis WHERE organization_id='OrgA' AND evaluation_id=@id
            UNION ALL SELECT result_uom FROM exact_fixture.basis WHERE organization_id='OrgA' AND evaluation_id=@id
            UNION ALL SELECT uom FROM exact_fixture.reservation_ledger WHERE organization_id='OrgA' AND evaluation_id=@id
            """, connection);
        command.Parameters.AddWithValue("id", key);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>(); while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values.ToArray();
    }
}
