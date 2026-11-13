using System.Data;
using System.Globalization;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Platform;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Integration.StockOutbox;

internal static class StockOutboxEpochControlV1
{
    private const string Contract = "Main.StockOutbox.EpochControl.v1";

    // The CLI owns this connection and selects the exact database owner before enabling Switch.
    internal static async Task<string> ApplyAsync(NpgsqlConnection connection, RmaTenantTargetV1 target, Guid tenant,
        Guid operation, Guid? drain, long expectedEpoch, string expectedControlVersion, CancellationToken ct,
        bool recoverOnly = false)
    {
        target.Validate();
        if (tenant == Guid.Empty || operation == Guid.Empty || drain == Guid.Empty || drain == operation
            || expectedEpoch < 1 || expectedControlVersion is null
            || !expectedControlVersion.StartsWith("pg-xmin-v1:", StringComparison.Ordinal)
            || !uint.TryParse(expectedControlVersion.AsSpan("pg-xmin-v1:".Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var version)
            || expectedControlVersion != OutboxEncodingV2.Encode(version))
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_SWITCH_INPUT_REJECTED");

        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await RequireTargetAsync(connection, target, ct);
        var factory = new StockOutboxFactoryV1(new DbContextOptionsBuilder<StockOutboxContextV1>()
            .UseNpgsql(connection, contextOwnsConnection: false).Options);
        var root = StockOutboxProtocolV1.Root(tenant);
        var input = SwitchInput(operation, drain, expectedEpoch, expectedControlVersion);
        OutboxLeaseMutationReceiptV2? original;
        await using (var reader = factory.CreateDbContext())
        await using (var read = await reader.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            await reader.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
            original = await ReceiptAsync(reader, root.Id, operation, ct);
        }
        if (original is not null) return FormatReceipt(original, root, operation, input);
        if (recoverOnly) throw new StockOutboxSwitchOutcomeUnknown();

        var options = new StockOutboxHostOptionsV1
        {
            Enabled = true, HostId = "Main.StockOutbox.Control", Issuer = "https://cp6.invalid/stock-outbox", Region = "control"
        };
        var host = new StockOutboxHostV1(factory, options, tenant, privacyRestoreConfigured: false, routeControl: true);
        var store = new CallerTransactionOutboxV2(host.Trusted, host);

        string? output = null;
        await host.Execute(async token =>
        {
            await using var db = factory.CreateDbContext();
            // The switch is all or nothing. EF savepoints would stamp the receipt
            // with a subtransaction xmin instead of the route's transaction ID.
            db.Database.AutoSavepointsEnabled = false;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'; SET LOCAL statement_timeout='30s'", token);
            var capability = await store.BindTransactionAsync(db, tx.GetDbTransaction(), root, false, token);
            original = await ReceiptAsync(db, root.Id, operation, token);
            if (original is null)
            {
                var prepared = drain is Guid prior
                    ? await store.AdvanceEpochV2(capability, operation, prior, expectedEpoch, expectedControlVersion, token)
                    : await store.BeginDrainV2(capability, operation, expectedEpoch, expectedControlVersion, token);
                if (prepared.Decision != OutboxDecisionV2.PendingCommit)
                    throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_SWITCH_" + prepared.Decision.ToString().ToUpperInvariant());
                original = await ReceiptAsync(db, root.Id, operation, token);
            }
            var receipt = original ?? throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_SWITCH_RECEIPT_MISSING");
            output = FormatReceipt(receipt, root, operation, input);
            if (receipt.WriterTransaction != capability.TransactionId) return;
            var saved = JsonSerializer.Deserialize<OutboxSwitchResultV2>(receipt.Output)!;
            if (saved.Epoch != (drain is null ? expectedEpoch : checked(expectedEpoch + 1)))
                throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_SWITCH_EPOCH_MISMATCH");
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", token);
            try { await tx.CommitAsync(token); }
            catch (Exception error) when (error is NpgsqlException or IOException or OperationCanceledException)
            {
                throw new StockOutboxSwitchOutcomeUnknown(error);
            }
        }, ct);
        return output ?? throw new StockOutboxSwitchOutcomeUnknown();
    }

    private static string SwitchInput(Guid operation, Guid? drain, long expectedEpoch, string expectedControlVersion)
        => drain is Guid switchId
            ? JsonSerializer.Serialize(new { Kind = "AdvanceEpoch", mutationId = operation, switchId, expectedEpoch, expectedControlVersion })
            : JsonSerializer.Serialize(new { Kind = "BeginDrain", switchId = operation, expectedEpoch, expectedControlVersion });

    private static string FormatReceipt(OutboxLeaseMutationReceiptV2 receipt, OutboxRootKeyV2 root, Guid operation, string input)
    {
        if (receipt.NativeId != Guid.Empty || receipt.Input != input)
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_SWITCH_CONFLICT");
        return JsonSerializer.Serialize(new { Contract, Outcome = "COMMITTED", TenantId = root.TenantId,
            RootId = root.Id, OperationId = operation, Input = receipt.Input, Result = receipt.Output });
    }

    private static Task<OutboxLeaseMutationReceiptV2?> ReceiptAsync(StockOutboxContextV1 db, string root, Guid operation, CancellationToken ct)
        => db.Set<OutboxLeaseMutationReceiptV2>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.RootId == root && row.MutationId == operation, ct);

    private static async Task RequireTargetAsync(NpgsqlConnection connection, RmaTenantTargetV1 target, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT CASE WHEN r.rolsuper AND session_user=current_user AND d.datdba=r.oid THEN
                d.datname=@database AND d.oid=@oid AND host(inet_server_addr())=@server AND inet_server_port()=@port
                AND NOT pg_is_in_recovery() AND (SELECT system_identifier::text FROM pg_control_system())=@system
                AND coalesce(shobj_description(d.oid,'pg_database'),'') NOT LIKE @candidate
                ELSE false END
            FROM pg_catalog.pg_roles r, pg_catalog.pg_database d
            WHERE r.rolname=current_user AND d.datname=current_database()
            """, connection);
        command.Parameters.AddWithValue("database", target.Database);
        command.Parameters.AddWithValue("oid", NpgsqlTypes.NpgsqlDbType.Oid, target.DatabaseOid);
        command.Parameters.AddWithValue("server", target.Server);
        command.Parameters.AddWithValue("port", target.Port);
        command.Parameters.AddWithValue("system", target.SystemIdentifier);
        command.Parameters.AddWithValue("candidate", PrivacyRestoreNativeV1.MarkerPrefix + "%");
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_SWITCH_TARGET_REJECTED");
    }
}

internal sealed class StockOutboxSwitchOutcomeUnknown(Exception? inner = null)
    : Exception("STOCK_OUTBOX_SWITCH_OUTCOME_UNKNOWN", inner);
