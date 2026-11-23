using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task OaConnResult_app_tx_d_rolls_back_without_outbox_then_recovers_and_reads_original_owner_chain()
    {
        var hook = new StartWriteHook();
        var commit = new CommitFault();
        await using var h = await Seed(hook, commit);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        ConnApi maker = null!;
        ConnResource root = null!;
        var prepared = await IntakeSetup(h, beforeFreeze: async () =>
        {
            await ConnPeople(h);
            await EngineeringAction(h, "oa-inbox", "approve");
            await EngineeringAction(h, "oa-inbox", "query");
            var menu = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "oa-inbox");
            if (!await h.Db.Sys_RoleMenus.AnyAsync(x => x.RoleId == 72 && x.MenuId == menu.MenuId))
            {
                h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId });
                await h.Db.SaveChangesAsync();
                h.Db.ChangeTracker.Clear();
            }
            maker = new ConnApi(host, await Login(host, h));
            root = await CreateResultConnector(h, maker, "app-result");
        });
        var schema = JsonSerializer.Serialize(new
        {
            start = "s",
            nodes = new object[]
            {
                new { id = "s", type = "start" },
                new { id = "a", type = "approval", approverStrategy = "Role", approverRoleId = 73, countersign = "all" },
                new { id = "dispatch", type = "serviceTask", serviceKind = "webApi", serviceConnectorName = "app-result",
                    serviceConnectorPin = new { sourceKind = "TENANT", rootId = root.Id, versionId = root.Version, operation = "lookup-v1" } },
                new { id = "e", type = "end" }
            },
            edges = new[] { new { from = "s", to = "a" }, new { from = "a", to = "dispatch" }, new { from = "dispatch", to = "e" } }
        });
        var (intake, first, second) = await DecisionSetup(h, schema, prepared: prepared);
        foreach (var id in new[] { first, second })
        {
            var actor = DecisionActor(h, id);
            var voted = await Vote(actor, await DecisionInput(actor));
            Assert.Null(voted.Result.DecisionId);
            Assert.Equal(FlowInstanceStatus.Running, voted.Result.Task.InstanceStatus);
        }

        var job = await h.Db.Wf_ServiceJobs.AsNoTracking().SingleAsync();
        Assert.False(await AppResultAllowsParkedClaim(h, job.Id));
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        using var http = DispatchHttp();
        wire.ResultPayload = OriginalConnectorResult();
        var worker = DispatchWorkerWithRecorder(h.Db, [ResultParticipant(h, root, wire, http)], Decisions(h));
        var omittedRecorder = false;
        hook.Before = command =>
        {
            var outbox = command.CommandText.IndexOf("INSERT INTO cp6_approval_decision_v2.outbox", StringComparison.Ordinal);
            if (outbox >= 0)
            {
                omittedRecorder = true;
                command.CommandText = command.CommandText[..outbox];
            }
            return Task.CompletedTask;
        };
        Assert.Equal(0, await worker.ScanOnceAsync(DateTime.UtcNow, "app-recorder-omitted"));
        hook.Before = null;
        Assert.True(omittedRecorder);
        Assert.Equal(wire.ResultPayload, await ReadConnectorResult(h, job.Id));
        Assert.Null(await ResumeReceipt(h, job.Id));
        var held = await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == job.InstanceId);
        Assert.Equal(FlowInstanceStatus.Running, held.Status);
        Assert.DoesNotContain("connectorMessage", held.VarsJson!);
        Assert.Equal(FlowTokenStatus.Active, await h.Db.Wf_FlowTokens.AsNoTracking().Where(t => t.Id == job.TokenId).Select(t => t.Status).SingleAsync());
        Assert.Equal(ServiceJobStatus.Pending, await h.Db.Wf_ServiceJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.Status).SingleAsync());
        Assert.Equal("APP_RESUME_DECISION_INCOMPLETE", await DispatchLastError(h, job.Id));
        Assert.True(await AppResultAllowsParkedClaim(h, job.Id));
        output.WriteLine("SIMULATED APP 队列守卫，原件入箱前拒绝等待中的领取，入箱后接受同 Scope 的领取。");
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant}
            """).ToArrayAsync());

        // 丢弃已经成功的本地提交确认，下一次扫描从持久状态判断原回执。
        hook.Before = command =>
        {
            if (command.CommandText.Contains("INSERT INTO cp6_oa_conn_v1.result_outbox")) commit.Armed = true;
            return Task.CompletedTask;
        };
        Assert.Equal(0, await worker.ScanOnceAsync(DateTime.UtcNow, "app-lost-commit"));
        hook.Before = null;
        var receipt = await ResumeReceipt(h, job.Id);
        Assert.NotNull(receipt);
        Assert.Equal(0, await worker.ScanOnceAsync(DateTime.UtcNow, "app-original-visible"));
        Assert.Equal(receipt, await ResumeReceipt(h, job.Id));
        var decisionBytes = await h.Db.Database.SqlQuery<byte[]>($"""
            SELECT payload AS "Value" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}
            """).SingleAsync();
        var fact = await FactForDecision(h, intake, decisionBytes);
        Assert.Equal(receipt, fact.Decision.TerminalActor.GetProperty("executionId").GetGuid());
        Assert.Equal("RESUME", fact.Decision.TerminalActor.GetProperty("trigger").GetString());
        Assert.Equal("HUMAN", fact.Decision.DecisionMode);
        var view = await ResultDispatch(maker, root, job.Id);
        Assert.Equal("APPLIED", view.GetProperty("workflowResume").GetString());
        Assert.Equal("UNPROVEN", view.GetProperty("businessOwnerApplication").GetString());

        await fact.Owner.ReceiveAsync(decisionBytes);
        var tool = await ApplicationTool(h);
        var one = (await ApplicationAttempt(h, tool)).Receipt;
        var two = (await ApplicationAttempt(h, tool)).Receipt;
        var principal = intake.Authority.Identity.Principal;
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.registrations(environment_id,tenant_id,principal_id,owner,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Wms.ToolDisposition.v1',{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_receipt_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,owner,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},{fact.Decision.SubmissionId},'Main.Wms.ToolDisposition.v1',{Guid.NewGuid()},true,true,true);
            """);
        var inbox = ReceiptReceiver(h, intake.Authority);
        Assert.Equal(202, (await inbox.ReceiveAsync(ReceiptBytes(two))).Status);
        Assert.Equal("PENDING_GAP", (await ResultDispatch(maker, root, job.Id)).GetProperty("businessOwnerApplication").GetString());
        var filled = Assert.IsType<ApprovalReceiptRecordedV2>((await inbox.ReceiveAsync(ReceiptBytes(one))).Body);
        Assert.Equal("2", filled.HighestContinuousRevision);
        Assert.Equal(two.ReceiptId, filled.ProjectionReceiptId);
        view = await ResultDispatch(maker, root, job.Id);
        Assert.Equal(two.Application, view.GetProperty("businessOwnerApplication").GetString());
        Assert.Equal("SIMULATED", view.GetProperty("businessOwnerEvidence").GetString());
        Assert.Equal(receipt, view.GetProperty("resumeReceiptId").GetGuid());
        await ApplicationCounts(h, 2);
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Single(wire.Sends);
        Assert.Empty(wire.Queries);
        output.WriteLine($"SIMULATED 连接器应用，APP 决策 {fact.Decision.DecisionId:D} 与 ResumeReceipt {receipt:D} 同事务。原 Owner 第 2 条回执先到，补齐后投影到 {two.Application}，外部送出总数 1。");
    }

    private static Task<bool> AppResultAllowsParkedClaim(Harness h, Guid job) => h.Db.Database.SqlQuery<bool>($"""
        SELECT cp6_oa_conn_v1.app_job_transition(snapshot.parked,
            snapshot.parked || jsonb_build_object(
                'Status',1,'LockedBy','app-result-guard','LockedAtUtc',snapshot.at,
                'LockExpiresAtUtc',snapshot.at+interval '300 seconds')) AS "Value"
        FROM public."Wf_ServiceJob" j
        CROSS JOIN LATERAL (
            SELECT to_jsonb(j) || jsonb_build_object('NextAttemptAtUtc','infinity'::timestamptz) AS parked,
                clock_timestamp() AS at
        ) snapshot
        WHERE j."TenantId"={h.Tenant} AND j."Id"={job}
        """).SingleAsync();
}
