using CP6.Core.Persistence.Tenancy;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("TenantStoragePostgres")]
public sealed class TenantStoragePostgresTests(TenantStoragePostgresFixture postgres)
{
    private TenantStorageCatalog Catalog(params TenantStorageBindingV1[] bindings) => new("main", ["fixture-v1"],
        bindings.Length == 0 ? [TenantStorageContext.Binding(), TenantStorageContext.Binding(TenantStorageContext.B)] : bindings,
        [new("db", "127.0.0.1", TenantStoragePostgresFixture.Port, TenantStoragePostgresFixture.Database, SslMode.Disable)],
        [new("principal-a", "tenant_a"), new("principal-b", "tenant_b")]);
    private TenantStorageCredentials Credentials() => new()
    {
        Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == TenantStorageContext.A ? postgres.PasswordA : postgres.PasswordB)
    };
    private static Task<object?> Scalar(TenantStorageTransaction transaction, string sql, CancellationToken ct = default)
        => transaction.ExecuteAsync((connection, _, token) => TenantStoragePostgresFixture.Scalar(connection, sql, token), ct);
    private static Task<int> Sql(TenantStorageTransaction transaction, string sql, CancellationToken ct = default)
        => transaction.ExecuteAsync((connection, _, token) => TenantStoragePostgresFixture.Sql(connection, sql, token), ct);
    private static string Table(Guid tenant) => "t_" + tenant.ToString("N") + ".component_probe";

    [TenantStoragePostgresFact]
    public async Task A_B_A_uses_dedicated_principals_and_reuses_the_A_physical_backend()
    {
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(2, 2, 1));
        var pids = new List<int>();
        foreach (var tenant in new[] { TenantStorageContext.A, TenantStorageContext.B, TenantStorageContext.A })
            await context.Run(tenant, async _ =>
            {
                await using var lease = await manager.OpenCurrentAsync();
                await lease.InTransactionAsync(async (transaction, ct) =>
                {
                    Assert.Equal(tenant == TenantStorageContext.A ? "tenant_a" : "tenant_b", await Scalar(transaction, $"SELECT marker FROM {Table(tenant)} WHERE id = 1", ct));
                    Assert.Equal("pg_catalog", await Scalar(transaction, "SHOW search_path", ct));
                    pids.Add((int)(await Scalar(transaction, "SELECT pg_catalog.pg_backend_pid()", ct))!);
                    return 0;
                });
            });
        Assert.Equal(pids[0], pids[2]);
        Assert.NotEqual(pids[0], pids[1]);
        Assert.Equal((2, 2, 0, 0), manager.Snapshot());
    }

    [TenantStoragePostgresFact]
    public async Task Forced_A_connection_for_B_wrong_database_privileged_role_and_session_override_never_reach_business()
    {
        var callbacks = 0;
        await using var a = await TenantStoragePostgresFixture.OpenRawAsync("tenant_a", postgres.PasswordA);
        var aPid = a.ProcessID;
        var wrongTenant = await Assert.ThrowsAsync<TenantStorageException>(async () =>
        {
            await TenantConnectionGuard.ValidateAsync(a, TenantStoragePostgresFixture.Database, "tenant_b", default);
            callbacks++;
        });
        Assert.Equal(TenantStorageFailure.IdentityRejected, wrongTenant.Failure);
        Assert.Equal(aPid, a.ProcessID); // The guard actually received A's already-open physical connection.
        await using var wrongDb = await TenantStoragePostgresFixture.OpenRawAsync("tenant_a", postgres.PasswordA, "tenant_other");
        Assert.Equal(TenantStorageFailure.IdentityRejected, (await Assert.ThrowsAsync<TenantStorageException>(async () =>
        {
            await TenantConnectionGuard.ValidateAsync(wrongDb, TenantStoragePostgresFixture.Database, "tenant_a", default);
            callbacks++;
        })).Failure);
        await using var privileged = await TenantStoragePostgresFixture.OpenRawAsync("privileged_fixture", postgres.PasswordAdmin);
        Assert.Equal(TenantStorageFailure.IdentityRejected, (await Assert.ThrowsAsync<TenantStorageException>(async () =>
        {
            await TenantConnectionGuard.ValidateAsync(privileged, TenantStoragePostgresFixture.Database, "privileged_fixture", default);
            callbacks++;
        })).Failure);
        await TenantStoragePostgresFixture.Sql(a, "SET search_path = public");
        Assert.Equal(TenantStorageFailure.IdentityRejected, (await Assert.ThrowsAsync<TenantStorageException>(async () =>
        {
            await TenantConnectionGuard.ValidateAsync(a, TenantStoragePostgresFixture.Database, "tenant_a", default);
            callbacks++;
        })).Failure);
        Assert.Equal(0, callbacks);
    }

    [TenantStoragePostgresFact]
    public async Task Ordinary_roles_are_denied_explicit_cross_schema_access_and_can_reuse_after_failure()
    {
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(2, 2, 1));
        foreach (var tenant in new[] { TenantStorageContext.A, TenantStorageContext.B })
            await context.Run(tenant, async _ =>
            {
                var other = tenant == TenantStorageContext.A ? TenantStorageContext.B : TenantStorageContext.A;
                await using (var lease = await manager.OpenCurrentAsync())
                {
                    var failure = await Assert.ThrowsAsync<TenantStorageException>(() => lease.InTransactionAsync(
                        (transaction, ct) => Scalar(transaction, $"SELECT marker FROM {Table(other)}", ct)));
                    Assert.Equal(TenantStorageFailure.DatabaseRejected, failure.Failure);
                    Assert.Equal("42501", failure.SqlState);
                    Assert.Null(failure.InnerException);
                    Assert.DoesNotContain(Table(other), failure.ToString());
                }
                await using var reused = await manager.OpenCurrentAsync();
                Assert.Equal(1L, await reused.InTransactionAsync((transaction, ct) => Scalar(transaction, $"SELECT count(*) FROM {Table(tenant)} WHERE id = 1", ct)));
            });
    }

    [TenantStoragePostgresFact]
    public async Task Explicit_commit_default_rollback_cancellation_and_pool_session_reset_are_real()
    {
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(1, 1, 1));
        await context.Run(TenantStorageContext.A, async _ =>
        {
            int pid;
            await using (var lease = await manager.OpenCurrentAsync())
            {
                pid = (int)(await lease.InTransactionAsync(async (tx, ct) =>
                {
                    await Sql(tx, $"INSERT INTO {Table(TenantStorageContext.A)} VALUES (101, 'rollback')", ct);
                    return await Scalar(tx, "SELECT pg_catalog.pg_backend_pid()", ct);
                }))!;
                await lease.InTransactionAsync(async (tx, ct) =>
                {
                    await Sql(tx, $"INSERT INTO {Table(TenantStorageContext.A)} VALUES (102, 'commit'); SET SESSION application_name = 'dirty-session'", ct);
                    await tx.CommitAsync(ct);
                    return 0;
                });
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.InTransactionAsync(async (tx, ct) =>
                {
                    await Sql(tx, $"INSERT INTO {Table(TenantStorageContext.A)} VALUES (103, 'cancel')", ct);
                    return await Scalar(tx, "SELECT pg_catalog.pg_sleep(5)", ct);
                }, cancellationToken: cancel.Token));
            }
            await using var next = await manager.OpenCurrentAsync();
            await next.InTransactionAsync(async (tx, ct) =>
            {
                Assert.Equal(pid, await Scalar(tx, "SELECT pg_catalog.pg_backend_pid()", ct));
                Assert.Equal(0L, await Scalar(tx, $"SELECT count(*) FROM {Table(TenantStorageContext.A)} WHERE id IN (101,103)", ct));
                Assert.Equal(1L, await Scalar(tx, $"SELECT count(*) FROM {Table(TenantStorageContext.A)} WHERE id = 102", ct));
                Assert.Equal("cp6-tenant-storage-v1", await Scalar(tx, "SHOW application_name", ct));
                Assert.Equal("pg_catalog", await Scalar(tx, "SHOW search_path", ct));
                return 0;
            });
        });
    }

    [TenantStoragePostgresFact]
    public async Task Native_savepoints_recover_constraint_and_timeout_without_rebinding_or_leaking_to_B()
    {
        var a = TenantStorageContext.A;
        var b = TenantStorageContext.B;
        var aTable = Table(a);
        var bTable = Table(b);
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(2, 2, 1));
        SavepointSession? original = null;
        var errors = new Dictionary<string, string>();
        var bPid = 0;

        await VerifyBAsync(commit: false);
        await context.Run(a, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            await lease.InTransactionAsync(async (session, ct) =>
            {
                await session.ExecuteAsync(async (connection, transaction, token) =>
                {
                    await TenantStoragePostgresFixture.Sql(connection, $"INSERT INTO {aTable} VALUES (501,'A-savepoint')", token);
                    var before = await ReadSavepointSessionAsync(connection, transaction, session.CurrentBinding, token);
                    original = before;
                    Assert.Equal(connection.ProcessID, before.Pid);
                    Assert.Equal(a, before.Tenant);
                    Assert.Equal("tenant_a", before.Login);
                    Assert.Equal(before.Login, before.EffectiveLogin);
                    Assert.Equal(TenantStoragePostgresFixture.Database, before.Database);
                    Assert.Equal("pg_catalog", before.SearchPath);
                    Assert.Equal("cp6-tenant-storage-v1", before.Application);
                    Assert.Equal("0", before.StatementTimeout);
                    foreach (var fault in new[]
                    {
                        (Stage: "constraint", Id: 502, Setup: "", Sql: $"INSERT INTO {aTable} VALUES (501,'duplicate')",
                            State: PostgresErrorCodes.UniqueViolation),
                        (Stage: "timeout", Id: 503, Setup: " SET LOCAL statement_timeout='300ms';", Sql: "SELECT pg_catalog.pg_sleep(2)",
                            State: PostgresErrorCodes.QueryCanceled)
                    })
                    {
                        await transaction.SaveAsync("pgt010", token);
                        await TenantStoragePostgresFixture.Sql(connection, $"INSERT INTO {aTable} VALUES ({fault.Id},'rolled-back')", token);
                        await TenantStoragePostgresFixture.Sql(connection, "SET LOCAL application_name='pgt010-dirty';" + fault.Setup, token);
                        await using var failing = new NpgsqlCommand(fault.Sql, connection, transaction);
                        var rejected = await Assert.ThrowsAsync<PostgresException>(() => failing.ExecuteNonQueryAsync(token));
                        Assert.Equal(fault.State, rejected.SqlState);
                        if (fault.Stage == "timeout") Assert.Contains("statement timeout", rejected.MessageText, StringComparison.Ordinal);
                        await using var aborted = new NpgsqlCommand("SELECT 1", connection, transaction);
                        var stillAborted = await Assert.ThrowsAsync<PostgresException>(() => aborted.ExecuteScalarAsync(token));
                        Assert.Equal(PostgresErrorCodes.InFailedSqlTransaction, stillAborted.SqlState);

                        await transaction.RollbackAsync("pgt010", token);
                        await transaction.ReleaseAsync("pgt010", token);
                        Assert.Equal(before, await ReadSavepointSessionAsync(connection, transaction, session.CurrentBinding, token));
                        Assert.Equal(0L, await TenantStoragePostgresFixture.Scalar(connection,
                            $"SELECT count(*) FROM {aTable} WHERE id IN (502,503)", token));
                        errors.Add(fault.Stage, rejected.SqlState + "/" + stillAborted.SqlState);
                    }
                    return 0;
                }, ct);
                await session.CommitAsync(ct);
                return 0;
            });
        });

        await VerifyBAsync(commit: true);
        SavepointSession? reborrowed = null;
        await context.Run(a, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            await lease.InTransactionAsync(async (session, ct) =>
            {
                var after = await session.ExecuteAsync((connection, transaction, token) =>
                    ReadSavepointSessionAsync(connection, transaction, session.CurrentBinding, token), ct);
                reborrowed = after;
                var before = original!;
                Assert.NotEqual(before.Xid, after.Xid);
                Assert.Equal(before with { Xid = after.Xid }, after);
                Assert.NotEqual(bPid, after.Pid);
                Assert.Equal("A-savepoint", await Scalar(session, $"SELECT marker FROM {aTable} WHERE id=501", ct));
                Assert.Equal(0L, await Scalar(session, $"SELECT count(*) FROM {aTable} WHERE id IN (502,503)", ct));
                await Sql(session, $"INSERT INTO {aTable} VALUES (504,'A-reborrowed')", ct);
                await session.CommitAsync(ct);
                return 0;
            });
        });
        Assert.Equal((2, 2, 0, 0), manager.Snapshot());
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable(TenantStoragePostgresFactAttribute.RootVariable)!,
            "pgt010-savepoints.json"), System.Text.Json.JsonSerializer.Serialize(new { original, reborrowed, bPid, errors }));

        Task VerifyBAsync(bool commit) => context.Run(b, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            await lease.InTransactionAsync(async (session, ct) =>
            {
                var pid = (int)(await Scalar(session, "SELECT pg_catalog.pg_backend_pid()", ct))!;
                Assert.Equal("tenant_b", await Scalar(session, "SELECT session_user::text", ct));
                Assert.Equal("pg_catalog", await Scalar(session, "SHOW search_path", ct));
                Assert.Equal("tenant_b", await Scalar(session, $"SELECT marker FROM {bTable} WHERE id=1", ct));
                Assert.Equal(0L, await Scalar(session, $"SELECT count(*) FROM {bTable} WHERE id IN (501,502,503,504)", ct));
                if (commit)
                {
                    Assert.Equal(bPid, pid);
                    await Sql(session, $"INSERT INTO {bTable} VALUES (501,'B-isolated')", ct);
                    await session.CommitAsync(ct);
                }
                else bPid = pid;
                return 0;
            });
        });
    }

    private sealed record SavepointSession(int Pid, string Xid, string Login, string EffectiveLogin, string Database,
        string SearchPath, string Application, string StatementTimeout, Guid Tenant, string BindingRevision);

    private static async Task<SavepointSession> ReadSavepointSessionAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, TenantStorageBindingV1 binding, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_catalog.pg_backend_pid(), pg_catalog.pg_current_xact_id()::text, session_user::text,
                   current_user::text, pg_catalog.current_database(), pg_catalog.current_setting('search_path'),
                   pg_catalog.current_setting('application_name'), pg_catalog.current_setting('statement_timeout')
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        return new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), binding.TenantId, binding.BindingRevision);
    }

    [TenantStoragePostgresFact]
    public async Task Nested_B_and_ended_scope_cannot_use_A_lease_or_retained_transaction()
    {
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(1, 1, 1));
        TenantConnectionLease? retained = null;
        TenantStorageTransaction? oldTransaction = null;
        var callbacks = 0;
        await context.Run(TenantStorageContext.A, async _ =>
        {
            retained = await manager.OpenCurrentAsync();
            await context.Run(TenantStorageContext.B, async _ =>
            {
                Assert.Equal(TenantStorageFailure.ContextChanged, (await Assert.ThrowsAsync<TenantStorageException>(() =>
                    retained.InTransactionAsync((_, _) => { callbacks++; return Task.FromResult(0); }))).Failure);
            });
            await retained.InTransactionAsync(async (tx, ct) =>
            {
                oldTransaction = tx;
                Assert.Equal(1L, await Scalar(tx, $"SELECT count(*) FROM {Table(TenantStorageContext.A)} WHERE id = 1", ct));
                return 0;
            });
            Assert.Equal(TenantStorageFailure.LeaseClosed, (await Assert.ThrowsAsync<TenantStorageException>(() =>
                oldTransaction!.ExecuteAsync((_, _, _) => { callbacks++; return Task.FromResult(0); }))).Failure);
        });
        Assert.Equal(TenantStorageFailure.ContextChanged, (await Assert.ThrowsAsync<TenantStorageException>(() =>
            retained!.InTransactionAsync((_, _) => { callbacks++; return Task.FromResult(0); }))).Failure);
        Assert.Equal(0, callbacks);
        await retained!.DisposeAsync();
        await retained.DisposeAsync();
        Assert.Equal((1, 1, 0, 0), manager.Snapshot());
    }

    [TenantStoragePostgresFact]
    public async Task Live_and_draining_pools_hold_budgets_until_final_disposal_and_idle_pool_is_evicted()
    {
        var context = new TenantStorageContext();
        var rotatedPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var credentials = Credentials();
        credentials.Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == TenantStorageContext.B ? postgres.PasswordB
            : binding.CredentialVersion == "v2" ? rotatedPassword : postgres.PasswordA);
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), credentials, new(1, 3, 2));
        try
        {
        await context.Run(TenantStorageContext.A, async _ =>
        {
            await using var old = await manager.OpenCurrentAsync();
            var oldPid = await old.InTransactionAsync((tx, ct) => Scalar(tx, "SELECT pg_catalog.pg_backend_pid()", ct));
            await context.Run(TenantStorageContext.B, _ => TenantStorageUnitTests.Rejected(TenantStorageFailure.CapacityExceeded, () => manager.OpenCurrentAsync()));
            await using (var owner = await postgres.OpenOwnerAsync())
                await TenantStoragePostgresFixture.Sql(owner, $"ALTER ROLE tenant_a PASSWORD '{rotatedPassword}'");
            manager.ReplaceCatalog(Catalog(TenantStorageContext.Binding(credential: "v2", revision: "r2"), TenantStorageContext.Binding(TenantStorageContext.B)));
            Assert.Equal((1, 2, 1, 1), manager.Snapshot());
            await TenantStorageUnitTests.Rejected(TenantStorageFailure.CapacityExceeded, () => manager.OpenCurrentAsync());
            Assert.Equal(TenantStorageFailure.BindingChanged, (await Assert.ThrowsAsync<TenantStorageException>(() => old.InTransactionAsync((_, _) => Task.FromResult(0)))).Failure);
            await Task.WhenAll(old.DisposeAsync().AsTask(), old.DisposeAsync().AsTask());
            Assert.Equal((0, 0, 0, 0), manager.Snapshot());
            await using var current = await manager.OpenCurrentAsync();
            Assert.NotEqual(oldPid, await current.InTransactionAsync((tx, ct) => Scalar(tx, "SELECT pg_catalog.pg_backend_pid()", ct)));
        });
        await context.Run(TenantStorageContext.B, async _ =>
        {
            await using var b = await manager.OpenCurrentAsync(); // Deterministically evicts now-idle A.
            Assert.Equal((1, 2, 1, 0), manager.Snapshot());
            Assert.Equal("tenant_b", await b.InTransactionAsync((tx, ct) => Scalar(tx, "SELECT session_user::text", ct)));
        });
        }
        finally
        {
            await using var owner = await postgres.OpenOwnerAsync();
            await TenantStoragePostgresFixture.Sql(owner, $"ALTER ROLE tenant_a PASSWORD '{postgres.PasswordA}'");
        }
    }

    [TenantStoragePostgresFact]
    public async Task Pool_wait_cancellation_releases_only_its_reservation_and_manager_disposal_waits_for_lease()
    {
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(2, 2, 1));
        await context.Run(TenantStorageContext.A, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.OpenCurrentAsync(cancel.Token));
            Assert.Equal((1, 1, 1, 0), manager.Snapshot());
            var dispose = manager.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            Assert.Equal((1, 1, 1, 1), manager.Snapshot());
            await lease.DisposeAsync();
            await dispose;
            await manager.DisposeAsync();
            Assert.Equal((0, 0, 0, 0), manager.Snapshot());
        });
    }

    [TenantStoragePostgresFact]
    public async Task Physical_pool_capacity_and_ended_scope_during_pool_wait_are_bounded()
    {
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(5, 3, 2));
        TenantConnectionLease[]? held = null;
        Task<TenantConnectionLease>? pending = null;
        try
        {
        await context.Run(TenantStorageContext.A, async _ =>
        {
            held = await Task.WhenAll(manager.OpenCurrentAsync(), manager.OpenCurrentAsync());
            Assert.Equal((1, 2, 2, 0), manager.Snapshot());
            await context.Run(TenantStorageContext.B, _ => TenantStorageUnitTests.Rejected(TenantStorageFailure.CapacityExceeded, () => manager.OpenCurrentAsync()));
            await using var owner = await postgres.OpenOwnerAsync();
            Assert.Equal(2L, await TenantStoragePostgresFixture.Scalar(owner,
                "SELECT count(*) FROM pg_catalog.pg_stat_activity WHERE application_name = 'cp6-tenant-storage-v1'"));
            pending = manager.OpenCurrentAsync();
            Assert.Equal((1, 2, 3, 0), manager.Snapshot());
        });
        }
        finally
        {
            if (held is not null) foreach (var lease in held) await lease.DisposeAsync();
        }
        await TenantStorageUnitTests.Rejected(TenantStorageFailure.ContextChanged,
            () => pending!.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal((1, 2, 0, 0), manager.Snapshot());
    }

    [TenantStoragePostgresFact]
    public async Task Binding_rotation_between_write_and_explicit_commit_rejects_and_rolls_back()
    {
        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(2, 2, 1));
        await context.Run(TenantStorageContext.A, async _ =>
        {
            await using (var lease = await manager.OpenCurrentAsync())
            {
                var failure = await Assert.ThrowsAsync<TenantStorageException>(() => lease.InTransactionAsync(async (tx, ct) =>
                {
                    await Sql(tx, $"INSERT INTO {Table(TenantStorageContext.A)} VALUES (104, 'rotation-must-rollback')", ct);
                    manager.ReplaceCatalog(Catalog(TenantStorageContext.Binding(revision: "r2")));
                    await tx.CommitAsync(ct);
                    return 0;
                }));
                Assert.Equal(TenantStorageFailure.BindingChanged, failure.Failure);
            }
            await using var next = await manager.OpenCurrentAsync();
            Assert.Equal(0L, await next.InTransactionAsync((tx, ct) => Scalar(tx,
                $"SELECT count(*) FROM {Table(TenantStorageContext.A)} WHERE id = 104", ct)));
        });
    }

    [TenantStoragePostgresFact]
    public async Task Ambient_PGOPTIONS_cannot_override_the_fixed_session_path()
    {
        var original = Environment.GetEnvironmentVariable("PGOPTIONS");
        Environment.SetEnvironmentVariable("PGOPTIONS", "-c search_path=public");
        try
        {
            var context = new TenantStorageContext();
            await using var manager = new TenantConnectionManager("main", context.Accessor, Catalog(), Credentials(), new(1, 1, 1));
            await context.Run(TenantStorageContext.A, async _ =>
            {
                await using var lease = await manager.OpenCurrentAsync();
                Assert.Equal("pg_catalog", await lease.InTransactionAsync((tx, ct) => Scalar(tx, "SHOW search_path", ct)));
            });
        }
        finally { Environment.SetEnvironmentVariable("PGOPTIONS", original); }
    }
}
