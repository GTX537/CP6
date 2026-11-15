using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task W641_committed_OA_withdrawal_parent_resume_and_signed_pause_share_native_commit_order(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        await GrantLegacyAuthorityAction(h, "oa-inbox", "withdraw");
        var fixture = await SeedW641Withdrawal(h);
        var direct = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { SslMode = SslMode.Disable };

        // Pause requests avoid the relay entirely. Only the first acknowledged COMMIT from the
        // real withdrawal connection can be held, after both signed sessions have logged in.
        await using var relay = pauseFirst
            ? new StockPostgresCommitRelay(direct.Port, direct.Username!, W641FixturePort(57155))
            : null;
        var writer = relay is null ? direct.ConnectionString
            : new NpgsqlConnectionStringBuilder(direct.ConnectionString) { Port = relay.Port }.ConnectionString;
        await using var withdrawApi = await StartW641Api(h, writer, 57156);
        await using var tenantApi = await StartW641Api(h, direct.ConnectionString, 57157);
        var actor = await Login(withdrawApi, h);
        var manager = await Login(tenantApi, admin);

        await using var observer = new NpgsqlConnection(direct.ConnectionString);
        await using var parentBarrier = new NpgsqlConnection(direct.ConnectionString);
        await using var tenantBarrier = new NpgsqlConnection(direct.ConnectionString);
        await observer.OpenAsync();
        await parentBarrier.OpenAsync();
        await tenantBarrier.OpenAsync();
        var initial = await ReadW641(observer, h.Tenant, fixture);
        Assert.Equal(FlowInstanceStatus.Running, AssetInt(initial.Row("instances", fixture.Child), "Status"));
        Assert.Equal(FlowTaskStatus.Pending, AssetInt(initial.Row("tasks", fixture.ChildTask), "Status"));
        Assert.Equal(ServiceJobStatus.Pending, AssetInt(initial.Row("jobs", fixture.ChildTimer), "Status"));
        Assert.Empty(initial.Rows("outbox"));
        Assert.DoesNotContain(initial.Rows("jobs"), row => row.GetProperty("Kind").GetString() == WfJobKind.SubFlowResume);

        Task<HttpResponseMessage> withdrawing;
        Task<HttpResponseMessage> pausing;
        FenceWait businessWait;
        FenceWait pauseWait;
        AssetRows originCommitted;
        AssetRows whileWaiting;
        AssetRows afterInline;
        PauseAuthority authorityBefore;
        TenantManagementCommandV1 pause;
        Guid originalJobId;

        if (pauseFirst)
        {
            Assert.NotNull(relay);
            relay.ArmHold();
            withdrawing = Withdraw();
            try
            {
                await relay.CommitHeld.WaitAsync(TimeSpan.FromSeconds(8));
                Assert.False(withdrawing.IsCompleted);
                originCommitted = await ReadW641(observer, h.Tenant, fixture);
                originalJobId = CheckW641CommittedWithdraw(initial, originCommitted, fixture);
                authorityBefore = await ReadPauseAuthority(h, admin.User);
                pause = new TenantManagementCommandV1("w641-inline-pause", authorityBefore.Revision, "suspend", null);

                await using var holdTenant = await tenantBarrier.BeginTransactionAsync();
                await LockW641TenantUpdate(tenantBarrier, holdTenant);
                pausing = Pause(pause);
                pauseWait = await WaitForFence(observer, tenantBarrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);

                relay.ReleaseCommit();
                // The client just received its original COMMIT. Only an explicit acquire by the
                // continuation matches here. A Wf write's implicit trigger has the UPDATE as its query.
                businessWait = await WaitForFence(observer, pauseWait.Backend,
                    "%SELECT%cp6_authentication_v1.acquire()%", null, withdrawing);
                whileWaiting = await ReadW641(observer, h.Tenant, fixture);
                Assert.Equal(originCommitted, whileWaiting);
                await holdTenant.RollbackAsync();
                await withdrawing.WaitAsync(TimeSpan.FromSeconds(15));
                afterInline = await ReadW641(observer, h.Tenant, fixture);
                Assert.Equal(originCommitted, afterInline);
            }
            finally
            {
                relay.ReleaseCommit();
            }
        }
        else
        {
            await using var holdParent = await parentBarrier.BeginTransactionAsync();
            await using (var lockParent = new NpgsqlCommand(
                """SELECT "Id" FROM public."Wf_FlowInstance" WHERE "TenantId"=@tenant AND "Id"=@parent FOR UPDATE""",
                parentBarrier, holdParent))
            {
                lockParent.Parameters.AddWithValue("tenant", h.Tenant);
                lockParent.Parameters.AddWithValue("parent", fixture.Parent);
                Assert.Equal(fixture.Parent, await lockParent.ExecuteScalarAsync());
            }

            withdrawing = Withdraw();
            // Accept the actual parent UPDATE or an explicit parent SELECT FOR UPDATE, both of
            // which are native waits on this one existing parent row.
            businessWait = await WaitForFence(observer, parentBarrier.ProcessID, "%Wf_FlowInstance%", null, withdrawing);
            originCommitted = await ReadW641(observer, h.Tenant, fixture);
            originalJobId = CheckW641CommittedWithdraw(initial, originCommitted, fixture);
            authorityBefore = await ReadPauseAuthority(h, admin.User);
            pause = new TenantManagementCommandV1("w641-inline-pause", authorityBefore.Revision, "suspend", null);

            await using var holdTenant = await tenantBarrier.BeginTransactionAsync();
            await LockW641TenantUpdate(tenantBarrier, holdTenant);
            pausing = Pause(pause);
            pauseWait = await WaitForFence(observer, businessWait.Backend,
                "%SELECT%cp6_authentication_v1.acquire()%", null, pausing);
            whileWaiting = await ReadW641(observer, h.Tenant, fixture);
            Assert.Equal(originCommitted, whileWaiting);

            await holdParent.RollbackAsync();
            await withdrawing.WaitAsync(TimeSpan.FromSeconds(15));
            // Keep the authority UPDATE uncommitted so this observes the business commit alone.
            await WaitForFence(observer, tenantBarrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            afterInline = await ReadW641(observer, h.Tenant, fixture);
            CheckW641Completed(originCommitted, afterInline, fixture, originalJobId);
            Assert.True((await ReadPauseAuthority(h, admin.User)).Enabled);
            await holdTenant.RollbackAsync();
        }

        using var pausedResponse = await pausing.WaitAsync(TimeSpan.FromSeconds(15));
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(pausedResponse);
        var authorityAfter = await ReadPauseAuthority(h, admin.User);
        CheckPause(authorityBefore, authorityAfter, receipt, pause);
        using var withdrawalResponse = await withdrawing.WaitAsync(TimeSpan.FromSeconds(15));
        await CheckW641WithdrawalHttp(withdrawalResponse);
        var afterPause = await ReadW641(observer, h.Tenant, fixture);
        Assert.Equal(afterInline, afterPause);

        // Existing W625 fixture uses the real lease worker and a transactional notification
        // service. The surviving Pending row, never a replacement job, is eligible after reactivation.
        await using var workers = CreateJournalTimerWorkers();
        var due = await h.Db.Wf_ServiceJobs.AsNoTracking().Where(j => j.Id == originalJobId)
            .Select(j => j.NextAttemptAtUtc).SingleAsync();
        Assert.Null(await ScanJournalTimer(workers, h.Tenant, due.AddSeconds(1)));
        Assert.Equal(afterPause, await ReadW641(observer, h.Tenant, fixture));
        await ResumeAssetTenant(tenantApi, manager, h.Tenant, receipt);
        Assert.Equal(pauseFirst ? 1 : 0, await ScanJournalTimer(workers, h.Tenant, due.AddSeconds(1)));
        var final = await ReadW641(observer, h.Tenant, fixture);
        CheckW641Completed(originCommitted, final, fixture, originalJobId);
        if (!pauseFirst) Assert.Equal(afterPause, final);
        Assert.Equal(0, await ScanJournalTimer(workers, h.Tenant, due.AddMinutes(5)));
        Assert.Equal(final, await ReadW641(observer, h.Tenant, fixture));

        output.WriteLine(JsonSerializer.Serialize(new
        {
            PauseFirst = pauseFirst, OriginalAckHeld = pauseFirst, h.Tenant,
            fixture.Parent, fixture.ParentToken, fixture.Child, fixture.ChildToken, OriginalJob = originalJobId,
            BusinessWait = businessWait, PauseWait = pauseWait, AuthorityBefore = authorityBefore, AuthorityAfter = authorityAfter,
            PauseReceipt = receipt, Initial = W641Snapshot(initial), OriginCommitted = W641Snapshot(originCommitted),
            InFlight = W641Snapshot(whileWaiting), AfterPause = W641Snapshot(afterPause), Final = W641Snapshot(final),
        }));

        Task<HttpResponseMessage> Withdraw() => withdrawApi.Send(OperationsRequest(actor, HttpMethod.Post,
            $"/api/wf/flow/{fixture.Child}/withdraw", new { }));
        Task<HttpResponseMessage> Pause(TenantManagementCommandV1 command) => tenantApi.Send(
            OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), command));
    }


    [CurrentPermissionPostgresFact]
    public async Task W641_lost_parent_resume_commit_ack_keeps_committed_withdrawal_and_one_original_effect()
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, "oa-inbox", "withdraw");
        var fixture = await SeedW641Withdrawal(h);
        var direct = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { SslMode = SslMode.Disable };
        await using var observer = new NpgsqlConnection(direct.ConnectionString);
        await observer.OpenAsync();
        var initial = await ReadW641(observer, h.Tenant, fixture);
        await using var relay = new StockPostgresCommitRelay(direct.Port, direct.Username!, W641FixturePort(57155));
        var writer = new NpgsqlConnectionStringBuilder(direct.ConnectionString) { Port = relay.Port }.ConnectionString;
        await using var host = await StartW641Api(h, writer, 57156);
        var actor = await Login(host, h);

        relay.ArmHold();
        var withdrawing = host.Send(OperationsRequest(actor, HttpMethod.Post, $"/api/wf/flow/{fixture.Child}/withdraw", new { }));
        AssetRows origin;
        Guid jobId;
        try
        {
            await relay.CommitHeld.WaitAsync(TimeSpan.FromSeconds(8));
            origin = await ReadW641(observer, h.Tenant, fixture);
            jobId = CheckW641CommittedWithdraw(initial, origin, fixture);
            relay.Arm();
        }
        finally
        {
            relay.ReleaseCommit();
        }
        await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        using var response = await withdrawing.WaitAsync(TimeSpan.FromSeconds(15));
        await CheckW641WithdrawalHttp(response);

        var committed = await ReadW641(observer, h.Tenant, fixture);
        CheckW641Completed(origin, committed, fixture, jobId);
        await using var workers = CreateJournalTimerWorkers();
        var due = await h.Db.Wf_ServiceJobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.NextAttemptAtUtc).SingleAsync();
        Assert.Equal(0, await ScanJournalTimer(workers, h.Tenant, due.AddMinutes(5)));
        Assert.Equal(committed, await ReadW641(observer, h.Tenant, fixture));
        output.WriteLine(JsonSerializer.Serialize(new { Original = W641Snapshot(origin), Completed = W641Snapshot(committed),
            OriginalJob = jobId, ContinuationAcknowledgementLost = true, Status = (int)response.StatusCode }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W641_signed_withdrawal_converges_ancestors_with_each_original_resume_job()
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, "oa-inbox", "withdraw");
        var fixture = await SeedW641Withdrawal(h, nested: true);
        var grandparent = fixture.Grandparent!.Value;
        await using var host = await StartW641Api(h, h.Db.Database.GetConnectionString()!, 57156);
        var actor = await Login(host, h);
        using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"/api/wf/flow/{fixture.Child}/withdraw", new { }));
        await CheckW641WithdrawalHttp(response);

        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var final = await ReadW641(observer, h.Tenant, fixture);
        Assert.Equal(FlowInstanceStatus.Withdrawn, AssetInt(final.Row("instances", fixture.Child), "Status"));
        Assert.Equal(FlowInstanceStatus.Rejected, AssetInt(final.Row("instances", fixture.Parent), "Status"));
        Assert.Equal("err", final.Row("instances", grandparent).GetProperty("CurrentNode").GetString());
        var jobs = final.Rows("jobs").Where(row => row.GetProperty("Kind").GetString() == WfJobKind.SubFlowResume).ToArray();
        Assert.Equal(2, jobs.Length);
        CheckLink(fixture.Child, fixture.Parent, fixture.ParentToken);
        CheckLink(fixture.Parent, grandparent, AssetId(final.Row("instances", fixture.Parent), "ParentTokenId"));
        var outbox = final.Rows("outbox");
        Assert.Equal(2, outbox.Length);
        Assert.Single(outbox, row => AssetId(row, "InstanceId") == fixture.Parent
            && AssetInt(row, "Type") == WfNotificationType.FlowRejected);
        Assert.Single(outbox, row => AssetId(row, "InstanceId") == grandparent
            && AssetInt(row, "Type") == WfNotificationType.TodoCreated);
        output.WriteLine(JsonSerializer.Serialize(new { fixture.Parent, grandparent, fixture.Child, Final = W641Snapshot(final) }));

        void CheckLink(Guid child, Guid parent, Guid parentToken)
        {
            var job = Assert.Single(jobs, row => AssetId(row, "TokenId") == child);
            Assert.Equal(ServiceJobStatus.Succeeded, AssetInt(job, "Status"));
            Assert.Equal(parent, AssetId(job, "InstanceId"));
            var payload = SubFlowResumePayload.Parse(job.GetProperty("ActionRefJson").GetString());
            Assert.NotNull(payload);
            Assert.Equal((parent, parentToken, child), (payload.ParentInstanceId, payload.ParentTokenId, payload.ChildInstanceId));
        }
    }

    private static async Task CheckW641WithdrawalHttp(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("OK", body.RootElement.GetProperty("message").GetString());
    }

    private sealed record W641Withdrawal(Guid Parent, Guid ParentToken, Guid Child, Guid ChildToken, Guid ChildTask,
        Guid ChildTimer, Guid? Grandparent);

    private static async Task<W641Withdrawal> SeedW641Withdrawal(Harness h, bool nested = false)
    {
        var db = h.Db;
        SubFlowTestHarness.SeedDef(db, "w641-withdraw-child", SubFlowTestHarness.ChildSchema(h.User));
        SubFlowTestHarness.SeedDef(db, "w641-withdraw-parent",
            SubFlowTestHarness.ParentSchema(h.User, "w641-withdraw-child", errorEdge: !nested));
        if (nested)
            SubFlowTestHarness.SeedDef(db, "w641-withdraw-grandparent",
                SubFlowTestHarness.ParentSchema(h.User, "w641-withdraw-parent", errorEdge: true));
        await db.SaveChangesAsync();
        await LegacyDefinitionFixture.ArrangePublishedHistory(db);
        var top = await SubFlowTestHarness.Engine(db)
            .SubmitAsync(nested ? "w641-withdraw-grandparent" : "w641-withdraw-parent", h.User, "{}");
        var parent = nested ? await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == top).Select(i => i.Id).SingleAsync() : top;
        var child = await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.ParentInstanceId == parent);
        var childToken = await db.Wf_FlowTokens.Where(t => t.InstanceId == child.Id && t.Status == FlowTokenStatus.Active)
            .Select(t => t.Id).SingleAsync();
        var childTask = await db.Wf_FlowTasks.Where(t => t.InstanceId == child.Id && t.Status == FlowTaskStatus.Pending)
            .Select(t => t.Id).SingleAsync();
        var existingTimer = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Wf_ServiceJobs.Add(new Wf_ServiceJob
        {
            Id = existingTimer, InstanceId = child.Id, TokenId = childToken, NodeId = "w641-original-child-job",
            Kind = ServiceKind.Timer, Status = ServiceJobStatus.Pending, DueAtUtc = now, NextAttemptAtUtc = now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(parent, child.ParentTokenId!.Value, child.Id, childToken, childTask, existingTimer, nested ? top : null);
    }

    private static int W641FixturePort(int dedicatedPort) =>
        Environment.GetEnvironmentVariable("CP6_SUBFLOW_FIXED_PORTS") == "true" ? dedicatedPort : 0;

    private static Task<AuthenticationApiProcess> StartW641Api(Harness h, string connection, int dedicatedPort)
    {
        var environment = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{W641FixturePort(dedicatedPort)}";
        return AuthenticationApiProcess.Start(connection, environment);
    }

    private static async Task LockW641TenantUpdate(NpgsqlConnection blocker, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand("""LOCK TABLE public."Sys_Tenants" IN SHARE MODE""", blocker, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static Guid CheckW641CommittedWithdraw(AssetRows initial, AssetRows committed, W641Withdrawal f)
    {
        Assert.Equal(FlowInstanceStatus.Withdrawn, AssetInt(committed.Row("instances", f.Child), "Status"));
        Assert.Equal(FlowTaskStatus.Cancelled, AssetInt(committed.Row("tasks", f.ChildTask), "Status"));
        Assert.Equal(FlowTokenStatus.Cancelled, AssetInt(committed.Row("tokens", f.ChildToken), "Status"));
        Assert.Equal(ServiceJobStatus.Cancelled, AssetInt(committed.Row("jobs", f.ChildTimer), "Status"));
        Assert.Equal(FlowFormToStatus.Voided, AssetInt(Assert.Single(committed.Rows("form-tos"),
            row => AssetId(row, "InstanceId") == f.Child && row.GetProperty("NodeId").GetString() == "ca"), "Status"));
        Assert.Single(committed.Rows("history"), row => AssetId(row, "InstanceId") == f.Child
            && row.GetProperty("Action").GetString() == "withdraw");

        Assert.Equal(initial.Row("instances", f.Parent).GetRawText(), committed.Row("instances", f.Parent).GetRawText());
        Assert.Equal(initial.Row("tokens", f.ParentToken).GetRawText(), committed.Row("tokens", f.ParentToken).GetRawText());
        Assert.Empty(committed.Rows("tasks").Where(row => AssetId(row, "InstanceId") == f.Parent));
        Assert.Empty(committed.Rows("outbox"));
        Assert.DoesNotContain(committed.Rows("history"), row => AssetId(row, "InstanceId") == f.Parent
            && row.GetProperty("Action").GetString() == "subFlowError");

        var job = Assert.Single(committed.Rows("jobs"), row => row.GetProperty("Kind").GetString() == WfJobKind.SubFlowResume);
        Assert.Equal((ServiceJobStatus.Pending, 0), (AssetInt(job, "Status"), AssetInt(job, "AttemptCount")));
        Assert.Equal(f.Parent, AssetId(job, "InstanceId"));
        Assert.Equal(f.Child, AssetId(job, "TokenId")); // The original sentinel identifies the child INSTANCE.
        Assert.Equal(SubFlowResume.JobNodeId, job.GetProperty("NodeId").GetString());
        var payload = SubFlowResumePayload.Parse(job.GetProperty("ActionRefJson").GetString());
        Assert.NotNull(payload);
        Assert.Equal((f.Parent, f.ParentToken, f.Child), (payload.ParentInstanceId, payload.ParentTokenId, payload.ChildInstanceId));
        return AssetId(job);
    }

    private static void CheckW641Completed(AssetRows committed, AssetRows completed,
        W641Withdrawal f, Guid originalJob)
    {
        Assert.Equal(committed.Row("instances", f.Child).GetRawText(), completed.Row("instances", f.Child).GetRawText());
        Assert.Equal(committed.Row("tokens", f.ChildToken).GetRawText(), completed.Row("tokens", f.ChildToken).GetRawText());
        Assert.Equal(committed.Row("tasks", f.ChildTask).GetRawText(), completed.Row("tasks", f.ChildTask).GetRawText());
        Assert.Equal(committed.Row("jobs", f.ChildTimer).GetRawText(), completed.Row("jobs", f.ChildTimer).GetRawText());
        var parent = completed.Row("instances", f.Parent);
        Assert.Equal(FlowInstanceStatus.Running, AssetInt(parent, "Status"));
        Assert.Equal("err", parent.GetProperty("CurrentNode").GetString());
        Assert.Single(completed.Rows("tokens"), row => AssetId(row, "InstanceId") == f.Parent
            && row.GetProperty("NodeId").GetString() == "err" && AssetInt(row, "Status") == FlowTokenStatus.Active);
        Assert.Single(completed.Rows("history"), row => AssetId(row, "InstanceId") == f.Parent
            && row.GetProperty("Action").GetString() == "subFlowError");

        var task = Assert.Single(completed.Rows("tasks"), row => AssetId(row, "InstanceId") == f.Parent);
        Assert.Equal("err", task.GetProperty("NodeId").GetString());
        Assert.Equal(FlowTaskStatus.Pending, AssetInt(task, "Status"));
        var notification = Assert.Single(completed.Rows("outbox"));
        Assert.Equal(f.Parent, AssetId(notification, "InstanceId"));
        Assert.Equal(AssetId(task), AssetId(notification, "TaskId"));
        Assert.Equal(WfNotificationType.TodoCreated, AssetInt(notification, "Type"));
        Assert.False(string.IsNullOrWhiteSpace(notification.GetProperty("EventKey").GetString()));
        Assert.Equal(0, AssetInt(notification, "DispatchStatus"));

        var pending = committed.Row("jobs", originalJob);
        var succeeded = Assert.Single(completed.Rows("jobs"), row => row.GetProperty("Kind").GetString() == WfJobKind.SubFlowResume);
        Assert.Equal(originalJob, AssetId(succeeded));
        Assert.Equal(ServiceJobStatus.Succeeded, AssetInt(succeeded, "Status"));
        Assert.NotEqual(JsonValueKind.Null, succeeded.GetProperty("CompletedAtUtc").ValueKind);
        foreach (var field in new[] { "TenantId", "InstanceId", "TokenId", "NodeId", "Kind", "ActionRefJson",
            "DueAtUtc", "NextAttemptAtUtc", "AttemptCount", "MaxAttempts", "CreateDate" })
            Assert.Equal(pending.GetProperty(field).GetRawText(), succeeded.GetProperty(field).GetRawText());
    }

    private static async Task<AssetRows> ReadW641(NpgsqlConnection observer, Guid tenant, W641Withdrawal f)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'instances', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowInstance" r
                WHERE r."TenantId"=@t AND r."Id"=ANY(@instances) ORDER BY r."Id"),
              'tokens', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowToken" r
                WHERE r."TenantId"=@t AND r."InstanceId"=ANY(@instances) ORDER BY r."Id"),
              'tasks', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowTask" r
                WHERE r."TenantId"=@t AND r."InstanceId"=ANY(@instances) ORDER BY r."Id"),
              'form-tos', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowFormTo" r
                WHERE r."TenantId"=@t AND r."InstanceId"=ANY(@instances) ORDER BY r."Id"),
              'history', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowHistory" r
                WHERE r."TenantId"=@t AND r."InstanceId"=ANY(@instances) ORDER BY r."Id"),
              'jobs', ARRAY(SELECT to_jsonb(r) FROM public."Wf_ServiceJob" r
                WHERE r."TenantId"=@t AND r."InstanceId"=ANY(@instances) ORDER BY r."Id"),
              'outbox', ARRAY(SELECT to_jsonb(r) FROM public."Wf_Notification" r
                WHERE r."TenantId"=@t AND r."InstanceId"=ANY(@instances) ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("t", tenant);
        command.Parameters.AddWithValue("instances", f.Grandparent is Guid grandparent
            ? new[] { f.Parent, f.Child, grandparent } : new[] { f.Parent, f.Child });
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static object W641Snapshot(AssetRows state) => new
    {
        Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.Data))).ToLowerInvariant(),
        Rows = JsonSerializer.Deserialize<JsonElement>(state.Data),
    };
}
