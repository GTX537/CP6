using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Common;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_hidden_candidates_register_rights_and_scope_staleness_fail_closed()
    {
        await using var h = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, bridge);
        var account = Guid.NewGuid();
        bridge.Account(account, "00000099");
        h.Db.MasterBases.Add(new MasterBase { BaseCd = "BPX", BaseName = "模拟隐藏据点" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await using (var grant = await h.Db.Database.BeginTransactionAsync())
        {
            await BpUseScopeGate.LockAsync(h.Db, MappingLegal, true, default);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.bases VALUES({h.Tenant},{MappingLegal},'BPX',true,'1');
                INSERT INTO main_bp.base_grants VALUES({h.Tenant},{MappingLegal},'BPX',72,true,true);
                """);
            await grant.CommitAsync();
        }
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, bridge.Environment());
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var hiddenPreview = await WorkspacePreview(host, cookies, WorkspaceInput(options, "0000940", "模拟隐藏候选", site: "BPX"));
        var hidden = PartnerId(await ApplyWorkspace(host, cookies, hiddenPreview, Guid.NewGuid()));
        var selectable = await AdoptMappingPartner(host, cookies, "0000941", "模拟可见候选");
        await using (var restriction = await h.Db.Database.BeginTransactionAsync())
        {
            await BpUseScopeGate.LockAsync(h.Db, MappingLegal, true, default);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.base_grants SET can_read=false,can_manage=false WHERE tenant_id={h.Tenant} AND legal_entity_key={MappingLegal} AND base_cd='BPX'
                """);
            await restriction.CommitAsync();
        }
        var proof = await SignedAccountObservation(host, cookies, account);
        var hiddenContext = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("0000940", "模拟隐藏候选"));
        var hiddenSet = hiddenContext.Body.GetProperty("candidates");
        Assert.Equal("REVIEW_REQUIRED", hiddenSet.GetProperty("visibility").GetString());
        Assert.Empty(hiddenSet.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(hidden.ToString("D"), hiddenContext.Body.GetRawText());
        Assert.DoesNotContain("0000940", hiddenContext.Body.GetRawText());
        Assert.DoesNotContain("模拟隐藏候选", hiddenContext.Body.GetRawText());
        Assert.DoesNotContain(hiddenContext.Body.GetProperty("allowedActions").EnumerateArray(), a => a.GetString() == "CREATE_AND_LINK");

        object Partner(string code, string name, string registration)
        {
            var json = JsonSerializer.SerializeToElement(WorkspaceInput(options, code, name, registration: registration, complete: true), MappingJson);
            return new { registration, identity = json.GetProperty("identity"), duplicateDecision = json.GetProperty("duplicateDecision") };
        }
        var attempted = await MapPreview(host, cookies, MappingInput(hiddenContext, "CREATE_AND_LINK", null, "模拟被遮罩的重复项",
            Partner("0000940", "模拟隐藏候选", "PRE_REGISTERED")));
        Assert.False(attempted.Body.GetProperty("canApply").GetBoolean());
        var clean = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("0000942", "模拟正式注册撤权"));
        var registered = Partner("0000942", "模拟正式注册撤权", "REGISTERED");
        var intent = MappingInput(clean, "CREATE_AND_LINK", null, "模拟已预览再撤注册", registered);
        var approved = await GoodMapPreview(host, cookies, intent);
        await StateCommand(host, cookies, selectable, await WorkspaceStatus(host, cookies, selectable), "freeze", Guid.NewGuid());
        await ApplyMap(host, cookies, approved, Guid.NewGuid(), 412);
        clean = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("0000942", "模拟正式注册撤权"));
        approved = await GoodMapPreview(host, cookies, MappingInput(clean, "CREATE_AND_LINK", null, "模拟已预览再撤注册", registered));
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 && row.ActionCode == "register").ExecuteDeleteAsync();
        await ApplyMap(host, cookies, approved, Guid.NewGuid(), 403);
        var afterRevoke = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        Assert.Equal(new[] { "PRE_REGISTERED" }, Assert.Single(afterRevoke.Body.GetProperty("bases").EnumerateArray())
            .GetProperty("createModes").EnumerateArray().Select(x => x.GetString()));
        clean = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("0000942", "模拟正式注册撤权"));
        await MapPreview(host, cookies, MappingInput(clean, "CREATE_AND_LINK", null, "模拟当前无注册", registered), 403);

        var pre = Partner("0000942", "模拟正式注册撤权", "PRE_REGISTERED");
        var beforeAddRevocation = await GoodMapPreview(host, cookies, MappingInput(clean, "CREATE_AND_LINK", null, "模拟已预览再撤创建", pre));
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 && row.ActionCode == "add").ExecuteDeleteAsync();
        await ApplyMap(host, cookies, beforeAddRevocation, Guid.NewGuid(), 403);
        afterRevoke = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        Assert.False(Assert.Single(afterRevoke.Body.GetProperty("bases").EnumerateArray()).GetProperty("canCreate").GetBoolean());
        clean = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("0000942", "模拟正式注册撤权"));
        Assert.DoesNotContain(clean.Body.GetProperty("allowedActions").EnumerateArray(), a => a.GetString() == "CREATE_AND_LINK");
        await MapPreview(host, cookies, MappingInput(clean, "CREATE_AND_LINK", null, "模拟无新建权",
            Partner("0000942", "模拟正式注册撤权", "PRE_REGISTERED")), 403);
        var found = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("0000941", "模拟可见候选"));
        var link = await GoodMapPreview(host, cookies, MappingInput(found, "LINK", selectable, "模拟仅映射权"));
        await StateCommand(host, cookies, selectable, await WorkspaceStatus(host, cookies, selectable), "unfreeze", Guid.NewGuid());
        await ApplyMap(host, cookies, link, Guid.NewGuid(), 412);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await MappingCounts(h));
        Assert.False(await h.Db.BusinessPartners.AnyAsync(row => row.BpCd == "0000942"));

        found = await ReadMappingContext(host, cookies, account, proof, MappingCriteria("0000941", "模拟可见候选"));
        var changed = await GoodMapPreview(host, cookies, MappingInput(found, "LINK", selectable, "模拟已有伙伴可关联身份"));
        await ApplyMap(host, cookies, changed, Guid.NewGuid());
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1 }, await MappingCounts(h));
        await DeviceEvidence("wp04-03-authorization.json", new { account, hiddenMemberSuppressed = true, hiddenMemberBlocksCreate = true,
            registerRevocationAfterPreview = 403, addRevocationBlocksPreviewAndApply = 403, staleTargetOrScope = 412, mappingOnlyLink = true });
    }
}
