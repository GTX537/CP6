using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task SeedCrmExportPolicyEditor(Harness h)
    {
        await GrantPolicyHttpPermissions(h);
        var db = h.Db;
        var actions = await (from grant in db.Sys_RoleActions
                             join menu in db.Sys_Menus on grant.MenuId equals menu.MenuId
                             where grant.RoleId == 71 && menu.MenuKey != "crm-privacy"
                             select new { grant.MenuId, grant.ActionCode }).Distinct().ToListAsync();
        var known = await db.Sys_MenuActions.AsNoTracking().ToListAsync();
        db.Sys_MenuActions.AddRange(actions.Where(a => !known.Any(k => k.MenuId == a.MenuId && k.ActionCode == a.ActionCode))
            .Select(a => new Sys_MenuAction { MenuId = a.MenuId, ActionCode = a.ActionCode, ActionName = a.ActionCode }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task ExerciseCrmExportRoleRevocation(AuthenticationApiProcess host, Harness h, string bridge, Task childExited)
    {
        const string path = "api/iam/role-policies/actions/71";
        var cookies = await Login(host, h);
        using var editorResponse = await host.Send(Request(HttpMethod.Get, "api/iam/role-policies/actions/editor?roleId=71", cookies));
        Assert.True(editorResponse.StatusCode == HttpStatusCode.OK, await editorResponse.Content.ReadAsStringAsync());
        var editor = (await editorResponse.Content.ReadFromJsonAsync<IamRolePolicyEditorV1>())!;
        var privacyMenu = Assert.Single(editor.Catalog.Menus, menu => menu.MenuKey == "crm-privacy");
        Assert.Contains(editor.Function.Actions, action => action.MenuId == privacyMenu.MenuId && action.ActionCode == "export");
        var actions = editor.Function.Actions.Where(action => action.MenuId != privacyMenu.MenuId || action.ActionCode != "export").ToList();
        var command = new IamRolePolicyCommandV1
        {
            OperationId = Guid.NewGuid(),
            ExpectedVersion = editor.Version,
            ExpectedReferencesVersion = editor.ReferencesVersion,
            ExpectedCatalogVersion = editor.Catalog.Version,
            MenuIds = editor.Function.MenuIds.Concat(actions.Select(action => action.MenuId)).Distinct().ToList(),
            Actions = actions,
            Reason = "PGT-017 simulated export permission revocation"
        };
        var before = await Read();
        Assert.True(before.Granted);
        await Stage("ready");
        Task<HttpResponseMessage> change;
        if (Environment.GetEnvironmentVariable("CP6_PGT017_ORDER") == "pause-first")
        {
            await Stage("permission-held");
            if (Environment.GetEnvironmentVariable("CP6_PGT017_ROLE_ABORT_AFTER_SEAL") == "1")
                await AbortAfterSeal();
            change = Revoke();
        }
        else
        {
            await Stage("owner-held");
            change = Revoke();
            try
            {
                await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
                await crm.OpenAsync();
                await using var blocked = new NpgsqlCommand("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                      WHERE datname=current_database() AND application_name='cp6-main-inquiry-tenant-pause-v1'
                        AND wait_event_type='Lock' AND cardinality(pg_blocking_pids(pid))>0)
                    """, crm);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while (!(bool)(await blocked.ExecuteScalarAsync(deadline.Token))!)
                {
                    if (change.IsCompleted)
                    {
                        using var early = await change;
                        Assert.Fail($"Main role policy returned HTTP {(int)early.StatusCode} while the admitted CRM Owner was waiting.");
                    }
                    await Task.Delay(25, deadline.Token);
                }
                Assert.Equal(before, await Read());
            }
            finally { await File.WriteAllTextAsync(Path.Combine(bridge, "release-owner"), "PASS"); }
        }
        using var response = await change;
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var receipt = (await response.Content.ReadFromJsonAsync<IamOperationV1>())!;
        Assert.Equal("COMMITTED", receipt.State);
        Assert.Equal(command.OperationId, receipt.OperationId);
        var after = await Read();
        Assert.False(after.Granted);
        Assert.True(after.TenantEnabled);
        Assert.NotEqual(before.Revision, after.Revision);
        Assert.Equal(receipt.Version, after.Revision);
        Assert.Equal(before.Snapshot + 1, after.Snapshot);
        Assert.Equal(before.Generation + 1, after.Generation);
        Assert.Equal(before.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audit);
        Assert.Equal(1, after.Receipts);
        using var replay = await Revoke();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt, await replay.Content.ReadFromJsonAsync<IamOperationV1>());
        Assert.Equal(after, await Read());
        await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
        await Stage("finished");

        Task<HttpResponseMessage> Revoke() => host.Send(Request(HttpMethod.Put, path, cookies, command));

        async Task AbortAfterSeal()
        {
            await using var main = new NpgsqlConnection(h.Db.Database.GetConnectionString());
            await main.OpenAsync();
            await using var hook = new NpgsqlCommand("""
                CREATE FUNCTION cp6_authentication_v1.pgt017_wait_for_abort() RETURNS trigger LANGUAGE plpgsql AS $test$
                BEGIN PERFORM pg_advisory_xact_lock(1129330260,551); RETURN NEW; END $test$;
                CREATE CONSTRAINT TRIGGER "Pgt017AbortAfterSeal" AFTER INSERT ON cp6_authentication_v1.iam_operations
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.pgt017_wait_for_abort()
                """, main);
            await hook.ExecuteNonQueryAsync();
            try
            {
                await using var barrier = await main.BeginTransactionAsync();
                await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1129330260,551)", main, barrier);
                await hold.ExecuteNonQueryAsync();
                var attempted = Revoke();
                try
                {
                    await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
                    await observer.OpenAsync();
                    var backend = await WaitForMainBlock(observer, main.ProcessID, "COMMIT%", attempted);
                    Assert.Equal(before, await Read());
                    Assert.Equal(before.Snapshot + 1, await CrmRoleFloor());
                    // Let CRM use the prior signed response while Main's source is still uncommitted.
                    await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
                    await Stage("finished");
                    await using var cancel = new NpgsqlCommand("SELECT pg_cancel_backend(@pid)", observer);
                    cancel.Parameters.AddWithValue("pid", backend);
                    Assert.Equal(true, await cancel.ExecuteScalarAsync());
                    using var failed = await attempted;
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
                    Assert.Contains("IAM_OPERATION_UNKNOWN", await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                }
                finally { await barrier.RollbackAsync(); }
            }
            finally
            {
                hook.CommandText = """
                    DROP TRIGGER "Pgt017AbortAfterSeal" ON cp6_authentication_v1.iam_operations;
                    DROP FUNCTION cp6_authentication_v1.pgt017_wait_for_abort()
                    """;
                await hook.ExecuteNonQueryAsync();
            }
            Assert.Equal(before, await Read());
            Assert.Equal(before.Snapshot + 1, await CrmRoleFloor());
            using var pending = await host.Send(Request(HttpMethod.Get,
                $"api/iam/role-policies/operations/{command.OperationId:D}?kind=role-policy-actions", cookies));
            Assert.True(pending.StatusCode == HttpStatusCode.OK, await pending.Content.ReadAsStringAsync());
            Assert.Equal("UNKNOWN", (await pending.Content.ReadFromJsonAsync<IamOperationV1>())!.State);
        }

        async Task<int> CrmRoleFloor()
        {
            await using var db = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
            await db.OpenAsync();
            await using var floor = new NpgsqlCommand("""
                SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.role_fences
                WHERE tenant_id=@tenant AND aggregate_id='permission:role:71'
                """, db);
            floor.Parameters.AddWithValue("tenant", h.Tenant);
            return (int)(await floor.ExecuteScalarAsync())!;
        }

        async Task Stage(string marker)
        {
            var wait = WaitConfigurationBridge(bridge, marker);
            if (await Task.WhenAny(wait, childExited) == childExited)
                Assert.True(File.Exists(Path.Combine(bridge, marker)), $"CRM exited before reaching {marker}.");
            await wait;
        }

        async Task<ExportRoleState> Read()
        {
            var tenant = h.Tenant;
            const string aggregate = "permission:role:71";
            var json = await h.Db.Database.SqlQuery<string>($"""
                WITH messages AS (
                    SELECT "AggregateVersion" FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}
                    UNION ALL
                    SELECT "AggregateVersion" FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}
                ), snapshot AS (
                    SELECT "Version" FROM crm_identity."Snapshot" WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}
                )
                SELECT jsonb_build_object(
                    'TenantEnabled', (SELECT "Enable" FROM public."Sys_Tenants" WHERE "Id"={tenant}),
                    'Granted', EXISTS(SELECT 1 FROM public."Sys_RoleAction" WHERE "TenantId"={tenant} AND "RoleId"=71
                      AND "MenuId"={privacyMenu.MenuId} AND "ActionCode"='export'),
                    'Revision', (SELECT revision FROM cp6_authentication_v1.identity_admission_revisions
                      WHERE tenant_id={tenant} AND object_key='role:71'),
                    'Snapshot', (SELECT "Version" FROM snapshot),
                    'Generation', (SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={tenant}),
                    'Outbox', (SELECT count(*) FROM messages),
                    'CurrentOutbox', (SELECT count(*) FROM messages WHERE "AggregateVersion"=(SELECT "Version" FROM snapshot)),
                    'Audit', (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={tenant}
                      AND "EventType"={(int)SecurityEventType.IamAdministrationChanged}
                      AND "ContextJson"::jsonb->>'operationId'={command.OperationId.ToString("D")}),
                    'Receipts', (SELECT count(*) FROM cp6_authentication_v1.iam_operations
                      WHERE tenant_id={tenant} AND operation_id={command.OperationId}))::text AS "Value"
                """).SingleAsync();
            return JsonSerializer.Deserialize<ExportRoleState>(json)!;
        }
    }

    private sealed record ExportRoleState(bool TenantEnabled, bool Granted, string Revision, int Snapshot,
        long Generation, long Outbox, long CurrentOutbox, long Audit, long Receipts);
}
