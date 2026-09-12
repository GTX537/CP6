using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class BusinessPartnerBindingConcurrencySqlTests(SqlDatabaseFixture database)
{
    [Fact]
    public async Task Concurrent_authority_bindings_have_one_winner_and_a_stable_conflict_without_partial_loser_changes()
    {
        var scenario = new ErpScenario(database);
        await scenario.InitializeAsync();
        var first = await CreateUnboundPartnerAsync(scenario, scenario.Tenant);
        var second = await CreateUnboundPartnerAsync(scenario, scenario.Tenant);
        await using var connectionSource = scenario.Db();
        var rendezvous = new BindingSaveRendezvous(scenario.Account);
        await using var firstContext = ConcurrentContext(connectionSource, scenario.Tenant, rendezvous);
        await using var secondContext = ConcurrentContext(connectionSource, scenario.Tenant, rendezvous);

        var attempts = await Task.WhenAll(
            BindAsync(firstContext, scenario, first, "binding-race-first"),
            BindAsync(secondContext, scenario, second, "binding-race-second"));

        // Both preflight queries have completed before either real UPDATE can reach SQL.
        Assert.Equal(2, rendezvous.Arrivals);
        Assert.NotEqual(firstContext.ContextId, secondContext.ContextId);
        var winner = Assert.Single(attempts, attempt => attempt.Error is null);
        var loser = Assert.Single(attempts, attempt => attempt.Error is not null);
        await using var verify = scenario.Db();
        var persisted = await verify.BusinessPartners.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, persisted.Length);
        Assert.Equal(winner.Before.Id, Assert.Single(persisted, row => row.CrmAccountId == scenario.Account).Id);
        AssertSaved(Assert.Single(persisted, row => row.Id == winner.Before.Id), winner, scenario);
        AssertUnchanged(Assert.Single(persisted, row => row.Id == loser.Before.Id), loser.Before);
        var loserAuditIds = await AuditIdsAsync(verify, loser.Before.Id);
        Assert.Equal(loser.Before.AuditIds, loserAuditIds);
        Assert.Equal(winner.Before.AuditIds.Length + 1, (await AuditIdsAsync(verify, winner.Before.Id)).Length);

        // Check the actual domain outcome after checking the database, so RED also proves
        // the unique SQL constraint preserved the losing row despite its unmapped exception.
        var conflict = Assert.IsType<ErpCommerceException>(loser.Error);
        Assert.Equal("C03_ACCOUNT_ALREADY_BOUND", conflict.Code);
    }

    [Fact]
    public async Task Concurrent_authority_bindings_of_the_same_account_in_different_tenants_both_succeed()
    {
        var scenario = new ErpScenario(database);
        await scenario.InitializeAsync();
        var first = await CreateUnboundPartnerAsync(scenario, scenario.Tenant, scenario.PartnerKey);
        var second = await CreateUnboundPartnerAsync(scenario, scenario.OtherTenant, scenario.PartnerKey);
        await using var connectionSource = scenario.Db();
        var rendezvous = new BindingSaveRendezvous(scenario.Account);
        await using var firstContext = ConcurrentContext(connectionSource, scenario.Tenant, rendezvous);
        await using var secondContext = ConcurrentContext(connectionSource, scenario.OtherTenant, rendezvous);

        var attempts = await Task.WhenAll(
            BindAsync(firstContext, scenario, first, "binding-tenant-first"),
            BindAsync(secondContext, scenario, second, "binding-tenant-second"));

        Assert.Equal(2, rendezvous.Arrivals);
        Assert.All(attempts, attempt => Assert.Null(attempt.Error));
        foreach (var attempt in attempts)
        {
            await using var verify = scenario.Db(attempt.Before.TenantId);
            var persisted = Assert.Single(await verify.BusinessPartners.AsNoTracking().ToArrayAsync());
            AssertSaved(persisted, attempt, scenario);
        }
    }

    private static CP6Context ConcurrentContext(CP6Context connectionSource, Guid tenant,
        BindingSaveRendezvous rendezvous) => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(connectionSource.Database.GetConnectionString()
                ?? throw new InvalidOperationException("C03_SQL_NOT_INITIALIZED"), sql => sql.CommandTimeout(60))
            .AddInterceptors(rendezvous).Options,
        new TenantContext { CurrentTenantId = tenant });

    private static async Task<PartnerBefore> CreateUnboundPartnerAsync(ErpScenario scenario, Guid tenant,
        string? key = null)
    {
        key ??= "BP" + Guid.NewGuid().ToString("N")[..10];
        await using var db = scenario.Db(tenant);
        await new BusinessPartnerService(db).CreateAsync(new()
        {
            BpCd = key, BpName = "Concurrent binding SQL customer", BaseCd = "B01", CustomerFlg = true,
            AccountsReceivableCd = "AR001", SalesStaffCd = "S01", BusinessStaffCd = "S02"
        }, "binding-baseline", preRegister: true);
        var row = await db.BusinessPartners.AsNoTracking().SingleAsync(partner => partner.BpCd == key);
        Assert.Null(row.CrmAccountId);
        return new(row.Id, tenant, row.BpCd, row.CrmAccountId, row.CurrencyCd, row.IsFrozen,
            row.Modifier, row.ModifyDate, ErpScenario.Version(row), await AuditIdsAsync(db, row.Id));
    }

    private static Task<Guid[]> AuditIdsAsync(CP6Context db, Guid partnerId)
        => db.Sys_FieldAuditLogs.AsNoTracking().Where(audit => audit.EntityName == nameof(BusinessPartner) &&
            audit.EntityKey == partnerId.ToString()).OrderBy(audit => audit.Id).Select(audit => audit.Id).ToArrayAsync();

    private static async Task<BindingAttempt> BindAsync(CP6Context db, ErpScenario scenario,
        PartnerBefore before, string actor)
    {
        var error = await Record.ExceptionAsync(() => new ErpCommerceAuthority(db, scenario.Clock)
            .SetBusinessPartnerProfileAsync(before.Key,
                new(before.RowVersion, scenario.Account, "USD", true), actor));
        return new(before, actor, error);
    }

    private static void AssertSaved(BusinessPartner row, BindingAttempt attempt, ErpScenario scenario)
    {
        Assert.Equal(attempt.Before.Id, row.Id);
        Assert.Equal(attempt.Before.TenantId, row.TenantId);
        Assert.Equal(scenario.Account, row.CrmAccountId);
        Assert.Equal("USD", row.CurrencyCd);
        Assert.True(row.IsFrozen);
        Assert.Equal(attempt.Actor, row.Modifier);
        Assert.Equal(scenario.Clock.GetUtcNow().UtcDateTime, row.ModifyDate);
        Assert.NotEqual(attempt.Before.RowVersion, ErpScenario.Version(row));
    }

    private static void AssertUnchanged(BusinessPartner row, PartnerBefore before)
    {
        Assert.Equal(before.AccountId, row.CrmAccountId);
        Assert.Equal(before.Currency, row.CurrencyCd);
        Assert.Equal(before.Frozen, row.IsFrozen);
        Assert.Equal(before.Modifier, row.Modifier);
        Assert.Equal(before.ModifiedAt, row.ModifyDate);
        Assert.Equal(before.RowVersion, ErpScenario.Version(row));
    }

    private sealed record PartnerBefore(Guid Id, Guid TenantId, string Key, Guid? AccountId, string? Currency,
        bool Frozen, string? Modifier, DateTime? ModifiedAt, byte[] RowVersion, Guid[] AuditIds);
    private sealed record BindingAttempt(PartnerBefore Before, string Actor, Exception? Error);

    private sealed class BindingSaveRendezvous(Guid account) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => Volatile.Read(ref arrivals);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries<BusinessPartner>()
                .Where(entry => entry.State == EntityState.Modified && entry.Entity.CrmAccountId == account &&
                    entry.Property(row => row.CrmAccountId).IsModified).ToArray();
            // CP6Context also saves the audit rows after the business UPDATE. Only the
            // first save, which contains the pending account binding, joins this barrier.
            if (entries.Length == 0) return result;
            Assert.Single(entries);
            var arrived = Interlocked.Increment(ref arrivals);
            Assert.InRange(arrived, 1, 2);
            if (arrived == 2) released.TrySetResult();
            // No business UPDATE has run at this point, so neither caller holds the target
            // row lock. A bounded wait prevents an early failure from stranding its peer.
            await released.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }
}
