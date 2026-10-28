using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 InventoryIdentityAuthority(Harness h,Func<CancellationToken,Task<Guid>>? actor=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"wms-inventory-identity","erp-business-partner","plm-engineering-baseline","plm-engineering-iteration","plm-configuration-rule","quality-specification","plm-controlled-document","file-content","plm-engineering-revision","plm-engineering-product","space-site","uom-profile","plm-engineering-bom","plm-engineering-routing","plm-tool-specification","erp-plate-mold","mes-machine","mes-work-center","product","plm-requirement-set","plm-engineering-requirement"})
        {
            catalog.RegisterScope(key,key,[1,2,3,4,5],5);catalog.RegisterFields(key,key switch{
                // RegistryVersion hashes the complete definitions, including labels. Keep the
                // native setup consistent with Program when its original is consumed over HTTP.
                "plm-engineering-iteration"=>[new("Metadata","项目计划"),new("Inputs","准确输入"),new("Tasks","任务交付")],
                "plm-requirement-set"=>[new("Scope","适用范围"),new("Members","准确成员")],"plm-engineering-requirement"=>[new("Definition","需求定义"),new("Source","需求来源")],
                "erp-business-partner"=>[new("BpName","Name")],"quality-specification"=>[new("Metadata","规格资料"),new("Requirements","完整质量要求")],
                "plm-controlled-document"=>[new("Content","文档内容"),new("Metadata","文档资料")],"file-content"=>[new("Content","文件内容"),new("FileName","文件名称")],_=>[]});
        }
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},actor??(_=>Task.FromResult(h.User)),catalog.Capture);
    }
    private static InventoryIdentityOwnerV1 InventoryIdentity(Harness h, ContentStore? files = null)
        => new(h.Db, InventoryIdentityAuthority(h), ScopeFence(h), new("DEMO"), Baselines(h, files ?? new()));
    private static async Task<Harness> GrantInventoryIdentity(Harness h, bool registerScope = true)
    {
        await EnsureInventoryProductMenu(h);
        if (registerScope) await RegisterScope(h);
        foreach (var action in new[] { "query", "add", "edit", "approve", "disable" }) await EngineeringAction(h, "wms-inventory-identity", action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-inventory-identity", ScopeType = 5 });
        var user = new Sys_User { UserName = "inventory-reviewer", Password = new BCryptPasswordHasher(4).Hash("Price1!Password"), RoleId = 72 };
        h.Db.Sys_Users.Add(user); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return h with { User = user.Id, Http = new DefaultHttpContext { User = CurrentPermissionContextTests.Principal(user.Id, h.Tenant) } };
    }
    private static async Task EnsureInventoryProductMenu(Harness h)
    {
        // Older native fixtures reserve 9144 for different menus. Seed the real menu by key with a free ID.
        if (!await h.Db.Sys_Menus.AnyAsync(x => x.MenuKey == "erp-product"))
        { h.Db.Sys_Menus.Add(new() { MenuId = checked(await h.Db.Sys_Menus.MaxAsync(x => x.MenuId) + 1), MenuKey = "erp-product", MenuName = "Product" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
    }
    private static InventoryIdentityDefinitionV1 SelfIdentity(string code = "LEGAL") => new("PARTY", code, "Explicit legal entity", "Original organization identity configuration",
        new("SELF", "Actual configured legal name", "JP", "REG-12345", null), null, null);
    private static InventoryIdentityDefinitionV1 PurposeIdentity() => new("PURPOSE", "TRIAL", "Trial stock", "Declared original purpose", null, new("TRIAL", "Retain trial origin through movements"), null);
    private static async Task<InventoryIdentityCommandV1> IdentityCreate(InventoryIdentityOwnerV1 o, InventoryIdentityDefinitionV1 d, Guid? department = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), "SAVE", "absent", d, "Create explicit identity", (await o.CreationContextAsync()).AuthorityVersion, department);
    private static InventoryIdentityCommandV1 IdentityAction(InventoryIdentityViewV1 v, string action)
        => new(Guid.NewGuid(), v.Reference.Id, action, v.Reference.Version, null, "Independent controlled identity action");
    private static async Task<InventoryIdentityViewV1> ApprovedIdentity(Harness maker, Harness approver, InventoryIdentityDefinitionV1 d)
    {
        var owner = InventoryIdentity(maker); var c = await IdentityCreate(owner, d); await owner.CommandAsync(c);
        await InventoryIdentity(approver).CommandAsync(IdentityAction(await owner.ReadAsync(c.Id), "APPROVE")); return await owner.ReadAsync(c.Id);
    }
    private async Task<(Harness Reviewer, InventoryIdentityDefinitionV1 Definition, UomProfileVersionV1 Unit)> InventoryProductSetup(Harness h, bool ratio = false)
    {
        var reviewer = await GrantInventoryIdentity(h); await GrantMappingRead(h); await GrantUomProfile(h);
        foreach (var (menu, action) in new[] { ("erp-product", "edit"), ("erp-product", "confirm-engineering"), ("plm-engineering-product", "confirm"),
            ("erp-product", "confirm-inventory"), ("plm-engineering-product", "confirm-inventory") }) await EngineeringAction(h, menu, action);
        var unit = await MakeUnit(Units(h));
        var inputUnit = ratio ? await MakeUnit(Units(h), BaseUnit("HF", scale: 0) with { Mode = "RATIO", BaseRef = unit.Reference, Denominator = "2" }) : unit;
        var engineering = await MakeEngineering(h); var candidate = await MappingCandidate(h, engineering);
        await h.Db.ProductMasters.Where(x => x.Id == candidate.BusinessRef!.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.QtyUnit, inputUnit.Profile.Code));
        var product = await ProductIdentity(h).ReadAsync(candidate.BusinessRef!.Id);
        candidate = candidate with { BusinessRef = new("Main.Erp.ProductIdentity.v1", product.ProductId, product.Version, product.Digest) };
        var mappings = EngineeringMappings(h); var draft = await MakeMapping(h, candidate); await mappings.CommandAsync(MappingAction(draft, "CONFIRM"));
        var mapping = await mappings.ReadAsync(draft.Reference.Id, null); var units = new CommercialProductUnitOwnerV1(h.Db, UnitAuthority(h));
        await units.BindAsync(new() { OperationId = Guid.NewGuid(), ProductId = product.ProductId, ExpectedBindingVersion = "absent", ProductVersion = product.Version, ProfileRef = inputUnit.Reference });
        var binding = await units.ReadAsync(product.ProductId, null);
        var party = await ApprovedIdentity(h, reviewer, SelfIdentity()); var purpose = await ApprovedIdentity(h, reviewer, PurposeIdentity());
        return (reviewer, new("PRODUCT", "STOCK-A", "Controlled product", "Product identity policy", null, null,
            new(candidate.BusinessRef, new("Main.Erp.ProductUnit.v1", product.ProductId, binding.Version, binding.Digest), mapping.Reference,
                "NOT_APPLICABLE", null, "Explicit non-engineered item assessment", party.Reference, purpose.Reference)), unit);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_party_purpose_independent_approval_history_CAS_and_original_recovery()
    {
        await using var h = await Seed(); var reviewer = await GrantInventoryIdentity(h); var o = InventoryIdentity(h); var c = await IdentityCreate(o, SelfIdentity());
        var result = await o.CommandAsync(c); Assert.Equal(result, await o.CommandAsync(c)); var draft = await o.ReadAsync(c.Id);
        Assert.False(draft.CanSelect); Assert.Equal("DRAFT", draft.Original.State);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(IdentityAction(draft, "APPROVE")))).Status);
        var approved = await InventoryIdentity(reviewer).CommandAsync(IdentityAction(draft, "APPROVE")); var current = await o.ReadAsync(c.Id);
        Assert.True(current.CanSelect); Assert.Equal("2", approved.Version); Assert.Equal(h.User, current.Original.AuthorId); Assert.Equal(reviewer.User, current.Original.ActorId);
        Assert.Equal(result, (await o.OperationAsync(c.OperationId, false)).Result);
        var noop = await o.CommandAsync(c with { OperationId = Guid.NewGuid(), ExpectedVersion = "2", ExpectedAuthorityVersion = null });
        Assert.False(noop.Changed); Assert.Equal(approved.Digest, noop.Digest);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(c with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", ExpectedAuthorityVersion = null }))).Status);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(c with { Reason = "Changed intent" }))).Message);
        await o.CommandAsync(IdentityAction(current, "DISABLE")); Assert.False((await o.ReadAsync(c.Id)).CanSelect);
        Assert.Equal(current.OriginalJson, (await o.ReadAsync(c.Id, "2")).OriginalJson);
        var id = Guid.NewGuid(); Assert.Equal("UNKNOWN", (await o.OperationAsync(id, false)).State); Assert.Equal("CLOSED", (await o.OperationAsync(id, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(c with { OperationId = id }))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_product_exact_native_sources_NA_confirmation_and_current_source_revocation()
    {
        await using var h = await Seed(); var f = await InventoryProductSetup(h); var o = InventoryIdentity(h);
        var c = await IdentityCreate(o, f.Definition); await o.CommandAsync(c); var draft = await o.ReadAsync(c.Id);
        var plmMenu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-product").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == plmMenu && x.ActionCode == "confirm-inventory").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => InventoryIdentity(f.Reviewer).CommandAsync(IdentityAction(draft, "APPROVE")))).Status);
        await EngineeringAction(h, "plm-engineering-product", "confirm-inventory");
        await InventoryIdentity(f.Reviewer).CommandAsync(IdentityAction(draft, "APPROVE")); var approved = await o.ReadAsync(c.Id);
        Assert.True(approved.CanSelect); Assert.Equal(3, approved.Original.Confirmations.Length);
        var catalogue = await o.CatalogueAsync(approved.Reference); Assert.Null(catalogue.Option.TechnicalIdentity.RevisionRef);
        Assert.Equal(approved.Reference.Id, catalogue.Option.TechnicalIdentity.ApplicabilityRef!.Id);
        Assert.Equal(f.Definition.Product!.Product, catalogue.Option.ProductRef); Assert.Equal("SELF", catalogue.Option.Owner.Kind);
        Assert.Equal("REQUIRES_FINAL_OWNER_GATES", catalogue.PhysicalEligibility);
        await Units(h).CommandAsync(Revise(f.Unit, f.Unit.Profile with { Enabled = false }));
        Assert.Equal(approved.OriginalJson, (await o.ReadAsync(c.Id)).OriginalJson); Assert.False((await o.ReadAsync(c.Id)).CanSelect);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CatalogueAsync(approved.Reference))).Status);
        Assert.Equal("COMMITTED", (await InventoryIdentity(f.Reviewer).OperationAsync(approved.Original.OperationId, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_ratio_unit_catalogue_uses_base_quantity_precision()
    {
        await using var h = await Seed(); var f = await InventoryProductSetup(h, ratio: true);
        var approved = await ApprovedIdentity(h, f.Reviewer, f.Definition);
        var catalogue = await InventoryIdentity(h).CatalogueAsync(approved.Reference);
        var input = catalogue.Sources.Single(x => x.Path == "uomProfile").Reference;
        var inputUnit = await Units(h).ReadAsync(input.Id, input.Version);
        Assert.Equal(0, inputUnit.Profile.QuantityScale);
        Assert.NotEqual(f.Unit.Reference.Id, input.Id);
        Assert.Equal(f.Unit.Reference.Id.ToString(), catalogue.Option.BaseUomRef.Id);
        Assert.Equal(f.Unit.Reference.Id.ToString(), catalogue.Option.PrecisionPolicyRef.Id);
        Assert.Equal(f.Unit.Reference.Version, catalogue.Option.PrecisionPolicyRef.Version);
        var conversion = await Units(h).ConvertAsync(new(inputUnit.Reference, f.Unit.Reference, "1"));
        Assert.Equal("0.500000000000000000", conversion.ConvertedQuantity);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_customer_exact_uuid_field_redaction_and_original_source_changes()
    {
        await using var h = await Seed(); var reviewer = await GrantInventoryIdentity(h); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h);
        var customer = await Partners(h).ReadAsync(id); var d = new InventoryIdentityDefinitionV1("PARTY", "CUSTOMER", "Customer owned", "Exact customer identity",
            new("CUSTOMER", null, null, null, customer.Reference), null, null);
        var approved = await ApprovedIdentity(h, reviewer, d); Assert.True(approved.CanSelect);
        await h.Db.BusinessPartners.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CustomerFlg, false));
        var old = await InventoryIdentity(h).ReadAsync(approved.Reference.Id); Assert.Equal(approved.OriginalJson, old.OriginalJson); Assert.False(old.CanSelect);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "erp-business-partner", FieldName = "BpName", Access = 3 }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => InventoryIdentity(h).ReadAsync(approved.Reference.Id))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => InventoryIdentity(reviewer).OperationAsync(approved.Original.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_approver_revocation_and_tenant_mode_environment_are_current()
    {
        await using var h = await Seed(); var reviewer = await GrantInventoryIdentity(h); var v = await ApprovedIdentity(h, reviewer, SelfIdentity());
        await h.Db.Sys_Users.Where(x => x.Id == reviewer.User).ExecuteUpdateAsync(u => u.SetProperty(x => x.Enable, false));
        Assert.False((await InventoryIdentity(h).ReadAsync(v.Reference.Id)).CanSelect);
        var wrongMode = new InventoryIdentityOwnerV1(h.Db, InventoryIdentityAuthority(h), ScopeFence(h), new("REAL"), Baselines(h, new()));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => wrongMode.ReadAsync(v.Reference.Id))).Status);
        await using var other = await Seed(); await GrantInventoryIdentity(other);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => InventoryIdentity(other).ReadAsync(v.Reference.Id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_actual_http_closed_contract_CSRF_and_original_read_permission()
    {
        await using var h = await Seed(); await GrantInventoryIdentity(h); var settings = await DocumentHostSettings(new()); settings["QualitySpecification__EvidenceMode"] = "DEMO";
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings); var cookies = await Login(host, h);
        const string root = "/api/inventory-identities/owner-v1";
        async Task<T> Read<T>(string path) { using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + path)); Assert.True(response.Headers.CacheControl?.NoStore); return await DocumentHttpData<T>(response); }
        var creation = await Read<InventoryIdentityCreationV1>("/creation-context"); var c = new InventoryIdentityCommandV1(Guid.NewGuid(), Guid.NewGuid(), "SAVE", "absent", PurposeIdentity(), "Actual host write", creation.AuthorityVersion);
        async Task<HttpResponseMessage> Post(object body, bool csrf = true) { using var req = OperationsRequest(cookies, HttpMethod.Post, root + "/commands", body); if (!csrf) req.Headers.Remove("X-CSRF-Token"); return await host.Send(req); }
        using (var response = await Post(c, false)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var forged = JsonSerializer.SerializeToNode(c, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject(); forged["approved"] = true;
        using (var response = await Post(forged)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        InventoryIdentityResultV1 result; using (var response = await Post(c)) result = await DocumentHttpData<InventoryIdentityResultV1>(response);
        Assert.Equal(result, (await Read<InventoryIdentityOperationV1>("/operations/" + c.OperationId)).Result);
        Assert.Equal("DRAFT", (await Read<InventoryIdentityViewV1>("/" + c.Id)).Original.State);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "wms-inventory-identity").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + "/operations/" + c.OperationId)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
