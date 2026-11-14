using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task ExerciseCrmExportScopeRevocation(AuthenticationApiProcess host, Harness h,
        InquirySiteFormScopeV1 target, string bridge, Task childExited, bool regrant)
    {
        const string path = "api/iam/inquiry-scopes/71";
        const string aggregate = "permission:role:71";
        var cookies = await Login(host, h);
        using var read = await host.Send(Request(HttpMethod.Get, path, cookies));
        Assert.True(read.StatusCode == HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var editor = (await read.Content.ReadFromJsonAsync<IamInquiryScopeEditorV1>())!;
        Assert.Contains(target, editor.SiteForms);
        var retained = Assert.Single(editor.SiteForms, pair => pair != target);
        var command = new IamInquiryScopeCommandV1
        {
            OperationId = Guid.NewGuid(),
            ExpectedVersion = editor.Version,
            ExpectedReferencesVersion = editor.ReferencesVersion,
            Reason = "PGT-017 simulated site/form scope revocation",
            SiteForms = [retained]
        };
        var before = await State();
        Assert.True(before.ExportAction);
        Assert.True(before.TenantEnabled);
        Assert.Equal(2, before.PairCount);
        Assert.Equal(0, before.Audit);
        Assert.Equal(0, before.Receipts);
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
                await using var wait = new NpgsqlCommand("""
                    SELECT pg_blocking_pids(pid) FROM pg_stat_activity
                    WHERE datname=current_database() AND application_name='cp6-main-inquiry-tenant-pause-v1'
                      AND wait_event_type='Lock' AND cardinality(pg_blocking_pids(pid))>0
                    """, crm);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while (await wait.ExecuteScalarAsync(deadline.Token) is not int[] blockers || blockers.Length == 0)
                {
                    if (change.IsCompleted)
                    {
                        using var early = await change;
                        Assert.Fail($"Main inquiry scope change returned HTTP {(int)early.StatusCode} before the admitted export.");
                    }
                    await Task.Delay(25, deadline.Token);
                }
                Assert.Equal(before, await State());
            }
            finally { await File.WriteAllTextAsync(Path.Combine(bridge, "release-owner"), "PASS"); }
        }

        using var response = await change;
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var receipt = (await response.Content.ReadFromJsonAsync<IamOperationV1>())!;
        Assert.Equal(command.OperationId, receipt.OperationId);
        Assert.Equal(IamInquiryScopeAdministrationV1.Kind, receipt.Kind);
        Assert.Equal("COMMITTED", receipt.State);
        var after = await State();
        Assert.True(after.ExportAction);
        Assert.True(after.TenantEnabled);
        Assert.Equal(1, after.PairCount);
        Assert.NotEqual(before.Revision, after.Revision);
        Assert.Equal(receipt.Version, after.Revision);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(before.Generation + 1, after.Generation);
        Assert.Equal(before.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audit);
        Assert.Equal(1, after.Receipts);
        Assert.Equal(retained, Assert.Single(await IamInquiryScopeAdministrationV1.ReadSourceAsync(h.Db, h.Tenant, 71, default)));
        var source = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == h.Tenant
            && x.AggregateId == aggregate);
        Assert.Equal(IdentityEventContracts.PermissionChangedV3, source.EventType);
        var data = JsonSerializer.Deserialize<PermissionIdentityDataV3>(source.PayloadJson, IdentityEventContracts.Json)!;
        Assert.Equal(retained, Assert.Single(data.SiteFormScopes));
        await using (var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER")))
        {
            await crm.OpenAsync();
            await using var floor = new NpgsqlCommand("""
                SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.role_fences
                WHERE tenant_id=@tenant AND aggregate_id='permission:role:71'
                """, crm);
            floor.Parameters.AddWithValue("tenant", h.Tenant);
            Assert.Equal(after.Version, await floor.ExecuteScalarAsync());
        }
        using var replay = await Revoke();
        Assert.True(replay.StatusCode == HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        Assert.Equal(receipt, await replay.Content.ReadFromJsonAsync<IamOperationV1>());
        Assert.Equal(after, await State());
        if (regrant) await SendPublication("revoked", after.Version);
        await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
        if (regrant)
        {
            await Stage("revocation-published");
            using var currentResponse = await host.Send(Request(HttpMethod.Get, path, cookies));
            Assert.True(currentResponse.StatusCode == HttpStatusCode.OK, await currentResponse.Content.ReadAsStringAsync());
            var current = (await currentResponse.Content.ReadFromJsonAsync<IamInquiryScopeEditorV1>())!;
            Assert.Equal(receipt.Version, current.Version);
            Assert.Equal(retained, Assert.Single(current.SiteForms));
            var restore = command with
            {
                OperationId = Guid.NewGuid(), ExpectedVersion = current.Version,
                ExpectedReferencesVersion = current.ReferencesVersion, SiteForms = [retained, target],
                Reason = "PGT-017 simulated site/form scope regrant"
            };
            using var restored = await host.Send(Request(HttpMethod.Put, path, cookies, restore));
            Assert.True(restored.StatusCode == HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
            var restoredReceipt = (await restored.Content.ReadFromJsonAsync<IamOperationV1>())!;
            Assert.Equal("COMMITTED", restoredReceipt.State);
            Assert.Equal(restore.OperationId, restoredReceipt.OperationId);
            var restoredState = await State(restore.OperationId);
            Assert.Equal(after.Version + 1, restoredState.Version);
            Assert.Equal(after.Generation + 1, restoredState.Generation);
            Assert.Equal(after.Outbox + 1, restoredState.Outbox);
            Assert.Equal(1, restoredState.CurrentOutbox);
            Assert.Equal(1, restoredState.Receipts);
            Assert.Equal(1, restoredState.Audit);
            Assert.Equal(restoredReceipt.Version, restoredState.Revision);
            Assert.Equal(2, restoredState.PairCount);
            using var restoreReplay = await host.Send(Request(HttpMethod.Put, path, cookies, restore));
            Assert.Equal(restoredReceipt, await restoreReplay.Content.ReadFromJsonAsync<IamOperationV1>());
            Assert.Equal(restoredState, await State(restore.OperationId));
            await SendPublication("regranted", restoredState.Version);
        }
        await Stage("finished");

        Task<HttpResponseMessage> Revoke() => host.Send(Request(HttpMethod.Put, path, cookies, command));

        async Task SendPublication(string stage, int version)
        {
            var envelope = await h.Db.Database.SqlQuery<byte[]>($"""
                SELECT "Payload" AS "Value" FROM crm_identity."Cp6_OutboxMessage"
                WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate} AND "AggregateVersion"={version}
                """).SingleAsync();
            var path = Path.Combine(bridge, stage + ".json");
            await File.WriteAllBytesAsync(path + ".tmp", envelope);
            File.Move(path + ".tmp", path);
        }

        async Task Stage(string name)
        {
            var wait = WaitConfigurationBridge(bridge, name);
            if (await Task.WhenAny(wait, childExited) == childExited)
                Assert.True(File.Exists(Path.Combine(bridge, name)), $"CRM exited before reaching {name}.");
            await wait;
        }

        async Task<InquiryScopeState> State(Guid? operation = null)
        {
            var tenant = h.Tenant;
            var operationId = operation ?? command.OperationId;
            var json = await h.Db.Database.SqlQuery<string>($"""
                WITH messages AS (
                    SELECT "AggregateVersion" FROM crm_identity."Cp6_OutboxMessage"
                    WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}
                    UNION ALL
                    SELECT "AggregateVersion" FROM crm_identity_priority."Cp6_OutboxMessage"
                    WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}
                )
                SELECT jsonb_build_object('TenantEnabled',t."Enable",
                    'ExportAction',EXISTS(SELECT 1 FROM public."Sys_RoleAction" a JOIN public."Sys_Menus" m ON m."MenuId"=a."MenuId"
                        WHERE a."TenantId"={tenant} AND a."RoleId"=71 AND m."MenuKey"='crm-privacy' AND a."ActionCode"='export'),
                    'PairCount',(SELECT count(*) FROM cp6_inquiry_scope_v1.role_scopes WHERE tenant_id={tenant} AND role_id=71),
                    'Revision',(SELECT revision FROM cp6_authentication_v1.identity_admission_revisions
                        WHERE tenant_id={tenant} AND object_key='role:71'),
                    'Version',s."Version",
                    'Generation',(SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={tenant}),
                    'Outbox',(SELECT count(*) FROM messages),
                    'CurrentOutbox',(SELECT count(*) FROM messages WHERE "AggregateVersion"=s."Version"),
                    'Audit',(SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={tenant}
                        AND "EventType"={(int)SecurityEventType.IamAdministrationChanged}
                        AND "ContextJson"::jsonb->>'operationId'={operationId.ToString("D")}),
                    'Receipts',(SELECT count(*) FROM cp6_authentication_v1.iam_operations
                        WHERE tenant_id={tenant} AND operation_id={operationId}))::text AS "Value"
                FROM public."Sys_Tenants" t JOIN crm_identity."Snapshot" s
                    ON s."TenantId"=t."Id" AND s."AggregateId"={aggregate}
                WHERE t."Id"={tenant}
                """).SingleAsync();
            return JsonSerializer.Deserialize<InquiryScopeState>(json)!;
        }
    }

    private sealed record InquiryScopeState(bool TenantEnabled, bool ExportAction, int PairCount, string Revision,
        int Version, long Generation, int Outbox, int CurrentOutbox, int Audit, int Receipts);
}
