using System.Diagnostics;
using CP6.Persistence.PostgreSql.Contracts;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace CP6.ExactValues.PostgreSqlTests;

public partial class ExactValueStorageTests
{
    private static readonly (string Table, string Column, bool Nullable)[] UomColumns =
        [("evaluation", "result_uom", true), ("segment", "uom", false), ("basis", "native_uom", false),
         ("basis", "result_uom", false), ("reservation_ledger", "uom", false)];

    public static IEnumerable<object[]> BadUoms => new[] { "", new string('U', 129), "EA ", new string('U', 128) + " ",
        "公斤", ".EA", "_EA", ":EA", "/EA", "-EA", "EA\n", "EA\r\n", new string('U', 128) + "\n" }.Select(x => new object[] { x });

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(128)]
    public async Task Atp_all_five_columns_preserve_full_accepted_lengths(int length)
    {
        var value = new string('U', length);
        var parameter = ExactValueStorage.AtpUomParameter("uom", value);
        Assert.Equal(NpgsqlDbType.Text, parameter.NpgsqlDbType); Assert.Equal(0, parameter.Size);
        await using var connection = await database.OpenWriterAsync();
        var id = await InsertAtpRows(connection, value);
        Assert.Equal(Enumerable.Repeat(value, 5), await ReadAtpUoms(connection, id));
    }

    [Theory]
    [MemberData(nameof(BadUoms))]
    public async Task Atp_all_five_columns_reject_raw_invalid_text_before_narrowing(string bad)
    {
        Assert.ThrowsAny<ArgumentException>(() => ExactValueStorage.AtpUomParameter("uom", bad));
        await using var connection = await database.OpenWriterAsync();
        var id = await InsertAtpRows(connection, "EA");
        foreach (var column in UomColumns)
            await Reject(connection, $"UPDATE exact_fixture.{column.Table} SET {column.Column}=exact_fixture.uom_input(@wire) WHERE organization_id='OrgA' AND evaluation_id=@id",
                "23514", Text("wire", bad), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        Assert.Equal(Enumerable.Repeat("EA", 5), await ReadAtpUoms(connection, id));
    }

    [Fact]
    public async Task Atp_database_column_checks_nullability_and_typmod_limit_are_distinguished_from_raw_guard()
    {
        await using var connection = await database.OpenWriterAsync();
        var id = await InsertAtpRows(connection, "EA");
        foreach (var column in UomColumns)
        {
            var sql = $"UPDATE exact_fixture.{column.Table} SET {column.Column}=@wire WHERE organization_id='OrgA' AND evaluation_id=@id";
            foreach (var bad in new[] { "EA\n", "EA\r\n", "公斤", "EA ", ".EA", "" })
                await Reject(connection, sql, "23514", Text("wire", bad), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
            await Reject(connection, sql, "22001", Text("wire", new string('U', 129)), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
            if (column.Nullable)
                await Execute(connection, sql, ExactValueStorage.AtpUomParameter("wire", null, true), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
            else
            {
                Assert.Throws<FormatException>(() => ExactValueStorage.AtpUomParameter("wire", null));
                await Reject(connection, sql, "23502", Text("wire", null), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
            }
        }
        // This is the database's documented coercion counterexample, not acceptance of raw input.
        // No Size or explicit varchar CAST is used anywhere: assignment typmod itself drops the extra space.
        var padded = new string('U', 128) + " ";
        await Execute(connection, "UPDATE exact_fixture.evaluation SET result_uom=@wire WHERE evaluation_id=@id", Text("wire", padded), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await using var read = new NpgsqlCommand("SELECT result_uom FROM exact_fixture.evaluation WHERE evaluation_id=@id", connection);
        read.Parameters.AddWithValue("id", id); Assert.Equal(new string('U', 128), await read.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Atp_composed_relationship_and_terminal_state_checks_remain_in_force()
    {
        await using var connection = await database.OpenWriterAsync();
        var id = await InsertAtpRows(connection, "EA");
        async Task Bad(string sql, string code = "23514") => await Reject(connection, sql + " WHERE evaluation_id=@id", code, new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await Bad("UPDATE exact_fixture.segment SET allocated_quantity=2");
        await Bad("UPDATE exact_fixture.segment SET organization_id='OrgB'", "23503");
        await Bad("UPDATE exact_fixture.basis SET representation_sequence=2");
        await Bad("UPDATE exact_fixture.basis SET predecessor_representation_id='Old',replacement_proof_digest=repeat('a',64)");
        await Bad("UPDATE exact_fixture.basis SET reservation_deducted=1");
        await Bad("UPDATE exact_fixture.reservation_ledger SET self_excluded=true");
        await Bad("UPDATE exact_fixture.evaluation SET status='COMPLETED'");
        await Bad("UPDATE exact_fixture.evaluation SET terminal_at='2026-10-13T00:00:01Z'");
        await Execute(connection, "UPDATE exact_fixture.basis SET representation_sequence=2,predecessor_representation_id='Old',replacement_proof_digest=repeat('a',64) WHERE evaluation_id=@id", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await Execute(connection, "UPDATE exact_fixture.reservation_ledger SET self_excluded=true,deducted_quantity=0 WHERE evaluation_id=@id", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await Bad("UPDATE exact_fixture.reservation_ledger SET self_excluded=false");
        await Execute(connection, """
            UPDATE exact_fixture.evaluation SET status='COMPLETED',result_scale=0,total_eligible_capacity=1,allocated_to_request=1,
              unavailable_quantity=0,evaluated_at='2026-10-13T00:00:00Z',valid_until='2026-10-13T01:00:00Z',terminal_at='2026-10-13T00:00:01Z',
              public_self_digest=repeat('a',64),public_payload_digest=repeat('b',64),public_representation_digest=repeat('c',64),
              protected_self_digest=repeat('d',64),protected_payload_digest=repeat('e',64),protected_representation_digest=repeat('f',64)
            WHERE evaluation_id=@id
            """, new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await Bad("UPDATE exact_fixture.evaluation SET result_uom=NULL");
        await Bad("UPDATE exact_fixture.evaluation SET status='REJECTED'");
    }

    [Fact]
    public async Task Atp_replay_binding_checks_organization_actor_delegation_request_and_both_digests()
    {
        await using var connection = await database.OpenWriterAsync();
        var id = await InsertAtpRows(connection, "EA");
        await InsertReplay(connection, id, "ActorA");
        const string sql = "SELECT exact_fixture.lock_replay_binding(@org,@actor,@delegation,@key,@body,@replay)";
        NpgsqlParameter[] Parameters(string? changed = null) => [Text("org", changed == "org" ? "OrgB" : "OrgA"),
            Text("actor", changed == "actor" ? "ActorB" : "ActorA"), Text("delegation", changed == "delegation" ? "Delegate" : null),
            Text("key", changed == "key" ? "OtherRequest" : id.ToString()), Text("body", new string(changed == "body" ? 'c' : 'a', 64)),
            Text("replay", new string(changed == "replay" ? 'c' : 'b', 64))];
        foreach (var field in new[] { "org", "actor", "delegation", "key", "body", "replay" })
            await Reject(connection, sql, "23514", Parameters(field));
        await using var transaction = await connection.BeginTransactionAsync();
        await using var valid = new NpgsqlCommand(sql, connection, transaction); valid.Parameters.AddRange(Parameters());
        Assert.Equal(id, await valid.ExecuteScalarAsync());
        await using var other = await database.OpenWriterAsync();
        await Reject(other, "SELECT 1 FROM exact_fixture.replay_record WHERE evaluation_id=@id FOR UPDATE NOWAIT", "55P03", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await Reject(other, "SELECT 1 FROM exact_fixture.evaluation WHERE evaluation_id=@id FOR UPDATE NOWAIT", "55P03", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Atp_recovery_locks_replay_in_C_order_before_evaluation_and_requires_binding()
    {
        await using var holder = await database.OpenWriterAsync();
        var id = await InsertAtpRows(holder, "EA");
        await Reject(holder, "SELECT exact_fixture.lock_recovery_rows('OrgA',@id)", "23514", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        // Insert in reverse order, then hold Z: the recovery call must lock A before waiting for Z.
        await InsertReplay(holder, id, "ActorZ"); await InsertReplay(holder, id, "ActorA");
        await using var holderTx = await holder.BeginTransactionAsync();
        await Execute(holder, "SELECT 1 FROM exact_fixture.replay_record WHERE evaluation_id=@id AND actor_id='ActorZ' FOR UPDATE", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await using var recovery = await database.OpenWriterAsync();
        await using var recoveryTx = await recovery.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT exact_fixture.lock_recovery_rows('OrgA',@id)", recovery, recoveryTx) { CommandTimeout = 15 };
        command.Parameters.AddWithValue("id", id);
        var pending = command.ExecuteNonQueryAsync();
        await using var observer = await database.OpenWriterAsync();
        try
        {
            var watch = Stopwatch.StartNew(); bool blocked = false;
            while (watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                await using var query = new NpgsqlCommand("SELECT @holder=ANY(pg_blocking_pids(@recovery))", observer);
                query.Parameters.AddWithValue("holder", holder.ProcessID); query.Parameters.AddWithValue("recovery", recovery.ProcessID);
                if ((bool)(await query.ExecuteScalarAsync())!) { blocked = true; break; }
                await Task.Delay(20);
            }
            Assert.True(blocked, "Recovery did not wait on the held replay row");
            await Reject(observer, "SELECT 1 FROM exact_fixture.replay_record WHERE evaluation_id=@id AND actor_id='ActorA' FOR UPDATE NOWAIT", "55P03", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
            // The evaluation is still unlocked while recovery waits for its later replay row.
            await Execute(observer, "SELECT 1 FROM exact_fixture.evaluation WHERE evaluation_id=@id FOR UPDATE NOWAIT", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        }
        finally { await holderTx.RollbackAsync(); await pending; }
        await Reject(observer, "SELECT 1 FROM exact_fixture.evaluation WHERE evaluation_id=@id FOR UPDATE NOWAIT", "55P03", new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        await recoveryTx.RollbackAsync();
    }

    private static Task InsertReplay(NpgsqlConnection connection, Guid id, string actor) => Execute(connection, """
        INSERT INTO exact_fixture.replay_record (organization_id,actor_id,delegation_id,operation,request_key,request_body_digest,
          replay_envelope_digest,evaluation_id,replay_state,created_at,updated_at)
        VALUES ('OrgA',@actor,NULL,'ATP_EVALUATE',@key,repeat('a',64),repeat('b',64),@id,'BOUND','2026-10-13T00:00:00Z','2026-10-13T00:00:00Z')
        """, Text("actor", actor), Text("key", id.ToString()), new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
}
