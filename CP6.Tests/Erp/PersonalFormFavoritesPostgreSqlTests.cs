using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PersonalFavorites_HTTP_explicit_assignment_replay_unavailable_placeholder_and_owner_cancellation()
    {
        await using var h = await Seed(); var (owner, form, _) = await PreparePersonal(h);
        await EngineeringAction(h, "oa-form-catalog", "favorite");
        var context = await owner.ContextAsync("personal");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h); const string path = "/api/oa/v2/catalog/personal/favorite";
        async Task<HttpResponseMessage> Set(bool on, string command, bool csrf = true)
        { var request = PersonalHttp(cookies, path, PersonalCommand(new { on }, command), csrf); request.Method = HttpMethod.Put; return await host.Send(request); }
        using (var response = await Set(true, "on"))
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("data").GetProperty("result").GetProperty("favorite").GetBoolean());
        }
        using (var csrf = await Set(false, "csrf-denied", false)) Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var same = await Set(true, "same-desire")) Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.favorites WHERE tenant_id={h.Tenant}").SingleAsync());
        using (var conflict = await Set(false, "on")) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await form.SetActivationAsync("personal", FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current, context.FormVersionId, false, "disabled"));
        using (var favorites = await host.Send(BoundRequest(cookies, "/api/oa/v2/catalog/favorites")))
        {
            Assert.Equal(HttpStatusCode.OK, favorites.StatusCode);
            using var json = JsonDocument.Parse(await favorites.Content.ReadAsStringAsync());
            var item = Assert.Single(json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal("personal", item.GetProperty("formKey").GetString()); Assert.False(item.GetProperty("available").GetBoolean());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("publication").ValueKind);
        }
        using (var denied = await Set(true, "disabled-on")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var removed = await Set(false, "off")) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var absent = await Set(false, "off-again")) Assert.Equal(HttpStatusCode.OK, absent.StatusCode);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.favorites WHERE tenant_id={h.Tenant}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "favorite").ExecuteDeleteAsync();
        using (var original = await Set(true, "on")) Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using (var denied = await Set(false, "revoked-new")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalFavorites_concurrent_connections_set_once_replay_history_and_close_without_toggling()
    {
        await using var h = await Seed(); await PreparePersonal(h); await EngineeringAction(h, "oa-form-catalog", "favorite");
        await using var peerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var app = "favorite-race-" + Guid.NewGuid().ToString("N");
        peerDb.Database.SetConnectionString(peerDb.Database.GetConnectionString() + ";Application Name=" + app);
        await using var monitor = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = PersonalForm(h with { Db = peerDb }); Task<PersonalFormResultV2>? competing = null; var calls = 0;
        var owner = PersonalForm(h, async _ =>
        {
            if (++calls == 2)
            {
                competing = peer.SetFavoriteAsync("personal", PersonalCommand(new { on = true }, "second"));
                var waiting = false;
                for (var i = 0; i < 100 && !competing.IsCompleted; i++)
                {
                    waiting = await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                    if (waiting) break; await Task.Delay(20);
                }
                Assert.True(waiting);
            }
            return h.User;
        });
        owner.Clock = () => new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var original = PersonalCommand(new { on = true }, "first");
        var first = await owner.SetFavoriteAsync("personal", original);
        var second = await competing!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(first.ResourceId, second.ResourceId);
        Assert.Equal(PersonalResult<PersonalFormFavoriteResultV2>(first), PersonalResult<PersonalFormFavoriteResultV2>(second));
        Assert.Single((await peer.FavoritesAsync()).Items);
        await peer.SetFavoriteAsync("personal", PersonalCommand(new { on = false }, "off"));
        Assert.True(PersonalResult<PersonalFormFavoriteResultV2>(await owner.SetFavoriteAsync("personal", original)).Favorite);
        Assert.Empty((await owner.FavoritesAsync()).Items); // A historical receipt never reapplies its desire.
        Assert.Equal("CLOSED", (await peer.CloseAsync(PersonalFormOwnerV2.Favorite, "personal", "unsent")).Outcome);
        Assert.Equal("COMMAND_CLOSED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetFavoriteAsync("personal", PersonalCommand(new { on = true }, "unsent")))).Message);
        var racing = PersonalCommand(new { on = true }, "closing-race");
        async Task<string> Write() { try { return (await owner.SetFavoriteAsync("personal", racing)).Outcome; } catch (ScopeAuthorizationExceptionV1 e) { return e.Message; } }
        var write = Write(); var close = peer.CloseAsync(PersonalFormOwnerV2.Favorite, "personal", "closing-race");
        await Task.WhenAll(write, close);
        Assert.Equal(close.Result.Outcome == "CLOSED" ? "COMMAND_CLOSED" : "SUCCEEDED", write.Result);
        Assert.Equal(close.Result.Outcome == "CLOSED" ? 0 : 1, (await owner.FavoritesAsync()).Total);
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalFavorites_same_key_and_command_are_isolated_by_actor_and_tenant()
    {
        await using var h = await Seed(); var (owner, _, _) = await PreparePersonal(h); await EngineeringAction(h, "oa-form-catalog", "favorite");
        await owner.SetFavoriteAsync("personal", PersonalCommand(new { on = true }, "same"));
        var actor = await GrantDelegate(h); TaskActor(h, actor); var peer = PersonalForm(h with { User = actor });
        Assert.Empty((await peer.FavoritesAsync()).Items);
        Assert.Equal("NOT_OBSERVED", (await peer.OriginalAsync(PersonalFormOwnerV2.Favorite, "personal", "same")).Outcome);
        await peer.SetFavoriteAsync("personal", PersonalCommand(new { on = false }, "same"));
        TaskActor(h, h.User); Assert.Single((await owner.FavoritesAsync()).Items);
        await using var foreign = await Seed(); var (stranger, _, _) = await PreparePersonal(foreign); await EngineeringAction(foreign, "oa-form-catalog", "favorite");
        Assert.Empty((await stranger.FavoritesAsync()).Items); Assert.False(Assert.Single((await stranger.CatalogAsync()).Items).Favorite);
        await stranger.SetFavoriteAsync("personal", PersonalCommand(new { on = true }, "same"));
        Assert.Single((await stranger.FavoritesAsync()).Items); Assert.Single((await owner.FavoritesAsync()).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalFavorites_lost_commit_current_read_revocation_and_transaction_failure_preserve_original_scope()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var (owner, _, _) = await PreparePersonal(h);
        await EngineeringAction(h, "oa-form-catalog", "favorite");
        var command = PersonalCommand(new { on = true }, "lost"); fault.Armed = true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetFavoriteAsync("personal", command))).Message);
        Assert.True(PersonalResult<PersonalFormFavoriteResultV2>(await owner.OriginalAsync(PersonalFormOwnerV2.Favorite, "personal", "lost")).Favorite);
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "favorite").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.favorites DISABLE TRIGGER favorite_writer");
        try
        {
            Assert.Equal("SUCCEEDED", (await owner.SetFavoriteAsync("personal", command)).Outcome);
            Assert.Single((await owner.FavoritesAsync()).Items);
            Assert.Equal("FAVORITE_WRITER_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SetFavoriteAsync("personal", PersonalCommand(new { on = false }, "blocked")))).Message);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.favorites ENABLE TRIGGER favorite_writer"); }
        await EngineeringAction(h, "oa-form-catalog", "favorite");
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.commands ADD CONSTRAINT favorite_result_unavailable CHECK(operation<>'FORM_FAVORITE_SET' OR command_id<>'rollback' OR NOT completed) NOT VALID");
        try
        {
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => owner.SetFavoriteAsync("personal", PersonalCommand(new { on = false }, "rollback")));
            Assert.Single((await owner.FavoritesAsync()).Items);
            Assert.Equal("NOT_OBSERVED", (await owner.OriginalAsync(PersonalFormOwnerV2.Favorite, "personal", "rollback")).Outcome);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.commands DROP CONSTRAINT favorite_result_unavailable"); }
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_personal_form_v2.favorites WHERE tenant_id={h.Tenant}"));
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.OriginalAsync(PersonalFormOwnerV2.Favorite, "personal", "lost"))).Message);
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.FavoritesAsync())).Message);
    }
}
