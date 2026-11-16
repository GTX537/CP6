using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string PartnerResource = "erp-business-partner";
    private const string PartnerApi = "api/business-partners";

    private static async Task GrantPartnerActions(Harness h, params string[] actions)
    {
        foreach (var action in actions)
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == 212 && x.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = 212, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 212, ActionCode = action });
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static BusinessPartnerDto PartnerInput(string code, string? name) => new()
    {
        BpCd = code, BpName = name, BpAbbrev = "简称", BaseCd = "B01", Status = 1, CustomerFlg = true,
        AccountsReceivableCd = "AR", SalesStaffCd = "SA", BusinessStaffCd = "BS"
    };

    private static async Task<JsonElement> PartnerResponse(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, body);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_legacy_http_requires_query_on_empty_reads_and_independent_export()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);

        foreach (var path in new[] { "list", "NOT-REGISTERED", "check-exists/NOT-REGISTERED", "export-csv" })
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/" + path));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }

        await GrantBusinessPartnerIdentity(h);
        using var identityOnly = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/list"));
        Assert.Equal(HttpStatusCode.Forbidden, identityOnly.StatusCode);
        using var exists = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/check-exists/NOT-REGISTERED"));
        Assert.False((await PartnerResponse(exists, HttpStatusCode.OK)).GetProperty("data").GetProperty("exists").GetBoolean());

        await GrantPartnerActions(h, "profile-query");
        using var list = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/list"));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var export = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/export-csv"));
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);

        await GrantPartnerActions(h, "export");
        using var allowed = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/export-csv"));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.True(allowed.Headers.CacheControl?.NoStore);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.ActionCode == "query").ExecuteDeleteAsync();
        using var revoked = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/export-csv"));
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_legacy_http_scopes_list_count_detail_and_csv_before_projecting_names()
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, "profile-query", "export");
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == PartnerResource)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        h.Db.BusinessPartners.AddRange(
            new() { BpCd = "OWN-A", BpName = "样例-乙", Status = 1, CustomerFlg = true, Creator = "price-owner" },
            new() { BpCd = "OWN-B", BpName = "样例-甲", Status = 1, CustomerFlg = true, Creator = "price-owner" },
            new() { BpCd = "OTHER-X", BpName = "范围外", Status = 1, CustomerFlg = true, Creator = "other" });
        foreign.Db.BusinessPartners.Add(new()
        {
            BpCd = "OWN-A", BpName = "其他租户", Status = 1, CustomerFlg = true, Creator = "price-owner"
        });
        await h.Db.SaveChangesAsync();
        await foreign.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        async Task<JsonElement> Get(string route, HttpStatusCode status = HttpStatusCode.OK)
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/" + route));
            return await PartnerResponse(response, status);
        }

        Assert.Equal("样例-乙", (await Get("OWN-A")).GetProperty("data").GetProperty("bpName").GetString());
        Assert.Equal(404, (await Get("OTHER-X?includeDeleted=true")).GetProperty("code").GetInt32());
        Assert.False((await Get("check-exists/OTHER-X")).GetProperty("data").GetProperty("exists").GetBoolean());
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = PartnerResource, FieldName = "BpName", Access = 3 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        Assert.Equal(JsonValueKind.Null, (await Get("OWN-A")).GetProperty("data").GetProperty("bpName").ValueKind);
        var list = (await Get("list?pageSize=1&maxRows=2")).GetProperty("data");
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        var row = Assert.Single(list.GetProperty("rows").EnumerateArray());
        Assert.Equal("OWN-A", row.GetProperty("bpCd").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("bpName").ValueKind);
        foreach (var route in new[] { "list?bpName=sample", "list?sortField=BpName&sortOrder=desc", "export-csv?bpName=sample" })
            await Get(route, HttpStatusCode.Forbidden);

        using var export = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/export-csv?pageSize=1"));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.True(export.Headers.CacheControl?.NoStore);
        var csv = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains(",OWN-A,,", csv);
        Assert.Contains(",OWN-B,,", csv);
        Assert.DoesNotContain("OTHER-X", csv);
        Assert.DoesNotContain("样例-", csv);

        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == PartnerResource).ExecuteDeleteAsync();
        using var visible = await host.Send(OperationsRequest(cookies, HttpMethod.Get, PartnerApi + "/export-csv"));
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Contains(",OWN-A,样例-乙,", Encoding.UTF8.GetString(await visible.Content.ReadAsByteArrayAsync()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_legacy_http_write_only_saves_without_readback_and_hidden_name_survives_edit()
    {
        await using var h = await Seed();
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, "add", "edit");
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.ActionCode == "query").ExecuteDeleteAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var input = PartnerInput("BP-SAVE", "登记中的样例");

        using var added = await host.Send(OperationsRequest(cookies, HttpMethod.Post, PartnerApi + "?preRegister=true", input));
        Assert.Equal(JsonValueKind.Null, (await PartnerResponse(added, HttpStatusCode.OK)).GetProperty("data").ValueKind);
        var stored = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(x => x.BpCd == input.BpCd);
        Assert.Equal(0, stored.Status);
        Assert.Equal("price-owner", stored.Creator);
        Assert.Equal("登记中的样例", stored.BpName);

        await GrantPartnerActions(h, "query", "profile-query");
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = PartnerResource, FieldName = "BpName", Access = 3 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        input.Status = 0;
        input.RowVersion = stored.RowVersion;
        input.BpName = null;
        input.BaseCd = "B02";
        using var updated = await host.Send(OperationsRequest(cookies, HttpMethod.Put, PartnerApi + "/BP-SAVE", input));
        var readback = (await PartnerResponse(updated, HttpStatusCode.OK)).GetProperty("data");
        Assert.Equal(JsonValueKind.Null, readback.GetProperty("bpName").ValueKind);
        Assert.Equal("B02", readback.GetProperty("baseCd").GetString());

        using var stale = await host.Send(OperationsRequest(cookies, HttpMethod.Put, PartnerApi + "/BP-SAVE", input));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        input.RowVersion = readback.GetProperty("rowVersion").GetBytesFromBase64();
        input.BaseCd = "B03";
        input.BpName = "越权更改";
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Put, PartnerApi + "/BP-SAVE", input));
        await PartnerResponse(denied, HttpStatusCode.Forbidden);
        var after = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(x => x.Id == stored.Id);
        Assert.Equal("登记中的样例", after.BpName);
        Assert.Equal("B02", after.BaseCd);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_legacy_final_profile_permission_failure_rolls_back_write_and_native_version()
    {
        var hook = new CommandHook();
        await using var h = await Seed(hook);
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, "add", "edit", "profile-query");
        var service = new BusinessPartnerService(h.Db, PartnerAuthority(h));
        var original = (await service.CreateAsync(PartnerInput("BP-ATOMIC", "原名称"), null))!;
        var id = await h.Db.BusinessPartners.Select(x => x.Id).SingleAsync();
        var identity = await Partners(h).ReadAsync(id);
        var reads = 0;
        hook.Before = command =>
        {
            if (command.CommandText.Contains("WITH field_subject") &&
                Equals(command.Parameters["field_resource"].Value, PartnerResource) && ++reads == 4)
                throw new IOException("档案回读最终权限检查失败");
            return Task.CompletedTask;
        };
        try
        {
            original.BpName = "新名称";
            var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(
                () => service.UpdateAsync(original.BpCd, original, "不可信请求主体"));
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", error.Message);
        }
        finally { hook.Before = null; }
        Assert.Equal(4, reads);
        Assert.Equal("原名称", (await service.GetByCdAsync(original.BpCd))!.BpName);
        Assert.Equal(identity.Reference, (await Partners(h).ReadAsync(id)).Reference);
    }
}
