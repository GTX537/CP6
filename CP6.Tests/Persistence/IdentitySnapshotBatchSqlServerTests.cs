using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.Tests.Persistence;

public sealed class IdentitySnapshotBatchSqlServerTests(
    IdentitySnapshotBatchSqlServerFixture fixture, ITestOutputHelper output)
    : IClassFixture<IdentitySnapshotBatchSqlServerFixture>
{
    [SqlServerFact]
    public async Task Async_snapshot_batch_refills_native_tokens_for_distinct_aggregate_keys()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        await using var db = fixture.CreateContext();
        Assert.Equal(0, await GenerationAsync(db, tenant));
        var snapshots = NewSnapshotBatch(tenant);
        db.CrmIdentitySnapshots.AddRange(snapshots);

        Assert.Equal(5, await db.SaveChangesAsync());

        await AssertStoredBatchAsync(db, snapshots);
        Assert.True(await GenerationAsync(db, tenant) > 0);
    }

    [SqlServerFact]
    public async Task Sync_legacy_UseSqlServer_snapshot_batch_refills_native_tokens()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        // This path deliberately bypasses DatabaseContextOptions, as existing direct callers do.
        await using var db = fixture.CreateLegacyContext();
        Assert.Equal(0, await GenerationAsync(db, tenant));
        var snapshots = NewSnapshotBatch(tenant);
        db.CrmIdentitySnapshots.AddRange(snapshots);

        Assert.Equal(5, db.SaveChanges());

        await AssertStoredBatchAsync(db, snapshots);
        Assert.True(await GenerationAsync(db, tenant) > 0);
    }

    [SqlServerFact]
    public async Task Caller_transaction_rollback_removes_the_batch_and_its_generation()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var snapshots = NewSnapshotBatch(tenant);
            db.CrmIdentitySnapshots.AddRange(snapshots);
            Assert.Equal(5, await db.SaveChangesAsync());
            await AssertStoredBatchAsync(db, snapshots);
            Assert.True(await GenerationAsync(db, tenant) > 0);
            await transaction.RollbackAsync();
        }
        await using var persisted = fixture.CreateContext();
        Assert.False(await persisted.CrmIdentitySnapshots.AsNoTracking().AnyAsync(x => x.TenantId == tenant));
        Assert.Equal(0, await GenerationAsync(persisted, tenant));
    }

    [SqlServerFact]
    public async Task Real_business_save_emits_five_versioned_snapshots_and_five_valid_outbox_messages()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        const string issuer = "https://identity.cp6.test";
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
        var validator = new IdentityEventValidator(bundle, issuer);
        var runtime = new CrmIdentityRuntime(new CrmIdentityOptions
        {
            Enabled = true, Issuer = issuer, Tenants = { [tenant] = "us" }, ProjectionReaderClientIds = ["reader-a"]
        }, validator);
        await using var db = fixture.CreateContext(tenant, runtime);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        // Preserve the original identity producer fixture's real Seed/ValidSnapshots scenario.
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "c02-" + tenant.ToString("N"), TenantName = "C02 SQL fixture" });
        db.Sys_Roles.Add(new Sys_Role { TenantId = tenant, RoleId = 7, RoleName = "C02 fixture role" });
        db.Sys_Depts.AddRange(new Sys_Dept { Id = first, TenantId = tenant, DeptCode = "ROOT", DeptName = "Fixture root", Path = $"/{first:D}/" },
            new Sys_Dept { Id = second, TenantId = tenant, DeptCode = "CHILD", DeptName = "Fixture child", ParentId = first, Path = $"/{first:D}/{second:D}/" });
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), TenantId = tenant, UserName = "c02-user", Password = "fixture-only-hash", RoleId = 7, DeptId = first });
        if (!await db.Sys_Menus.AnyAsync(x => x.MenuId == 9901))
            db.Sys_Menus.Add(new Sys_Menu { MenuId = 9901, MenuKey = "crm-lead", MenuName = "Fixture leads" });
        db.Sys_RoleActions.Add(new Sys_RoleAction { TenantId = tenant, RoleId = 7, MenuId = 9901, ActionCode = "query" });
        db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { TenantId = tenant, RoleId = 7, ResourceKey = "crm-lead", ScopeType = 2 });

        await db.SaveChangesAsync();

        var snapshots = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
        Assert.Equal(5, snapshots.Length);
        Assert.All(snapshots, row =>
        {
            Assert.Equal(1, row.Version);
            Assert.Equal(8, row.RowVersion.Length);
            Assert.Equal(IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(row.PayloadJson)), row.PayloadSha256);
        });
        var tracked = db.CrmIdentitySnapshots.Local.ToDictionary(x => x.AggregateId, StringComparer.Ordinal);
        Assert.Equal(5, tracked.Count);
        Assert.All(snapshots, row => Assert.Equal(row.RowVersion, tracked[row.AggregateId].RowVersion));
        var messages = await db.Set<Cp6OutboxMessage>().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
        Assert.Equal(5, messages.Length);
        Assert.All(messages, message => Assert.True(validator.IsValid(message.Payload)));
        Assert.True(await GenerationAsync(db, tenant) > 0);
    }

    [SqlServerFact]
    public async Task Ordinary_non_snapshot_batch_keeps_its_native_token_refill()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        var records = Enumerable.Range(0, 5).Select(index => new CrmServiceTokenRecord
        {
            Issuer = "https://bug147.cp6.test", Jti = Guid.NewGuid().ToString("D"),
            ClientId = "client-" + index, TenantId = tenant, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }).ToArray();
        await using var db = fixture.CreateContext();
        db.CrmServiceTokenRecords.AddRange(records);

        Assert.Equal(5, await db.SaveChangesAsync());

        Assert.All(records, row => Assert.Equal(8, row.RowVersion.Length));
        var persisted = (await db.CrmServiceTokenRecords.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync())
            .ToDictionary(x => x.Jti, StringComparer.Ordinal);
        Assert.Equal(5, persisted.Count);
        Assert.All(records, row => Assert.Equal(row.RowVersion, persisted[row.Jti].RowVersion));
        Assert.Equal(0, await GenerationAsync(db, tenant));
        Assert.False(await db.CrmIdentitySnapshots.AnyAsync(x => x.TenantId == tenant));
    }

    [SqlServerFact]
    public async Task Later_row_check_failure_rolls_back_automatic_batch_transaction_and_generation()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        var constraint = "Bug147_Reject_" + Guid.NewGuid().ToString("N");
        await fixture.ExecuteOwnedAsync($"""
            ALTER TABLE crm_identity.Snapshot WITH NOCHECK ADD CONSTRAINT [{constraint}]
            CHECK (TenantId <> CONVERT(uniqueidentifier,'{tenant:D}') OR AggregateId <> N'user:batch-e');
            """);
        try
        {
            await using var db = fixture.CreateContext();
            var snapshots = NewSnapshotBatch(tenant);
            db.CrmIdentitySnapshots.AddRange(snapshots);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var failure = DatabaseFailureClassifier.Classify(exception);
            Assert.Equal(547, failure.DatabaseErrorCode);
            Assert.Equal(DatabaseFailureKind.CheckConstraint, failure.Kind);
            Assert.True(failure.MatchesConstraint(constraint));
            output.WriteLine("BUG147 native later-row failure=547; CHECK identity matches the temporary constraint.");

            // A separate connection proves rollback, rather than relying on the failed tracker.
            await using var persisted = fixture.CreateContext();
            Assert.False(await persisted.CrmIdentitySnapshots.AsNoTracking().AnyAsync(x => x.TenantId == tenant));
            Assert.Equal(0, await GenerationAsync(persisted, tenant));
        }
        finally
        {
            await fixture.ExecuteOwnedAsync($"ALTER TABLE crm_identity.Snapshot DROP CONSTRAINT [{constraint}];");
            Assert.Equal(0, await fixture.CountConstraintAsync(constraint));
            output.WriteLine("BUG147 temporary CHECK cleanup verified absent.");
        }
    }

    [SqlServerFact]
    public async Task Database_BIN2_keys_keep_case_variants_distinct_across_separate_contexts()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        var upper = NewSnapshotBatch(tenant)[0];
        upper.AggregateId = "user:Case-A";
        var lower = NewSnapshotBatch(tenant)[1];
        lower.AggregateId = "user:case-a";
        // EF's SQL Server tracker comparer is case insensitive; only one variant belongs to each tracker.
        foreach (var row in new[] { upper, lower })
        {
            await using var writer = fixture.CreateContext();
            writer.CrmIdentitySnapshots.Add(row);
            Assert.Equal(1, await writer.SaveChangesAsync());
            Assert.Equal(8, row.RowVersion.Length);
        }
        await using var read = fixture.CreateContext();
        var stored = (await read.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync())
            .ToDictionary(x => x.AggregateId, StringComparer.Ordinal);
        Assert.Equal(2, stored.Count);
        Assert.Equal(upper.RowVersion, stored[upper.AggregateId].RowVersion);
        Assert.Equal(lower.RowVersion, stored[lower.AggregateId].RowVersion);
        Assert.NotEqual(upper.RowVersion, lower.RowVersion);
    }

    private static CrmIdentitySnapshot[] NewSnapshotBatch(Guid tenant) =>
        new[] { "user:batch-a", "user:batch-b", "user:batch-c", "user:batch-d", "user:batch-e" }
            .Select((aggregate, index) =>
            {
                var payload = $"{{\"fixture\":{index}}}";
                return new CrmIdentitySnapshot
                {
                    TenantId = tenant, AggregateId = aggregate, Version = 1,
                    EventType = IdentityEventContracts.UserChanged, PayloadJson = payload,
                    PayloadSha256 = IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(payload)),
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
            }).ToArray();

    private static async Task AssertStoredBatchAsync(CP6Context db, CrmIdentitySnapshot[] snapshots)
    {
        Assert.All(snapshots, snapshot => Assert.Equal(8, snapshot.RowVersion.Length));
        Assert.Equal(5, snapshots.Select(x => Convert.ToHexString(x.RowVersion)).Distinct().Count());
        var tenant = snapshots[0].TenantId;
        var stored = (await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync())
            .ToDictionary(x => x.AggregateId, StringComparer.Ordinal);
        Assert.Equal(5, stored.Count);
        Assert.Contains("user:batch-a", stored.Keys);
        Assert.Contains("user:batch-e", stored.Keys);
        foreach (var snapshot in snapshots)
        {
            var row = stored[snapshot.AggregateId];
            Assert.Equal(snapshot.RowVersion, row.RowVersion);
            Assert.Equal(snapshot.PayloadJson, row.PayloadJson);
            Assert.Equal(snapshot.PayloadSha256, row.PayloadSha256);
            Assert.Equal(1, row.Version);
        }
    }

    private static async Task<long> GenerationAsync(CP6Context db, Guid tenant)
        => await db.CrmIdentityTenantGenerations.AsNoTracking().Where(x => x.TenantId == tenant)
            .Select(x => (long?)x.Generation).SingleOrDefaultAsync() ?? 0;
}

/// <summary>One fresh, migrated database for this regression class; it never alters an existing database.</summary>
public sealed class IdentitySnapshotBatchSqlServerFixture : IAsyncLifetime
{
    private readonly string databaseName = $"CP6Test_Bug147_{Guid.NewGuid():N}";
    private string? connectionString;
    private string? masterConnectionString;
    private bool created;

    public string SetupSummary { get; private set; } = "BUG147 SQL input absent; SqlServerFact is environment-gated.";

    public CP6Context CreateContext(Guid? tenant = null, CrmIdentityRuntime? identity = null)
    {
        if (!created || connectionString is null) throw new InvalidOperationException("BUG147_OWNED_DATABASE_NOT_READY");
        var database = new DatabaseOptions(DatabaseProvider.SqlServer);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database,
            connectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        var context = new CP6Context(options, tenant.HasValue ? new TenantContext { CurrentTenantId = tenant.Value } : null,
            identity: identity);
        context.Database.SetCommandTimeout(120);
        return context;
    }

    public CP6Context CreateLegacyContext()
    {
        if (!created || connectionString is null) throw new InvalidOperationException("BUG147_OWNED_DATABASE_NOT_READY");
        return new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(120)).Options);
    }

    public async Task ExecuteOwnedAsync(string sqlText)
    {
        if (!created || connectionString is null) throw new InvalidOperationException("BUG147_OWNED_DATABASE_NOT_READY");
        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = sqlText;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CountConstraintAsync(string name)
    {
        if (!created || connectionString is null) throw new InvalidOperationException("BUG147_OWNED_DATABASE_NOT_READY");
        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'crm_identity.Snapshot') AND name=@name;";
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar);
        if (string.IsNullOrEmpty(configured)) return;
        var supplied = new SqlConnectionStringBuilder(configured);
        RequireLoopback(supplied.DataSource);
        masterConnectionString = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master", Pooling = false, MultipleActiveResultSets = false
        }.ConnectionString;
        connectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;

        try
        {
            await using (var master = new SqlConnection(masterConnectionString))
            {
                await master.OpenAsync();
                await using var command = master.CreateCommand();
                command.CommandTimeout = 120;
                command.CommandText = $"CREATE DATABASE [{databaseName}] COLLATE Chinese_PRC_CI_AS;";
                await command.ExecuteNonQueryAsync();
                created = true;
            }
            await using var db = CreateContext();
            var available = db.Database.GetMigrations().ToArray();
            Assert.Equal(140, available.Length);
            await db.Database.MigrateAsync();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
            Assert.Equal(available, applied);
            Assert.Empty(pending);

            await using var sql = new SqlConnection(connectionString);
            await sql.OpenAsync();
            await using var collation = sql.CreateCommand();
            collation.CommandText = """
                SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')),
                       (SELECT collation_name FROM sys.columns
                        WHERE object_id=OBJECT_ID(N'crm_identity.Snapshot') AND name=N'AggregateId'),
                       (SELECT COUNT(*) FROM sys.triggers
                        WHERE parent_id=OBJECT_ID(N'crm_identity.Snapshot')
                          AND name=N'trg_CrmIdentitySnapshot_TenantGeneration' AND is_disabled=0);
                """;
            await using var reader = await collation.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Chinese_PRC_CI_AS", reader.GetString(0));
            Assert.Equal("Latin1_General_100_BIN2", reader.GetString(1));
            Assert.Equal(1, reader.GetInt32(2));
            SetupSummary = $"BUG147 database={databaseName}; applied={applied.Length}; pending={pending.Length}; " +
                "database-collation=Chinese_PRC_CI_AS; Snapshot.AggregateId-collation=Latin1_General_100_BIN2; generation-trigger-enabled=true.";
            Console.WriteLine(SetupSummary);
        }
        catch
        {
            await DropOwnedDatabaseAsync();
            throw;
        }
    }

    public Task DisposeAsync() => DropOwnedDatabaseAsync();

    private async Task DropOwnedDatabaseAsync()
    {
        if (!created || masterConnectionString is null) return;
        await using var master = new SqlConnection(masterConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandTimeout = 120;
        // The identifier is generated once by this fixture; normal DROP refuses active sessions.
        command.CommandText = $"DROP DATABASE [{databaseName}]; SELECT DB_ID(@database);";
        command.Parameters.AddWithValue("@database", databaseName);
        Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
        created = false;
        Console.WriteLine($"BUG147 cleanup-completed database={databaseName}; absent=true; force=false.");
    }

    private static void RequireLoopback(string dataSource)
    {
        var value = dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? dataSource[4..] : dataSource;
        var host = value.Split('\\', ',')[0];
        if (host is not ("127.0.0.1" or "::1" or "[::1]") && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BUG147_LOOPBACK_SQL_SERVER_REQUIRED");
    }
}
