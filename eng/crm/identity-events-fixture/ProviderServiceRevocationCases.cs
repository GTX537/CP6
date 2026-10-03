using System.Data.Common;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

sealed partial class IdentitySqlFixture
{
    public async Task ProviderConcurrentServiceRevocationAsync()
    {
        var tenant = await SeedAsync();
        var jti = Guid.NewGuid().ToString("D");
        var expiry = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds());
        await RecordProviderServiceTokenAsync(tenant, jti, expiry);

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstSelect = new ServiceRevokeSelectGate(holdAfterExecution: true);
        var secondSelect = new ServiceRevokeSelectGate(holdAfterExecution: false);
        await using var first = Create(tenant, interceptor: firstSelect);
        await using var second = Create(tenant, interceptor: secondSelect);
        first.Database.SetCommandTimeout(30);
        second.Database.SetCommandTimeout(30);
        Task? firstRevoke = null;
        Task? secondRevoke = null;
        try
        {
            firstRevoke = new CrmServiceTokenRecordStore(first, Runtime(tenant))
                .RevokeAsync(Issuer, "reader-a", tenant, jti, budget.Token);
            await WaitForServiceRevokeSelectAsync(firstSelect.Reached.Task, firstRevoke, budget.Token);
            secondRevoke = new CrmServiceTokenRecordStore(second, Runtime(tenant))
                .RevokeAsync(Issuer, "reader-a", tenant, jti, budget.Token);
            await WaitForServiceRevokeSelectAsync(secondSelect.Reached.Task, secondRevoke, budget.Token);
            await ObserveServiceRevokeBlockingAsync(firstSelect.ConnectionId, secondSelect.ConnectionId,
                firstRevoke, secondRevoke, budget.Token);

            firstSelect.Release.TrySetResult();
            // Both calls use the real producer once. A native concurrency failure is not retried by the fixture.
            await Task.WhenAll(firstRevoke, secondRevoke);
        }
        finally
        {
            firstSelect.Release.TrySetResult();
            budget.Cancel();
            // Join every worker before either context is disposed, including failures before both gates arrive.
            await DrainServiceRevocationsAsync(firstRevoke, secondRevoke);
        }

        var committed = await ReadCommittedServiceRevocationAsync(tenant, jti, expiry);
        await RevokeProviderServiceTokenAsync(tenant, jti);
        var repeated = await ReadCommittedServiceRevocationAsync(tenant, jti, expiry);
        RequireSameServiceRevocation(committed, repeated);
    }

    public async Task ProviderServiceRevocationRollbackAsync()
    {
        var tenant = await SeedAsync();
        var jti = Guid.NewGuid().ToString("D");
        var expiry = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds());
        await RecordProviderServiceTokenAsync(tenant, jti, expiry);
        byte[] originalVersion;
        await using (var before = Create(tenant))
            originalVersion = await before.CrmServiceTokenRecords.AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Issuer == Issuer && x.Jti == jti)
                .Select(x => x.RowVersion).SingleAsync();

        var constraint = "C02_Service_" + Guid.NewGuid().ToString("N");
        await AddOutboxRejectAsync(tenant, priority: true, constraint);
        try
        {
            await ExpectProviderCheckFailureAsync(() => RevokeProviderServiceTokenAsync(tenant, jti), constraint);
            await using var read = Create(tenant);
            var record = await read.CrmServiceTokenRecords.AsNoTracking()
                .SingleAsync(x => x.TenantId == tenant && x.Issuer == Issuer && x.Jti == jti);
            var aggregate = IdentityEventContracts.TokenAggregate(Issuer, jti);
            Require(record.RevokedAtUtc is null && record.RowVersion.SequenceEqual(originalVersion),
                "failed service revoke committed its record timestamp or row version");
            Require(!await read.CrmIdentitySnapshots.AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant && x.AggregateId == aggregate)
                && await read.CrmIdentitySnapshots.CountAsync(x => x.TenantId == tenant) == 5,
                "failed service revoke committed its token snapshot");
            Require(await CountOutbox(tenant, true) == 0 && await CountOutbox(tenant) == 5,
                "failed service revoke committed priority or changed standard outbox messages");
        }
        finally { await DropOutboxRejectAsync(priority: true, constraint); }

        await RevokeProviderServiceTokenAsync(tenant, jti);
        var committed = await ReadCommittedServiceRevocationAsync(tenant, jti, expiry);
        await RevokeProviderServiceTokenAsync(tenant, jti);
        var repeated = await ReadCommittedServiceRevocationAsync(tenant, jti, expiry);
        RequireSameServiceRevocation(committed, repeated);
    }

    private async Task RecordProviderServiceTokenAsync(Guid tenant, string jti, DateTimeOffset expiry)
    {
        await using var db = Create(tenant);
        await new CrmServiceTokenRecordStore(db, Runtime(tenant))
            .RecordAsync(Issuer, "reader-a", tenant, jti, expiry, default);
        Require(await CountOutbox(tenant, true) == 0 && await CountOutbox(tenant) == 5,
            "service record preparation changed identity messages");
    }

    private async Task RevokeProviderServiceTokenAsync(Guid tenant, string jti)
    {
        await using var db = Create(tenant);
        await new CrmServiceTokenRecordStore(db, Runtime(tenant))
            .RevokeAsync(Issuer, "reader-a", tenant, jti, default);
    }

    private async Task<ServiceRevocationState> ReadCommittedServiceRevocationAsync(Guid tenant, string jti,
        DateTimeOffset expiry)
    {
        await using var read = Create(tenant);
        var record = await read.CrmServiceTokenRecords.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.Issuer == Issuer && x.Jti == jti);
        var aggregate = IdentityEventContracts.TokenAggregate(Issuer, jti);
        var snapshot = await read.CrmIdentitySnapshots.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.AggregateId == aggregate);
        Require(record.RevokedAtUtc is not null && record.ClientId == "reader-a" && record.ExpiresAtUtc == expiry,
            "service revoke did not persist the bound token timestamp");
        Require(snapshot.Version == 1 && snapshot.EventType == IdentityEventContracts.TokenRevoked
            && !snapshot.IsDeleted && await read.CrmIdentitySnapshots.CountAsync(x => x.TenantId == tenant) == 6,
            "service revoke did not commit exactly one token snapshot version");
        Require(await CountOutbox(tenant, true) == 1 && await CountOutbox(tenant) == 5,
            "service revoke did not commit exactly one priority message");
        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        Require(payload.RootElement.GetProperty("subjectId").GetString() == "service:reader-a"
            && payload.RootElement.GetProperty("jti").GetString() == jti
            && payload.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset() == expiry,
            "service revoke changed token identity or expiry");
        return new(record.RevokedAtUtc!.Value, record.RowVersion, snapshot.Version,
            snapshot.RowVersion, snapshot.PayloadJson);
    }

    private static void RequireSameServiceRevocation(ServiceRevocationState committed, ServiceRevocationState repeated)
        => Require(committed.RevokedAtUtc == repeated.RevokedAtUtc
            && committed.RecordVersion.SequenceEqual(repeated.RecordVersion)
            && committed.SnapshotVersion == repeated.SnapshotVersion
            && committed.SnapshotRowVersion.SequenceEqual(repeated.SnapshotRowVersion)
            && committed.Payload == repeated.Payload, "repeated service revoke changed committed state");

    private async Task ObserveServiceRevokeBlockingAsync(int blocker, int waiter, Task first, Task second,
        CancellationToken cancellationToken)
    {
        Require(blocker > 0 && waiter > 0 && blocker != waiter, "service revokes did not use two native sessions");
        await using var observer = new DatabaseConnectionFactory(database).Create(connection);
        await observer.OpenAsync(cancellationToken);
        var query = database.Provider == DatabaseProvider.PostgreSql ? """
            SELECT COUNT(*)::integer FROM pg_stat_activity
            WHERE datname=current_database() AND pid=@waiter AND @blocker=ANY(pg_blocking_pids(pid))
            """ : """
            SELECT COUNT(*) FROM sys.dm_exec_requests
            WHERE database_id=DB_ID() AND session_id=@waiter AND blocking_session_id=@blocker
            """;
        while (true)
        {
            if (first.IsCompleted || second.IsCompleted)
            {
                if (first.IsCompleted) await first;
                if (second.IsCompleted) await second;
                throw new InvalidOperationException("service revoke completed before its native row-lock contention was observed");
            }
            var blocked = await observer.QuerySingleAsync<int>(new CommandDefinition(query,
                new { blocker, waiter }, commandTimeout: 5, cancellationToken: cancellationToken));
            if (blocked == 1)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    observation = "service-token-revoke-native-block", provider = database.Provider.ToString(),
                    blocker, waiter, blocked = true
                }));
                return;
            }
            await Task.Delay(20, cancellationToken);
        }
    }

    private static async Task WaitForServiceRevokeSelectAsync(Task reached, Task worker, CancellationToken cancellationToken)
    {
        await Task.WhenAny(reached, worker).WaitAsync(cancellationToken);
        if (worker.IsCompleted)
        {
            await worker;
            throw new InvalidOperationException("service revoke completed without reaching its SELECT barrier");
        }
        await reached.WaitAsync(cancellationToken);
    }

    private static async Task DrainServiceRevocationsAsync(params Task?[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task is null) continue;
            try { await task; }
            catch { /* The body observes failures; cleanup still joins every worker before context disposal. */ }
        }
    }

    private sealed record ServiceRevocationState(DateTimeOffset RevokedAtUtc, byte[] RecordVersion,
        int SnapshotVersion, byte[] SnapshotRowVersion, string Payload);

    private sealed class ServiceRevokeSelectGate(bool holdAfterExecution) : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ConnectionId { get; private set; }
        private int used;

        private static bool IsLockedServiceSelect(DbCommand command)
            => command.CommandText.Contains("ServiceToken", StringComparison.Ordinal)
                && (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                    || command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal));

        private bool Reach(DbCommand command)
        {
            if (Interlocked.Exchange(ref used, 1) != 0) return false;
            ConnectionId = command.Connection switch
            {
                SqlConnection sql => sql.ServerProcessId,
                NpgsqlConnection postgres => postgres.ProcessID,
                _ => throw new InvalidOperationException("service revoke requires a native provider session")
            };
            Reached.TrySetResult();
            return true;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!holdAfterExecution && IsLockedServiceSelect(command)) Reach(command);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            // ExecuteReader has reached the native SELECT. Its transaction keeps the row lock while held here.
            if (holdAfterExecution && IsLockedServiceSelect(command) && Reach(command))
                await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
