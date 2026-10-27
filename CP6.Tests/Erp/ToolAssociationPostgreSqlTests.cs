using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 AssociationAuthority(Harness h, CP6Context? db = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "plm-tool-specification", "plm-engineering-product", "erp-plate-mold", "wms-plate-mold" })
        { catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 5); catalog.RegisterFields(key, key == "wms-plate-mold" ? ToolFields.Select(f => new FieldRegistry.Field(f, f)) : []); }
        catalog.Seal(); return new(db ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static ToolAssociationOwnerV1 Associations(Harness h, CP6Context? db = null) => new(db ?? h.Db, AssociationAuthority(h, db));
    private static async Task<(ToolAssociationProposalCommandV1 Command, string Etag, ToolSpecificationCommandV1 Draft)> AssociationSetup(Harness h)
    {
        await GrantToolInstance(h); await GrantSpecification(h);
        await EngineeringAction(h, "plm-tool-specification", "instance-associate"); await EngineeringAction(h, "wms-plate-mold", "engineering-link");
        var technical = await SeedPlate(h); var draft = await ToolDraft(h, technical); await Specifications(h).CommandAsync(draft);
        var physical = await SeedToolInstance(h); var view = await ToolInstances(h).ReadAsync(physical);
        var spec = (await Specifications(h).ReadAsync(technical)).Specification!;
        return (new(Guid.NewGuid(), Guid.NewGuid(), spec.Reference, view.Reference,
            new("Product A", "Cut A", "Configuration A", "Trial relation", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null), "Measured exact drawing against physical serial"), view.Etag, draft);
    }
    private static ToolAssociationDecisionCommandV1 AssociationDecision(ToolAssociationViewV1 view, string decision = "CONFIRM")
        => new(Guid.NewGuid(), view.Subject.ProposalId, view.RoundId, view.SubjectDigest, view.ControlRevision, view.Generation, decision,
            "Inspected this exact instance against this exact technical revision; maintenance qualification remains unknown");
    private static Task<ToolAssociationResultV1> AssociationDecide(ToolAssociationOwnerV1 owner, bool engineering, ToolAssociationViewV1 view, string decision = "CONFIRM")
        => engineering ? new ToolAssociationEngineeringOwnerV1(owner).DecideAsync(ToolAssociationOwnerV1.Etag(view), AssociationDecision(view, decision))
            : new ToolAssociationPhysicalOwnerV1(owner).DecideAsync(ToolAssociationOwnerV1.Etag(view), AssociationDecision(view, decision));
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_HTTP_requires_both_current_owner_queries()
    {
        await using var h = await Seed(); await GrantToolInstance(h);
        var id = await SeedToolInstance(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get,
            $"/api/wms/specials/v1/tools/{id}/engineering-links"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_exact_candidates_and_two_real_owner_decisions_preserve_independent_native_facts()
    {
        await using var h = await Seed(); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h);
        var native = await h.Db.PlateMolds.AsNoTracking().SingleAsync(x => x.Id == c.Technical.Id);
        Assert.Empty(await owner.CandidatesAsync(c.Physical.Id, native.WdPtnNo, native.WdRev + 1));
        var candidate = Assert.Single(await owner.CandidatesAsync(c.Physical.Id, native.WdPtnNo, native.WdRev)); Assert.Equal(c.Technical, candidate.Technical);
        var before = await ToolInstances(h).ReadAsync(c.Physical.Id); var proposal = await owner.ProposeAsync(c.Physical.Id, etag, c);
        Assert.Equal("PROPOSED", proposal.Outcome); var view = await owner.ReadAsync(c.ProposalId);
        Assert.Equal("UNKNOWN", view.State); Assert.Equal(2, view.UnresolvedRoles.Length);
        var engineering = await AssociationDecide(owner, true, view); Assert.Equal("UNKNOWN", engineering.Outcome);
        view = await owner.ReadAsync(c.ProposalId); Assert.Equal("Main.Plm.ToolInstanceConformance.v1", view.Engineering!.Owner); Assert.Null(view.Physical);
        var physical = await AssociationDecide(owner, false, view); Assert.Equal("CONFIRMED", physical.Outcome); Assert.True(physical.NotExecutionPermit);
        view = await owner.ReadAsync(c.ProposalId); Assert.Equal("KNOWN", view.CurrentKnowledge); Assert.True(view.EverConfirmed); Assert.Empty(view.UnresolvedRoles);
        Assert.Equal("Main.Wms.ToolAssociationAdoption.v1", view.Physical!.Owner); Assert.NotEqual(view.Engineering.Id, view.Physical.Id);
        Assert.Equal(before.Reference, (await ToolInstances(h).ReadAsync(c.Physical.Id)).Reference);
        Assert.Equal("DRAFT", (await Specifications(h).ReadAsync(c.Technical.Id)).Specification!.State);
        Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Equal(proposal, await owner.ProposeAsync(c.Physical.Id, etag, c));
        Assert.Equal("PROPOSED", (await owner.ReadAsync(c.ProposalId, "1")).LastKnownState);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_association_v1.decisions"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_tool_association_v1.versions"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("UPDATE cp6_tool_association_v1.heads SET revision=revision+2"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_professional_actions_are_independent_and_current_source_scopes_hide_history_and_recovery()
    {
        await using var h = await Seed(); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h); await owner.ProposeAsync(c.Physical.Id, etag, c);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-tool-specification").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "instance-associate").ExecuteDeleteAsync();
        var view = await owner.ReadAsync(c.ProposalId);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => AssociationDecide(owner, true, view))).Status);
        await AssociationDecide(owner, false, view); Assert.Equal("UNKNOWN", (await owner.ReadAsync(c.ProposalId)).State);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "plm-tool-specification").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.PlateMolds.Where(x => x.Id == c.Technical.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "another"));
        Assert.Empty(await owner.ListAsync(c.Physical.Id)); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(c.ProposalId, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(c.OperationId, false))).Status);
        await using var foreign = await Seed(); await AssociationSetup(foreign);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Associations(foreign).ReadAsync(c.ProposalId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_refresh_discards_both_old_slots_and_stale_round_cannot_apply()
    {
        await using var h = await Seed(); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h); await owner.ProposeAsync(c.Physical.Id, etag, c);
        var old = await owner.ReadAsync(c.ProposalId); await AssociationDecide(owner, true, old);
        var one = await owner.ReadAsync(c.ProposalId); await AssociationDecide(owner, false, one, "REFRESH");
        var fresh = await owner.ReadAsync(c.ProposalId); Assert.NotEqual(old.RoundId, fresh.RoundId); Assert.Equal("2", fresh.Generation); Assert.Null(fresh.Engineering);
        var stale = await new ToolAssociationPhysicalOwnerV1(owner).DecideAsync(ToolAssociationOwnerV1.Etag(fresh), AssociationDecision(old));
        Assert.False(stale.Applied); Assert.Equal("STALE_OBSERVATION", stale.Outcome);
        await AssociationDecide(owner, false, fresh); Assert.Equal("UNKNOWN", (await owner.ReadAsync(c.ProposalId)).State);
        await AssociationDecide(owner, true, await owner.ReadAsync(c.ProposalId)); Assert.Equal("CONFIRMED", (await owner.ReadAsync(c.ProposalId)).State);
        Assert.Equal(stale, (await owner.OperationAsync(stale.OperationId, false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_source_and_permission_ABA_invalidate_current_knowledge_and_old_confirmation()
    {
        await using var h = await Seed(); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h); await owner.ProposeAsync(c.Physical.Id, etag, c);
        await AssociationDecide(owner, true, await owner.ReadAsync(c.ProposalId));
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-tool-specification").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "instance-associate").ExecuteDeleteAsync();
        await EngineeringAction(h, "plm-tool-specification", "instance-associate");
        var p = await owner.ReadAsync(c.ProposalId); var invalid = await AssociationDecide(owner, false, p);
        Assert.False(invalid.Applied); Assert.Equal("STALE_OBSERVATION", invalid.Outcome); Assert.Equal("2", invalid.Generation);
        p = await owner.ReadAsync(c.ProposalId); await AssociationDecide(owner, true, p); await AssociationDecide(owner, false, await owner.ReadAsync(c.ProposalId));
        Assert.Equal("CONFIRMED", (await owner.ReadAsync(c.ProposalId)).State);
        await h.Db.PlateMoldStocks.Where(x => x.Id == c.Physical.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedShots, 501));
        await h.Db.PlateMoldStocks.Where(x => x.Id == c.Physical.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedShots, 500));
        var changed = await owner.ReadAsync(c.ProposalId); Assert.Equal("UNKNOWN", changed.State); Assert.Equal("CONFIRMED", changed.LastKnownState); Assert.True(changed.EverConfirmed);
        var result = await AssociationDecide(owner, false, changed); Assert.False(result.Applied); Assert.Equal("SOURCE_CHANGED", result.Outcome);
        Assert.Equal("PROPOSED", (await owner.OperationAsync(c.OperationId, false)).Result!.Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_technical_revision_never_silently_retargets_and_reject_or_withdraw_never_revives()
    {
        await using var h = await Seed(); var (c, etag, draft) = await AssociationSetup(h); var owner = Associations(h); await owner.ProposeAsync(c.Physical.Id, etag, c);
        await AssociationDecide(owner, true, await owner.ReadAsync(c.ProposalId)); await AssociationDecide(owner, false, await owner.ReadAsync(c.ProposalId));
        await Specifications(h).CommandAsync(NextDraft(draft, await Specifications(h).ReadAsync(c.Technical.Id)));
        Assert.Equal("UNKNOWN", (await owner.ReadAsync(c.ProposalId)).State);
        Assert.Equal("TOOL_TECHNICAL_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ProposeAsync(c.Physical.Id, etag, c with { OperationId = Guid.NewGuid(), ProposalId = Guid.NewGuid() }))).Message);
        var revoked = await AssociationDecide(owner, false, await owner.ReadAsync(c.ProposalId), "WITHDRAW"); Assert.Equal("REVOKED", revoked.Outcome);
        var terminal = await owner.ReadAsync(c.ProposalId);
        Assert.Equal("TOOL_ASSOCIATION_TERMINAL", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => AssociationDecide(owner, true, terminal))).Message);
        var next = c with { OperationId = Guid.NewGuid(), ProposalId = Guid.NewGuid(), Technical = (await Specifications(h).ReadAsync(c.Technical.Id)).Specification!.Reference, Supersedes = c.ProposalId };
        await owner.ProposeAsync(c.Physical.Id, etag, next); var rejected = await AssociationDecide(owner, true, await owner.ReadAsync(next.ProposalId), "REJECT");
        Assert.Equal("REJECTED", rejected.Outcome); Assert.False((await owner.ReadAsync(next.ProposalId)).EverConfirmed);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_same_technical_version_keeps_instances_independent_and_allows_only_one_current_link_per_instance()
    {
        await using var h = await Seed(); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h); await owner.ProposeAsync(c.Physical.Id, etag, c);
        var other = new CP6.Entity.DomainModels.Wms.PlateMoldStock { PlateNo = "PLT2-OTHER", PlateType = "MOLD", Status = 0, UsedShots = 7, Creator = "price-owner" };
        h.Db.PlateMoldStocks.Add(other); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var otherView = await ToolInstances(h).ReadAsync(other.Id);
        var otherProposal = c with { OperationId = Guid.NewGuid(), ProposalId = Guid.NewGuid(), Physical = otherView.Reference };
        await owner.ProposeAsync(other.Id, otherView.Etag, otherProposal);
        await AssociationDecide(owner, true, await owner.ReadAsync(c.ProposalId)); await AssociationDecide(owner, false, await owner.ReadAsync(c.ProposalId));
        Assert.Equal("UNKNOWN", (await owner.ReadAsync(otherProposal.ProposalId)).State);
        var duplicate = c with { OperationId = Guid.NewGuid(), ProposalId = Guid.NewGuid() }; await owner.ProposeAsync(c.Physical.Id, etag, duplicate);
        await AssociationDecide(owner, true, await owner.ReadAsync(duplicate.ProposalId));
        var duplicateView = await owner.ReadAsync(duplicate.ProposalId);
        Assert.Equal("TOOL_ASSOCIATION_ALREADY_CONFIRMED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => AssociationDecide(owner, false, duplicateView))).Message);
        Assert.Equal(7, (await ToolInstances(h).ReadAsync(other.Id)).Source.GetProperty("usedShots").GetInt32());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_lost_commit_ack_original_result_conflicts_and_close_are_durable()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h);
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ProposeAsync(c.Physical.Id, etag, c))).Message);
        var original = (await owner.OperationAsync(c.OperationId, false)).Result!;
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 443 && x.ActionCode == "engineering-link").ExecuteDeleteAsync();
        Assert.Equal(original, await owner.ProposeAsync(c.Physical.Id, etag, c));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ProposeAsync(c.Physical.Id, etag, c with { Reason = "Changed" }))).Message);
        var closed = Guid.NewGuid(); Assert.Equal("UNKNOWN", (await owner.OperationAsync(closed, false)).State); await owner.OperationAsync(closed, true);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ProposeAsync(c.Physical.Id, etag, c with { OperationId = closed }))).Message);
        Assert.Single(await owner.ListAsync(c.Physical.Id));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_actual_HTTP_pair_query_confirmation_csrf_and_original_recovery()
    {
        await using var h = await Seed(); var (c, etag, _) = await AssociationSetup(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<HttpResponseMessage> Send(string path, object body, string match, bool csrf = true)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, path); request.Content = JsonContent.Create(body); request.Headers.TryAddWithoutValidation("If-Match", match);
            if (!csrf) request.Headers.Remove("X-CSRF-TOKEN"); return await host.Send(request);
        }
        using var denied = await Send($"/api/wms/specials/v1/tools/{c.Physical.Id}/engineering-links", c, etag, false); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var saved = await Send($"/api/wms/specials/v1/tools/{c.Physical.Id}/engineering-links", c, etag); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.True(saved.Headers.CacheControl!.NoStore);
        var owner = Associations(h); var p = await owner.ReadAsync(c.ProposalId);
        using var engineering = await Send("/api/tool-specifications/owner-v1/instance-association-decisions", AssociationDecision(p), ToolAssociationOwnerV1.Etag(p)); Assert.Equal(HttpStatusCode.OK, engineering.StatusCode);
        p = await owner.ReadAsync(c.ProposalId); var command = AssociationDecision(p);
        using var physical = await Send("/api/wms/specials/v1/tools/engineering-link-decisions", command, ToolAssociationOwnerV1.Etag(p)); Assert.Equal(HttpStatusCode.OK, physical.StatusCode);
        Assert.Equal("CONFIRMED", (await owner.ReadAsync(c.ProposalId)).State);
        using var recovery = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/tool-associations/owner-v1/operations/{command.OperationId}")); Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 215 && x.ActionCode == "query").ExecuteDeleteAsync();
        using var revoked = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/tool-associations/owner-v1/{c.ProposalId}")); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_same_transaction_physical_change_rolls_back_decision_projection_and_journal()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h);
        await owner.ProposeAsync(c.Physical.Id, etag, c); await AssociationDecide(owner, true, await owner.ReadAsync(c.ProposalId));
        var view = await owner.ReadAsync(c.ProposalId); var decision = AssociationDecision(view);
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("AND state='CONFIRMED'")) return; hook.Before = null;
            await using var writer = command.Connection!.CreateCommand(); writer.Transaction = command.Transaction;
            writer.CommandText = "UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=501 WHERE \"Id\"=@id";
            writer.Parameters.Add(new Npgsql.NpgsqlParameter("id", c.Physical.Id)); await writer.ExecuteNonQueryAsync();
        };
        Assert.Equal("TOOL_INSTANCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ToolAssociationPhysicalOwnerV1(owner).DecideAsync(ToolAssociationOwnerV1.Etag(view), decision))).Message);
        var after = await owner.ReadAsync(c.ProposalId); Assert.Equal(view.Revision, after.Revision); Assert.Null(after.Physical);
        Assert.Equal(500, (await ToolInstances(h).ReadAsync(c.Physical.Id)).Source.GetProperty("usedShots").GetInt32());
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(decision.OperationId, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_actual_peer_writer_waits_for_positive_application_and_lost_ack_keeps_original()
    {
        var hook = new CommandHook(); var fault = new CommitFault(); await using var h = await Seed(hook, fault); var (c, etag, _) = await AssociationSetup(h); var owner = Associations(h);
        await owner.ProposeAsync(c.Physical.Id, etag, c); await AssociationDecide(owner, true, await owner.ReadAsync(c.ProposalId));
        var view = await owner.ReadAsync(c.ProposalId); var decision = AssociationDecision(view); Task<int>? writer = null;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("AND state='CONFIRMED'")) return; hook.Before = null;
            writer = Task.Run(async () => { await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); return await peer.PlateMoldStocks.Where(x => x.Id == c.Physical.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedShots, 501)); });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                  AND query LIKE 'UPDATE %T_PlateMoldStock%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            Assert.False(writer.IsCompleted);
        };
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ToolAssociationPhysicalOwnerV1(owner).DecideAsync(ToolAssociationOwnerV1.Etag(view), decision))).Message);
        Assert.Equal(1, await writer!); var recovered = (await owner.OperationAsync(decision.OperationId, false)).Result!; Assert.Equal("CONFIRMED", recovered.Outcome);
        Assert.Equal(recovered, await new ToolAssociationPhysicalOwnerV1(owner).DecideAsync(ToolAssociationOwnerV1.Etag(view), decision));
        Assert.Equal("UNKNOWN", (await owner.ReadAsync(c.ProposalId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_two_actors_recheck_the_original_decider_source_permissions_not_the_current_reader()
    {
        await using var h = await Seed(); var (c, etag, _) = await AssociationSetup(h);
        var second = new CP6.Entity.DomainModels.Sys.Sys_User { UserName = "physical-owner", Password = "not-a-login-fixture", RoleId = 73 };
        h.Db.Sys_Roles.Add(new() { RoleId = 73, RoleName = "physical-only" }); h.Db.Sys_Users.Add(second);
        foreach (var grant in await h.Db.Sys_RoleActions.AsNoTracking().Where(x => x.RoleId == 72 && (x.ActionCode == "query" || x.ActionCode == "engineering-link")).ToArrayAsync())
            h.Db.Sys_RoleActions.Add(new() { RoleId = 73, MenuId = grant.MenuId, ActionCode = grant.ActionCode });
        foreach (var key in new[] { "plm-tool-specification", "plm-engineering-product", "erp-plate-mold", "wms-plate-mold" })
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 73, ResourceKey = key, ScopeType = 5 });
        h.Db.WmsRoleScopes.Add(new() { RoleId = 73, WarehouseCd = "*" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var other = h with { User = second.Id, Http = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = CurrentPermissionContextTests.Principal(second.Id, h.Tenant) } };
        var engineering = Associations(h); var physical = Associations(other);
        await engineering.ProposeAsync(c.Physical.Id, etag, c); await AssociationDecide(engineering, true, await engineering.ReadAsync(c.ProposalId));
        await AssociationDecide(physical, false, await physical.ReadAsync(c.ProposalId)); Assert.Equal("CONFIRMED", (await physical.ReadAsync(c.ProposalId)).State);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-product").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        var stale = await physical.ReadAsync(c.ProposalId); Assert.Equal("UNKNOWN", stale.State); Assert.Contains("ENG_SPEC_CONFIRMATION", stale.UnresolvedRoles);
        var result = await AssociationDecide(physical, false, stale); Assert.False(result.Applied); Assert.Equal("STALE_OBSERVATION", result.Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_association_new_tenant_capability_seed_never_grants_professional_or_physical_confirmation_to_roles()
    {
        await using var h = await Seed();
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-tool-specification").Select(x => x.MenuId).SingleAsync();
        Assert.False(await h.Db.Sys_MenuActions.AnyAsync(x => x.ActionCode == "instance-associate" || x.ActionCode == "engineering-link"));
        CP6.WebApi.Seed.WmsPermissionSeed.EnsureSeeded(h.Db); h.Db.ChangeTracker.Clear();
        Assert.True(await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu && x.ActionCode == "instance-associate"));
        Assert.True(await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == 443 && x.ActionCode == "engineering-link"));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => x.ActionCode == "instance-associate" || x.ActionCode == "engineering-link"));
    }
}
