using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W687_completion_finance_actual_HTTP_collects_and_settles_after_backflush()
    {
        await using var h = await Seed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        await GrantLegacyAuthorityAction(h, "fin-cost", "settle");
        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);
        using var response = await CompleteForFinance(host, cookies, number);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal(WorkOrderStatus.Completed, await h.Db.WorkOrders.Where(x => x.WorkOrderNo == number).Select(x => x.Status).SingleAsync());
        Assert.Equal(2.06172839m, await h.Db.WorkOrderMaterials.Where(x => x.WorkOrderNo == number).Select(x => x.ActualQty).SingleAsync());
        Assert.Single(await h.Db.StockTransactions.Where(x => x.RelatedNo == number && x.RelatedType == "ISSUE").ToListAsync());
        var evt = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceNo == number && x.TargetModule == "FIN");
        Assert.True(evt.Status == IntegrationEventStatus.Success, evt.LastError);
        var completion = await h.Db.ProductionResults.AsNoTracking().SingleAsync(x => x.WorkOrderNo == number);
        Assert.Equal(completion.Id, evt.Id);
        var sheet = await h.Db.CostSheets.AsNoTracking().SingleAsync(x => x.WorkOrderNo == number);
        Assert.Equal((CostSheetStatus.Settled, 11.71m, 263.16m, 56.96m), (sheet.Status, sheet.MaterialActual, sheet.LaborActual, sheet.OverheadActual));
        Assert.Equal(sheet.No, evt.TargetNo);
        var journals = await h.Db.JournalEntries.AsNoTracking().Where(x => x.Source == VoucherSource.Cost).ToListAsync();
        Assert.Equal(3, journals.Count);
        Assert.All(journals, item => Assert.Equal(JournalStatus.Posted, item.Status));
        using var repeated = await CompleteForFinance(host, cookies, number);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal(3, await h.Db.JournalEntries.CountAsync(x => x.Source == VoucherSource.Cost));
        Assert.Single(await h.Db.IntegrationEvents.Where(x => x.SourceNo == number && x.TargetModule == "FIN").ToListAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("collect")]
    [InlineData("settle")]
    public async Task W687_completion_finance_needs_both_finance_actions(string missing)
    {
        await using var h = await Seed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        if (missing == "collect")
        {
            await GrantLegacyAuthorityAction(h, "fin-cost", "settle");
            var id = await h.Db.Sys_Menus.Where(x => x.MenuKey == "fin-cost").Select(x => x.MenuId).SingleAsync();
            await h.Db.Sys_RoleActions.Where(x => x.MenuId == id && x.ActionCode == "collect").ExecuteDeleteAsync();
        }
        await using var host = await StartIsolatedCompletionApi(h);
        var cookies = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, h.Tenant);
        using var response = await CompleteForFinance(host, cookies, number);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal(WorkOrderStatus.Completed, await h.Db.WorkOrders.Where(x => x.WorkOrderNo == number).Select(x => x.Status).SingleAsync());
        Assert.Equal(2.06172839m, await h.Db.WorkOrderMaterials.Where(x => x.WorkOrderNo == number).Select(x => x.ActualQty).SingleAsync());
        var evt = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceNo == number && x.TargetModule == "FIN");
        Assert.Equal((IntegrationEventStatus.DeadLetter, "ACTION_FORBIDDEN"), (evt.Status, evt.LastError));
        Assert.Null(evt.NextRetryAt);
        Assert.Equal(before, await ReadCostSettlement(observer, h.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task W687_completion_finance_third_gl_leg_rolls_back_and_finance_can_recover_manually()
    {
        await using var h = await Seed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        await GrantLegacyAuthorityAction(h, "fin-cost", "settle");
        await h.Db.GlAccounts.Where(x => x.Role == "COGS").ExecuteUpdateAsync(update => update.SetProperty(x => x.IsLeaf, false));
        await using var host = await StartIsolatedCompletionApi(h);
        var cookies = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, h.Tenant);
        using var completed = await CompleteForFinance(host, cookies, number);
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var evt = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceNo == number && x.TargetModule == "FIN");
        Assert.Equal((IntegrationEventStatus.DeadLetter, "E-FIN-104"), (evt.Status, evt.LastError));
        Assert.Null(evt.NextRetryAt);
        Assert.Equal(before, await ReadCostSettlement(observer, h.Tenant));
        Assert.Equal(2.06172839m, await h.Db.WorkOrderMaterials.Where(x => x.WorkOrderNo == number).Select(x => x.ActualQty).SingleAsync());

        await h.Db.GlAccounts.Where(x => x.Role == "COGS").ExecuteUpdateAsync(update => update.SetProperty(x => x.IsLeaf, true));
        using var collected = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { }));
        Assert.True(collected.StatusCode == HttpStatusCode.OK, await collected.Content.ReadAsStringAsync());
        using var settled = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/settle"));
        Assert.True(settled.StatusCode == HttpStatusCode.OK, await settled.Content.ReadAsStringAsync());
        var sheet = await h.Db.CostSheets.AsNoTracking().SingleAsync(x => x.WorkOrderNo == number);
        Assert.Equal(CostSheetStatus.Settled, sheet.Status);
        Assert.Equal(3, await h.Db.JournalEntries.CountAsync(x => x.Source == VoucherSource.Cost));
        Assert.Equal(evt.Id, await h.Db.IntegrationEvents.Where(x => x.SourceNo == number && x.TargetModule == "FIN").Select(x => x.Id).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task W687_completion_finance_zero_keeps_the_collected_sheet_without_a_journal()
    {
        await using var h = await Seed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        await GrantLegacyAuthorityAction(h, "fin-cost", "settle");
        await h.Db.ProductMaterials.ExecuteUpdateAsync(update => update.SetProperty(x => x.SupplyPrice, (decimal?)0m));
        await h.Db.ProcessCostRates.ExecuteUpdateAsync(update => update.SetProperty(x => x.LaborRate, 0m).SetProperty(x => x.OverheadRate, 0m));
        await using var host = await StartIsolatedCompletionApi(h);
        var cookies = await Login(host, h);
        using var response = await CompleteForFinance(host, cookies, number);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var sheet = await h.Db.CostSheets.AsNoTracking().SingleAsync(x => x.WorkOrderNo == number);
        Assert.Equal((CostSheetStatus.Collected, 0m), (sheet.Status, sheet.TotalActual));
        Assert.False(await h.Db.JournalEntries.AnyAsync());
        var evt = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceNo == number && x.TargetModule == "FIN");
        Assert.Equal((IntegrationEventStatus.Skipped, "zero total cost"), (evt.Status, evt.LastError));
        Assert.Null(evt.NextRetryAt);
    }

    [CurrentPermissionPostgresFact]
    public async Task W687_completion_finance_legacy_worker_stops_the_same_event_without_new_attempt_rows()
    {
        await using var h = await Seed();
        var row = new IntegrationEvent
        {
            SourceModule = "MES", TargetModule = "FIN", HookName = nameof(IFinBridgeHook.OnWorkOrderCompletedAsync),
            SourceNo = "W687-LEGACY", Creator = "price-owner", Status = IntegrationEventStatus.Failed,
            LastError = "FINANCE_OWNER_REQUIRED", Attempts = 1, NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
            PayloadJson = JsonSerializer.Serialize(new { workOrderNo = "W687-LEGACY", userName = "price-owner" }),
        };
        h.Db.IntegrationEvents.Add(row);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        using var services = W620Services(h.Tenant);
        await W620Worker(services).ProcessOnceAsync();
        await W620Worker(services).ProcessOnceAsync();
        var rows = await h.Db.IntegrationEvents.AsNoTracking().Where(x => x.TargetModule == "FIN" && x.SourceNo == row.SourceNo).ToListAsync();
        Assert.True(rows.Count == 1, $"Stored {rows.Count} finance events for the original completion event");
        var saved = rows.Single();
        Assert.Equal(row.Id, saved.Id);
        Assert.Equal(IntegrationEventStatus.DeadLetter, saved.Status);
        Assert.Null(saved.NextRetryAt);
        Assert.Equal(1, saved.Attempts);
        Assert.False(await h.Db.CostSheets.AnyAsync());
        Assert.False(await h.Db.JournalEntries.AnyAsync());
    }

    private static async Task<string> SeedCompletedFinanceFlow(Harness h)
    {
        var number = await SeedNativeCostCollect(h);
        await GrantLegacyAuthorityAction(h, "mes-production-result", "complete");
        await new GlAccountService(h.Db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        await h.Db.WorkOrders.Where(x => x.WorkOrderNo == number)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, WorkOrderStatus.InProgress)
                .SetProperty(x => x.CompletedQty, 0m).SetProperty(x => x.ActualEndDate, (DateTime?)null));
        await h.Db.WorkOrderProcesses.Where(x => x.WorkOrderNo == number && x.ProcessCd == "PRINT")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ProcessStatus, 2));
        await h.Db.WorkOrderProcesses.Where(x => x.WorkOrderNo == number && x.ProcessCd == "PACK")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ProcessStatus, 1).SetProperty(x => x.IsHourOverridden, true));
        await h.Db.WorkOrderMaterials.Where(x => x.WorkOrderNo == number)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ActualQty, 0m));
        await h.Db.ProductMaterials.ExecuteUpdateAsync(update => update.SetProperty(x => x.UnitUsage, (decimal?)0.5m)
            .SetProperty(x => x.UsageUnit, "EA"));
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-A" && x.ValidTo == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LaborRate, 123.45678901m)
                .SetProperty(x => x.OverheadRate, 45.12345678m));
        h.Db.Warehouses.Add(new() { WarehouseCd = "W01", WarehouseName = "Materials", Creator = "price-owner" });
        h.Db.Locations.Add(new() { WarehouseCd = "W01", LocationCd = "W01-RM", Creator = "price-owner" });
        h.Db.Stocks.Add(new() { WarehouseCd = "W01", LocationCd = "W01-RM", ProductCd = "PAPER", LotNo = "",
            UnitCd = "EA", OwnerType = StockOwnerType.Self, PhysicalQty = 10m, UnitPrice = 5.67890123m, Creator = "price-owner" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return number;
    }

    private static Task<HttpResponseMessage> CompleteForFinance(AuthenticationApiProcess host, Dictionary<string, string> cookies, string number)
        => host.Send(OperationsRequest(cookies, HttpMethod.Post, "/api/mes/production-results/complete",
            new { WorkOrderNo = number, ProcessCd = "PACK", TaskCd = "2", OperatorCd = "price-owner", GoodQty = 4.12345678m }));

    private static Task<AuthenticationApiProcess> StartIsolatedCompletionApi(Harness h)
    {
        var environment = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
        {
            ["StockFinBridge__Enabled"] = "false",
            ["IntegrationEvent__Enabled"] = "false",
        };
        return AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
    }
}
