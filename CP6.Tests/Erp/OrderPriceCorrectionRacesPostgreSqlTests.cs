using System.Data.Common;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class CommandHook : DbCommandInterceptor
    {
        internal Func<DbCommand, Task>? Before, After;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (Before is { } before) await before(command); return result; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        { if (After is { } after) await after(command); return result; }
    }

    [CurrentPermissionPostgresFact]
    public async Task Committed_raw_revoke_before_root_rejects_even_after_an_earlier_source_allow()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook);
        var source = await new CurrentFieldPermissionSourceReaderV1(h.Db, new HttpContextAccessor { HttpContext = h.Http },
            h.Catalog.Capture).ReadAsync("order");
        Assert.Single(source.Roles);
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("cp6_authentication_v1.acquire()")) return;
            hook.Before = null;
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            Assert.Equal(1, await writer.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72"));
        };
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(), null))).Message);
        Assert.Equal(0, await Intents(h)); Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.Equal(3m, d.IndividualUnitPrice));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_root_retains_raw_revoker_until_atomic_commit_and_next_intent_is_denied()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook);
        Task? revoker = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return;
            hook.After = null;
            revoker = Task.Run(async () =>
            {
                await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                Assert.Equal(1, await writer.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72"));
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(revoker.IsCompleted);
        };
        try { Assert.Equal(1, (await h.Service.BatchUpdatePriceAsync(h.Request(), null)).UpdatedCount); }
        finally { if (revoker is not null) await revoker; }
        Assert.True(blocked); Assert.Equal(1, await Intents(h)); h.Db.ChangeTracker.Clear();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(8m), null))).Message);
        Assert.Equal(4m, await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 1).Select(d => d.IndividualUnitPrice).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Final_source_fault_after_save_rolls_back_rows_and_intents_without_cached_allow()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var reads = 0;
        hook.Before = command =>
        {
            if (command.CommandText.Contains("WITH field_subject") && ++reads == 2)
                throw new IOException("owned final source failure");
            return Task.CompletedTask;
        };
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(), null))).Message);
        Assert.Equal(2, reads); Assert.Equal(0, await Intents(h));
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.Equal(3m, d.IndividualUnitPrice));
        Assert.False(h.Db.ChangeTracker.HasChanges());
    }

    [CurrentPermissionPostgresFact]
    public async Task Deleted_sibling_cannot_be_reactivated_inside_an_already_checked_propagation_set()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook);
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 2).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsDeleted, true));
        Task? resurrection = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("\"T_OrderDetail\"") || !command.CommandText.Contains("FOR UPDATE")) return;
            hook.After = null;
            resurrection = Task.Run(async () =>
            {
                await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                Assert.Equal(1, await writer.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE public.\"T_OrderDetail\" SET \"IsDeleted\"=false WHERE \"TenantId\"={h.Tenant} AND \"WebOrderDetailNo\"=2"));
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE 'UPDATE public."T_OrderDetail"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(resurrection.IsCompleted);
        };
        var request = h.Request(); request.Items[0].SetUnitPriceAfter = 9m;
        try { Assert.Equal(1, (await h.Service.BatchUpdatePriceAsync(request, null)).UpdatedCount); }
        finally { if (resurrection is not null) await resurrection; }
        Assert.True(blocked); h.Db.ChangeTracker.Clear(); Assert.Equal(1, await Intents(h));
        var later = await h.Db.OrderDetails.SingleAsync(d => d.WebOrderDetailNo == 2);
        Assert.False(later.IsDeleted); Assert.Null(later.SetUnitPrice); // Reactivation ordered after the correction.
    }

    [CurrentPermissionPostgresFact]
    public async Task Final_original_session_denial_and_cancellation_cannot_commit_prepared_business()
    {
        await using var h = await Seed(); var checks = 0;
        var owner = new OrderPriceCorrectionOwnerV1(h.Db, new HttpContextAccessor { HttpContext = h.Http }, _ =>
        {
            if (++checks == 3) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
            return Task.FromResult(h.User);
        }, h.Catalog.Capture);
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ExecuteAsync(h.Request()))).Message);
        Assert.Equal(3, checks); Assert.Equal(0, await Intents(h));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Owner.ExecuteAsync(h.Request(), cancellation.Token));
        Assert.Equal(0, await Intents(h));
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.Equal(3m, d.IndividualUnitPrice));
    }

    private sealed class CommitFault : DbTransactionInterceptor
    {
        internal bool Armed;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { if (Armed) { Armed = false; throw new IOException("owned lost commit acknowledgement"); } return Task.CompletedTask; }
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_clock_expiry_during_final_source_read_rolls_back_despite_old_query_time_parameter()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook);
        var expires = DateTime.Now.AddSeconds(2);
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpireDate, expires));
        var reads = 0;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject") || ++reads != 2) return;
            while (DateTime.Now <= expires) await Task.Delay(20);
        };
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(), null))).Message);
        Assert.Equal(2, reads); Assert.Equal(0, await Intents(h));
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.Equal(3m, d.IndividualUnitPrice));
    }

    [CurrentPermissionPostgresFact]
    public async Task Lost_commit_acknowledgement_reports_unknown_and_never_automatically_replays()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); fault.Armed = true;
        Assert.Equal("PRICE_CORRECTION_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(), null))).Message);
        Assert.Equal(1, await Intents(h));
        Assert.Equal(4m, await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 1).Select(d => d.IndividualUnitPrice).SingleAsync());
        Assert.Equal(0, h.Workflow.Calls);
    }
}
