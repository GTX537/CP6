using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task SeedDepartmentEditor(Harness h)
    {
        await GrantHttpPermissions(h);
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(row => row.MenuKey == "pub-dept");
        if (menu is null)
        {
            menu = new Sys_Menu { MenuId = Interlocked.Increment(ref policyMenuId), MenuKey = "pub-dept", MenuName = "Departments" };
            h.Db.Sys_Menus.Add(menu);
        }
        h.Db.Sys_RoleActions.AddRange(new[] { "query", "edit" }
            .Select(action => new Sys_RoleAction { RoleId = 71, MenuId = menu.MenuId, ActionCode = action }));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task ExerciseCrmExportDepartmentRevocation(AuthenticationApiProcess host, Harness h, string bridge, Task childExited)
    {
        const string path = "api/iam/departments";
        var cookies = await Login(host, h);
        using var read = await host.Send(Request(HttpMethod.Get, path + "/editor", cookies));
        Assert.True(read.StatusCode == HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var editor = (await read.Content.ReadFromJsonAsync<IamDepartmentEditorV1>())!;
        var department = Assert.Single(editor.Departments, row => row.Id == h.Parent);
        var command = new DeptDto
        {
            OperationId = Guid.NewGuid(),
            ExpectedVersion = department.Version,
            ExpectedTreeVersion = editor.TreeVersion,
            ExpectedReferencesVersion = editor.ReferencesVersion,
            Reason = "PGT-017 simulated department revocation",
            DeptCode = department.DeptCode,
            DeptName = department.DeptName,
            ParentId = department.ParentId,
            LeaderId = department.LeaderId,
            Sort = department.Sort,
            Enable = false
        };
        var before = await State();
        Assert.True(before.Enabled);
        await Stage("ready");
        Task<HttpResponseMessage> change;
        if (Environment.GetEnvironmentVariable("CP6_PGT017_ORDER") == "pause-first")
        {
            await Stage("permission-held");
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
                    SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                        AND application_name='cp6-main-inquiry-tenant-pause-v1' AND wait_event_type='Lock'
                        AND cardinality(pg_blocking_pids(pid))>0)
                    """, crm);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while (!(bool)(await blocked.ExecuteScalarAsync(deadline.Token))!)
                {
                    if (change.IsCompleted)
                    {
                        using var early = await change;
                        Assert.Fail($"Main department update returned HTTP {(int)early.StatusCode} before the admitted export.");
                    }
                    await Task.Delay(25, deadline.Token);
                }
                Assert.Equal(before, await State());
            }
            finally { await File.WriteAllTextAsync(Path.Combine(bridge, "release-owner"), "PASS"); }
        }
        using var result = await change;
        Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        var receipt = (await result.Content.ReadFromJsonAsync<IamOperationV1>())!;
        Assert.Equal("COMMITTED", receipt.State);
        Assert.Equal(command.OperationId, receipt.OperationId);
        var after = await State();
        Assert.False(after.Enabled);
        Assert.True(after.TenantEnabled);
        Assert.True(after.OtherDepartmentEnabled);
        Assert.True(after.ExportAction);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(before.Generation + 1, after.Generation);
        Assert.Equal(before.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.Audit);
        Assert.Equal(1, after.Receipts);
        using var replay = await Revoke();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt, await replay.Content.ReadFromJsonAsync<IamOperationV1>());
        Assert.Equal(after, await State());
        await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
        await Stage("finished");

        Task<HttpResponseMessage> Revoke() => host.Send(Request(HttpMethod.Put, $"{path}/{h.Parent:D}", cookies, command));

        async Task Stage(string marker)
        {
            var wait = WaitConfigurationBridge(bridge, marker);
            if (await Task.WhenAny(wait, childExited) == childExited)
                Assert.True(File.Exists(Path.Combine(bridge, marker)), $"CRM exited before reaching {marker}.");
            await wait;
        }

        async Task<DepartmentSourceState> State()
        {
            var tenant = h.Tenant;
            var aggregate = $"department:{h.Parent:D}";
            var json = await h.Db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_object(
                  'Enabled', d."Enable", 'Version', snapshot."Version",
                  'TenantEnabled', (SELECT "Enable" FROM public."Sys_Tenants" WHERE "Id"={tenant}),
                  'OtherDepartmentEnabled', (SELECT "Enable" FROM public."Sys_Dept" WHERE "TenantId"={tenant} AND "Id"={h.Child}),
                  'ExportAction', EXISTS(SELECT FROM public."Sys_RoleAction" a JOIN public."Sys_Menus" m ON a."MenuId"=m."MenuId"
                      WHERE a."TenantId"={tenant} AND a."RoleId"=71 AND a."ActionCode"='export' AND m."MenuKey"='crm-privacy'),
                  'Generation', (SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={tenant}),
                  'Outbox', (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage"
                      WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}),
                  'Audit', (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={tenant}
                      AND "EventType"={(int)SecurityEventType.IamAdministrationChanged}
                      AND "ContextJson"::jsonb->>'operationId'={command.OperationId.ToString("D")}),
                  'Receipts', (SELECT count(*) FROM cp6_authentication_v1.iam_operations
                      WHERE tenant_id={tenant} AND operation_id={command.OperationId}))::text AS "Value"
                FROM public."Sys_Dept" d JOIN crm_identity."Snapshot" snapshot ON snapshot."TenantId"=d."TenantId"
                  AND snapshot."AggregateId"={aggregate}
                WHERE d."TenantId"={tenant} AND d."Id"={h.Parent}
                """).SingleAsync();
            return JsonSerializer.Deserialize<DepartmentSourceState>(json)!;
        }
    }

    private sealed record DepartmentSourceState(bool Enabled, bool TenantEnabled, bool OtherDepartmentEnabled,
        bool ExportAction, int Version, long Generation, int Outbox, int Audit, int Receipts);
}
