using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 EngineeringAuthority(Harness h, CP6Context? context = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "plm-engineering-product", "product", "space-site" })
        { catalog.RegisterScope(resource, resource, resource == "space-site" ? [5] : [1, 2, 3, 4, 5], 5); catalog.RegisterFields(resource, []); }
        catalog.Seal(); return new(context ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static EngineeringProductOwnerV1 Engineering(Harness h, CP6Context? context = null) => new(context ?? h.Db, EngineeringAuthority(h, context));
    private static EngineeringProductMappingService EngineeringMappings(Harness h, CP6Context? context = null) => new(context ?? h.Db, EngineeringAuthority(h, context));
    private static EngineeringProductInputV1 EngineeringInput() => new() { Code = "SAME-CODE", Name = "Same name", Category = "PART", Definition = "Material A\nThickness: 2mm", IdentificationSource = "Explicit source A", Enabled = true };
    private static async Task<EngineeringProductCommandV1> NewEngineering(EngineeringProductOwnerV1 owner, EngineeringProductInputV1? input = null, Guid? department = null)
        => new() { OperationId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Action = "CREATE", Product = input ?? EngineeringInput(), DepartmentId = department, ExpectedAuthorityVersion = (await owner.CreationContextAsync()).AuthorityVersion };
    private static async Task<EngineeringProductVersionV1> MakeEngineering(Harness h, EngineeringProductInputV1? input = null)
    { var owner = Engineering(h); var command = await NewEngineering(owner, input); await owner.CommandAsync(command); return await owner.ReadAsync(command.ProductId, null); }
    private static EngineeringProductCommandV1 ReviseEngineering(EngineeringProductVersionV1 value, EngineeringProductInputV1 input) => new() { OperationId = Guid.NewGuid(), ProductId = value.Reference.Id, Action = "REVISE", ExpectedVersion = value.Reference.Version, Product = input };
    private static async Task EngineeringAction(Harness h, string menuKey, string action)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(row => row.MenuKey == menuKey);
        if (menu == null) { menu = new() { MenuId = menuKey == "space-site" ? 9151 : 9144, MenuKey = menuKey, MenuName = menuKey }; h.Db.Sys_Menus.Add(menu); }
        if (!await h.Db.Sys_MenuActions.AnyAsync(row => row.MenuId == menu.MenuId && row.ActionCode == action)) h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
        if (!await h.Db.Sys_RoleActions.AnyAsync(row => row.MenuId == menu.MenuId && row.RoleId == 72 && row.ActionCode == action)) h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static async Task<Guid> PhysicalSite(Harness h, string code)
    { var site = new Space_Site { SiteCode = code, SiteName = code, Creator = "price-owner", Enable = true }; h.Db.Space_Sites.Add(site); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return site.Id; }
    private static async Task<EngineeringMappingCandidateV1> MappingCandidate(Harness h, EngineeringProductVersionV1 engineering, string code = "A")
    {
        var business = await SeedIdentityProduct(h, code); var site = await PhysicalSite(h, code);
        var service = EngineeringMappings(h);
        return new() { EngineeringRef = engineering.Reference, BusinessRef = (await service.BusinessChoicesAsync(code)).Single(row => row.Reference.Id == business).Reference,
            SiteRef = (await service.SiteChoicesAsync(code)).Single(row => row.Reference.Id == site).Reference, Purpose = "Identity correspondence at named site", Evidence = "Both original Owners compared specification A", ConflictsResolved = true };
    }
    private static EngineeringMappingCommandV1 MappingDraft(EngineeringMappingCandidateV1 candidate) => new() { OperationId = Guid.NewGuid(), MappingId = Guid.NewGuid(), Action = "DRAFT", ExpectedVersion = "absent", Candidate = candidate };
    private static EngineeringMappingCommandV1 MappingAction(EngineeringMappingVersionV1 value, string action) => new() { OperationId = Guid.NewGuid(), MappingId = value.Reference.Id, ExpectedVersion = value.Reference.Version, Action = action };
    private static async Task<EngineeringMappingVersionV1> MakeMapping(Harness h, EngineeringMappingCandidateV1 candidate)
    { var service = EngineeringMappings(h); var draft = MappingDraft(candidate); await service.CommandAsync(draft); return await service.ReadAsync(draft.MappingId, null); }
    private static async Task GrantMappingRead(Harness h)
    { await GrantEngineering(h); await GrantProductIdentity(h); await EngineeringAction(h, "space-site", "query"); }
    private static Task<EngineeringMappingVersionV1> ConsumeMapping(Harness h, EngineeringMappingRefV1 reference)
        => EngineeringAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false, async (access, ct) =>
        { var store = new EngineeringProductStoreV1(h.Db); await store.Fence(false, ct); return await EngineeringProductMappingService.CurrentExact(access, store, reference, ct); });

    [CurrentPermissionPostgresFact]
    public async Task EngineeringProduct_duplicate_codes_do_not_merge_identity_and_history_CAS_remains_immutable()
    {
        await using var h = await Seed(); await GrantEngineering(h); var owner = Engineering(h);
        var a = await MakeEngineering(h); var b = await MakeEngineering(h, a.Product with { Definition = "Material B; not equivalent" });
        Assert.NotEqual(a.Reference.Id, b.Reference.Id); Assert.Equal(2, (await owner.ListAsync("SAME-CODE")).Length);
        var command = ReviseEngineering(a, a.Product with { Name = "Updated label" }); var result = await owner.CommandAsync(command); Assert.Equal(result, await owner.CommandAsync(command));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command with { OperationId = Guid.NewGuid() }))).Status);
        Assert.Equal(a.OriginalJson, (await owner.ReadAsync(a.Reference.Id, "1")).OriginalJson);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(ReviseEngineering(b, b.Product with { Code = "REKEY" })))).Status);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_engineering_product_v1.versions"))).SqlState);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringProduct_department_creation_and_revoked_recovery_use_actual_mapping(int type)
    {
        await using var h = await Seed(); await GrantEngineering(h); var owner = Engineering(h); var hidden = await MakeEngineering(h); var admin = await MappingAdmin(h);
        Assert.Equal(hidden.Reference.Id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("plm-engineering-product", "SAME-CODE", _ => Task.FromResult(h.User), default)).Id);
        await StandaloneScope(h, admin, type); Assert.Empty(await owner.ListAsync(null));
        var missing = await NewEngineering(owner); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(missing))).Status);
        var command = await NewEngineering(owner, department: admin.Child); var created = await owner.CommandAsync(command);
        Assert.Equal(created.ObjectId, Assert.Single(await owner.ListAsync(null)).Reference.Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-product' AND object_id={created.ObjectId}");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(command.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringProduct_tenant_creator_and_current_exact_fence_are_independent_of_labels()
    {
        await using var h = await Seed(); await GrantEngineering(h); var own = await MakeEngineering(h);
        await using var foreign = await Seed(); await GrantEngineering(foreign); var another = await MakeEngineering(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Engineering(h).ReadAsync(another.Reference.Id, null))).Status);
        await h.Db.Sys_RoleDataScopes.Where(row => row.ResourceKey == "plm-engineering-product").ExecuteUpdateAsync(update => update.SetProperty(row => row.ScopeType, 1));
        await h.Db.Sys_Users.Where(row => row.Id == h.User).ExecuteUpdateAsync(update => update.SetProperty(row => row.UserName, "renamed-" + h.User.ToString("N")));
        Assert.Equal(own.Reference, (await Engineering(h).ReadAsync(own.Reference.Id, null)).Reference);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => EngineeringAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false,
            (access, ct) => EngineeringProductOwnerV1.CurrentExact(access, new EngineeringProductStoreV1(h.Db), own.Reference, ct)))).Status);
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => EngineeringAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false,
            (access, ct) => EngineeringProductOwnerV1.CurrentExact(access, new EngineeringProductStoreV1(other), own.Reference, ct)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_two_sites_keep_original_ERP_keys_and_require_both_Owner_actions()
    {
        await using var h = await Seed(); await GrantMappingRead(h); var engineering = await MakeEngineering(h); var service = EngineeringMappings(h);
        var a = await MakeMapping(h, await MappingCandidate(h, engineering, "SiteA")); var b = await MakeMapping(h, await MappingCandidate(h, engineering, "SiteB"));
        Assert.Equal("DRAFT", a.State); Assert.False(a.CanAdoptIdentitySite);
        var confirmation = MappingAction(a, "CONFIRM");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(confirmation))).Status);
        Assert.Equal("DRAFT", (await service.ReadAsync(a.Reference.Id, null)).State);
        await EngineeringAction(h, "erp-product", "confirm-engineering");
        var result = await service.CommandAsync(confirmation); Assert.Equal(result, await service.CommandAsync(confirmation)); await service.CommandAsync(MappingAction(b, "CONFIRM"));
        var both = await service.ListAsync(engineering.Reference.Id); Assert.Equal(2, both.Length); Assert.All(both, row => Assert.True(row.CanAdoptIdentitySite));
        Assert.NotEqual(both[0].Candidate.BusinessRef!.Id, both[1].Candidate.BusinessRef!.Id); Assert.NotEqual(both[0].Candidate.SiteRef!.Id, both[1].Candidate.SiteRef!.Id);
        foreach (var mapping in both)
        {
            Assert.Equal("confirm", mapping.EngineeringConfirmation!.Action); Assert.Equal("confirm-engineering", mapping.BusinessConfirmation!.Action);
            Assert.Equal(h.User, mapping.BusinessConfirmation.ActorId); Assert.NotEqual(mapping.EngineeringConfirmation.Owner, mapping.BusinessConfirmation.Owner);
            Assert.Equal("REQUIRED_FENCED", mapping.TechnicalApplicability); Assert.Equal("REQUIRED_FENCED", mapping.LegalEntityApplicability);
            Assert.Equal(mapping.Reference, (await ConsumeMapping(h, mapping.Reference)).Reference);
        }
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(row => row.ActionCode == "edit" && row.MenuId == h.Db.Sys_Menus.Where(m => m.MenuKey == "erp-product").Select(m => m.MenuId).Single()));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_unresolved_draft_can_be_saved_but_not_confirmed_or_replayed_with_changed_body()
    {
        await using var h = await Seed(); await GrantEngineering(h); var engineering = await MakeEngineering(h); var service = EngineeringMappings(h);
        var draft = MappingDraft(new() { EngineeringRef = engineering.Reference, Conflicts = "Business material still unknown" }); var result = await service.CommandAsync(draft);
        Assert.Equal(result, await service.CommandAsync(draft)); var value = await service.ReadAsync(draft.MappingId, null); Assert.False(value.CanAdoptIdentitySite);
        Assert.Equal("ENGINEERING_BUSINESS_SOURCE_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(MappingAction(value, "CONFIRM")))).Message);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(draft with { Candidate = draft.Candidate! with { Evidence = "different" } }))).Message);
        var late = MappingDraft(new() { EngineeringRef = engineering.Reference }); Assert.Equal("UNKNOWN", (await service.OperationAsync(late.OperationId, false)).State);
        Assert.Equal("CLOSED", (await service.OperationAsync(late.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(late))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_real_site_ABA_and_engineering_disable_invalidate_new_adoption_preserving_history()
    {
        await using var h = await Seed(); await GrantMappingRead(h); await EngineeringAction(h, "erp-product", "confirm-engineering");
        var engineering = await MakeEngineering(h); var service = EngineeringMappings(h); var candidate = await MappingCandidate(h, engineering);
        var draft = await MakeMapping(h, candidate); await service.CommandAsync(MappingAction(draft, "CONFIRM")); var original = await service.ReadAsync(draft.Reference.Id, null);
        await h.Db.Space_Sites.Where(row => row.Id == candidate.SiteRef!.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Enable, false));
        await h.Db.Space_Sites.Where(row => row.Id == candidate.SiteRef!.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Enable, true));
        Assert.Equal("ENGINEERING_SITE_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ConsumeMapping(h, original.Reference))).Message);
        var history = await service.ReadAsync(original.Reference.Id, original.Reference.Version); Assert.Equal(original.OriginalJson, history.OriginalJson); Assert.False(history.CanAdoptIdentitySite);
        var refreshed = (await service.SiteChoicesAsync("A")).Single(row => row.Reference.Id == candidate.SiteRef!.Id);
        await service.CommandAsync(MappingAction(original, "DRAFT") with { Candidate = candidate with { SiteRef = refreshed.Reference } });
        var revised = await service.ReadAsync(original.Reference.Id, null); Assert.Null(revised.BusinessConfirmation);
        await service.CommandAsync(MappingAction(revised, "CONFIRM")); var confirmed = await service.ReadAsync(original.Reference.Id, null);
        await Engineering(h).CommandAsync(ReviseEngineering(engineering, engineering.Product with { Enabled = false }));
        Assert.Equal("ENGINEERING_PRODUCT_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ConsumeMapping(h, confirmed.Reference))).Message);
        await service.CommandAsync(MappingAction(confirmed, "DISABLE")); Assert.Equal("DISABLED", (await service.ReadAsync(original.Reference.Id, null)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_commit_failure_rolls_back_confirmation_and_original_then_exact_CAS_writers_arbitrate()
    {
        var interruption = new InterruptCurrencyCommit(); await using var h = await Seed(interruption); await GrantMappingRead(h); await EngineeringAction(h, "erp-product", "confirm-engineering");
        var service = EngineeringMappings(h); var draft = await MakeMapping(h, await MappingCandidate(h, await MakeEngineering(h))); var command = MappingAction(draft, "CONFIRM");
        interruption.Interrupt = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(command))).Message);
        Assert.Equal(draft.OriginalJson, (await service.ReadAsync(draft.Reference.Id, null)).OriginalJson); Assert.Equal("UNKNOWN", (await service.OperationAsync(command.OperationId, false)).State);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<string> Save(EngineeringProductMappingService writer)
        { try { await writer.CommandAsync(command with { OperationId = Guid.NewGuid() }); return "COMMITTED"; } catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return error.Message; } }
        var values = await Task.WhenAll(Save(service), Save(EngineeringMappings(h, peer))); Assert.Single(values, value => value == "COMMITTED"); Assert.Single(values, value => value == "ENGINEERING_MAPPING_CHANGED");
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_engineering_product_v1.mapping_versions CASCADE"))).SqlState);
    }
}
