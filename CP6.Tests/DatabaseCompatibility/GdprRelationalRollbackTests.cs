using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.DatabaseCompatibility;

public sealed partial class GdprRelationalTests
{
    [Wp5ReportsFact]
    public async Task Native_tombstone_failure_rolls_back_purge_and_allows_later_success()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var runtime = Runtime(tenant, otherTenant);
        var user = await SeedIdentityAsync(tenant, runtime);
        var otherUser = await SeedIdentityAsync(otherTenant, runtime);
        var grant = await GrantAndLogoutAsync(tenant, user);
        var otherGrant = await GrantAndLogoutAsync(otherTenant, otherUser);
        var before = await ReadPurgeStateAsync(tenant);
        var otherBefore = await ReadPurgeStateAsync(otherTenant);
        Assert.Equal(1, await CountGrantAsync(grant.Id));
        Assert.Equal(1, await CountLogoutAsync(grant.Id));
        Assert.Equal(1, await CountGrantAsync(otherGrant.Id));
        Assert.Equal(1, await CountLogoutAsync(otherGrant.Id));
        Assert.Equal(5, before.Count(row => row.StartsWith("normal:", StringComparison.Ordinal)));
        Assert.DoesNotContain(before, row => row.StartsWith("priority:", StringComparison.Ordinal));
        var constraint = "CK_wp5_gdpr_tombstone_" + Guid.NewGuid().ToString("N");
        var installed = false;
        try
        {
            await using (var ddl = fixture.CreatePriorityContext())
            {
                // Version 1 token revocation succeeds before deletion; version 2 tombstones fail afterwards.
                await ddl.Database.ExecuteSqlRawAsync(fixture.Database.Provider == DatabaseProvider.PostgreSql
                    ? $"ALTER TABLE crm_identity_priority.\"Cp6_OutboxMessage\" ADD CONSTRAINT \"{constraint}\" CHECK (\"TenantId\" <> '{tenant:D}'::uuid OR \"AggregateVersion\" <> 2) NOT VALID"
                    : $"ALTER TABLE [crm_identity_priority].[Cp6_OutboxMessage] WITH NOCHECK ADD CONSTRAINT [{constraint}] CHECK ([TenantId] <> '{tenant:D}' OR [AggregateVersion] <> 2)");
                installed = true;
            }

            var observed = new PurgeDeleteObserver();
            var tenantScope = new TenantContext { CurrentTenantId = tenant };
            await using (var db = CreateObservedIdentityContext(tenantScope, runtime, observed))
            {
                var service = new GdprService(db, null!, null!, null!, new SecurityAuditService(db), tenantScope, identity: runtime);
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => service.EraseTenantAsync(tenant, "purge"));
                var failure = DatabaseFailureClassifier.Classify(error);
                Assert.Equal(DatabaseFailureKind.CheckConstraint, failure.Kind);
                Assert.Equal(constraint, failure.ConstraintName);
                Assert.False(failure.CanRetryTransaction);
                if (fixture.Database.Provider == DatabaseProvider.PostgreSql) Assert.Equal("23514", failure.SqlState);
                else Assert.Equal(547, failure.DatabaseErrorCode);
                output.WriteLine($"Native GDPR failure: provider={fixture.Database.Provider}; constraint={constraint}; SQLSTATE={failure.SqlState}; code={failure.DatabaseErrorCode}.");
            }

            // These are completed native DELETE row counts, captured before the actual CHECK failure.
            Assert.Equal(1, observed.DeletedRows("Sys_Tenants"));
            Assert.Equal(1, observed.DeletedRows("Sys_Users"));
            Assert.Equal(2, observed.DeletedRows("Sys_Dept"));
            Assert.Equal(before, await ReadPurgeStateAsync(tenant));
            Assert.Equal(otherBefore, await ReadPurgeStateAsync(otherTenant));
        }
        finally
        {
            if (installed)
            {
                await using var cleanup = fixture.CreatePriorityContext();
                await cleanup.Database.ExecuteSqlRawAsync(fixture.Database.Provider == DatabaseProvider.PostgreSql
                    ? $"ALTER TABLE crm_identity_priority.\"Cp6_OutboxMessage\" DROP CONSTRAINT \"{constraint}\""
                    : $"ALTER TABLE [crm_identity_priority].[Cp6_OutboxMessage] DROP CONSTRAINT [{constraint}]");
                await using var native = fixture.CreateConnection();
                Assert.Equal(0, await native.QuerySingleAsync<int>(fixture.Database.Provider == DatabaseProvider.PostgreSql
                    ? "SELECT COUNT(*)::int FROM pg_catalog.pg_constraint WHERE conname=@constraint AND conrelid='crm_identity_priority.\"Cp6_OutboxMessage\"'::regclass"
                    : "SELECT COUNT(*) FROM sys.check_constraints WHERE name=@constraint AND parent_object_id=OBJECT_ID('crm_identity_priority.Cp6_OutboxMessage')", new { constraint }));
            }
        }

        // A fresh, explicit business invocation succeeds after removing the owned failure condition.
        var retryScope = new TenantContext { CurrentTenantId = tenant };
        await using (var db = fixture.CreateIdentityContext(retryScope, runtime))
        {
            await new GdprService(db, null!, null!, null!, new SecurityAuditService(db), retryScope, identity: runtime)
                .EraseTenantAsync(tenant, "purge");
        }
        await AssertRecoveredPurgeAsync(tenant, grant, runtime);
        var after = await ReadPurgeStateAsync(tenant);
        Assert.Equal(before.Where(row => row.StartsWith("normal:", StringComparison.Ordinal)),
            after.Where(row => row.StartsWith("normal:", StringComparison.Ordinal)));
        Assert.Equal(otherBefore, await ReadPurgeStateAsync(otherTenant));
    }

    private CP6Context CreateObservedIdentityContext(TenantContext tenant, CrmIdentityRuntime runtime, IInterceptor observer)
    {
        var profile = DatabaseMigrationProfile.For(fixture.Database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), fixture.Database,
            fixture.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        options.AddInterceptors(observer);
        var context = new CP6Context(options.Options, tenant, identity: runtime);
        context.Database.SetCommandTimeout(180);
        return context;
    }

    private async Task<string[]> ReadPurgeStateAsync(Guid tenant)
    {
        var rows = new List<string>();
        await using (var db = fixture.CreateContext(tenant))
        {
            AddPersistedRows(rows, "tenant", db, await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == tenant).ToArrayAsync());
            AddPersistedRows(rows, "users", db, await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
            AddPersistedRows(rows, "departments", db, await db.Sys_Depts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
            AddPersistedRows(rows, "roles", db, await db.Sys_Roles.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
            AddPersistedRows(rows, "role-actions", db, await db.Sys_RoleActions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
            AddPersistedRows(rows, "role-scopes", db, await db.Sys_RoleDataScopes.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
            AddPersistedRows(rows, "snapshots", db, await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
            AddPersistedRows(rows, "normal", db, await db.Set<Cp6OutboxMessage>().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
            AddPersistedRows(rows, "audit", db, await db.Sys_SecurityLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.EventType == (int)SecurityEventType.GdprTenantErased && x.RequestTenantCode == TenantCode(tenant)).ToArrayAsync());
        }
        await using (var priority = fixture.CreatePriorityContext())
        {
            AddPersistedRows(rows, "priority", priority, await priority.Set<Cp6OutboxMessage>().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync());
        }
        await using var native = fixture.CreateConnection();
        var grants = await native.QueryAsync<CrmOidcGrant>(fixture.Database.Provider == DatabaseProvider.PostgreSql
            ? "SELECT * FROM public.\"CrmOidcGrant\" WHERE \"OrganizationId\"=@tenant"
            : "SELECT * FROM dbo.CrmOidcGrant WHERE OrganizationId=@tenant", new { tenant });
        rows.AddRange(grants.Select(row => "grant:" + Fingerprint(row)));
        var logouts = await native.QueryAsync<CrmOidcLogout>(fixture.Database.Provider == DatabaseProvider.PostgreSql
            ? "SELECT l.* FROM public.\"CrmOidcLogout\" l INNER JOIN public.\"CrmOidcGrant\" g ON l.\"GrantId\"=g.\"Id\" WHERE g.\"OrganizationId\"=@tenant"
            : "SELECT l.* FROM dbo.CrmOidcLogout l INNER JOIN dbo.CrmOidcGrant g ON l.GrantId=g.Id WHERE g.OrganizationId=@tenant", new { tenant });
        rows.AddRange(logouts.Select(row => "logout:" + Fingerprint(row)));
        return rows.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddPersistedRows<T>(List<string> destination, string table, DbContext context, IEnumerable<T> rows) where T : class
    {
        foreach (var row in rows)
        {
            var values = context.Entry(row).Properties.OrderBy(property => property.Metadata.Name, StringComparer.Ordinal)
                .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue, StringComparer.Ordinal);
            destination.Add(table + ":" + Fingerprint(values));
        }
    }

    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private async Task AssertRecoveredPurgeAsync(Guid tenant, CrmOidcGrant grant, CrmIdentityRuntime runtime)
    {
        await using (var db = fixture.CreateContext(tenant))
        {
            Assert.False(await db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant));
            Assert.False(await db.Sys_Users.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await db.Sys_Depts.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await db.Sys_Roles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await db.Sys_RoleActions.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await db.Sys_RoleDataScopes.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            var snapshots = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
            Assert.Equal(6, snapshots.Length);
            Assert.Equal(5, snapshots.Count(x => x.IsDeleted && x.Version == 2));
            var revoked = Assert.Single(snapshots, x => x.EventType == IdentityEventContracts.TokenRevoked);
            Assert.Equal(IdentityEventContracts.TokenAggregate(Issuer, grant.Id.ToString("D")), revoked.AggregateId);
            Assert.Equal(1, revoked.Version);
            Assert.DoesNotContain(snapshots, x => x.AggregateId.EndsWith(grant.SourceJti, StringComparison.Ordinal));
            Assert.All(snapshots, row =>
            {
                Assert.Equal(8, row.RowVersion.Length);
                Assert.Equal(IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(row.PayloadJson)), row.PayloadSha256);
            });
            Assert.Equal(5, await db.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == tenant));
            var audit = Assert.Single(await db.Sys_SecurityLogs.IgnoreQueryFilters()
                .Where(x => x.EventType == (int)SecurityEventType.GdprTenantErased && x.RequestTenantCode == TenantCode(tenant)).ToArrayAsync());
            Assert.Equal(TenantContext.DefaultTenant, audit.TenantId);
            Assert.Equal("purge:" + TenantCode(tenant), audit.Reason);
        }
        Assert.Equal(0, await CountGrantAsync(grant.Id));
        Assert.Equal(0, await CountLogoutAsync(grant.Id));
        await using var priority = fixture.CreatePriorityContext();
        var messages = await priority.Set<Cp6OutboxMessage>().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
        Assert.Equal(6, messages.Length);
        Assert.All(messages, message => Assert.True(runtime.Validator.IsValid(message.Payload)));
    }

    private sealed class PurgeDeleteObserver : DbCommandInterceptor
    {
        private readonly Dictionary<string, int> deleted = new(StringComparer.Ordinal);
        private static readonly string[] ObservedTables = ["Sys_Tenants", "Sys_Users", "Sys_Dept"];

        public int DeletedRows(string table) => deleted.GetValueOrDefault(table);

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Record(command, result);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Record(command, result);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command, int result)
        {
            if (!command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var table in ObservedTables)
            {
                if (command.CommandText.Contains("[" + table + "]", StringComparison.Ordinal)
                    || command.CommandText.Contains("\"" + table + "\"", StringComparison.Ordinal))
                    deleted[table] = deleted.GetValueOrDefault(table) + result;
            }
        }
    }
}
