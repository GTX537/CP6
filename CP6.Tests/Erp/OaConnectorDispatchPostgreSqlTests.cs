using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task OaConnDispatch_revoked_scope_holds_original_job_before_any_business_progress()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        var profile = await ConnRegistration(h, "CONNECTOR", DispatchProfile());
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        var root = await ConnCreate(maker, "CONNECTOR", new("revoked-dispatch", "TENANT", profile), "撤权顺序");
        root = await ConnActivate(maker, checker, root);
        var job = await DispatchJob(h, root, "revoked-dispatch");
        await SetDispatchScope(maker, false);

        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        using var http = DispatchHttp();
        var participant = DispatchParticipant(h, root, wire, http);
        Assert.Equal(1, await DispatchWorker(h.Db, [participant]).ScanOnceAsync(DateTime.UtcNow, "revoked"));

        var pending = await h.Db.Wf_ServiceJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ServiceJobStatus.Pending, pending.Status);
        Assert.Equal(0, pending.AttemptCount);
        Assert.Equal("SCOPE_MAPPING_DISABLED", pending.LastError);
        Assert.Equal(DateTime.MaxValue, pending.NextAttemptAtUtc);
        Assert.Empty(wire.Sends);
        Assert.Equal(0, participant.Preparations);
        Assert.Equal(FlowTokenStatus.Active, await h.Db.Wf_FlowTokens.Where(t => t.Id == job.TokenId).Select(t => t.Status).SingleAsync());
        Assert.Equal(0, await DispatchMemberCount(h, job.Id));
        output.WriteLine($"WF_AUTH 撤权先提交，Job {job.Id:D}，准入 0，POST 0，token 保持 Active。");
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnDispatch_admission_commits_before_http_and_revocation_preserves_the_original_transport_fact()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        var profile = await ConnRegistration(h, "CONNECTOR", DispatchProfile());
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        var root = await ConnActivate(maker, checker,
            await ConnCreate(maker, "CONNECTOR", new("admit-first", "TENANT", profile), "准入先提交"));
        var job = await DispatchJob(h, root, "admit-first");
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        wire.BeforeEffect = sent => AssertDispatchCommitted(h, job, root, sent);
        wire.HoldReply = reply.Task;
        using var http = DispatchHttp();
        var worker = DispatchWorker(h.Db, [DispatchParticipant(h, root, wire, http)]);
        var scan = worker.ScanOnceAsync(DateTime.UtcNow, "admit-first");

        var route = DispatchesRoute(root);
        try
        {
            var sent = await wire.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("DISPATCH_ADMITTED", Assert.Single((await maker.Data(route)).GetProperty("entries").EnumerateArray())
                .GetProperty("state").GetString());
            await SetDispatchScope(maker, false);
            var stopping = Assert.Single((await checker.Data(route)).GetProperty("entries").EnumerateArray());
            Assert.Equal("CANCELLATION_REQUESTED", stopping.GetProperty("state").GetString());
            Assert.Equal("DISPATCH_ADMITTED", stopping.GetProperty("transportState").GetString());
            output.WriteLine($"独立连接在 HTTP 首次接收时读到准入，能取得原 WF_AUTH 与 Job 锁，Job {job.Id:D}，输入摘要 {sent.Digest}。");
        }
        finally
        {
            reply.TrySetResult();
            await scan;
        }

        Assert.Equal(1, await scan);
        await SetDispatchScope(maker, true);
        var after = Assert.Single((await checker.Data(route)).GetProperty("entries").EnumerateArray());
        Assert.Equal("CANCELLATION_REQUESTED", after.GetProperty("state").GetString());
        Assert.Equal("HTTP_ACK", after.GetProperty("transportState").GetString());
        Assert.Equal("SIMULATED", after.GetProperty("adoption").GetString());
        Assert.Equal(1, wire.Effects);
        Assert.Single(wire.Sends);
        Assert.Empty(wire.Queries);
        await AssertOriginalTokenHeld(h, job);
        var editor = new ConnApi(host, await Login(host, "conn-editor", "Editor1!Test", h.Tenant.ToString("N")));
        await editor.Reject(route, null, HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
        await using var foreign = await Seed();
        await ConnPeople(foreign);
        var foreignActor = new ConnApi(host, await Login(host, foreign));
        await foreignActor.Reject(route, null, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        var json = after.GetRawText();
        Assert.DoesNotContain("opaque-binding", json);
        Assert.DoesNotContain(wire.Sends.Single().Effect, json);
        Assert.DoesNotContain(wire.Sends.Single().Digest, json);
        output.WriteLine("WF_AUTH 撤权提交发生在 HTTP 200 之前。原事实保留，恢复授权后仍为 CANCELLATION_REQUESTED，运输仅 HTTP_ACK。");
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnDispatch_lost_response_after_effect_process_reaper_queries_original_without_sending_again()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        var profile = await ConnRegistration(h, "CONNECTOR", DispatchProfile());
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        var root = await ConnActivate(maker, checker,
            await ConnCreate(maker, "CONNECTOR", new("recover-original", "TENANT", profile), "原操作恢复"));
        var job = await DispatchJob(h, root, "recover-original");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        wire.BeforeEffect = call => AssertDispatchCommitted(h, job, root, call);
        wire.LoseReply = true;
        wire.FirstQueryNotObserved = true;
        wire.SecondQueryWrongDigest = true;
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.HoldReply = reply.Task;
        var (initialProcess, initialPidPath) = StartDispatchProcess(h, root, wire, job.Id);
        using var initial = initialProcess;
        OaConnectorDispatchWireFixture.Call sent;
        try
        {
            sent = await wire.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(45));
            Assert.Equal(1, wire.Effects);
            Assert.Equal(1, await DispatchMemberCount(h, job.Id));
            Assert.Equal(0, await ObservationCount(h, job.Id));
        }
        finally
        {
            // 实际停止独立 worker 进程。提供方进程和已完成的效果保留。
            if (!initial.HasExited) initial.Kill(entireProcessTree: true);
            reply.TrySetResult();
            await initial.WaitForExitAsync();
        }

        var initialPid = await File.ReadAllTextAsync(initialPidPath);
        var original = await DispatchAdmission(h, job.Id);
        Assert.Equal(sent.Effect, original.EffectKey);
        Assert.Equal(sent.Transport, original.TransportKey);
        Assert.Equal(sent.Body, original.InputBytes);
        await h.Db.Wf_FlowInstances.Where(i => i.Id == job.InstanceId).ExecuteUpdateAsync(
            rows => rows.SetProperty(i => i.VarsJson, """{"subject":"changed-after-first-send"}"""));
        // 缩短隔离夹具中的五分钟等待。产品 reaper 仍以原 Job 的真实到期租约接管。
        await h.Db.Wf_ServiceJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
            rows => rows.SetProperty(j => j.LockExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));

        var (replacementProcess, replacementPidPath) = StartDispatchProcess(h, root, wire, job.Id);
        using var replacement = replacementProcess;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await replacement.WaitForExitAsync(timeout.Token);
            Assert.True(replacement.ExitCode == 0, await replacement.StandardOutput.ReadToEndAsync() + await replacement.StandardError.ReadToEndAsync());
        }
        finally
        {
            if (!replacement.HasExited) replacement.Kill(entireProcessTree: true);
        }
        var replacementPid = await File.ReadAllTextAsync(replacementPidPath);
        Assert.NotEqual(initialPid, replacementPid);
        Assert.Equal("NOT_OBSERVED", await DispatchLastError(h, job.Id));
        Assert.Single(wire.Queries);
        using var http = DispatchHttp();
        var fresh = DispatchParticipant(h, root, wire, http);
        await using (var second = server.Context(new TenantContext { CurrentTenantId = h.Tenant }))
        {
            await RevokeDispatchAction(h, "conn.read");
            await MakeDispatchDue(h, job.Id);
            Assert.Equal(1, await DispatchWorker(second, [fresh]).ScanOnceAsync(DateTime.UtcNow, "query-forbidden"));
            Assert.Equal("DISPATCH_QUERY_FORBIDDEN", await DispatchLastError(h, job.Id));
            Assert.Single(wire.Queries);
            await EngineeringAction(h, "oa-flow-admin", "conn.read");
            await MakeDispatchDue(h, job.Id);
            Assert.Equal(1, await DispatchWorker(second, [fresh]).ScanOnceAsync(DateTime.UtcNow, "query-wrong-digest"));
            Assert.Equal("OUTCOME_UNKNOWN", await DispatchLastError(h, job.Id));
            await MakeDispatchDue(h, job.Id);
            Assert.Equal(1, await DispatchWorker(second, [fresh]).ScanOnceAsync(DateTime.UtcNow, "query-exact"));
        }

        Assert.Equal(0, fresh.Preparations);
        Assert.Equal("ORIGINAL_OPERATION_OBSERVED", await DispatchLastError(h, job.Id));
        Assert.Equal(3, wire.Queries.Count);
        Assert.All(wire.Queries, q => Assert.Equal((sent.Effect, sent.Transport, sent.Digest), (q.Effect, q.Transport, q.Digest)));
        Assert.Equal(1, wire.Effects);
        Assert.Single(wire.Sends);
        var recovered = await DispatchAdmission(h, job.Id);
        Assert.Equal(original, recovered with { InputBytes = original.InputBytes });
        Assert.Equal(sent.Body, recovered.InputBytes);
        Assert.Equal(4, await h.Db.Wf_ServiceJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.AttemptCount).SingleAsync());
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(DISTINCT claim_at)::int AS "Value" FROM cp6_oa_conn_v1.dispatch_observations
            WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND job_id={job.Id}
            """).SingleAsync());
        Assert.Equal("ORIGINAL_OPERATION_OBSERVED", Assert.Single((await maker.Data(DispatchesRoute(root)))
            .GetProperty("entries").EnumerateArray()).GetProperty("originalQueryState").GetString());
        await AssertOriginalTokenHeld(h, job);
        output.WriteLine($"SIMULATED 原操作恢复，Job {job.Id:D}，进程 {initialPid} → {replacementPid}，POST=1，效果=1，GET=3。");
        output.WriteLine($"原 effectKey={sent.Effect}，transport key={sent.Transport}，输入 SHA-256={sent.Digest}。NOT_OBSERVED → 摘要错配未知 → 查到原操作。");
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnDispatch_non_idempotent_timeout_does_not_send_again_or_create_another_identity()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        var profile = await ConnRegistration(h, "CONNECTOR", DispatchProfile(idempotent: false, seconds: 2));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        var root = await ConnActivate(maker, checker,
            await ConnCreate(maker, "CONNECTOR", new("non-idempotent-write", "TENANT", profile), "非幂等写"));
        var job = await DispatchJob(h, root, "non-idempotent-write");
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        wire.HoldReply = reply.Task;
        using var http = DispatchHttp();
        try
        {
            Assert.Equal(1, await DispatchWorker(h.Db, [DispatchParticipant(h, root, wire, http)]).ScanOnceAsync(DateTime.UtcNow, "non-idempotent"));
        }
        finally { reply.TrySetResult(); }

        Assert.Equal("OUTCOME_UNKNOWN", await DispatchLastError(h, job.Id));
        var original = await DispatchAdmission(h, job.Id);
        Assert.Null(original.IdempotencyUntil);
        Assert.Null(original.QueryUntil);
        Assert.Equal(0, await DispatchWorker(h.Db, [DispatchParticipant(h, root, wire, http)]).ScanOnceAsync(DateTime.UtcNow, "automatic"));
        await MakeDispatchDue(h, job.Id);
        Assert.Equal(1, await DispatchWorker(h.Db, [DispatchParticipant(h, root, wire, http)]).ScanOnceAsync(DateTime.UtcNow, "reclaimed"));
        Assert.Equal("DISPATCH_RECOVERY_REQUIRED", await DispatchLastError(h, job.Id));
        Assert.Equal(original.EffectKey, (await DispatchAdmission(h, job.Id)).EffectKey);
        Assert.Equal("OUTCOME_UNKNOWN", Assert.Single((await maker.Data(DispatchesRoute(root)))
            .GetProperty("entries").EnumerateArray()).GetProperty("transportState").GetString());
        Assert.Equal(1, await DispatchMemberCount(h, job.Id));
        Assert.Equal(1, await ObservationCount(h, job.Id));
        Assert.Single(wire.Sends);
        Assert.Empty(wire.Queries);
        Assert.Equal(1, wire.Effects);
        await AssertOriginalTokenHeld(h, job);
        output.WriteLine($"SIMULATED 非幂等超时，Job {job.Id:D}，POST=1，自动重发=0，原键保留，OUTCOME_UNKNOWN。");
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnDispatch_remote_authority_fence_and_current_owner_and_control_each_precede_http()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        var profile = await ConnRegistration(h, "CONNECTOR", DispatchProfile() with { RequiresCurrentAuthorizationAtApply = true });
        var local = await ConnRegistration(h, "CONNECTOR", DispatchProfile());
        var configuredOnly = await ConnRegistration(h, "CONNECTOR", DispatchProfile(), "ADOPTED");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        var instant = await ConnActivate(maker, checker,
            await ConnCreate(maker, "CONNECTOR", new("remote-revocation", "TENANT", profile), "要求远端即时撤权"));
        var localRoot = await ConnActivate(maker, checker,
            await ConnCreate(maker, "CONNECTOR", new("owner-revocation", "TENANT", local), "需要当前 Owner 准入"));
        var onlyConfigured = await ConnCreate(maker, "CONNECTOR", new("configured-only", "TENANT", configuredOnly), "未证生产采用");
        Assert.Equal("UNPROVEN", (await maker.Data(onlyConfigured.Route + "/context")).GetProperty("profile").GetProperty("adoption").GetString());
        var instantJob = await DispatchJob(h, instant, "remote-revocation");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        using var http = DispatchHttp();
        var immediate = DispatchParticipant(h, instant, wire, http);
        Assert.Equal(1, await DispatchWorker(h.Db, [immediate]).ScanOnceAsync(DateTime.UtcNow, "remote-gate"));
        Assert.Equal("REMOTE_AUTHORITY_FENCE_UNPROVEN", await DispatchLastError(h, instantJob.Id));
        Assert.Equal(0, immediate.Preparations);
        Assert.Empty(wire.Sends);
        Assert.Equal(0, await DispatchMemberCount(h, instantJob.Id));

        var ownerJob = await DispatchJob(h, localRoot, "owner-revocation");
        var denied = DispatchParticipant(h, localRoot, wire, http);
        await RevokeDispatchAction(h, "conn.connector.activate");
        Assert.Equal(1, await DispatchWorker(h.Db, [denied]).ScanOnceAsync(DateTime.UtcNow, "owner-gate"));
        Assert.Equal("DISPATCH_QUALIFICATION_REQUIRED", await DispatchLastError(h, ownerJob.Id));
        Assert.Equal(1, denied.Preparations);
        Assert.Empty(wire.Sends);
        Assert.Equal(0, await DispatchMemberCount(h, ownerJob.Id));

        var stop = await ConnImpact(checker, localRoot, "DISABLE");
        await checker.Data(localRoot.Route + "/disable", ConnCmd(localRoot.Head,
            new ConnControlInputV1(localRoot.Version, stop.GetProperty("previewId").GetGuid(), null, "当前停用")));
        await MakeDispatchDue(h, ownerJob.Id);
        Assert.Equal(1, await DispatchWorker(h.Db, [denied]).ScanOnceAsync(DateTime.UtcNow, "disabled-root"));
        Assert.Equal("BLOCKED_CONNECTOR", await DispatchLastError(h, ownerJob.Id));
        Assert.Empty(wire.Sends);
        Assert.Equal(0, await DispatchMemberCount(h, ownerJob.Id));
        output.WriteLine($"SIMULATED 远端等价授权门缺证，Job {instantJob.Id:D} POST=0。当前 Owner 撤权与停用先提交，Job {ownerJob.Id:D} POST=0。");
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConnDispatch_missing_required_runtime_adoption_cannot_be_inferred_from_valid_configuration()
    {
        await using var h = await Seed();
        var (authoring, published) = await PrepareActivation(h, "adoption-proof");
        var publication = published.Result!.Publication!;
        await authoring.SetActivationAsync(publication.Key,
            ActivationCommand((await authoring.ActivationPreviewAsync(publication.Key)).Current, publication.VersionId));
        await ConnPeople(h);
        var message = await ConnRegistration(h, "MESSAGE", new(Registration: "configured-message", Schema: "envelope-v1",
            VarsSchema: "vars-v1", RateLimitPolicy: "test-rate", StarterAuthority: "configured-starter", StarterId: h.User), "ADOPTED");
        var timer = await ConnRegistration(h, "TIMER", new(TimeZone: "UTC", TimeRules: "configured-time",
            Calendar: "configured-calendar", StarterAuthority: "configured-starter", StarterId: h.User), "ADOPTED");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        foreach (var (source, profile, config) in new (string, Guid, ConnConfigV1?)[]
        {
            ("MESSAGE", message, null),
            ("TIMER", timer, new(Cron: "0 9 * * *", Vars: JsonSerializer.SerializeToElement(new { }))),
        })
        {
            var selection = new ConnSelectionV1("configured-" + source, source, profile, publication.Key, publication.VersionId);
            var start = await maker.Data("creation-contexts?kind=TRIGGER", selection);
            var created = await maker.Data("triggers",
                ConnCmd(start.GetProperty("expectedVersion").GetString()!, new ConnDraftV1(selection, "仅登记的运行依赖", config)));
            var resource = new ConnResource("triggers", created.GetProperty("rootId").GetGuid(), created.GetProperty("versionId").GetGuid(),
                created.GetProperty("result").GetProperty("revision").GetString()!);
            var vote = await ConnReview(maker, checker, resource, "PUBLISH");
            var publish = await maker.Data(resource.Route + "/publish",
                ConnCmd(resource.Head, new ConnControlInputV1(resource.Version, DecisionRef: vote)));
            resource = resource with { Head = publish.GetProperty("result").GetProperty("revision").GetString()! };
            var preview = await ConnImpact(maker, resource, "ENABLE");
            var impact = preview.GetProperty("previewId").GetGuid();
            var permit = await ConnReview(maker, checker, resource, "ENABLE", impact);
            await maker.Reject(resource.Route + "/enable",
                ConnCmd(resource.Head, new ConnControlInputV1(resource.Version, impact, permit, "仅登记资料无运行收据")),
                HttpStatusCode.ServiceUnavailable, "EXTERNAL_CONTRACT_UNPROVEN");
            var context = await maker.Data(resource.Route + "/context");
            Assert.Equal("UNPROVEN", context.GetProperty("profile").GetProperty("adoption").GetString());
            Assert.Equal("DISABLED", context.GetProperty("state").GetString());
        }

        using var scope = await host.Send(OperationsRequest(maker.Cookies, HttpMethod.Get, "api/iam/scope-authorization"));
        var authority = (await scope.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.True(authority.GetProperty("mapping").GetProperty("enabled").GetBoolean());
        Assert.Contains("FORMAL_WORKFLOW_CONSUMERS_PENDING", authority.GetProperty("adoption").GetString());
        Assert.Empty(await h.Db.Wf_FlowInstances.AsNoTracking().ToListAsync());
        Assert.Empty(await h.Db.Wf_TriggerFires.AsNoTracking().ToListAsync());
        output.WriteLine("MESSAGE pin 及 TIMER 时间配置已登记并有复核，缺真实运行采用时均为 UNPROVEN，实例与触发流水为零。WF_AUTH 仅确认原生写者基础。");
    }

    [TenantManagementPostgresTheory]
    [InlineData("SENT")]
    [InlineData("OUTCOME_UNKNOWN")]
    public async Task OaConnDispatch_legacy_history_blocks_a_second_writer_even_when_the_legacy_name_differs(string state)
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        var profile = await ConnRegistration(h, "CONNECTOR", ConnFixture());
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var checker = new ConnApi(host, await Login(host, "conn-reviewer", "Reviewer1!Test", h.Tenant.ToString("N")));
        var root = await ConnCreate(maker, "CONNECTOR", new("historical-dispatch", "TENANT", profile), "旧派发历史");
        root = await ConnActivate(maker, checker, root);
        var job = await DispatchJob(h, root, "old-provider-name", pinned: false);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.runtime_members(tenant_id,environment_id,root_id,version_id,kind,id,state)
            VALUES({h.Tenant},{ScopeEnvironment},{root.Id},{root.Version},'DISPATCH',{job.Id},{state})
            """);

        var oldProvider = new ConnAppWitness("old-provider-name");
        Assert.Equal(1, await DispatchWorker(h.Db, [], oldProvider).ScanOnceAsync(DateTime.UtcNow, "legacy"));

        Assert.Equal(0, oldProvider.Calls);
        var held = await h.Db.Wf_ServiceJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal("DISPATCH_RECOVERY_REQUIRED", held.LastError);
        Assert.Equal(job.ActionRefJson, held.ActionRefJson);
        Assert.Equal(1, await DispatchMemberCount(h, job.Id));
        Assert.Equal(state, await h.Db.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM cp6_oa_conn_v1.runtime_members WHERE tenant_id={h.Tenant}
              AND environment_id={ScopeEnvironment} AND kind='DISPATCH' AND id={job.Id}
            """).SingleAsync());

        var noSnapshot = await DispatchJob(h, root, "historical-dispatch", pinned: false);
        Assert.Equal(1, await h.Db.Wf_ServiceJobs.Where(j => j.Id == noSnapshot.Id)
            .ExecuteUpdateAsync(rows => rows.SetProperty(j => j.ActionRefJson, (string?)null)));
        Assert.Equal(1, await DispatchWorker(h.Db, [], oldProvider).ScanOnceAsync(DateTime.UtcNow, "legacy-no-snapshot"));
        Assert.Equal("UNVERIFIED_LEGACY", await DispatchLastError(h, noSnapshot.Id));
        Assert.Equal(0, await DispatchMemberCount(h, noSnapshot.Id));
        Assert.Equal(0, oldProvider.Calls);
        await AssertOriginalTokenHeld(h, noSnapshot);
        output.WriteLine($"旧 {state} Job={job.Id:D}，原历史=1，旧按名调用=0，缺原操作证明保持回查待办。无动作快照 Job={noSnapshot.Id:D} 原节点停泊。");
    }

    private static ConnProfileV1 DispatchProfile(bool idempotent = true, int seconds = 20) => ConnFixture(duration: seconds) with
    {
        Implementation = "wp1216-http-fixture",
        EffectClass = "EXTERNAL_MUTATION",
        IdempotencyContract = idempotent ? "simulated-idempotency/1" : "simulated-no-idempotency/1",
        ResultQueryContract = idempotent ? "simulated-original-query/1" : "simulated-manual-assessment/1",
        AdmissionBasis = "simulated-owner-admission/1",
        RequiresCurrentAuthorizationAtApply = false,
    };

    private static string DispatchesRoute(ConnResource root) => root.Route + "/versions/" + root.Version + "/dispatches";
    private static HttpClient DispatchHttp() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
    private static OaConnectorDispatchHttpParticipant DispatchParticipant(Harness h, ConnResource root,
        OaConnectorDispatchWireFixture wire, HttpClient http) =>
        new(h.Tenant, ScopeEnvironment, new("TENANT", root.Id, root.Version, "lookup-v1"), wire.Origin, http);

    private sealed record DispatchAdmissionRow(Guid RootId, Guid VersionId, Guid InstanceId, Guid TokenId, string NodeId, Guid FlowVersionId,
        string EffectKey, string TransportKey, string InputSha256, byte[] InputBytes, DateTime? IdempotencyUntil, DateTime? QueryUntil);
    private static Task<DispatchAdmissionRow> DispatchAdmission(Harness h, Guid job) => h.Db.Database.SqlQuery<DispatchAdmissionRow>($"""
        SELECT root_id AS "RootId",version_id AS "VersionId",instance_id AS "InstanceId",token_id AS "TokenId",node_id AS "NodeId",
                flow_version_id AS "FlowVersionId",effect_key AS "EffectKey",transport_key AS "TransportKey",input_sha256 AS "InputSha256",
                input_bytes AS "InputBytes",idempotency_until AS "IdempotencyUntil",query_until AS "QueryUntil"
        FROM cp6_oa_conn_v1.dispatch_admissions WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND job_id={job}
        """).SingleAsync();

    private static async Task AssertDispatchCommitted(Harness h, Wf_ServiceJob job, ConnResource root, OaConnectorDispatchWireFixture.Call sent)
    {
        await using var connection = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using (var lockRoot = new NpgsqlCommand("""
            SET LOCAL lock_timeout='1500ms';
            SELECT cp6_authentication_v1.acquire()
            """, connection, tx))
            await lockRoot.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand("""
            SELECT "Id" FROM public."Wf_ServiceJob" WHERE "TenantId"=$1 AND "Id"=$2 FOR UPDATE NOWAIT
            """, connection, tx);
        command.Parameters.Add(new() { Value = h.Tenant });
        command.Parameters.Add(new() { Value = job.Id });
        await command.ExecuteNonQueryAsync();
        command.CommandText = """
            SELECT a.root_id,a.version_id,a.instance_id,a.token_id,a.node_id,a.effect_key,a.transport_key,a.input_sha256,a.input_bytes
            FROM cp6_oa_conn_v1.dispatch_admissions a JOIN cp6_oa_conn_v1.runtime_members m
              ON (m.tenant_id,m.environment_id,m.root_id,m.kind,m.id)=(a.tenant_id,a.environment_id,a.root_id,a.member_kind,a.job_id)
            WHERE a.tenant_id=$1 AND a.job_id=$2 AND m.state='DISPATCH_ADMITTED'
            """;
        await using (var row = await command.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());
            Assert.Equal((root.Id, root.Version, job.InstanceId, job.TokenId, job.NodeId),
                (row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetGuid(3), row.GetString(4)));
            Assert.Equal((sent.Effect, sent.Transport, sent.Digest), (row.GetString(5), row.GetString(6), row.GetString(7)));
            Assert.Equal(sent.Body, row.GetFieldValue<byte[]>(8));
            Assert.Equal("""{"subject":"original"}""", Encoding.UTF8.GetString(sent.Body));
            Assert.False(await row.ReadAsync());
        }
        await tx.CommitAsync();
    }

    private static Task<string?> DispatchLastError(Harness h, Guid id) => h.Db.Wf_ServiceJobs.AsNoTracking()
        .Where(j => j.Id == id).Select(j => j.LastError).SingleAsync();
    private static Task<int> MakeDispatchDue(Harness h, Guid id) => h.Db.Wf_ServiceJobs.Where(j => j.Id == id)
        .ExecuteUpdateAsync(rows => rows.SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
    private static async Task RevokeDispatchAction(Harness h, string action)
    {
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "oa-flow-admin").Select(m => m.MenuId).SingleAsync();
        Assert.Equal(1, await h.Db.Sys_RoleActions.Where(g => g.RoleId == 72 && g.MenuId == menu && g.ActionCode == action).ExecuteDeleteAsync());
        var current = await PostgreSqlPermissionReader.ReadAsync(h.Db, h.User, CancellationToken.None);
        Assert.DoesNotContain("oa-flow-admin:" + action, current.ActionKeys);
    }
    private static Task<int> ObservationCount(Harness h, Guid id) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.dispatch_observations
        WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND job_id={id}
        """).SingleAsync();
    private static async Task AssertOriginalTokenHeld(Harness h, Wf_ServiceJob job)
    {
        Assert.Equal(ServiceJobStatus.Pending, await h.Db.Wf_ServiceJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.Status).SingleAsync());
        Assert.Equal(FlowTokenStatus.Active, await h.Db.Wf_FlowTokens.AsNoTracking().Where(t => t.Id == job.TokenId && t.NodeId == job.NodeId)
            .Select(t => t.Status).SingleAsync());
    }

    private static Task<int> DispatchMemberCount(Harness h, Guid job) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.runtime_members
        WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND kind='DISPATCH' AND id={job}
        """).SingleAsync();

    private static async Task SetDispatchScope(ConnApi api, bool enabled)
    {
        const string route = "api/iam/scope-authorization";
        using var response = await api.Host.Send(OperationsRequest(api.Cookies, HttpMethod.Get, route));
        Assert.True(response.IsSuccessStatusCode, await OperationsFailure(response));
        var current = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        using var changed = await api.Host.Send(OperationsRequest(api.Cookies, HttpMethod.Post, route,
            new ScopeAuthorizationCommandV1(Guid.NewGuid(), current.GetProperty("version").GetString()!, enabled, "隔离运行顺序验证")));
        Assert.True(changed.IsSuccessStatusCode, await OperationsFailure(changed));
    }

    internal static WfServiceJobService DispatchWorker(CP6Context db, IConnectorDispatchAdmissionV1[] providers, params IWfConnector[] legacy)
        => DispatchWorkerWithRecorder(db, providers, null, legacy);

    private static WfServiceJobService DispatchWorkerWithRecorder(CP6Context db, IConnectorDispatchAdmissionV1[] providers,
        ApprovalTaskDecisionOwnerV2? recorder, params IWfConnector[] legacy)
    {
        var fence = new ScopeAuthorizationFenceV1(db, Options.Create(new ScopeAuthorizationOptionsV1 { EnvironmentId = ScopeEnvironment }));
        var runtime = new ConnectorControlRuntimeV1(db, fence,
            Options.Create(new OaConnectorControlOptionsV1 { AllowSimulatedParticipants = true }), providers);
        var executor = new WebApiExecutor(new TenantConnectorResolver(db, legacy), runtime);
        return new WfServiceJobService(db, new FlowEngine(db, new ApproverResolver(db)), [executor],
            connectorResults: runtime, appRecorder: recorder);
    }

    private static (Process Worker, string PidPath) StartDispatchProcess(Harness h, ConnResource root,
        OaConnectorDispatchWireFixture wire, Guid job, bool result = false, bool pauseApply = false)
    {
        var pidPath = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "dispatch-worker-" + Guid.NewGuid().ToString("N") + ".pid");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(OaConnectorDispatchProcessTests).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName~OaConnectorDispatchProcessTests.RunScan");
        start.Environment["CP6_WP1216_SCAN_PID"] = pidPath;
        start.Environment["CP6_WP1216_SCAN_DB"] = h.Db.Database.GetConnectionString()!;
        start.Environment["CP6_WP1216_SCAN_BINDING"] = JsonSerializer.Serialize(new DispatchProcessBinding(h.Tenant, ScopeEnvironment,
            new("TENANT", root.Id, root.Version, "lookup-v1"), job, wire.Origin, result, pauseApply));
        return (Process.Start(start)!, pidPath);
    }

    private static async Task<Wf_ServiceJob> DispatchJob(Harness h, ConnResource root, string name, bool pinned = true)
    {
        var node = new FlowNode
        {
            Id = "dispatch", Type = "serviceTask", ServiceKind = "webApi", ServiceConnectorName = name,
            ServiceConnectorPin = pinned ? new("TENANT", root.Id, root.Version, "lookup-v1") : null,
            ServiceMaxRetries = 0,
        };
        var schema = JsonSerializer.Serialize(new FlowSchema
        {
            Start = node.Id, Nodes = [node, new() { Id = "end", Type = "end" }],
            Edges = [new() { From = node.Id, To = "end" }],
        });
        var definition = new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "dispatch-" + Guid.NewGuid().ToString("N"),
            FlowName = "派发合同夹具", SchemaJson = schema };
        var version = new Wf_FlowDefVersion
        {
            FlowDefId = definition.Id, Version = 1, Status = WfDefinitionVersionStatus.Published,
            SchemaJson = schema, FlowNameSnapshot = definition.FlowName, PublishedAtUtc = DateTime.UtcNow, PublishedBy = h.User,
        };
        h.Db.AddRange(definition, version);
        await h.Db.SaveChangesAsync();
        var instance = await new FlowEngine(h.Db, new ApproverResolver(h.Db)).SubmitAsync(definition.FlowKey, h.User, """{"subject":"original"}""");
        h.Db.ChangeTracker.Clear();
        return await h.Db.Wf_ServiceJobs.AsNoTracking().SingleAsync(j => j.InstanceId == instance);
    }
}

internal sealed record DispatchProcessBinding(Guid Tenant, Guid Environment, ConnectorPinV1 Pin, Guid Job, Uri Origin,
    bool Result, bool PauseApply);

internal sealed class OaConnectorDispatchProcessFactAttribute : FactAttribute
{
    public OaConnectorDispatchProcessFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_WP1216_SCAN_PID") == null) Skip = "由隔离 PostgreSQL 用例启动独立 worker 进程。";
    }
}

public sealed class OaConnectorDispatchProcessTests
{
    [OaConnectorDispatchProcessFact]
    public async Task RunScan()
    {
        var binding = JsonSerializer.Deserialize<DispatchProcessBinding>(Environment.GetEnvironmentVariable("CP6_WP1216_SCAN_BINDING")!)!;
        await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("CP6_WP1216_SCAN_PID")!, Environment.ProcessId.ToString());
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CP6_WP1216_SCAN_DB")).Options;
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        var provider = binding.Result
            ? new OaConnectorResultHttpParticipant(binding.Tenant, binding.Environment, binding.Pin, binding.Origin, http)
                { BeforeApply = binding.PauseApply ? ct => Task.Delay(Timeout.InfiniteTimeSpan, ct) : null }
            : new OaConnectorDispatchHttpParticipant(binding.Tenant, binding.Environment, binding.Pin, binding.Origin, http);
        var services = new ServiceCollection().AddScoped<ITenantContext, TenantContext>()
            .AddScoped(sp => new CP6Context(options, sp.GetRequiredService<ITenantContext>()))
            .AddScoped<ITenantEnumerator, TenantEnumerator>()
            .AddScoped<IWfServiceJobService>(sp => OrderPriceCorrectionPostgreSqlTests.DispatchWorker(sp.GetRequiredService<CP6Context>(), [provider]));
        await using var serviceProvider = services.BuildServiceProvider();
        using var scan = new WfServiceJobScanWorker(serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WfServiceJobScanWorker>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await scan.StartAsync(timeout.Token);
        try
        {
            await using var witness = new CP6Context(options, new TenantContext { CurrentTenantId = binding.Tenant });
            while (!await witness.Wf_ServiceJobs.AsNoTracking().AnyAsync(j => j.Id == binding.Job &&
                (j.LastError != null || j.Status == ServiceJobStatus.Succeeded), timeout.Token))
                await Task.Delay(50, timeout.Token);
        }
        finally { await scan.StopAsync(CancellationToken.None); }
    }
}
