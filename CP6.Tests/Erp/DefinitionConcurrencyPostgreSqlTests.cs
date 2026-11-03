using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> SecondDefinitionAuthor(Harness h)
    {
        var user = new Sys_User { UserName = "author-two-" + h.Tenant.ToString("N"), Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(user);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var context = new DefaultHttpContext { User = CurrentPermissionContextTests.Principal(user.Id, h.Tenant) };
        var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        return h with { Db = db, User = user.Id, Http = context };
    }
    private static async Task ObserveDefinitionWait(CP6.Core.EFDbContext.CP6Context monitor, string app, Task pending)
    {
        for (var i = 0; i < 100 && !pending.IsCompleted; i++)
        {
            if (await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Competing author never waited on the actual PostgreSQL lock.");
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionConcurrency_two_FLOW_authors_same_token_have_one_winner_and_separate_original_slots()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); var initial = await owner.SaveAsync("race", AuthoringCommand());
        var peer = await SecondDefinitionAuthor(h); await using var peerDb = peer.Db;
        await using var monitor = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var app = "flow-two-authors-" + Guid.NewGuid().ToString("N"); peerDb.Database.SetConnectionString(peerDb.Database.GetConnectionString() + ";Application Name=" + app);
        var other = Authoring(peer); Task<WorkflowCommandResultV1>? competing = null; var calls = 0;
        var first = Authoring(h, async _ =>
        {
            if (++calls == 2)
            {
                competing = other.SaveAsync("race", AuthoringCommand("same-command-id", initial.ResourceVersion, "loser"));
                await ObserveDefinitionWait(monitor, app, competing);
            }
            return h.User;
        });
        var intent = AuthoringCommand("same-command-id", initial.ResourceVersion, "winner"); var saved = await first.SaveAsync("race", intent);
        Assert.Equal("DEFINITION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async () => await competing!.WaitAsync(TimeSpan.FromSeconds(10)))).Message);
        Assert.Equal("winner", (await other.ReadAsync("race")).Fields["Name"]);
        Assert.Equal("NOT_OBSERVED", (await other.OriginalAsync("race", "same-command-id")).Outcome);
        Assert.Equal(saved.ResourceVersion, (await owner.SaveAsync("race", intent)).ResourceVersion);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.audit WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(h.User, await h.Db.Database.SqlQuery<Guid>($"SELECT actor_id AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant} AND command_id='same-command-id'").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionConcurrency_two_FLOW_authors_next_draft_keep_exact_source_and_one_version_number()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h);
        var initial = await owner.SaveAsync("next-race", AuthoringCommand());
        var published = await owner.PublishAsync("next-race", PublicationCommand(await Preview(owner, "next-race", initial)));
        var source = published.Result!.Publication!;
        var peer = await SecondDefinitionAuthor(h); await using var peerDb = peer.Db;
        await using var monitor = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var app = "flow-next-authors-" + Guid.NewGuid().ToString("N"); peerDb.Database.SetConnectionString(peerDb.Database.GetConnectionString() + ";Application Name=" + app);
        var other = Authoring(peer); Task<WorkflowLifecycleResultV2>? competing = null; var calls = 0;
        var first = Authoring(h, async _ =>
        {
            if (++calls == 2)
            {
                competing = other.CreateNextDraftAsync("next-race", NextDraftCommand(published.ResourceVersion!, source.VersionId, "next-two"));
                await ObserveDefinitionWait(monitor, app, competing);
            }
            return h.User;
        });
        var intent = NextDraftCommand(published.ResourceVersion!, source.VersionId, "next-one");
        var created = await first.CreateNextDraftAsync("next-race", intent);
        Assert.Equal("DEFINITION_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async () => await competing!.WaitAsync(TimeSpan.FromSeconds(10)))).Message);
        Assert.Equal(2, created.Result!.Draft!.VersionNumber); Assert.Equal(AuthoringSchema, created.Result.Draft.Fields["SchemaJson"]);
        Assert.Equal(created.ResourceVersion, (await owner.CreateNextDraftAsync("next-race", intent)).ResourceVersion);
        Assert.Equal("NOT_OBSERVED", (await other.LifecycleOriginalAsync("next-race", FlowAuthoringOwnerV2.NextDraftOperation, "next-two")).Outcome);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.drafts WHERE tenant_id={h.Tenant} AND status=0").SingleAsync());
        Assert.Equal(source.VersionId, (await owner.VersionsAsync("next-race")).Single().VersionId);
        Assert.Equal(source.PublishedAtUtc, (await owner.VersionsAsync("next-race")).Single().PublishedAtUtc);
    }
}
