using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W685_cost_history_rate_and_amounts_require_current_permission_in_actual_HTTP()
    {
        await using var h = await Seed();
        await SeedCostRateHistoryAsync(h, "W685-HISTORY", "WC1");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);

        var observations = new List<(string Path, HttpStatusCode Status, string Body)>();
        foreach (var path in new[] { "/api/fin/cost", "/api/fin/cost/W685-HISTORY", "/api/fin/cost/W685-HISTORY/fg-unit-cost" })
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
            observations.Add((path, response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        Assert.All(observations, item =>
        {
            Assert.True(item.Status == HttpStatusCode.Forbidden, $"{item.Path}: {(int)item.Status} {item.Body}");
            Assert.DoesNotContain("123.45678901", item.Body);
            Assert.DoesNotContain("246.91", item.Body);
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task W685_cost_history_requires_each_current_source_field_and_keeps_exact_authorized_values()
    {
        await using var h = await Seed();
        await SeedCostRateHistoryAsync(h, "W685-ARCHIVED", "ARCHIVED");
        await GrantLegacyAuthorityAction(h, "fin-cost", "view");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var listPath = "/api/fin/cost?status=1";
        var detailPath = "/api/fin/cost/W685-ARCHIVED";
        var unitPath = detailPath + "/fg-unit-cost";
        var paths = new[] { listPath, detailPath, unitPath };

        await AssertDenied("ACTION_FORBIDDEN");
        await GrantCost(h);
        foreach (var hidden in new[] { "LaborRate", "OverheadRate" })
        {
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "mes-process-cost-rate", FieldName = hidden, Access = 3 });
            await h.Db.SaveChangesAsync();
            await AssertDenied("FIELD_READ_FORBIDDEN");
            await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "mes-process-cost-rate" && x.FieldName == hidden).ExecuteDeleteAsync();
            h.Db.ChangeTracker.Clear();
        }

        using var listResponse = await host.Send(OperationsRequest(cookies, HttpMethod.Get, listPath));
        using var detailResponse = await host.Send(OperationsRequest(cookies, HttpMethod.Get, detailPath));
        using var unitResponse = await host.Send(OperationsRequest(cookies, HttpMethod.Get, unitPath));
        foreach (var response in new[] { listResponse, detailResponse, unitResponse })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        using var unit = JsonDocument.Parse(await unitResponse.Content.ReadAsStringAsync());
        Assert.Equal(246.91m, list.RootElement.GetProperty("data")[0].GetProperty("laborActual").GetDecimal());
        var lines = detail.RootElement.GetProperty("data").GetProperty("lines").EnumerateArray().OrderBy(x => x.GetProperty("element").GetInt32());
        Assert.Equal(new[] { 123.45678901m, 15.125m }, lines.Select(x => x.GetProperty("unitPrice").GetDecimal()));
        Assert.Equal(69.295m, unit.RootElement.GetProperty("data").GetProperty("fgUnitCost").GetDecimal());

        var financeMenu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "fin-cost").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == financeMenu && x.ActionCode == "view").ExecuteDeleteAsync();
        await AssertDenied("ACTION_FORBIDDEN");

        async Task AssertDenied(string code)
        {
            foreach (var path in paths)
            {
                using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{path}: {(int)response.StatusCode} {body}");
                Assert.True(response.Headers.CacheControl?.NoStore);
                Assert.Contains(code, body);
                Assert.DoesNotContain("123.45678901", body);
                Assert.DoesNotContain("246.91", body);
                Assert.DoesNotContain("69.295", body);
            }
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W685_cost_history_limits_scoped_amounts_to_visible_rate_sources_and_current_tenant()
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        await SeedCostRateHistoryAsync(h, "W685-OWN", "WC1");
        await SeedCostRateHistoryAsync(h, "W685-OUTSIDE", "WC2");
        await SeedCostRateHistoryAsync(h, "W685-NOSOURCE", "ARCHIVED");
        await SeedCostRateHistoryAsync(foreign, "W685-FOREIGN", "WC1");
        foreach (var (center, creator) in new[] { ("WC1", "price-owner"), ("WC2", "rate-owner-other") })
        {
            h.Db.WorkCenters.Add(new() { WgCd = center, WgName = center, Creator = creator });
            h.Db.ProcessCostRates.Add(new()
            {
                WgCd = center, ValidFrom = new(2026, 1, 1), LaborRate = 123.45678901m, OverheadRate = 15.125m, Creator = creator,
            });
        }
        await h.Db.SaveChangesAsync();
        await GrantLegacyAuthorityAction(h, "fin-cost", "view");
        await GrantCost(h);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "mes-process-cost-rate").ExecuteUpdateAsync(update =>
            update.SetProperty(x => x.ScopeType, 1));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);

        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/fin/cost"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var list = JsonDocument.Parse(body);
        Assert.Equal("W685-OWN", Assert.Single(list.RootElement.GetProperty("data").EnumerateArray()).GetProperty("workOrderNo").GetString());
        foreach (var number in new[] { "W685-OUTSIDE", "W685-FOREIGN", "W685-NOSOURCE" })
        {
            foreach (var suffix in new[] { "", "/fg-unit-cost" })
            {
                using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/fin/cost/{number}{suffix}"));
                var result = await denied.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                Assert.Contains("E-FIN-403", result);
                Assert.DoesNotContain("123.45678901", result);
                Assert.DoesNotContain("69.295", result);
            }
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W685_cost_history_action_installs_for_existing_tenants_without_role_grants()
    {
        await using var h = await Seed();
        await using var other = await Seed();
        await GrantLegacyAuthorityAction(h, "fin-cost", "collect");
        var menuId = await h.Db.Sys_Menus.Where(m => m.MenuKey == "fin-cost").Select(m => m.MenuId).SingleAsync();
        var migration = new CP6.Persistence.PostgreSql.Migrations.Core.FinCostViewV1();
        foreach (var operation in migration.UpOperations.Cast<SqlOperation>())
            await h.Db.Database.ExecuteSqlRawAsync(operation.Sql);
        await using var newTenant = await Seed();
        Assert.False(await newTenant.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menuId && x.ActionCode == "view"));
        CP6.WebApi.Seed.FinCostViewSeed.EnsureSeeded(h.Db);
        CP6.WebApi.Seed.FinCostViewSeed.EnsureSeeded(newTenant.Db);
        foreach (var tenant in new[] { h, other, newTenant })
        {
            Assert.Equal(1, await tenant.Db.Sys_MenuActions.CountAsync(x => x.MenuId == menuId && x.ActionCode == "view"));
            Assert.False(await tenant.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == menuId && x.ActionCode == "view"));
        }
    }

    private static async Task SeedCostRateHistoryAsync(Harness h, string number, string center)
    {
        h.Db.WorkOrders.Add(new WorkOrder { WorkOrderNo = number, ProductCd = "COST-ITEM", Creator = "price-owner" });
        h.Db.CostSheets.Add(new CostSheet
        {
            No = number, WorkOrderNo = number, ProductCd = "COST-ITEM", Creator = "historical-import",
            CompletedQty = 2m, Status = CostSheetStatus.Collected,
            LaborActual = 246.91m, LaborStandard = 123.46m, OverheadActual = 30.25m, OverheadStandard = 15.13m,
            Lines =
            [
                new CostSheetLine { LineNo = 1, Element = CostElement.Labor, WgCd = center, Hours = 2m, StandardHours = 1m,
                    UnitPrice = 123.45678901m, ActualAmount = 246.91m, StandardAmount = 123.46m, RateValidFrom = new(2026, 1, 1), HourSource = "Derived" },
                new CostSheetLine { LineNo = 2, Element = CostElement.Overhead, WgCd = center, Hours = 2m, StandardHours = 1m,
                    UnitPrice = 15.125m, ActualAmount = 30.25m, StandardAmount = 15.13m, RateValidFrom = new(2026, 1, 1), HourSource = "Derived" },
            ],
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }
}
