using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task InventoryIdentity_department_creation_binding_and_revoked_history_are_explicit(int type)
    {
        await using var h = await Seed(); await GrantInventoryIdentity(h); var o = InventoryIdentity(h); var hidden = await IdentityCreate(o, PurposeIdentity()); await o.CommandAsync(hidden);
        var admin = await MappingAdmin(h); Assert.Equal(hidden.Id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("inventory-identity", "TRIAL", _ => Task.FromResult(h.User), default)).Id);
        await StandaloneScope(h, admin, type); Assert.Empty(await o.ListAsync());
        var missing = await IdentityCreate(o, SelfIdentity()); Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(missing))).Message);
        var c = await IdentityCreate(o, SelfIdentity(), admin.Child); await o.CommandAsync(c); Assert.Equal(c.Id, Assert.Single(await o.ListAsync()).Reference.Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='inventory-identity' AND object_id={c.Id}");
        Assert.Empty(await o.ListAsync()); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.OperationAsync(c.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_CAS_and_close_race_commit_have_one_durable_original()
    {
        await using var h = await Seed(); await GrantInventoryIdentity(h); var o = InventoryIdentity(h); var c = await IdentityCreate(o, PurposeIdentity()); await o.CommandAsync(c);
        await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var peer = InventoryIdentity(h with { Db = db });
        var change = c with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", ExpectedAuthorityVersion = null, Definition = c.Definition! with { Name = "Changed first" } };
        async Task<string> Save(InventoryIdentityOwnerV1 owner, InventoryIdentityCommandV1 command)
        { try { await owner.CommandAsync(command); return "COMMITTED"; } catch (OrderPriceCorrectionException e) { Assert.True(e.Status is 409 or 412); return e.Message; } }
        var outcomes = await Task.WhenAll(Save(o, change), Save(peer, change with { OperationId = Guid.NewGuid(), Definition = change.Definition! with { Name = "Competing" } }));
        Assert.Single(outcomes, x => x == "COMMITTED"); Assert.Single(outcomes, x => x == "INVENTORY_IDENTITY_CHANGED");
        var race = change with { OperationId = Guid.NewGuid(), ExpectedVersion = "2", Definition = change.Definition! with { Name = "Commit versus cancellation" } };
        var write = Save(o, race); var close = peer.OperationAsync(race.OperationId, true); await Task.WhenAll(write, close);
        var result = await o.OperationAsync(race.OperationId, false); Assert.Equal(close.Result, result); Assert.Equal(result.State == "COMMITTED" ? "COMMITTED" : "ORIGINAL_OPERATION_CLOSED", write.Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_lost_commit_ack_recovers_actual_result_and_database_guards_preserve_originals()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); await GrantInventoryIdentity(h); var o = InventoryIdentity(h); var c = await IdentityCreate(o, PurposeIdentity());
        fault.Armed = true; await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(c)); var result = await o.OperationAsync(c.OperationId, false); Assert.Equal("COMMITTED", result.State);
        Assert.Equal(result.Result, await o.CommandAsync(c));
        foreach (var sql in new[] { "DELETE FROM cp6_inventory_identity_v1.heads", "UPDATE cp6_inventory_identity_v1.versions SET digest=digest", "TRUNCATE cp6_inventory_identity_v1.versions CASCADE", "UPDATE cp6_inventory_identity_v1.heads SET revision=revision+2" })
            await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_inventory_identity_v1.versions DISABLE TRIGGER inventory_identity_provenance");
        try { Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ReadAsync(c.Id))).Status); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_inventory_identity_v1.versions ENABLE TRIGGER inventory_identity_provenance"); }
        Assert.Equal(result.Result!.Digest, (await o.ReadAsync(c.Id)).Reference.Digest);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_final_source_revocation_rolls_back_both_identity_and_original_receipt()
    {
        var hook = new CapabilityRevocation(); await using var h = await Seed(hook); var f = await InventoryProductSetup(h); var o = InventoryIdentity(h); var c = await IdentityCreate(o, f.Definition);
        hook.Armed = true; Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1>(() => o.CommandAsync(c))).Message); Assert.False(hook.Armed);
        Assert.Equal("UNKNOWN", (await o.OperationAsync(c.OperationId, false)).State);
        Assert.False(await h.Db.Database.SqlQuery<Guid>($"SELECT object_id AS \"Value\" FROM cp6_inventory_identity_v1.heads WHERE tenant_id={h.Tenant} AND object_id={c.Id}").AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_unknown_technical_and_forged_source_or_changed_identity_are_rejected()
    {
        await using var h = await Seed(); var f = await InventoryProductSetup(h); var o = InventoryIdentity(h); var c = await IdentityCreate(o, f.Definition); var p = f.Definition.Product!;
        foreach (var invalid in new[] { p with { TechnicalKind = "UNKNOWN" }, p with { TechnicalKind = "KNOWN", Baseline = null }, p with { TechnicalKind = "NOT_APPLICABLE", Baseline = p.Product }, p with { TechnicalBasis = "" }, p with { Product = p.Product with { Digest = new string('b', 64) } }, p with { Party = p.Purpose } })
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(c with { Definition = f.Definition with { Product = invalid } }));
        Assert.Equal("UNKNOWN", (await o.OperationAsync(c.OperationId, false)).State); await o.CommandAsync(c);
        Assert.Equal("INVENTORY_IDENTITY_KEY_IMMUTABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CommandAsync(c with { OperationId = Guid.NewGuid(), ExpectedAuthorityVersion = null, ExpectedVersion = "1", Definition = f.Definition with { Code = "REKEY" } }))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task InventoryIdentity_known_technical_uses_real_frozen_native_baseline_and_exact_original()
    {
        // Bind the database through a real tenant registration before any source original exists.
        // This tenant does not grant the consuming tenant any source or registration authority.
        await using var bootstrap = await Seed(); await GrantTransfer(bootstrap); await TransferEnvironment(bootstrap).SaveAsync(RegisterTransfer());
        await using var h = await Seed(); await EnsureInventoryProductMenu(h); Harness? reviewer = null; InventoryProductV1? product = null;
        var f = await BaselineSetup(h, beforeCheck: async () =>
        {
            reviewer = await GrantInventoryIdentity(h, false);
            await GrantTransfer(h, false); await TransferEnvironment(h).SaveAsync(RegisterTransfer());
            // The baseline includes a protected iteration review. Its independent identity approver must
            // be admitted by the real project Owner before the review freezes, not by bypassing that reader.
            var projects = Iterations(h); var project = Assert.Single(await projects.ListAsync("PROJECT"));
            await projects.CommandAsync(IterationChange(project, "EDIT_PROJECT") with
            { Project = project.Body.Project!.Definition with { Members = [..project.Body.Project.Definition.Members, reviewer.User] } });
            if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "product")) await GrantProductIdentity(h);
            await EngineeringAction(h, "space-site", "query");
            foreach (var (menu, action) in new[] { ("erp-product", "edit"), ("erp-product", "confirm-engineering"), ("plm-engineering-product", "confirm"),
                ("erp-product", "confirm-inventory"), ("plm-engineering-product", "confirm-inventory") }) await EngineeringAction(h, menu, action);
            var e = Assert.Single(await Engineering(h).ListAsync(null)); var candidate = await MappingCandidate(h, e, "KNOWN");
            var unit = await MakeUnit(Units(h), BaseUnit("IU"));
            await h.Db.ProductMasters.Where(x => x.Id == candidate.BusinessRef!.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.QtyUnit, "IU"));
            var b = await ProductIdentity(h).ReadAsync(candidate.BusinessRef!.Id); candidate = candidate with { BusinessRef = new("Main.Erp.ProductIdentity.v1", b.ProductId, b.Version, b.Digest) };
            var draft = await MakeMapping(h, candidate); await EngineeringMappings(h).CommandAsync(MappingAction(draft, "CONFIRM")); var map = await EngineeringMappings(h).ReadAsync(draft.Reference.Id, null);
            var units = new CommercialProductUnitOwnerV1(h.Db, UnitAuthority(h)); await units.BindAsync(new() { OperationId = Guid.NewGuid(), ProductId = b.ProductId, ProductVersion = b.Version, ExpectedBindingVersion = "absent", ProfileRef = unit.Reference });
            var binding = await units.ReadAsync(b.ProductId, null); var party = await ApprovedIdentity(h, reviewer, SelfIdentity()); var purpose = await ApprovedIdentity(h, reviewer, PurposeIdentity());
            product = new(candidate.BusinessRef, new("Main.Erp.ProductUnit.v1", b.ProductId, binding.Version, binding.Digest), map.Reference, "KNOWN", null, "Adopt actual reviewed frozen technical original", party.Reference, purpose.Reference);
        });
        var baselineOwner = Baselines(h, f.Review.Source.Files); var frozen = await baselineOwner.FreezeAsync(f.Command); var baseline = await baselineOwner.ReadAsync(frozen.Baseline.Id);
        var o = InventoryIdentity(h, f.Review.Source.Files); var c = await IdentityCreate(o, new("PRODUCT", "KNOWN", "Frozen product", "Actual baseline identity", null, null, product! with { Baseline = baseline.Reference }));
        await o.CommandAsync(c); await InventoryIdentity(reviewer!, f.Review.Source.Files).CommandAsync(IdentityAction(await o.ReadAsync(c.Id), "APPROVE")); var v = await o.ReadAsync(c.Id);
        Assert.True(v.CanSelect); Assert.Equal(baseline.OriginalJson, Assert.Single(v.Original.Sources, x => x.Path == "technicalBaseline").OriginalJson);
        var option = (await o.CatalogueAsync(v.Reference)).Option; Assert.Equal(baseline.Reference, option.TechnicalIdentity.RevisionRef); Assert.Null(option.TechnicalIdentity.ApplicabilityRef);
        var consumed = await TransferSources(h, f.Review.Source.Files).ReadAsync(v.Reference);
        Assert.Equal(baseline.Reference, consumed.Product.TechnicalIdentity.RevisionRef); Assert.Equal(ScopeEnvironment, consumed.Environment.EnvironmentId);
        var projects = Iterations(h); var project = Assert.Single(await projects.ListAsync("PROJECT"));
        await projects.CommandAsync(IterationChange(project, "EDIT_PROJECT") with
        { Project = project.Body.Project!.Definition with { Members = project.Body.Project.Definition.Members.Where(x => x != reviewer!.User).ToArray() } });
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => InventoryIdentity(reviewer!, f.Review.Source.Files).ReadAsync(c.Id))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TransferSources(reviewer!, f.Review.Source.Files).ReadAsync(v.Reference))).Status);
    }
}
