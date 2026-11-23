using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static byte[] OriginalConnectorResult() => " { \"message\": \"原结果\", \"amount\": 17.20 }\n"u8.ToArray();

    private static async Task<ConnResource> CreateResultConnector(Harness h, ConnApi maker, string name)
    {
        var checker = new ConnApi(maker.Host, await Login(maker.Host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        var profile = await ConnRegistration(h, "CONNECTOR", DispatchProfile());
        return await ConnActivate(maker, checker,
            await ConnCreate(maker, "CONNECTOR", new(name, "TENANT", profile), "持久结果合同"));
    }

    private static OaConnectorResultHttpParticipant ResultParticipant(Harness h, ConnResource root,
        OaConnectorDispatchWireFixture wire, HttpClient http) =>
        new(h.Tenant, ScopeEnvironment, new("TENANT", root.Id, root.Version, "lookup-v1"), wire.Origin, http);

    private static Task<int> ExpireResultClaim(Harness h, Guid job) => h.Db.Wf_ServiceJobs.Where(j => j.Id == job)
        .ExecuteUpdateAsync(rows => rows.SetProperty(j => j.LockExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));

    private static Task<byte[]?> ReadConnectorResult(Harness h, Guid job) => h.Db.Database.SqlQuery<byte[]>($"""
        SELECT payload AS "Value" FROM cp6_oa_conn_v1.result_inbox
        WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND job_id={job}
        """).SingleOrDefaultAsync();

    private static Task<Guid?> ResumeReceipt(Harness h, Guid job) => h.Db.Database.SqlQuery<Guid?>($"""
        SELECT receipt_id AS "Value" FROM cp6_oa_conn_v1.result_applications
        WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND job_id={job}
        """).SingleOrDefaultAsync();

    private static async Task<JsonElement> ResultDispatch(ConnApi maker, ConnResource root, Guid job) =>
        Assert.Single((await maker.Data(DispatchesRoute(root))).GetProperty("entries").EnumerateArray(),
            row => row.GetProperty("jobId").GetGuid() == job);

    private static async Task WaitForResult(Func<Task<bool>> condition)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!await condition()) await Task.Delay(50, limit.Token);
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnResult_crash_after_inbox_uses_original_under_replacement_hosted_worker()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var root = await CreateResultConnector(h, maker, "result-process-recovery");
        var job = await DispatchJob(h, root, "result-process-recovery");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        var original = OriginalConnectorResult();
        wire.ResultPayload = original;
        var (initial, initialPidPath) = StartDispatchProcess(h, root, wire, job.Id, result: true, pauseApply: true);
        using (initial)
        {
            try
            {
                await WaitForResult(async () => await ReadConnectorResult(h, job.Id) != null);
                Assert.Equal(original, await ReadConnectorResult(h, job.Id));
                Assert.Null(await ResumeReceipt(h, job.Id));
                Assert.Equal(FlowTokenStatus.Active, await h.Db.Wf_FlowTokens.AsNoTracking().Where(t => t.Id == job.TokenId).Select(t => t.Status).SingleAsync());
            }
            finally
            {
                if (!initial.HasExited) initial.Kill(entireProcessTree: true);
                await initial.WaitForExitAsync();
            }
        }
        await ExpireResultClaim(h, job.Id);
        var (replacement, replacementPidPath) = StartDispatchProcess(h, root, wire, job.Id, result: true);
        using (replacement)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
                await replacement.WaitForExitAsync(timeout.Token);
                Assert.True(replacement.ExitCode == 0, await replacement.StandardOutput.ReadToEndAsync() + await replacement.StandardError.ReadToEndAsync());
            }
            finally
            {
                if (!replacement.HasExited) replacement.Kill(entireProcessTree: true);
                await replacement.WaitForExitAsync();
            }
        }
        Assert.NotEqual(await File.ReadAllTextAsync(initialPidPath), await File.ReadAllTextAsync(replacementPidPath));
        var receipt = await ResumeReceipt(h, job.Id);
        Assert.NotNull(receipt);
        var entry = await ResultDispatch(maker, root, job.Id);
        Assert.Equal("SUCCEEDED", entry.GetProperty("resultState").GetString());
        Assert.Equal("APPLIED", entry.GetProperty("workflowResume").GetString());
        Assert.Equal(receipt, entry.GetProperty("resumeReceiptId").GetGuid());
        Assert.Equal("UNPROVEN", entry.GetProperty("businessOwnerApplication").GetString());
        Assert.Equal("SIMULATED", entry.GetProperty("adoption").GetString());
        Assert.DoesNotContain("原结果", entry.GetRawText());
        Assert.DoesNotContain(wire.Sends.Single().Effect, entry.GetRawText());
        var applied = await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x => x.Id == job.InstanceId);
        Assert.Equal(FlowInstanceStatus.Approved, applied.Status);
        using var vars = JsonDocument.Parse(applied.VarsJson!);
        Assert.Equal("original", vars.RootElement.GetProperty("subject").GetString());
        Assert.Equal("原结果", vars.RootElement.GetProperty("connectorMessage").GetString());
        Assert.Equal(17.20m, vars.RootElement.GetProperty("connectorAmount").GetDecimal());
        Assert.Equal(receipt, (await ResultDispatch(maker, root, job.Id)).GetProperty("resumeReceiptId").GetGuid());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.result_outbox WHERE tenant_id={h.Tenant} AND job_id={job.Id}
            """).SingleAsync());
        Assert.Empty(wire.Queries);
        Assert.Single(wire.Sends);
        output.WriteLine($"SIMULATED 提供方一次发送、零查证，原结果 {original.Length} 字节，跨进程恢复原 Job {job.Id:D} 与回执 {receipt:D}。");
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnResult_old_epoch_keeps_fact_conflict_isolated_and_only_current_lease_resumes()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var root = await CreateResultConnector(h, maker, "result-epoch");
        var job = await DispatchJob(h, root, "result-epoch");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        wire.ResultPayload = OriginalConnectorResult();
        wire.QueryResultPayload = """{"message":"异体结果","amount":17.20}"""u8.ToArray();
        var send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.HoldReply = send.Task;
        wire.HoldQueryReply = query.Task;
        using var http = DispatchHttp();
        await using var oldDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var newDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var participant = ResultParticipant(h, root, wire, http);
        var old = DispatchWorker(oldDb, [participant]).ScanOnceAsync(DateTime.UtcNow, "result-old", timeout.Token);
        Task<int>? current = null;
        try
        {
            await wire.Arrived.Task.WaitAsync(timeout.Token);
            await ExpireResultClaim(h, job.Id);
            current = DispatchWorker(newDb, [participant]).ScanOnceAsync(DateTime.UtcNow, "result-current", timeout.Token);
            await WaitForResult(() => Task.FromResult(!wire.Queries.IsEmpty));
            send.TrySetResult();
            Assert.Equal(0, await old);
            Assert.Equal(wire.ResultPayload, await ReadConnectorResult(h, job.Id));
            Assert.Null(await ResumeReceipt(h, job.Id));
            Assert.Equal("result-current", await h.Db.Wf_ServiceJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.LockedBy).SingleAsync());
            query.TrySetResult();
            Assert.Equal(1, await current);
        }
        finally
        {
            send.TrySetResult();
            query.TrySetResult();
            await old;
            if (current != null) await current;
        }
        Assert.Equal("RESULT_CONFLICT", await h.Db.Database.SqlQuery<string>($"""
            SELECT reason AS "Value" FROM cp6_oa_conn_v1.result_quarantine WHERE tenant_id={h.Tenant} AND job_id={job.Id}
            """).SingleAsync());
        await MakeDispatchDue(h, job.Id);
        Assert.Equal(1, await DispatchWorker(newDb, [participant]).ScanOnceAsync(DateTime.UtcNow, "result-retry"));
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"""
            SELECT attempt AS "Value" FROM cp6_oa_conn_v1.result_applications WHERE tenant_id={h.Tenant} AND job_id={job.Id}
            """).SingleAsync());
        Assert.Equal(wire.ResultPayload, await ReadConnectorResult(h, job.Id));
        Assert.Single(wire.Sends);
        Assert.Single(wire.Queries);
        output.WriteLine($"SIMULATED 旧租约留原事实，第二次观察异体隔离，第三次本地应用，Job={job.Id:D}，POST=1，GET=1。");
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnResult_withdrawn_token_keeps_late_success_as_history()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var root = await CreateResultConnector(h, maker, "result-stale-token");
        var job = await DispatchJob(h, root, "result-stale-token");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        using var http = DispatchHttp();
        await using var worker = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.ResultPayload = OriginalConnectorResult();
        wire.HoldReply = reply.Task;
        var scan = DispatchWorker(worker, [ResultParticipant(h, root, wire, http)]).ScanOnceAsync(DateTime.UtcNow, "history-only");
        try
        {
            await wire.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(18));
            await new TaskCenterService(h.Db).WithdrawAsync(job.InstanceId, h.User);
        }
        finally
        {
            reply.TrySetResult();
            await scan;
        }
        Assert.Equal(wire.ResultPayload, await ReadConnectorResult(h, job.Id));
        Assert.Null(await ResumeReceipt(h, job.Id));
        Assert.Equal(FlowInstanceStatus.Withdrawn, await h.Db.Wf_FlowInstances.AsNoTracking().Where(i => i.Id == job.InstanceId).Select(i => i.Status).SingleAsync());
        Assert.Equal("STALE_TOKEN", (await ResultDispatch(maker, root, job.Id)).GetProperty("workflowResume").GetString());
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"""
            SELECT receipt_id AS "Value" FROM cp6_oa_conn_v1.result_outbox WHERE tenant_id={h.Tenant} AND job_id={job.Id}
            """).ToArrayAsync());
        Assert.Single(wire.Sends);
        Assert.Empty(wire.Queries);
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnResult_bad_identity_schema_reserved_keys_and_decimal_scale_are_quarantined()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var root = await CreateResultConnector(h, maker, "result-bad-wire");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        using var http = DispatchHttp();
        var participant = ResultParticipant(h, root, wire, http);
        foreach (var (bytes, schema, service, expected) in new[]
        {
            ("""{"message":"越界","amount":17.20,"StarterId":"spoofed"}"""u8.ToArray(), "response-v1", "simulated-service/1", "RESULT_SCHEMA_INVALID"),
            ("""{"message":"精度","amount":17.201}"""u8.ToArray(), "response-v1", "simulated-service/1", "RESULT_SCHEMA_INVALID"),
            (OriginalConnectorResult(), "unbound-schema", "simulated-service/1", "RESULT_SCHEMA_INVALID"),
            (OriginalConnectorResult(), "response-v1", "unbound-service", "RESULT_IDENTITY_MISMATCH"),
        })
        {
            var job = await DispatchJob(h, root, "result-bad-wire");
            wire.ResultPayload = bytes;
            wire.ResultSchema = schema;
            wire.ResultService = service;
            Assert.Equal(1, await DispatchWorker(h.Db, [participant]).ScanOnceAsync(DateTime.UtcNow, "bad-result"));
            Assert.Equal("QUARANTINED", (await ResultDispatch(maker, root, job.Id)).GetProperty("resultState").GetString());
            Assert.Equal(expected, await h.Db.Database.SqlQuery<string>($"""
                SELECT reason AS "Value" FROM cp6_oa_conn_v1.result_quarantine WHERE tenant_id={h.Tenant} AND job_id={job.Id}
                """).SingleAsync());
            Assert.Null(await ReadConnectorResult(h, job.Id));
            Assert.Null(await ResumeReceipt(h, job.Id));
            await AssertOriginalTokenHeld(h, job);
        }
        Assert.Equal(4, wire.Sends.Count);
        Assert.Empty(wire.Queries);
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnResult_current_apply_permission_precedes_workflow_and_saved_result_survives_revocation()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var root = await CreateResultConnector(h, maker, "result-current-permission");
        var job = await DispatchJob(h, root, "result-current-permission");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        wire.ResultPayload = OriginalConnectorResult();
        using var http = DispatchHttp();
        await using var workerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var worker = DispatchWorker(workerDb, [ResultParticipant(h, root, wire, http)]);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.HoldReply = reply.Task;
        var scan = worker.ScanOnceAsync(DateTime.UtcNow, "authority-first");
        try
        {
            await wire.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(18));
            await RevokeDispatchAction(h, "conn.connector.activate");
        }
        finally
        {
            reply.TrySetResult();
            await scan;
        }
        Assert.Equal(0, await scan);
        Assert.Equal(wire.ResultPayload, await ReadConnectorResult(h, job.Id));
        Assert.Null(await ResumeReceipt(h, job.Id));
        var held = await ResultDispatch(maker, root, job.Id);
        Assert.Equal("SUCCEEDED", held.GetProperty("resultState").GetString());
        Assert.Equal("HOLD", held.GetProperty("workflowResume").GetString());
        await EngineeringAction(h, "oa-flow-admin", "conn.connector.activate");
        await ExpireResultClaim(h, job.Id);
        Assert.Equal(1, await worker.ScanOnceAsync(DateTime.UtcNow, "current-authority"));
        Assert.NotNull(await ResumeReceipt(h, job.Id));
        Assert.Single(wire.Sends);
        Assert.Empty(wire.Queries);
    }
}
