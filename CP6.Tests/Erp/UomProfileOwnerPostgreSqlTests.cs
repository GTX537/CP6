using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Tests.Platform;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantUomProfile(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "erp-uom-profile");
        if (menu == null)
        {
            menu = new() { MenuId = 9147, MenuKey = "erp-uom-profile", MenuName = "Unit profiles" };
            h.Db.Sys_Menus.Add(menu);
        }
        foreach (var action in new[] { "query", "add", "edit" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "uom-profile", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    private static StringContent UomJson(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static OperationsOwnerV1 UnitAuthority(Harness h, CP6.Core.EFDbContext.CP6Context? context = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "uom-profile", "product" })
        { catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 5); catalog.RegisterFields(resource, []); }
        catalog.Seal(); var db = context ?? h.Db;
        return new OperationsOwnerV1(db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static UomProfileOwnerV1 Units(Harness h, CP6.Core.EFDbContext.CP6Context? context = null)
        => new(context ?? h.Db, UnitAuthority(h, context));
    private static UomProfileInputV1 BaseUnit(string code = "EA", string dimension = "COUNT", int scale = 18) => new()
    { Code = code, Name = code, DimensionKey = dimension, QuantityScale = scale, Numerator = "1", Denominator = "1", Mode = "BASE", Enabled = true };
    private static async Task<UomProfileCommandV1> NewUnit(UomProfileOwnerV1 owner, UomProfileInputV1? profile = null, Guid? department = null)
        => new() { OperationId = Guid.NewGuid(), ProfileId = Guid.NewGuid(), Action = "CREATE", Profile = profile ?? BaseUnit(),
            DepartmentId = department, ExpectedAuthorityVersion = (await owner.CreationContextAsync()).AuthorityVersion };
    private static UomProfileCommandV1 Revise(UomProfileVersionV1 current, UomProfileInputV1 profile) => new()
    { OperationId = Guid.NewGuid(), ProfileId = current.Reference.Id, Action = "REVISE", ExpectedVersion = current.Reference.Version, Profile = profile };
    private static async Task<UomProfileVersionV1> MakeUnit(UomProfileOwnerV1 owner, UomProfileInputV1? profile = null)
    { var command = await NewUnit(owner, profile); await owner.CommandAsync(command); return await owner.ReadAsync(command.ProfileId, null); }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_actual_http_creation_original_replay_and_current_revocation()
    {
        await using var h = await Seed(); await GrantUomProfile(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var context = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/uom-profiles/owner-v1/creation-context"));
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var contextJson = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var authority = contextJson.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString();
        var operation = Guid.NewGuid(); var profile = Guid.NewGuid();
        var command = new
        {
            operationId = operation, action = "CREATE", profileId = profile, expectedVersion = (string?)null,
            expectedAuthorityVersion = authority, departmentId = (Guid?)null,
            profile = new { code = "EA-" + profile.ToString("N")[..8], name = "個", dimensionKey = "COUNT",
                quantityScale = 18, packMultiple = (string?)null, mode = "BASE", baseRef = (object?)null,
                numerator = "1", denominator = "1", enabled = true }
        };
        async Task<HttpResponseMessage> Save(object body)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, "api/uom-profiles/owner-v1/commands");
            request.Content = UomJson(body); return await host.Send(request);
        }
        using var first = await Save(command); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(command); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var receipt = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(profile, receipt.RootElement.GetProperty("data").GetProperty("objectId").GetGuid());
        Assert.Equal("1", receipt.RootElement.GetProperty("data").GetProperty("version").GetString());
        using var current = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/uom-profiles/owner-v1/{profile:D}"));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var menuId = await h.Db.Sys_Menus.Where(m => m.MenuKey == "erp-uom-profile").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "query" && a.MenuId == menuId).ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/uom-profiles/owner-v1/operations/{operation:D}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.True(denied.Headers.CacheControl?.NoStore);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_native_history_CAS_no_reused_identity_and_original_close_reject_late_writes()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var owner = Units(h);
        var create = await NewUnit(owner); var receipt = await owner.CommandAsync(create);
        Assert.Equal(receipt, await owner.CommandAsync(create));
        var original = await owner.ReadAsync(create.ProfileId, "1");
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(
            create with { Profile = create.Profile! with { Name = "Different" } }))).Message);
        var disabled = Revise(original, original.Profile with { Enabled = false });
        await owner.CommandAsync(disabled);
        Assert.Equal("2", (await owner.ReadAsync(create.ProfileId, null)).Reference.Version);
        var history = await owner.ReadAsync(create.ProfileId, "1");
        Assert.Equal(original.OriginalJson, history.OriginalJson); Assert.False(history.IsCurrent);
        Assert.Equal("UOM_VERSION_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(
            disabled with { OperationId = Guid.NewGuid() }))).Message);
        Assert.Equal("UOM_IDENTITY_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(async () =>
            await owner.CommandAsync(await NewUnit(owner)))).Message);
        var late = await NewUnit(owner, BaseUnit("BOX"));
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(late.OperationId, false)).State);
        Assert.Equal("CLOSED", (await owner.OperationAsync(late.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(late))).Message);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_uom_profile_v1.heads WHERE tenant_id={h.Tenant}").SingleAsync());
        foreach (var sql in new[] { "UPDATE cp6_uom_profile_v1.versions SET body_bytes=decode('7b7d','hex')",
            "DELETE FROM cp6_uom_profile_v1.versions", "TRUNCATE cp6_uom_profile_v1.versions CASCADE",
            "UPDATE cp6_uom_profile_v1.heads SET revision=1", "DELETE FROM cp6_uom_profile_v1.heads" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        Assert.Equal(original.OriginalJson, (await owner.ReadAsync(create.ProfileId, "1")).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_exact_large_quantities_fractional_precision_and_pack_rules_never_round()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var owner = Units(h);
        var each = await MakeUnit(owner);
        var quarter = await MakeUnit(owner, BaseUnit("QTR") with { Mode = "RATIO", BaseRef = each.Reference, Numerator = "1", Denominator = "4" });
        Assert.Equal("0.500000000000000000", (await owner.ConvertAsync(new(quarter.Reference, each.Reference, "2"))).ConvertedQuantity);
        Assert.Equal("0.000000000000100000", (await owner.ConvertAsync(new(each.Reference, each.Reference, "0.0000000000001"))).ConvertedQuantity);
        var huge = "100000000000000000000000000000000000000000000000001";
        Assert.Equal(huge + ".000000000000000000", (await owner.ConvertAsync(new(each.Reference, each.Reference, huge))).ConvertedQuantity);
        Assert.Equal("-0.500000000000000000", (await owner.ConvertAsync(new(quarter.Reference, each.Reference, "-2"))).ConvertedQuantity);
        var third = await MakeUnit(owner, BaseUnit("THRD") with { Mode = "RATIO", BaseRef = each.Reference, Denominator = "3" });
        Assert.Equal("UOM_CONVERSION_NOT_EXACT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ConvertAsync(new(third.Reference, each.Reference, "1")))).Message);
        var packed = await MakeUnit(owner, BaseUnit("PACK") with { Mode = "RATIO", BaseRef = each.Reference, PackMultiple = "0.5" });
        Assert.Equal("UOM_PACK_MULTIPLE_VIOLATION", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ConvertAsync(new(packed.Reference, each.Reference, "0.25")))).Message);
        foreach (var invalid in new[] { "1e3", " 1", "+1", "01", "-0", "0.0000000000000000001" })
            Assert.Equal(422, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ConvertAsync(new(each.Reference, each.Reference, invalid)))).Status);
        var kilograms = await MakeUnit(owner, BaseUnit("KG", "MASS"));
        Assert.Equal(422, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ConvertAsync(new(each.Reference, kilograms.Reference, "1")))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_base_revisions_invalidate_derived_adoption_but_allow_explicit_deactivation_and_history()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var owner = Units(h);
        var each = await MakeUnit(owner);
        var box = await MakeUnit(owner, BaseUnit("BOX") with { Mode = "RATIO", BaseRef = each.Reference, Numerator = "12" });
        await owner.CommandAsync(Revise(each, each.Profile with { Name = "Current base definition" }));
        Assert.Equal("UOM_PROFILE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ConvertAsync(new(box.Reference, box.Reference, "1")))).Message);
        await owner.CommandAsync(Revise(box, box.Profile with { Enabled = false }));
        Assert.Equal(box.OriginalJson, (await owner.ReadAsync(box.Reference.Id, "1")).OriginalJson);
        var stopped = await owner.ReadAsync(box.Reference.Id, null);
        Assert.Equal("UOM_PROFILE_DISABLED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ConvertAsync(new(stopped.Reference, stopped.Reference, "1")))).Message);
        var currentBase = await owner.ReadAsync(each.Reference.Id, null);
        await owner.CommandAsync(Revise(stopped, stopped.Profile with { Enabled = true, BaseRef = currentBase.Reference }));
        var resumed = await owner.ReadAsync(box.Reference.Id, null);
        Assert.Equal("12.000000000000000000", (await owner.ConvertAsync(new(resumed.Reference, currentBase.Reference, "1"))).ConvertedQuantity);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task UomProfile_department_creation_is_explicit_current_and_recovery_respects_revocation(int type)
    {
        await using var h = await Seed(); await GrantUomProfile(h); var owner = Units(h);
        var hidden = await MakeUnit(owner); var admin = await MappingAdmin(h);
        var choices = await StandaloneAdmin(h, admin).FindObjectsAsync("uom-profile", "EA", _ => Task.FromResult(h.User), default);
        Assert.Equal(hidden.Reference.Id, Assert.Single(choices).Id);
        await StandaloneScope(h, admin, type);
        Assert.Empty(await owner.ListAsync(null));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(hidden.Reference.Id, "1"))).Status);
        var missingDepartment = await NewUnit(owner, BaseUnit("BOX"));
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(missingDepartment))).Message);
        var explicitCreation = await NewUnit(owner, BaseUnit("BOX"), admin.Child);
        var created = await owner.CommandAsync(explicitCreation);
        Assert.Equal(created.ObjectId, Assert.Single(await owner.ListAsync(null)).Reference.Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false
            WHERE tenant_id={h.Tenant} AND object_kind='uom-profile' AND object_id={created.ObjectId}
            """);
        Assert.Empty(await owner.ListAsync(null));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(explicitCreation.OperationId, false))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_tenants_and_creator_UUID_remain_separate_from_labels_and_legacy_unit_codes()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var owner = Units(h);
        h.Db.MasterGenericCodes.Add(new() { GroupCode = "Unit", Code = "EA", Name = "Legacy code only" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Empty(await owner.ListAsync("EA"));
        var own = await MakeUnit(owner);
        await using var foreign = await Seed(); await GrantUomProfile(foreign);
        var foreignUnit = await MakeUnit(Units(foreign)); Assert.NotEqual(own.Reference.Id, foreignUnit.Reference.Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(foreignUnit.Reference.Id, null))).Status);
        await h.Db.Sys_RoleDataScopes.Where(row => row.ResourceKey == "uom-profile").ExecuteUpdateAsync(update => update.SetProperty(row => row.ScopeType, 1));
        await h.Db.Sys_Users.Where(row => row.Id == h.User).ExecuteUpdateAsync(update => update.SetProperty(row => row.UserName, "renamed-" + h.User.ToString("N")));
        Assert.Equal(own.Reference, (await owner.ReadAsync(own.Reference.Id, null)).Reference);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_product_binding_actual_http_pins_both_native_versions_and_rejects_stale_adoption()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantProductIdentity(h);
        var productMenu = await h.Db.Sys_Menus.SingleAsync(row => row.MenuKey == "erp-product");
        h.Db.Sys_MenuActions.Add(new() { MenuId = productMenu.MenuId, ActionCode = "edit", ActionName = "Edit product" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = productMenu.MenuId, ActionCode = "edit" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var owner = Units(h); var each = await MakeUnit(owner); var id = await SeedIdentityProduct(h);
        await h.Db.ProductMasters.Where(row => row.Id == id).ExecuteUpdateAsync(update => update.SetProperty(row => row.QtyUnit, "EA"));
        var product = await ProductIdentity(h).ReadAsync(id);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        var command = new { operationId = Guid.NewGuid(), productId = id, expectedBindingVersion = "absent", productVersion = product.Version, profileRef = each.Reference };
        var request = OperationsRequest(cookies, HttpMethod.Post, "api/products/identity-owner-v1/unit-bindings/commands"); request.Content = UomJson(command);
        using var saved = await host.Send(request); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var current = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/products/identity-owner-v1/{id:D}/unit-profile"));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode); Assert.True(current.Headers.CacheControl?.NoStore);
        await h.Db.ProductMasters.Where(row => row.Id == id).ExecuteUpdateAsync(update => update.SetProperty(row => row.QtyUnit, "BOX"));
        await h.Db.ProductMasters.Where(row => row.Id == id).ExecuteUpdateAsync(update => update.SetProperty(row => row.QtyUnit, "EA"));
        using var stale = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/products/identity-owner-v1/{id:D}/unit-profile"));
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var original = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/products/identity-owner-v1/{id:D}/unit-profile?version=1"));
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using var data = JsonDocument.Parse(await original.Content.ReadAsStringAsync());
        Assert.False(data.RootElement.GetProperty("data").GetProperty("currentApplicable").GetBoolean());
        using var editor = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/products/identity-owner-v1/{id:D}/unit-profile/editor"));
        Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
        using var editData = JsonDocument.Parse(await editor.Content.ReadAsStringAsync());
        Assert.Equal("1", editData.RootElement.GetProperty("data").GetProperty("expectedBindingVersion").GetString());
        Assert.NotEqual(product.Version, editData.RootElement.GetProperty("data").GetProperty("productVersion").GetString());
    }
}
