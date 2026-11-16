using System.IdentityModel.Tokens.Jwt;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W688_completion_recovery_after_backflush_with_two_restarted_hosts()
        => await RunCompletionRecovery("finance", null, true);

    [TenantManagementPostgresTheory]
    [InlineData("material", null)]
    [InlineData("partial", null)]
    [InlineData("finance", "settle")]
    [InlineData("finance", "session")]
    public async Task W688_completion_recovery_resumes_the_durable_step_with_current_authority(string boundary, string? revoke)
        => await RunCompletionRecovery(boundary, revoke, false);

    private async Task RunCompletionRecovery(string boundary, string? revoke, bool race)
    {
        await using var h = await Seed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        await GrantLegacyAuthorityAction(h, "fin-cost", "settle");
        var pauseMaterial = boundary is "material" or "partial";
        if (boundary == "partial")
        {
            var product = await h.Db.WorkOrders.Where(x => x.WorkOrderNo == number).Select(x => x.ProductCd).SingleAsync();
            h.Db.ProductMaterials.Add(new()
            {
                ProductCd = product, ProcessCd = "PRINT", MaterialCd = "PAPER-2",
                UnitUsage = 0.5m, UsageUnit = "EA", SupplyPrice = 1.23m,
            });
            h.Db.Stocks.Add(new()
            {
                WarehouseCd = "W01", LocationCd = "W01-RM", ProductCd = "PAPER-2", LotNo = "", UnitCd = "EA",
                OwnerType = CP6.Entity.DomainModels.Wms.StockOwnerType.Self, PhysicalQty = 10m, UnitPrice = 1.23m, Creator = "price-owner",
            });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, h.Tenant);
        var producer = $"w688-producer-{boundary}-{h.Tenant:N}";
        await using var host = await StartCompletionRecoveryHost(h, producer, false);
        var cookies = await Login(host, h);
        var jti = new JwtSecurityTokenHandler().ReadJwtToken(cookies["cp6_at"]).Id;

        // Only the post-commit step blocks. The other connection can still read the confirmed MES result.
        await using (var blocker = new NpgsqlConnection(h.Db.Database.GetConnectionString()))
        {
            await blocker.OpenAsync();
            if (pauseMaterial)
            {
                await using var setup = new NpgsqlCommand("""
                    CREATE FUNCTION public.w688_pause_material() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                      IF current_setting('application_name') LIKE 'w688-producer-%' AND NEW."RelatedType"='ISSUE'
                         AND (current_setting('application_name') LIKE 'w688-producer-material-%'
                             OR EXISTS (SELECT 1 FROM public."T_StockTransaction" AS previous
                                 WHERE previous."TenantId"=NEW."TenantId" AND previous."RelatedNo"=NEW."RelatedNo"
                                   AND previous."RelatedType"='ISSUE')) THEN
                        PERFORM pg_advisory_xact_lock(688017);
                      END IF;
                      RETURN NEW;
                    END;
                    $$;
                    CREATE TRIGGER w688_pause_material BEFORE INSERT ON public."T_StockTransaction"
                    FOR EACH ROW EXECUTE FUNCTION public.w688_pause_material();
                    SELECT pg_advisory_lock(688017);
                    """, blocker);
                await setup.ExecuteNonQueryAsync();
            }
            else
            {
                await using var statement = new NpgsqlCommand(
                    """BEGIN; LOCK TABLE public."Fin_CostSheet" IN ACCESS EXCLUSIVE MODE;""", blocker);
                await statement.ExecuteNonQueryAsync();
            }

            var complete = CompleteForFinance(host, cookies, number);
            try
            {
                await WaitForCompletionRecoveryCondition(async token =>
                {
                    await using var waiting = new NpgsqlCommand("""
                        SELECT EXISTS(SELECT 1 FROM pg_stat_activity
                        WHERE application_name=@app AND wait_event_type='Lock'
                          AND (query LIKE '%Fin_CostSheet%' OR query LIKE '%INSERT INTO%T_StockTransaction%'))
                        """, observer);
                    waiting.Parameters.AddWithValue("app", producer);
                    return (bool)(await waiting.ExecuteScalarAsync(token))!;
                });
                Assert.Equal(WorkOrderStatus.Completed, await h.Db.WorkOrders.Where(x => x.WorkOrderNo == number)
                    .Select(x => x.Status).SingleAsync());
                Assert.Equal(boundary == "material" ? 0m : 2.06172839m,
                    await h.Db.WorkOrderMaterials.Where(x => x.WorkOrderNo == number).SumAsync(x => x.ActualQty));
            }
            finally
            {
                await host.StopAsync();
                var interrupted = await Record.ExceptionAsync(async () => { using var response = await complete; });
                var sql = pauseMaterial
                    ? """SELECT pg_advisory_unlock(688017); DROP TRIGGER w688_pause_material ON public."T_StockTransaction"; DROP FUNCTION public.w688_pause_material();"""
                    : "ROLLBACK;";
                await using var release = new NpgsqlCommand(sql, blocker);
                await release.ExecuteNonQueryAsync();
                Assert.IsAssignableFrom<HttpRequestException>(interrupted);
            }
        }

        var completionId = await h.Db.ProductionResults.Where(x => x.WorkOrderNo == number).Select(x => x.Id).SingleAsync();
        var pending = await h.Db.IntegrationEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == completionId);
        Assert.True(pending?.Status == IntegrationEventStatus.Pending, "Committed completion has no durable pending finance event.");
        Assert.Equal(before, await ReadCostSettlement(observer, h.Tenant));

        if (revoke == "settle")
        {
            var menuId = await h.Db.Sys_Menus.Where(x => x.MenuKey == "fin-cost").Select(x => x.MenuId).SingleAsync();
            await h.Db.Sys_RoleActions.Where(x => x.MenuId == menuId && x.ActionCode == "settle").ExecuteDeleteAsync();
        }
        if (revoke == "session")
        {
            await using var logout = new NpgsqlCommand("""
                UPDATE cp6_authentication_v1.access_sessions SET revoked_at=clock_timestamp() WHERE access_jti=@jti
                """, observer);
            logout.Parameters.AddWithValue("jti", jti);
            Assert.Equal(1, await logout.ExecuteNonQueryAsync());
        }

        await using var peer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await peer.OpenAsync();
        await using var gate = await peer.BeginTransactionAsync();
        if (race)
        {
            await using var locked = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire()", peer, gate);
            await locked.ExecuteNonQueryAsync();
        }
        var firstApp = $"w688-worker-a-{h.Tenant:N}";
        var secondApp = $"w688-worker-b-{h.Tenant:N}";
        await using var restarted = await StartCompletionRecoveryHost(h, firstApp, true);
        await using var competing = race ? await StartCompletionRecoveryHost(h, secondApp, true) : null;
        Assert.NotEqual(host.ProcessId, restarted.ProcessId);
        if (race)
        {
            await WaitForCompletionRecoveryCondition(async token =>
            {
                await using var waiting = new NpgsqlCommand("""
                    SELECT count(DISTINCT application_name)::int FROM pg_stat_activity
                    WHERE application_name IN (@first, @second) AND wait_event_type='Lock'
                      AND query LIKE '%cp6_authentication_v1.acquire()%'
                    """, observer);
                waiting.Parameters.AddWithValue("first", firstApp);
                waiting.Parameters.AddWithValue("second", secondApp);
                return (int)(await waiting.ExecuteScalarAsync(token))! == 2;
            });
        }
        await gate.CommitAsync();

        await WaitForCompletionRecoveryCondition(token => h.Db.IntegrationEvents.AnyAsync(x => x.Id == completionId
            && x.Status != IntegrationEventStatus.Pending, token));
        var outcome = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.Id == completionId);
        Assert.Null(outcome.NextRetryAt);
        Assert.Equal(1, outcome.Attempts);
        Assert.Single(await h.Db.IntegrationEvents.Where(x => x.SourceNo == number && x.TargetModule == "FIN").ToListAsync());
        var issues = await h.Db.StockTransactions.Where(x => x.RelatedNo == number && x.RelatedType == "ISSUE").ToListAsync();
        var materials = await h.Db.WorkOrderMaterials.Where(x => x.WorkOrderNo == number).ToListAsync();
        Assert.Equal(boundary == "partial" ? 2 : 1, issues.Count);
        Assert.Equal(issues.Count, materials.Count);
        Assert.All(materials, item => Assert.Equal(2.06172839m, item.ActualQty));
        if (revoke == null)
        {
            Assert.True(outcome.Status == IntegrationEventStatus.Success, outcome.LastError);
            var sheet = await h.Db.CostSheets.AsNoTracking().SingleAsync(x => x.WorkOrderNo == number);
            var materialCost = boundary == "partial" ? 14.25m : 11.71m;
            Assert.Equal((CostSheetStatus.Settled, materialCost, 263.16m, 56.96m), (sheet.Status, sheet.MaterialActual, sheet.LaborActual, sheet.OverheadActual));
            Assert.Equal("price-owner", sheet.Creator);
            Assert.Equal(sheet.No, outcome.TargetNo);
            Assert.Equal(3, await h.Db.JournalEntries.CountAsync(x => x.Source == VoucherSource.Cost && x.Status == JournalStatus.Posted));
        }
        else
        {
            Assert.Equal(IntegrationEventStatus.DeadLetter, outcome.Status);
            Assert.Contains(outcome.LastError, revoke == "session" ? ["REAUTH_REQUIRED"] : new[] { "ACTION_FORBIDDEN", "AUTHORITY_CHANGED" });
            Assert.Equal(before, await ReadCostSettlement(observer, h.Tenant));
        }
    }

    private static Task<AuthenticationApiProcess> StartCompletionRecoveryHost(Harness h, string name, bool runWorker)
    {
        var connection = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { ApplicationName = name };
        var environment = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
        {
            ["StockFinBridge__Enabled"] = "false",
            ["Startup__SkipHostedServices"] = runWorker ? "false" : "true",
            ["IntegrationEvent__Enabled"] = runWorker ? "true" : "false",
            ["IntegrationEvent__PollIntervalSeconds"] = "1",
        };
        return AuthenticationApiProcess.Start(connection.ConnectionString, environment);
    }

    private static async Task WaitForCompletionRecoveryCondition(Func<CancellationToken, Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition(timeout.Token))
            await Task.Delay(20, timeout.Token);
    }
}
