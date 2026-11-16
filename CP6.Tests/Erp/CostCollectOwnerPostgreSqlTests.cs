using System.Net;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly DateTime CostCollectEnd = new(2026, 6, 30, 16, 30, 0);

    [CurrentPermissionPostgresFact]
    public async Task W686_cost_collect_actual_HTTP_values_work_order_hours_at_effective_rates()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);

        using var result = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect",
            new { laborStd = 999m, overheadStd = 888m }));
        Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        Assert.True(result.Headers.CacheControl?.NoStore);

        var sheet = await h.Db.CostSheets.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.WorkOrderNo == number);
        Assert.Equal(CostSheetStatus.Collected, sheet.Status);
        Assert.Equal("price-owner", sheet.Creator);
        Assert.Equal(4.12345678m, sheet.CompletedQty);
        Assert.Equal(5, sheet.Lines.Count);
        var labor = Assert.Single(sheet.Lines, line => line.Element == CostElement.Labor && line.ProcessCd == "PRINT");
        Assert.Equal((2.12345678m, 3.46759259m, 123.45678901m, 262.16m, 428.10m),
            (labor.Hours, labor.StandardHours, labor.UnitPrice, labor.ActualAmount, labor.StandardAmount));
        Assert.Equal(new DateTime(2026, 6, 1), labor.RateValidFrom);
        var overhead = Assert.Single(sheet.Lines, line => line.Element == CostElement.Overhead && line.ProcessCd == "PRINT");
        Assert.Equal((1.23456789m, 45.12345678m, 55.71m), (overhead.Hours, overhead.UnitPrice, overhead.ActualAmount));
        Assert.Equal((18.23m, 263.16m, 56.96m), (sheet.MaterialActual, sheet.LaborActual, sheet.OverheadActual));
        Assert.Single(await h.Db.FinSequences.AsNoTracking().Where(x => x.SeqKey == "CS").ToListAsync());
        Assert.False(await h.Db.JournalEntries.AnyAsync());

        await h.Db.WorkOrderProcesses.Where(x => x.WorkOrderNo == number && x.ProcessCd == "PRINT")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ActualLaborHour, (decimal?)3m));
        var repeated = await Task.WhenAll(
            host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { })),
            host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { })));
        foreach (var repeat in repeated)
        {
            using (repeat)
                Assert.True(repeat.StatusCode == HttpStatusCode.OK, await repeat.Content.ReadAsStringAsync());
        }
        var recalculated = await h.Db.CostSheets.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.WorkOrderNo == number);
        Assert.Equal((sheet.Id, sheet.No, 5, 371.37m), (recalculated.Id, recalculated.No, recalculated.Lines.Count, recalculated.LaborActual));
        Assert.Equal(1, await h.Db.FinSequences.Where(x => x.SeqKey == "CS").Select(x => x.CurrentValue).SingleAsync());

        await h.Db.CostSheets.Where(x => x.Id == sheet.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, CostSheetStatus.Settled));
        using var settled = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { }));
        Assert.Equal(HttpStatusCode.BadRequest, settled.StatusCode);
        Assert.Contains("E-FIN-402", await settled.Content.ReadAsStringAsync());
        Assert.Equal(371.37m, await h.Db.CostSheets.Where(x => x.Id == sheet.Id).Select(x => x.LaborActual).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task W686_cost_collect_checks_current_grants_and_actual_rate_object_before_writes()
    {
        await using var h = await Seed();
        await using var other = await Seed();
        var number = await SeedNativeCostCollect(h);
        var foreign = await SeedNativeCostCollect(other);
        await GrantNativeCostCollect(h);
        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, h.Tenant);

        await Denied(foreign, "OBJECT_FORBIDDEN");
        await h.Db.WorkOrders.Where(x => x.WorkOrderNo == number)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Creator, "other-operator"));
        await Denied(number, "OBJECT_FORBIDDEN");
        await h.Db.WorkOrders.Where(x => x.WorkOrderNo == number)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Creator, "price-owner"));
        foreach (var hidden in new[] { "LaborRate", "OverheadRate" })
        {
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "mes-process-cost-rate", FieldName = hidden, Access = 3 });
            await h.Db.SaveChangesAsync();
            await Denied(number, "FIELD_READ_FORBIDDEN");
            await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "mes-process-cost-rate" && x.FieldName == hidden).ExecuteDeleteAsync();
            h.Db.ChangeTracker.Clear();
        }
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "mes-process-cost-rate")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScopeType, 1));
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-A" && x.ValidTo == CostCollectEnd)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ValidTo, (DateTime?)null).SetProperty(x => x.Creator, "outside-scope"));
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-A" && x.LaborRate == 9m)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ValidTo, (DateTime?)CostCollectEnd));
        await Denied(number, "OBJECT_FORBIDDEN");

        var menuId = await h.Db.Sys_Menus.Where(x => x.MenuKey == "fin-cost").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menuId && x.ActionCode == "collect").ExecuteDeleteAsync();
        await Denied(number, "fin-cost:collect", "ACTION_FORBIDDEN");
        Assert.Equal(before, await ReadCostSettlement(observer, h.Tenant));

        async Task Denied(string workOrder, params string[] reasons)
        {
            using var result = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{workOrder}/collect",
                new { laborStd = 100m, overheadStd = 30m }));
            var content = await result.Content.ReadAsStringAsync();
            Assert.True(result.StatusCode == HttpStatusCode.Forbidden, content);
            Assert.Contains(reasons, reason => content.Contains(reason, StringComparison.Ordinal));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W686_cost_collect_missing_rate_or_required_material_price_has_no_effect()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, h.Tenant);
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-B")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ValidTo, (DateTime?)CostCollectEnd.AddDays(-1)));
        await Fail("E-A2-RATE-002");
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-B")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ValidTo, (DateTime?)null));
        await h.Db.ProductMaterials.ExecuteUpdateAsync(update => update.SetProperty(x => x.SupplyPrice, (decimal?)null));
        await Fail("COST_MATERIAL_PRICE_REQUIRED");

        async Task Fail(string code)
        {
            using var result = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect",
                new { laborStd = 999m, overheadStd = 100m }));
            var content = await result.Content.ReadAsStringAsync();
            Assert.True(result.StatusCode == HttpStatusCode.BadRequest, content);
            Assert.Contains(code, content);
            Assert.Equal(before, await ReadCostSettlement(observer, h.Tenant));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W686_cost_collect_postgresql_rounds_once_and_labels_only_the_fallback_hour()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        await h.Db.WorkOrderProcesses.Where(x => x.WorkOrderNo == number && x.ProcessCd == "PRINT")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ActualLaborHour, (decimal?)90000000.00000001m)
                .SetProperty(x => x.ActualMachineHour, (decimal?)null).SetProperty(x => x.ActualHourSource, "Manual"));
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-A" && x.ValidTo == CostCollectEnd)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LaborRate, 100499999.99999999m));
        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);
        using var result = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { }));
        Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        var rows = await h.Db.CostSheetLines.AsNoTracking().Where(x => x.ProcessCd == "PRINT" && x.Element != CostElement.Material).ToListAsync();
        var labor = Assert.Single(rows, x => x.Element == CostElement.Labor);
        Assert.Equal(9045000000000000.10m, labor.ActualAmount);
        Assert.Equal((90000000.00000001m, 100499999.99999999m, "Manual"), (labor.Hours, labor.UnitPrice, labor.HourSource));
        Assert.Null(labor.WarningCode);
        var overhead = Assert.Single(rows, x => x.Element == CostElement.Overhead);
        Assert.Equal((2.31172839m, "StandardFallback", "W-A2-COST-001"), (overhead.Hours, overhead.HourSource, overhead.WarningCode));
    }

    [CurrentPermissionPostgresFact]
    public async Task W686_cost_collect_final_authority_failure_rolls_back_the_number_sheet_and_lines()
    {
        var hook = new CommandHook();
        await using var h = await Seed(hook);
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("fin-cost", "Cost", [5], 5); catalog.RegisterFields("fin-cost", []);
        catalog.RegisterScope("mes-work-order", "Work Order", [1, 2, 3, 4, 5], 1); catalog.RegisterFields("mes-work-order", []);
        catalog.RegisterScope("product", "Product", [1, 2, 3, 4, 5], 1); catalog.RegisterFields("product", []);
        catalog.RegisterScope("mes-process-cost-rate", "Rates", [1, 2, 3, 4, 5], 1);
        catalog.RegisterFields("mes-process-cost-rate", [new("LaborRate", "Labor"), new("OverheadRate", "Overhead")]);
        catalog.Seal();
        var owner = new OperationsOwnerV1(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
        var service = new CostCollectService(h.Db, new FinSequenceService(h.Db), new ProcessCostRateService(h.Db), owner);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var bare = await ReadCostSettlement(observer, h.Tenant);
        var wroteLines = false;
        hook.After = command =>
        {
            wroteLines |= command.CommandText.Contains("INSERT INTO \"Fin_CostSheetLine\"", StringComparison.Ordinal);
            return Task.CompletedTask;
        };
        hook.Before = command => wroteLines && command.CommandText.Contains("WITH field_subject", StringComparison.Ordinal)
            ? throw new IOException("Final authority read unavailable") : Task.CompletedTask;

        var refused = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CollectAsync(number, 0m, 0m, "not-the-actor"));
        Assert.True(wroteLines);
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", refused.Message);
        Assert.Equal(bare, await ReadCostSettlement(observer, h.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task W686_cost_collect_mes_callback_requires_an_independent_finance_command()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        var collect = new Mock<ICostCollectService>(MockBehavior.Strict);
        var settle = new Mock<ICostSettleService>(MockBehavior.Strict);
        var bridge = new FinBridgeHook(h.Db, Mock.Of<IArInvoiceService>(), collect.Object, settle.Object, NullLogger<FinBridgeHook>.Instance);

        var result = await bridge.OnWorkOrderCompletedAsync(number, "price-owner");
        Assert.False(result.Success);
        Assert.Equal("FINANCE_OWNER_REQUIRED", result.Message);
        Assert.Empty(collect.Invocations);
        Assert.Empty(settle.Invocations);
        var error = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceNo == number);
        Assert.Equal(IntegrationEventStatus.DeadLetter, error.Status);
        Assert.Null(error.NextRetryAt);
        Assert.Equal("FINANCE_OWNER_REQUIRED", error.LastError);
        Assert.False(await h.Db.CostSheets.AnyAsync());
        Assert.False(await h.Db.FinSequences.AnyAsync());
    }

    private static async Task GrantNativeCostCollect(Harness h)
    {
        await GrantLegacyAuthorityAction(h, "fin-cost", "collect");
        await GrantLegacyAuthorityAction(h, "mes-work-order", "query");
        await GrantProductIdentity(h);
        await GrantCost(h);
    }

    private static async Task<string> SeedNativeCostCollect(Harness h)
    {
        var suffix = h.Tenant.ToString("N");
        var number = "WC68" + suffix[..16];
        var product = "P68" + suffix[..12];
        const string actor = "price-owner";
        h.Db.ProductMasters.Add(new() { ProductCd = product, ItemCd = product, Creator = actor });
        h.Db.WorkOrders.Add(new()
        {
            WorkOrderNo = number, ProductCd = product, Creator = actor, Status = WorkOrderStatus.Completed,
            CompletedQty = 4.12345678m, PlanStartDate = new(2026, 5, 15), ActualEndDate = CostCollectEnd,
        });
        h.Db.WorkOrderProcesses.AddRange(
            new() { WorkOrderNo = number, ProcessCd = "PRINT", TaskCd = "1", WgCd = "W686-A", SortOrder = 1, Creator = actor,
                ActualLaborHour = 2.12345678m, ActualMachineHour = 1.23456789m },
            new() { WorkOrderNo = number, ProcessCd = "PACK", TaskCd = "2", WgCd = "W686-B", SortOrder = 2, Creator = actor,
                ActualLaborHour = 0.1m, ActualMachineHour = 0.4m });
        h.Db.ProductProcesses.AddRange(
            new() { ProductCd = product, ProcessCd = "PRINT", TaskCd = "1", SetupHour = 0.25m, CycleTime = 0.5m, StandardCrewSize = 1.5m },
            new() { ProductCd = product, ProcessCd = "PACK", TaskCd = "2", SetupHour = 0.1m, CycleTime = 0.2m, StandardCrewSize = 1m });
        h.Db.WorkOrderMaterials.Add(new()
        {
            WorkOrderNo = number, ProcessCd = "PRINT", MaterialCd = "PAPER", Creator = actor, PlanQty = 4.5m, ActualQty = 3.21098765m,
        });
        h.Db.ProductMaterials.Add(new() { ProductCd = product, ProcessCd = "PRINT", MaterialCd = "PAPER", SupplyPrice = 5.67890123m });
        foreach (var center in new[] { "W686-A", "W686-B" })
            h.Db.WorkCenters.Add(new() { WgCd = center, WgName = center, Creator = actor });
        h.Db.ProcessCostRates.AddRange(
            new() { WgCd = "W686-A", LaborRate = 9m, OverheadRate = 4m, ValidFrom = new(2026, 5, 1), ValidTo = new(2026, 5, 31, 23, 59, 59), Creator = actor },
            new() { WgCd = "W686-A", LaborRate = 123.45678901m, OverheadRate = 45.12345678m, ValidFrom = new(2026, 6, 1), ValidTo = CostCollectEnd, Creator = actor },
            new() { WgCd = "W686-A", LaborRate = 987m, OverheadRate = 654m, ValidFrom = new(2026, 7, 1), Creator = actor },
            new() { WgCd = "W686-B", LaborRate = 10.00000001m, OverheadRate = 3.125m, ValidFrom = new(2026, 1, 1), Creator = actor });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return number;
    }
}
