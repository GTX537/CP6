using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 CheckAuthority(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "plm-configuration-rule", "quality-specification", "plm-controlled-document", "file-content", "plm-engineering-revision", "plm-engineering-product", "space-site", "uom-profile", "plm-engineering-bom", "plm-engineering-routing", "plm-tool-specification", "erp-plate-mold", "mes-machine", "mes-work-center", "product" })
        {
            catalog.RegisterScope(key, key, key == "space-site" ? [5] : [1, 2, 3, 4, 5], 5);
            catalog.RegisterFields(key, key == "quality-specification" ? [new("Metadata", "Metadata"), new("Requirements", "Requirements")]
                : key == "file-content" ? [new("Content", "Content"), new("FileName", "Name")]
                : key == "plm-controlled-document" ? [new("Content", "Content"), new("Metadata", "Metadata")] : []);
        }
        catalog.Seal(); return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static EngineeringSourceCheckService Checks(Harness h, ContentStore files) => new(h.Db, CheckAuthority(h), Documents(h, files), Specifications(h, files));
    private sealed record CheckFixture(ContentStore Files, EngineeringSourceCheckCommandV1 Command, ConfigurationRuleSnapshotV1 Policy);
    private async Task<CheckFixture> CheckSetup(Harness h)
    {
        var q = await QualitySetup(h); var quality = await QualityApprove(h, q); await GrantConfiguration(h);
        var product = await Engineering(h).ReadAsync(q.Document.Body.ProductRef.Id, null);
        var rules = ConfigurationRules(h); var create = await NewConfiguration(rules, product.Reference);
        create = create with { Body = create.Body! with
        {
            Scope = EngineeringSourceCheckValuesV1.PolicyScope,
            EngineeringChecks = new("engineering-check-policy/1", product.Product.Category, "PURCHASED_FINISHED", "APPLIES", "Explicit purchased finished product policy",
                EngineeringSourceCheckValuesV1.Kinds.Select(k => new EngineeringCompositionPolicyV1(k,
                    k is "DOCUMENT" or "QUALITY_SPEC" ? "APPLIES" : "NOT_APPLICABLE", k is "DOCUMENT" or "QUALITY_SPEC" ? 1 : 0,
                    "Purchased finished product with approved specification; no internal manufacturing")).ToArray())
        }};
        await rules.CommandAsync(create); var policy = await rules.ReadAsync(create.RuleId, null);
        policy = await Step(rules, await Step(rules, policy, "SUBMIT"), "APPROVE");
        var revisions = new EngineeringRevisionOwnerV1(h.Db, CheckAuthority(h), Documents(h, q.Files), Specifications(h, q.Files));
        var revision = await revisions.ReadAsync(q.Document.Body.RevisionOrigin.Id, null);
        await revisions.CommandAsync(PatchRevision(revision,
            new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new(ValidOptions(), "APPLIES", "Exact approved policy", policy.Reference) },
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = new(Guid.NewGuid(), "DOCUMENT", "/standard", 0, "APPLIES", "", "", q.Document.Reference) },
            new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT", Component = new(Guid.NewGuid(), "QUALITY_SPEC", "/quality", 1, "APPLIES", "", "", quality.Version.Reference) }));
        revision = await revisions.ReadAsync(revision.Reference.Id, null);
        return new(q.Files, new(Guid.NewGuid(), revision.Reference, revision.CandidateRef, revision.Etag, policy.Reference, "Check exact candidate"), policy);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_native_sources_policy_NA_persisted_original_and_same_key_recovery()
    {
        await using var h = await Seed(); var f = await CheckSetup(h); var checks = Checks(h, f.Files);
        var result = await checks.CheckAsync(f.Command); Assert.Equal(result, await checks.CheckAsync(f.Command));
        var view = await checks.ReadAsync(result.CheckRunId);
        Assert.Equal("PASS", view.Original.State); Assert.Equal("CURRENT", view.Freshness); Assert.NotNull(view.Original.Manifest);
        Assert.Equal(3, view.Original.Items.Count(x => x.State == "NOT_APPLICABLE"));
        Assert.Contains(view.Original.Sources, x => x.Reference.Owner == "Main.Quality.Specification.v1");
        Assert.Equal("COMMITTED", (await checks.OperationAsync(f.Command.OperationId, false)).State);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => checks.CheckAsync(f.Command with { Reason = "Different input" }))).Message);
        Assert.Equal(view.OriginalJson, Assert.Single(await checks.HistoryAsync(f.Command.Revision.Id)).OriginalJson);
        var held = await CheckAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, (a, t) => checks.HoldExact(a, view.Reference, t));
        Assert.Equal(view.Original.Id, held.Id);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_policy_withdrawal_invalidates_new_use_but_preserves_original()
    {
        await using var h = await Seed(); var f = await CheckSetup(h); var checks = Checks(h, f.Files);
        var result = await checks.CheckAsync(f.Command); var before = await checks.ReadAsync(result.CheckRunId);
        await Step(ConfigurationRules(h), f.Policy, "WITHDRAW");
        var after = await checks.ReadAsync(result.CheckRunId); Assert.Equal("STALE", after.Freshness); Assert.Equal(before.OriginalJson, after.OriginalJson);
        Assert.Equal("PASS", after.Original.State);
        Assert.Equal("ENGINEERING_CHECK_NOT_CURRENT_PASS", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            CheckAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, (a, t) => checks.HoldExact(a, before.Reference, t)))).Message);
        var second = await checks.CheckAsync(f.Command with { OperationId = Guid.NewGuid() });
        Assert.Equal("BLOCK", (await checks.ReadAsync(second.CheckRunId)).Original.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringSourceCheck_current_source_revocation_denies_original_disclosure()
    {
        await using var h = await Seed(); var f = await CheckSetup(h); var checks = Checks(h, f.Files);
        var result = await checks.CheckAsync(f.Command);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "quality-specification").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => checks.ReadAsync(result.CheckRunId))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => checks.OperationAsync(f.Command.OperationId, false))).Status);
        // The new check records UNKNOWN, without copying inaccessible source content.
        var unknown = await checks.CheckAsync(f.Command with { OperationId = Guid.NewGuid() });
        await CheckAuthority(h).ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (a, t) =>
        {
            var store = new EngineeringSourceCheckStoreV1(h.Db); await store.Fence(t); var raw = await store.Read(unknown.CheckRunId, t);
            Assert.Contains(raw.Value.Items, x => x.Code == "SOURCE_ACCESS_DENIED" && x.State == "UNKNOWN");
            Assert.DoesNotContain(raw.Value.Sources, x => x.Reference.Owner == "Main.Quality.Specification.v1"); return true;
        });
    }
    [Fact]
    public void EngineeringSourceCheck_policy_cannot_waive_internal_manufacturing_or_reinterpret_options_only()
    {
        var policy = new EngineeringCheckPolicyV1("engineering-check-policy/1", "actual category", "INTERNAL", "APPLIES", "Explicit basis",
            EngineeringSourceCheckValuesV1.Kinds.Select(k => new EngineeringCompositionPolicyV1(k, "NOT_APPLICABLE", 0, "Attempted waiver")).ToArray());
        Assert.Equal("ENGINEERING_CHECK_POLICY_BYPASS", Assert.Throws<OrderPriceCorrectionException>(() => EngineeringSourceCheckValuesV1.Policy(policy)).Message);
    }
}
