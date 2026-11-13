using System.Net;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("asset-create")]
    [InlineData("asset-edit")]
    [InlineData("asset-delete")]
    [InlineData("price-save")]
    [InlineData("price-delete")]
    [InlineData("plan-save")]
    [InlineData("plan-delete")]
    public async Task PGT017_master_data_writes_recheck_current_action_before_effect(string route)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        var tenant = h.Tenant;
        var code = tenant.ToString("N")[..12];
        var category = new AssetCategory { Id = Guid.NewGuid(), Code = code, Name = "before" };
        var price = new SupplierPrice
        {
            Id = Guid.NewGuid(), SupplierId = "fixture", ItemId = code, Price = 1m,
            ValidFrom = new DateTime(2026, 11, 13)
        };
        db.AssetCategories.Add(category);
        db.SupplierPrices.Add(price);
        db.ItemPlanningPolicies.Add(new() { ItemCd = code, SafetyStock = 1m });
        var resource = route.StartsWith("asset-", StringComparison.Ordinal) ? "fin-asset-category"
            : route.StartsWith("price-", StringComparison.Ordinal) ? "pur-supplier-price" : "plan-item-policy";
        var action = route.EndsWith("-delete", StringComparison.Ordinal) ? "delete" : route == "asset-edit" ? "edit" : "add";
        if (!await db.Sys_Menus.AnyAsync(m => m.MenuKey == resource))
        {
            var menuId = resource switch { "fin-asset-category" => 32860, "pur-supplier-price" => 32861, _ => 32862 };
            db.Sys_Menus.Add(new() { MenuId = menuId, MenuKey = resource, MenuName = resource });
            await db.SaveChangesAsync();
        }
        await GrantLegacyAuthorityAction(h, resource, action);
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, action);
        const decimal amount = 12.1234m;

        (HttpMethod Method, string Path, object? Body) request = route switch
        {
            "asset-create" => (HttpMethod.Post, "api/fin/asset-category", new { Code = code + "-new", Name = "new" }),
            "asset-edit" => (HttpMethod.Put, $"api/fin/asset-category/{category.Id}", new { category.Code, Name = "changed" }),
            "asset-delete" => (HttpMethod.Delete, $"api/fin/asset-category/{category.Id}", null),
            "price-save" => (HttpMethod.Post, "api/pur/supplier-price", new
                { price.Id, price.SupplierId, price.ItemId, Price = amount, price.ValidFrom }),
            "price-delete" => (HttpMethod.Delete, $"api/pur/supplier-price/{price.Id}", null),
            "plan-save" => (HttpMethod.Post, "api/plan/item-policy", new { ItemCd = code, SafetyStock = 12.12345678m }),
            "plan-delete" => (HttpMethod.Delete, $"api/plan/item-policy/{code}", null),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        var before = await Effects();
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            await using var lockActions = new NpgsqlCommand(
                """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
            await lockActions.ExecuteNonQueryAsync();
            change = host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
            var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
            write = Write();
            var pending = await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            output.WriteLine($"{route}: IAM backend {iam.Backend}; writer backend {pending.Backend}");
            Assert.Equal(before, await Effects());
            await hold.RollbackAsync();
        }
        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var response = await write;
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{route}: {response.StatusCode} {await OperationsFailure(response)}");
        Assert.Equal(before, await Effects());

        await GrantLegacyAuthorityAction(h, resource, action);
        using var allowed = await Write();
        Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
        Assert.NotEqual(before, await Effects());
        if (route == "price-save")
            Assert.Equal(amount, await db.SupplierPrices.AsNoTracking().Where(p => p.Id == price.Id).Select(p => p.Price).SingleAsync());
        if (route == "plan-save")
            Assert.Equal(12.12345678m, await db.ItemPlanningPolicies.AsNoTracking().Where(p => p.ItemCd == code)
                .Select(p => p.SafetyStock).SingleAsync());

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(writer, request.Method, request.Path, request.Body));

        Task<string> Effects() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(
               (SELECT jsonb_agg(to_jsonb(a) ORDER BY a."Id") FROM public."Fin_AssetCategory" a WHERE a."TenantId"={tenant}),
               (SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM public."Pur_SupplierPrice" p WHERE p."TenantId"={tenant}),
               (SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM public."Plan_ItemPlanningPolicy" p WHERE p."TenantId"={tenant})
            )::text AS "Value"
            """).SingleAsync();
    }
}
