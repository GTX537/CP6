using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_Main_pause_and_CRM_configuration_commit()
    {
        Assert.NotNull(Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE"));
        await InquiryProducerHttp("configuration:write", crossDatabasePause: true);
    }

    private async Task ExerciseCrmTenantPause(AuthenticationApiProcess host, Harness target, string bridge,
        bool publicSubmission = false, Task? childExited = null, bool waitForCompletePause = false, bool shortenDeadline = false)
    {
        await using var admin = await Seed();
        var user = await admin.Db.Sys_Users.SingleAsync(row => row.Id == admin.ActorId);
        user.IsPlatformAdmin = true;
        await admin.Db.SaveChangesAsync();
        var manager = await Login(host, admin);
        var path = $"api/platform/tenant/{target.Tenant}/management-v1";
        using var read = await host.Send(Request(HttpMethod.Get, path, manager));
        Assert.True(read.StatusCode == HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var current = (await read.Content.ReadFromJsonAsync<TenantManagementSnapshotV1>())!;
        var auditType = shortenDeadline ? SecurityEventType.TenantUpdated : SecurityEventType.TenantSuspended;
        var before = await State();
        Assert.True(before.Enabled);
        Assert.Equal(1, before.SnapshotVersion);
        Assert.Equal(publicSubmission ? 0 : 1, before.ActiveBrowserGrants);
        var order = Environment.GetEnvironmentVariable("CP6_PGT017_ORDER");
        var pauseFirst = order is "pause-first" or "pause-first-review";
        var recovery = order == "pause-recovery";
        var operation = shortenDeadline ? "pgt017-crm-tenant-deadline" : publicSubmission ? "pgt017-crm-public-pause"
            : waitForCompletePause ? "pgt017-crm-human-write-pause" : "pgt017-crm-configuration-pause";
        var expiry = shortenDeadline ? DateTime.SpecifyKind(DateTime.Today.AddDays(-1), DateTimeKind.Unspecified) : current.ExpireDate;
        var configuration = shortenDeadline ? new TenantConfigurationV1(current.TenantName, expiry, current.Remark, current.TimeZoneId) : null;
        var command = new TenantManagementCommandV1(operation, current.Revision, shortenDeadline ? "update" : "suspend", configuration);

        Task<HttpResponseMessage> pause;
        await WaitStage("ready");
        if ((publicSubmission || waitForCompletePause) && pauseFirst)
        {
            await WaitStage("permission-held");
            pause = Pause();
        }
        else if (pauseFirst || recovery)
        {
            if (pauseFirst) await WaitConfigurationBridge(bridge, "permission-held");
            using var connection = new NpgsqlConnection(target.Db.Database.GetConnectionString());
            await connection.OpenAsync();
            await using var barrier = await connection.BeginTransactionAsync();
            await using var hold = new NpgsqlCommand(
                """SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", connection, barrier);
            hold.Parameters.AddWithValue("tenant", target.Tenant);
            Assert.Equal(1, await hold.ExecuteScalarAsync());
            pause = Pause();
            await using var observer = new NpgsqlConnection(target.Db.Database.GetConnectionString());
            await observer.OpenAsync();
            var backend = await WaitForMainBlock(observer, connection.ProcessID, "%UPDATE%Sys_Tenants%", pause);
            Assert.Equal(before, await State());
            if (recovery)
            {
                await using var cancel = new NpgsqlCommand("SELECT pg_cancel_backend(@pid)", observer);
                cancel.Parameters.AddWithValue("pid", backend);
                Assert.Equal(true, await cancel.ExecuteScalarAsync());
                using var failed = await pause;
                Assert.True((int)failed.StatusCode >= 500, await failed.Content.ReadAsStringAsync());
                await barrier.RollbackAsync();
                Assert.Equal(before, await State());
                await File.WriteAllTextAsync(Path.Combine(bridge, "authority-aborted"), "PASS");
                await WaitConfigurationBridge(bridge, "original-recovered");
                pause = Pause();
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(bridge, "release-permission"), "PASS");
                await WaitConfigurationBridge(bridge, "sealed-denied");
                await File.WriteAllTextAsync(Path.Combine(bridge, "write"), "PASS");
                await WaitForMainBlock(observer, backend, "%cp6_authentication_v1.acquire()%", pause);
                Assert.Equal(before, await State());
                await barrier.RollbackAsync();
            }
        }
        else
        {
            await WaitStage("owner-held");
            pause = Pause();
            await using var observer = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
            await observer.OpenAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await using var wait = new NpgsqlCommand(
                """
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                  WHERE datname=current_database() AND application_name='cp6-main-inquiry-tenant-pause-v1'
                    AND wait_event_type='Lock' AND cardinality(pg_blocking_pids(pid))>0)
                """,
                observer);
            try
            {
                while (!(bool)(await wait.ExecuteScalarAsync(deadline.Token))!)
                {
                    if (pause.IsCompleted)
                    {
                        using var early = await pause;
                        Assert.Fail($"Main {command.Kind} returned HTTP {(int)early.StatusCode} while the admitted CRM Owner was still waiting.");
                    }
                    await Task.Delay(25, deadline.Token);
                }
                Assert.Equal(before, await State());
            }
            finally { await File.WriteAllTextAsync(Path.Combine(bridge, "release-owner"), "PASS"); }
        }

        using var result = await pause;
        Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        var receipt = (await result.Content.ReadFromJsonAsync<TenantManagementReceiptV1>())!;
        var after = await State();
        Assert.Equal(shortenDeadline, after.Enabled);
        Assert.Equal(expiry, after.Expires);
        if (shortenDeadline)
        {
            Assert.NotEqual(before.Expires, after.Expires);
            Assert.Equal(new DateTimeOffset(expiry!.Value).ToUniversalTime(), after.ProjectedExpiry);
        }
        Assert.NotEqual(before.Policy, after.Policy);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(before.Generation + 1 + before.ActiveBrowserGrants, after.Generation);
        Assert.Equal(before.SnapshotVersion + 1, after.SnapshotVersion);
        Assert.Equal(before.PriorityOutbox + (shortenDeadline ? 0 : 1), after.PriorityOutbox);
        Assert.Equal(before.StandardOutbox + (shortenDeadline ? 1 : 0), after.StandardOutbox);
        Assert.Equal(before.RevocationOutbox + before.ActiveBrowserGrants, after.RevocationOutbox);
        Assert.Equal(0, after.ActiveBrowserGrants);
        Assert.Equal(before.Audit + 1, after.Audit);
        Assert.Equal(before.Receipts + 1, after.Receipts);
        Assert.Equal(command.OperationRef, receipt.OperationRef);
        Assert.Equal(after.Revision, receipt.Snapshot.Revision);
        using var replay = await Pause();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt, await replay.Content.ReadFromJsonAsync<TenantManagementReceiptV1>());
        Assert.Equal(after, await State());
        await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
        await WaitStage("finished");
        if (recovery)
        {
            var reactivate = new TenantManagementCommandV1("pgt017-crm-configuration-reactivate", after.Revision, "reactivate", null);
            using var reopened = await host.Send(Request(HttpMethod.Post, path + "/commands", manager, reactivate));
            Assert.True(reopened.StatusCode == HttpStatusCode.OK, await reopened.Content.ReadAsStringAsync());
            var reactivated = await State();
            Assert.True(reactivated.Enabled);
            Assert.Equal(after.SnapshotVersion + 1, reactivated.SnapshotVersion);
            Assert.Equal(after.Generation + 1, reactivated.Generation);
            Assert.Equal(0, reactivated.ActiveBrowserGrants);
            Assert.Equal(after.PriorityOutbox, reactivated.PriorityOutbox);
            Assert.Equal(after.StandardOutbox + 1, reactivated.StandardOutbox);
            Assert.Equal(after.Receipts + 1, reactivated.Receipts);
            await File.WriteAllTextAsync(Path.Combine(bridge, "reactivated"), "PASS");
            await WaitConfigurationBridge(bridge, "reactivation-verified");
        }

        Task<HttpResponseMessage> Pause() => host.Send(Request(HttpMethod.Post, path + "/commands", manager, command));

        async Task WaitStage(string stage)
        {
            var wait = WaitConfigurationBridge(bridge, stage);
            if (childExited is not null && await Task.WhenAny(wait, childExited) == childExited && !wait.IsCompleted)
            {
                // The child can exit after writing the final marker but before the poll observes it.
                Assert.True(File.Exists(Path.Combine(bridge, stage)), $"CRM exited before reaching {stage}. See the child test log.");
            }
            await wait;
        }

        async Task<CrmPauseState> State()
        {
            var tenant = target.Tenant;
            var aggregate = $"tenant:{tenant:D}";
            var json = await target.Db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_object('Enabled',t."Enable",'Expires',t."ExpireDate",
                  'ProjectedExpiry',(SELECT "PayloadJson"::jsonb->'expiresAtUtc' FROM crm_identity."Snapshot"
                      WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}),
                  'Policy',t."AuthenticationPolicyRevision",'Revision',v.revision,
                  'Generation',coalesce((SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={tenant}),0),
                  'SnapshotVersion',coalesce((SELECT "Version" FROM crm_identity."Snapshot" WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}),0),
                  'PriorityOutbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}),
                  'StandardOutbox',(SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}),
                  'RevocationOutbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={tenant} AND "AggregateId" LIKE 'token:%'),
                  'ActiveBrowserGrants',(SELECT count(*) FROM public."CrmOidcGrant" WHERE "OrganizationId"={tenant} AND NOT "Revoked"),
                  'Audit',(SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "UserId"={admin.ActorId} AND "RequestTenantCode"=t."TenantCode"
                    AND "EventType"={(int)auditType}),
                  'Receipts',(SELECT count(*) FROM cp6_tenant_management_v1.commands WHERE tenant_id={tenant}))::text AS "Value"
                FROM public."Sys_Tenants" t JOIN cp6_tenant_management_v1.versions v ON v.tenant_id=t."Id" WHERE t."Id"={tenant}
                """).SingleAsync();
            return JsonSerializer.Deserialize<CrmPauseState>(json)!;
        }
    }

    private static async Task<int> WaitForMainBlock(NpgsqlConnection observer, int blocker, string query, Task<HttpResponseMessage> pause)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var wait = new NpgsqlCommand("""
            SELECT pid FROM pg_stat_activity WHERE datname=current_database()
                AND @blocker=ANY(pg_blocking_pids(pid)) AND query LIKE @query
            """, observer);
        wait.Parameters.AddWithValue("blocker", blocker);
        wait.Parameters.AddWithValue("query", query);
        while (true)
        {
            if (await wait.ExecuteScalarAsync(deadline.Token) is int pid) return pid;
            Assert.False(pause.IsCompleted, "Main suspension finished before the expected database wait.");
            await Task.Delay(25, deadline.Token);
        }
    }

    private sealed record CrmPauseState(bool Enabled, DateTime? Expires, DateTimeOffset? ProjectedExpiry, Guid Policy, long Revision, long Generation, int SnapshotVersion,
        int PriorityOutbox, int StandardOutbox, int RevocationOutbox, int ActiveBrowserGrants, int Audit, int Receipts);
}

internal sealed class Pgt017MainFactAttribute : FactAttribute
{
    public Pgt017MainFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")))
            Skip = "Requires the owned Main and CRM PostgreSQL interop run.";
    }
}
