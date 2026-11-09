using System.Security.Cryptography;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("StockWriteFencePostgres", DisableParallelization = true)]
public sealed class StockWriteFencePostgresCollection : ICollectionFixture<TenantManagementPostgresFixture>;

[Collection("StockWriteFencePostgres")]
public sealed class StockWriteFencePostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_stock_writer_holds_the_native_revocation_fence_in_its_transaction()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenantA });
        foreach (var tenant in new[] { tenantA, tenantB })
            db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "stock writer fixture", Enable = true });
        await db.SaveChangesAsync();

        var connectionString = db.Database.GetConnectionString()!;
        await using var control = await Open(connectionString);
        const string loginA = "stock_fence_a", loginB = "stock_fence_b";
        var passwordA = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var passwordB = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(control, $"""
            CREATE ROLE {loginA} LOGIN PASSWORD '{passwordA}';
            CREATE ROLE {loginB} LOGIN PASSWORD '{passwordB}';
            """);
        TenantLoginV1[] logins = [new(tenantA, loginA), new(tenantB, loginB)];
        Guid installation;
        await using (var readLayout = await control.BeginTransactionAsync())
        {
            installation = await StockReadStorageV1.InstallAsync(control, readLayout, logins);
            await readLayout.CommitAsync();
        }
        Assert.NotEqual(Guid.Empty, installation);
        await using (var mismatched = await control.BeginTransactionAsync())
        {
            var rejected = await Assert.ThrowsAsync<TenantStorageException>(() =>
                StockWriteAuthorityFenceV1.InstallAsync(control, mismatched, installation, [new(tenantB, loginA)]));
            Assert.Equal(TenantStorageFailure.ConfigurationRejected, rejected.Failure);
            await mismatched.RollbackAsync();
        }
        await using (var fence = await control.BeginTransactionAsync())
        {
            await StockWriteAuthorityFenceV1.InstallAsync(control, fence, installation, [logins[0]]);
            await fence.CommitAsync();
        }

        await using var writer = await Open(connectionString, loginA, passwordA);
        await using var reader = await Open(connectionString, loginB, passwordB);
        await using var revoker = await Open(connectionString);
        const string revisionSql = "SELECT revision FROM cp6_platform_admin_guard_v1.root";
        var start = (long)(await CurrentPermissionPostgresFixture.Scalar(control, revisionSql))!;

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        await using (var work = await writer.BeginTransactionAsync(ct))
        await using (var revocation = await revoker.BeginTransactionAsync(ct))
        {
            Assert.True(await Acquire(writer, work, tenantA, ct));
            // Independent connection reads the last committed root revision while the writer holds it.
            Assert.Equal(start, await CurrentPermissionPostgresFixture.Scalar(control, revisionSql));

            await using var revoke = new NpgsqlCommand("""UPDATE public."Sys_Tenants" SET "Enable"=false WHERE "Id"=@tenant""", revoker, revocation);
            revoke.Parameters.AddWithValue("tenant", tenantA);
            var pending = revoke.ExecuteNonQueryAsync(ct);
            await WaitForBlock(control, revoker.ProcessID, writer.ProcessID, ct);
            await work.RollbackAsync(ct);
            Assert.Equal(1, await pending);
            Assert.Equal(start, await CurrentPermissionPostgresFixture.Scalar(control, revisionSql));
            await revocation.CommitAsync(ct);
        }
        var revoked = (long)(await CurrentPermissionPostgresFixture.Scalar(control, revisionSql))!;
        Assert.True(revoked > start);

        long restored;
        await using (var revocation = await revoker.BeginTransactionAsync(ct))
        {
            await using var restore = new NpgsqlCommand("""UPDATE public."Sys_Tenants" SET "Enable"=true WHERE "Id"=@tenant""", revoker, revocation);
            restore.Parameters.AddWithValue("tenant", tenantA);
            Assert.Equal(1, await restore.ExecuteNonQueryAsync(ct));
            await using var work = await writer.BeginTransactionAsync(ct);
            var pending = Acquire(writer, work, tenantA, ct);
            await WaitForBlock(control, writer.ProcessID, revoker.ProcessID, ct);
            await revocation.CommitAsync(ct);
            Assert.True(await pending);
            restored = (long)(await CurrentPermissionPostgresFixture.Scalar(control, revisionSql))!;
            Assert.True(restored > revoked);
            await work.RollbackAsync(ct);
        }

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(
            () => Acquire(reader, null, tenantB, ct))).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(
            () => Acquire(writer, null, tenantB, ct))).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
            CurrentPermissionPostgresFixture.Sql(writer, "UPDATE cp6_platform_admin_guard_v1.root SET revision=0", ct))).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
            CurrentPermissionPostgresFixture.Sql(writer, """SELECT "Password" FROM public."Sys_Users" """, ct))).SqlState);
        Assert.Equal(restored, await CurrentPermissionPostgresFixture.Scalar(control, revisionSql));
    }

    private static async Task<NpgsqlConnection> Open(string connectionString, string? login = null, string? password = null)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = "pg_catalog", Pooling = false, Enlist = false, IncludeErrorDetail = false
        };
        if (login is not null)
        {
            settings.Username = login;
            settings.Password = password;
        }
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<bool> Acquire(NpgsqlConnection db, NpgsqlTransaction? tx, Guid tenant, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT cp6_stock_write_fence_v1.acquire(@tenant)", db, tx);
        command.Parameters.AddWithValue("tenant", tenant);
        return await command.ExecuteScalarAsync(ct) is true;
    }

    private static async Task WaitForBlock(NpgsqlConnection observer, int waiting, int blocker, CancellationToken ct)
    {
        await using var check = new NpgsqlCommand("SELECT @blocker=ANY(pg_catalog.pg_blocking_pids(@waiting))", observer);
        check.Parameters.AddWithValue("blocker", blocker);
        check.Parameters.AddWithValue("waiting", waiting);
        while (await check.ExecuteScalarAsync(ct) is not true)
            await Task.Delay(20, ct);
    }
}
