using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Diagnostics;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CP6.Crm.SourceFence.Tests;

public sealed class SourceFenceTests
{
    [Fact]
    public async Task Freeze_rejects_all_20_source_DML_and_preserves_ERP_and_reads()
    {
        await using var db = await SqlFixture.CreateAsync();
        var run = Guid.NewGuid();
        var before = await db.Fence.PreflightAsync();
        Assert.Equal("Uninitialized", before.State);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        var status = await db.Fence.FreezeAsync(run, 0);
        Assert.Equal("Frozen", status.State);
        Assert.Equal(1, status.Generation);
        Assert.True(status.GuardInventoryVerified);
        Assert.False(status.CompleteWriteFenceVerified);
        foreach (var table in SqlFixture.Tables)
        {
            await SqlError(() => db.ExecuteAsync($"UPDATE dbo.[{table}] SET Id=Id WHERE 1=0;"), 51041);
            await SqlError(() => db.ExecuteAsync($"DELETE dbo.[{table}] WHERE 1=0;"), 51041);
            await SqlError(() => db.ExecuteAsync($"INSERT dbo.[{table}] (Id) SELECT Id FROM dbo.[{table}] WHERE 1=0;"), 51041);
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT COUNT_BIG(*) FROM dbo.[{table}];"));
        }
        await db.ExecuteAsync("EXECUTE AS USER=N'FenceWriter'; UPDATE dbo.ErpSentinel SET Value=11 WHERE Id=1; REVERT;");
        Assert.Equal(11, await db.ScalarAsync<int>("SELECT Value FROM dbo.ErpSentinel WHERE Id=1;"));
        Assert.Equal("Frozen", (await new SourceFence(db.Options).StatusAsync()).State);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM crm_source_control.Audit;"));
    }

    [Fact]
    public async Task Low_privilege_raw_bulk_truncate_and_alter_cannot_bypass_fence()
    {
        await using var db = await SqlFixture.CreateAsync();
        await db.Fence.FreezeAsync(Guid.NewGuid(), 0);
        foreach (var table in SqlFixture.Tables)
        {
            foreach (var statement in new[] {
                $"INSERT dbo.[{table}] (Id) SELECT Id FROM dbo.[{table}] WHERE 1=0;",
                $"UPDATE dbo.[{table}] SET Id=Id WHERE 1=0;",
                $"DELETE dbo.[{table}] WHERE 1=0;",
                $"TRUNCATE TABLE dbo.[{table}];",
                $"ALTER TABLE dbo.[{table}] ADD Unexpected int NULL;" })
                await SqlError(() => db.ExecuteAsync("EXECUTE AS USER=N'FenceWriter'; " + statement), 229, 1088, 4712);
        }
        foreach (var options in new[] { SqlBulkCopyOptions.Default, SqlBulkCopyOptions.FireTriggers })
        {
            await using var connection = await db.OpenAsync();
            await new SqlCommand("EXECUTE AS USER=N'FenceWriter';", connection).ExecuteNonQueryAsync();
            using var bulk = new SqlBulkCopy(connection, options, null) { DestinationTableName = "dbo.Crm_Account" };
            using var rows = new DataTable();
            rows.Columns.Add("Id", typeof(Guid)); rows.Columns.Add("Name", typeof(string));
            rows.Columns.Add("NormalizedName", typeof(string)); rows.Columns.Add("CreateDate", typeof(DateTime));
            rows.Columns.Add("TenantId", typeof(Guid)); rows.Columns.Add("IsDeleted", typeof(bool));
            rows.Rows.Add(Guid.NewGuid(), "bulk-fixture", "BULK-FIXTURE", DateTime.UtcNow, Guid.NewGuid(), false);
            foreach (DataColumn column in rows.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await SqlError(() => bulk.WriteToServerAsync(rows), 229, 5304);
        }
        await SqlError(() => db.ExecuteAsync("EXECUTE AS USER=N'FenceWriter'; DELETE crm_source_control.Audit;"), 229);
        await SqlError(() => db.ExecuteAsync("EXECUTE AS USER=N'FenceWriter'; UPDATE crm_source_control.Control SET State=N'Reopened';"), 229);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Crm_Account;"));
    }

    [Fact]
    public async Task Ownership_chain_and_mixed_ERP_transaction_are_blocked_and_rolled_back()
    {
        await using var db = await SqlFixture.CreateAsync();
        await db.ExecuteAsync("CREATE PROCEDURE dbo.LegacyPurge AS BEGIN SET XACT_ABORT ON; BEGIN TRANSACTION; UPDATE dbo.ErpSentinel SET Value=999; DELETE dbo.Crm_Account; COMMIT; END;");
        await db.ExecuteAsync("GRANT EXECUTE ON dbo.LegacyPurge TO FenceWriter;");
        await db.Fence.FreezeAsync(Guid.NewGuid(), 0);
        await SqlError(() => db.ExecuteAsync("EXECUTE AS USER=N'FenceWriter'; EXEC dbo.LegacyPurge;"), 51041);
        Assert.Equal(10, await db.ScalarAsync<int>("SELECT Value FROM dbo.ErpSentinel WHERE Id=1;"));
    }

    [Fact]
    public async Task Reopen_restores_unrelated_permissions_and_actual_EF_OUTPUT_insert()
    {
        await using var db = await SqlFixture.CreateAsync();
        await db.ExecuteAsync("GRANT SELECT ON dbo.Crm_Account TO public; DENY REFERENCES ON dbo.Crm_Account TO public; GRANT UPDATE ON dbo.Crm_Account TO FenceWriter;");
        var original = await PermissionSnapshot(db);
        var run = Guid.NewGuid();
        await db.Fence.FreezeAsync(run, 0);
        await using (var context = new AccountContext(db.ConnectionString))
        {
            context.Add(new Account());
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains(((SqlException)failure.InnerException!).Errors.Cast<SqlError>(), e => e.Number == 334 || e.Number == 51041);
        }
        Assert.Equal("Reopened", (await db.Fence.ReopenAsync(run, 1)).State);
        Assert.Equal(original, await PermissionSnapshot(db));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers WHERE parent_id IN (SELECT object_id FROM sys.tables WHERE name LIKE 'Crm[_]%');"));
        await using (var context = new AccountContext(db.ConnectionString))
        {
            context.Add(new Account());
            Assert.Equal(1, await context.SaveChangesAsync());
        }
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Crm_Account;"));
    }

    [Fact]
    public async Task Immediate_reopen_replay_remains_idempotent_after_legacy_writes_resume()
    {
        await using var db = await SqlFixture.CreateAsync();
        var run = Guid.NewGuid();
        await db.Fence.FreezeAsync(run, 0);
        await db.Fence.ReopenAsync(run, 1);
        await db.ExecuteAsync(SqlFixture.InsertAccount);

        var replay = await db.Fence.ReopenAsync(run, 1);
        Assert.Equal("Reopened", replay.State);
        Assert.Equal(run, replay.RunId);
        Assert.Equal(2, replay.Generation);
        Assert.Equal(1L, replay.SourceRows["Crm_Account"]);
        Assert.False(replay.EmptyProfileVerified);
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM crm_source_control.Audit;"));
        await FenceError(() => db.Fence.PreflightAsync(), "C04A_SOURCE_NONEMPTY");
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 2), "C04A_SOURCE_NONEMPTY");

        await db.ExecuteAsync("DELETE dbo.Crm_Account;");
        await FenceError(() => db.Fence.FreezeAsync(run, 2), "C04A_RUN_REUSED");
        await FenceError(() => db.Fence.FreezeAsync(run, 0), "C04A_CONFLICT");
        await db.Fence.FreezeAsync(Guid.NewGuid(), 2);
        await FenceError(() => db.Fence.ReopenAsync(run, 1), "C04A_CONFLICT");
    }

    [Fact]
    public async Task Lifecycle_uses_monotonic_generation_new_runs_and_audit_once()
    {
        await using var db = await SqlFixture.CreateAsync();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        Assert.Equal(1, (await db.Fence.FreezeAsync(first, 0)).Generation);
        Assert.Equal(1, (await db.Fence.FreezeAsync(first, 0)).Generation);
        await FenceError(() => db.Fence.FreezeAsync(second, 0), "C04A_CONFLICT");
        await FenceError(() => db.Fence.ReopenAsync(second, 1), "C04A_CONFLICT");
        await FenceError(() => db.Fence.ReopenAsync(first, 0), "C04A_CONFLICT");
        Assert.Equal(2, (await db.Fence.ReopenAsync(first, 1)).Generation);
        Assert.Equal(2, (await db.Fence.ReopenAsync(first, 1)).Generation);
        await FenceError(() => db.Fence.FreezeAsync(first, 2), "C04A_RUN_REUSED");
        await FenceError(() => db.Fence.FreezeAsync(first, 0), "C04A_CONFLICT");
        Assert.Equal(3, (await db.Fence.FreezeAsync(second, 2)).Generation);
        await FenceError(() => db.Fence.ReopenAsync(first, 1), "C04A_CONFLICT");
        Assert.Equal(4, (await db.Fence.SealForwardOnlyAsync(second, 3)).Generation);
        Assert.Equal(4, (await db.Fence.SealForwardOnlyAsync(second, 3)).Generation);
        await FenceError(() => db.Fence.ReopenAsync(second, 4), "C04A_FORWARD_ONLY");
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 4), "C04A_FORWARD_ONLY");
        Assert.Equal(4, await db.ScalarAsync<int>("SELECT COUNT(*) FROM crm_source_control.Audit;"));
        await SqlError(() => db.ExecuteAsync("DELETE crm_source_control.Audit;"), 51044);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Any_source_row_including_soft_deleted_rejects_empty_profile(bool deleted)
    {
        await using var db = await SqlFixture.CreateAsync();
        await db.ExecuteAsync(SqlFixture.InsertAccount + (deleted ? "UPDATE dbo.Crm_Account SET IsDeleted=1;" : ""));
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0), "C04A_SOURCE_NONEMPTY");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers;"));
    }

    [Theory]
    [InlineData("DROP TABLE dbo.Crm_Activity;")]
    [InlineData("CREATE TABLE dbo.Crm_Unexpected (Id int NOT NULL);")]
    [InlineData("CREATE SCHEMA another AUTHORIZATION dbo;", "CREATE TABLE another.Crm_Account (Id int NOT NULL);")]
    public async Task Missing_extra_or_wrong_schema_source_tables_are_rejected(string sql, string? more = null)
    {
        await using var db = await SqlFixture.CreateAsync();
        await db.ExecuteAsync(sql);
        if (more is not null) await db.ExecuteAsync(more);
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0), "C04A_SOURCE_INVENTORY");
    }

    [Fact]
    public async Task Wrong_database_identity_nonlocal_name_and_zero_run_are_rejected_without_DDL()
    {
        await using var db = await SqlFixture.CreateAsync();
        await FenceError(() => new SourceFence(db.Options with { ExpectedDatabaseName = "CP6DB" }).FreezeAsync(Guid.NewGuid(), 0), "C04A_DATABASE_IDENTITY");
        await FenceError(() => new SourceFence(db.Options with { ExpectedDatabaseGuid = Guid.NewGuid() }).StatusAsync(), "C04A_DATABASE_IDENTITY");
        await FenceError(() => new SourceFence(db.Options with { ConnectionString = "Server=example.invalid;Database=" + db.Name + ";Integrated Security=true" }).StatusAsync(), "C04A_LOCAL_REHEARSAL_REQUIRED");
        await FenceError(() => db.Fence.FreezeAsync(Guid.Empty, 0), "C04A_INVALID_COMMAND");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
    }

    [Theory]
    [InlineData("GRANT INSERT ON dbo.Crm_Account TO public;")]
    [InlineData("DENY DELETE ON dbo.Crm_Account TO public;")]
    [InlineData("GRANT UPDATE (Name) ON dbo.Crm_Account TO public;")]
    [InlineData("CREATE TRIGGER dbo.Existing ON dbo.Crm_Account AFTER UPDATE AS RETURN;")]
    public async Task Existing_public_mutation_permissions_or_triggers_are_not_overwritten(string setup)
    {
        await using var db = await SqlFixture.CreateAsync();
        await db.ExecuteAsync(setup);
        var permissions = await PermissionSnapshot(db);
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0), setup.StartsWith("CREATE", StringComparison.Ordinal) ? "C04A_GUARD_DRIFT" : "C04A_PERMISSION_CONFLICT");
        Assert.Equal(permissions, await PermissionSnapshot(db));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
    }

    [Theory]
    [InlineData("DISABLE TRIGGER dbo.C04A_Fence_Crm_Account ON dbo.Crm_Account;", "C04A_GUARD_DRIFT")]
    [InlineData("DROP TRIGGER dbo.C04A_Fence_Crm_Account;", "C04A_GUARD_DRIFT")]
    [InlineData("ALTER TRIGGER dbo.C04A_Fence_Crm_Account ON dbo.Crm_Account AFTER INSERT,UPDATE,DELETE AS RETURN;", "C04A_GUARD_DRIFT")]
    [InlineData("REVOKE INSERT ON dbo.Crm_Account FROM public;", "C04A_GUARD_DRIFT")]
    [InlineData("DROP TABLE crm_source_control.Control;", "C04A_METADATA_DRIFT")]
    [InlineData("DISABLE TRIGGER crm_source_control.C04A_Audit_AppendOnly ON crm_source_control.Audit;", "C04A_METADATA_DRIFT")]
    public async Task Guard_or_metadata_tamper_refuses_status_reopen_and_idempotent_freeze(string tamper, string code)
    {
        await using var db = await SqlFixture.CreateAsync();
        var run = Guid.NewGuid();
        await db.Fence.FreezeAsync(run, 0);
        await db.ExecuteAsync(tamper);
        await FenceError(() => db.Fence.StatusAsync(), code);
        await FenceError(() => db.Fence.ReopenAsync(run, 1), code);
        await FenceError(() => db.Fence.FreezeAsync(run, 0), code);
    }

    [Fact]
    public async Task Freeze_drains_prior_writer_then_counts_committed_rows()
    {
        await using var db = await SqlFixture.CreateAsync();
        await using var writer = await db.OpenAsync();
        using var transaction = writer.BeginTransaction();
        await new SqlCommand(SqlFixture.InsertAccount, writer, transaction).ExecuteNonQueryAsync();
        var freezing = db.Fence.FreezeAsync(Guid.NewGuid(), 0);
        await Task.Delay(250);
        Assert.False(freezing.IsCompleted);
        transaction.Commit();
        await FenceError(() => freezing, "C04A_SOURCE_NONEMPTY");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers;"));
    }

    [Fact]
    public async Task Lock_timeout_rolls_back_all_guards_metadata_and_permissions()
    {
        await using var db = await SqlFixture.CreateAsync();
        var original = await PermissionSnapshot(db);
        await using var writer = await db.OpenAsync();
        using var transaction = writer.BeginTransaction();
        await new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.Crm_StageHistory WITH(TABLOCKX,HOLDLOCK);", writer, transaction).ExecuteScalarAsync();
        var stopwatch = Stopwatch.StartNew();
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0), "C04A_LOCK_TIMEOUT");
        Assert.True(stopwatch.ElapsedMilliseconds >= 1000);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15));
        transaction.Rollback();
        Assert.Equal(original, await PermissionSnapshot(db));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers;"));
        await db.ExecuteAsync(SqlFixture.InsertAccount);
    }

    [Fact]
    public async Task Concurrent_operators_install_only_one_run_and_audit_event()
    {
        await using var db = await SqlFixture.CreateAsync();
        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await db.Fence.FreezeAsync(Guid.NewGuid(), 0); return "ok"; }
            catch (SourceFenceException error) { return error.Code; }
        });
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(result => result == "ok"));
        Assert.Equal(1, results.Count(result => result == "C04A_CONFLICT"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM crm_source_control.Audit;"));
    }

    [Fact]
    public async Task Mid_install_DDL_failure_rolls_back_previous_19_guards_and_metadata()
    {
        await using var db = await SqlFixture.CreateAsync();
        var permissions = await PermissionSnapshot(db);
        await db.ExecuteAsync("CREATE TRIGGER C04A_Test_FailLastGuard ON DATABASE FOR CREATE_TRIGGER AS BEGIN IF EVENTDATA().value('(/EVENT_INSTANCE/ObjectName)[1]','nvarchar(128)')=N'C04A_Fence_Crm_StageHistory' THROW 51123,'C04A_TEST_INJECTED_DDL_FAILURE',1; END;");
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0), "C04A_SQL_FAILURE");
        Assert.Equal(permissions, await PermissionSnapshot(db));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers WHERE parent_class=1;"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.extended_properties WHERE name=N'CP6.C04A.SourceFence.SchemaVersion';"));
        await db.ExecuteAsync(SqlFixture.InsertAccount);
    }

    [Fact]
    public async Task Cancelled_lock_wait_is_atomic_and_never_reports_success()
    {
        await using var db = await SqlFixture.CreateAsync();
        await using var writer = await db.OpenAsync();
        using var transaction = writer.BeginTransaction();
        await new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.Crm_StageHistory WITH(TABLOCKX,HOLDLOCK);", writer, transaction).ExecuteScalarAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0, cancellation.Token), "C04A_CANCELLED");
        transaction.Rollback();
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers;"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
    }

    [Fact]
    public async Task Prior_writer_rollback_drains_then_freeze_succeeds()
    {
        await using var db = await SqlFixture.CreateAsync();
        await using var writer = await db.OpenAsync();
        using var transaction = writer.BeginTransaction();
        await new SqlCommand(SqlFixture.InsertAccount, writer, transaction).ExecuteNonQueryAsync();
        var freezing = db.Fence.FreezeAsync(Guid.NewGuid(), 0);
        await Task.Delay(250);
        Assert.False(freezing.IsCompleted);
        transaction.Rollback();
        Assert.Equal("Frozen", (await freezing).State);
    }

    [Theory]
    [InlineData("ALTER TABLE crm_source_control.Audit DROP CONSTRAINT PK_C04A_Audit;")]
    [InlineData("ALTER TABLE crm_source_control.Control DROP CONSTRAINT CK_C04A_Control_Singleton;")]
    public async Task Metadata_constraint_drift_is_rejected_before_a_transition(string tamper)
    {
        await using var db = await SqlFixture.CreateAsync();
        var run = Guid.NewGuid();
        await db.Fence.FreezeAsync(run, 0);
        await db.ExecuteAsync(tamper);
        await FenceError(() => db.Fence.ReopenAsync(run, 1), "C04A_METADATA_DRIFT");
        await SqlError(() => db.ExecuteAsync("DELETE dbo.Crm_Account WHERE 1=0;"), 51041);
    }

    [Fact]
    public async Task Operator_applock_timeout_does_not_initialize_metadata()
    {
        await using var db = await SqlFixture.CreateAsync();
        await using var connection = await db.OpenAsync();
        using var transaction = connection.BeginTransaction();
        using var acquire = new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'CP6.C04A.SourceFence.v1', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @DbPrincipal=N'public'; SELECT @result;", connection, transaction);
        Assert.True((int)(await acquire.ExecuteScalarAsync())! >= 0);
        await FenceError(() => db.Fence.FreezeAsync(Guid.NewGuid(), 0), "C04A_LOCK_TIMEOUT");
        transaction.Rollback();
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';"));
    }

    private static async Task FenceError(Func<Task<SourceFenceStatus>> action, string code)
    {
        var error = await Assert.ThrowsAsync<SourceFenceException>(action);
        Assert.Equal(code, error.Code);
        Assert.Equal(code, error.Message);
        Assert.Null(error.InnerException);
    }

    private static Task<string> PermissionSnapshot(SqlFixture db) => db.ScalarAsync<string>("SELECT COALESCE(STRING_AGG(CONVERT(nvarchar(max),CONCAT(major_id,':',minor_id,':',grantee_principal_id,':',grantor_principal_id,':',type,':',state)),N';') WITHIN GROUP (ORDER BY major_id,minor_id,grantee_principal_id,type),N'') FROM sys.database_permissions WHERE class=1 AND major_id IN (SELECT object_id FROM sys.tables WHERE name LIKE N'Crm[_]%');");

    private sealed class Account
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "EF fixture";
        public string NormalizedName { get; set; } = "EF FIXTURE";
        public Guid TenantId { get; set; } = Guid.NewGuid();
        public bool IsDeleted { get; set; }
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;
        public byte[]? RowVersion { get; set; }
    }

    private sealed class AccountContext(string connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlServer(connection);
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Account>().ToTable("Crm_Account", "dbo");
            model.Entity<Account>().Property(row => row.RowVersion).IsRowVersion();
        }
    }

    private static async Task SqlError(Func<Task> action, params int[] numbers)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(action);
        Assert.Contains(exception.Errors.Cast<SqlError>(), error => numbers.Contains(error.Number));
    }
}
