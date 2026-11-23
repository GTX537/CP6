using System.Security.Cryptography;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

internal sealed class OaConnectorForwardPostgresFactAttribute : FactAttribute
{
    internal const string BaselineAssemblyVariable = "CP6_WP1217_BASELINE_TEST_DLL";

    public OaConnectorForwardPostgresFactAttribute()
    {
        if (!CurrentPermissionPostgresFactAttribute.Selected ||
            Environment.GetEnvironmentVariable(BaselineAssemblyVariable) is null)
            Skip = "需启用隔离 PostgreSQL 并提供 Main 基线测试程序集。";
    }
}

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaConnectorForwardPostgresFact]
    public async Task OaConnResult_forward_migration_preserves_historical_app_commands_and_dispatch()
    {
        const string baseline = "20261123065100_WorkflowDeadlineEvidenceV1";
        const string candidate = "20261123083000_OaConnectorResultsV1";
        var baselineAssembly = Environment.GetEnvironmentVariable(OaConnectorForwardPostgresFactAttribute.BaselineAssemblyVariable)!;
        Assert.True(Path.IsPathFullyQualified(baselineAssembly) && File.Exists(baselineAssembly));
        var database = await server.CreateIsolatedDatabaseAsync(baseline);
        await using var h = await SeedForDatabase(database);
        var applied = (await h.Db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(baseline, applied.Last());
        Assert.False(await HasResultInbox(h));

        var (_, first, _) = await DecisionSetup(h);
        await NativeOperationPermissions(h);
        var recipient = Guid.NewGuid();
        h.Db.Sys_Users.Add(new() { Id = recipient, UserName = "migration-reviewer", Password = "fixture", RoleId = 73 });
        await h.Db.SaveChangesAsync();
        await TaskPermission(h, recipient);
        h.Db.ChangeTracker.Clear();
        var originalOwner = Decisions(DecisionActor(h, first));
        var initial = await DecisionInput(DecisionActor(h, first));
        var task = await originalOwner.ContextAsync(initial.Task);
        const string transferId = "migration-transfer";
        var transferred = await originalOwner.TransferTaskAsync(initial.Task, PersonalCommand(
            new NativeTransferInputV2(initial.Task, recipient, "保留基线 APP 转交原件", task.TaskRowVersion), transferId, task.InstanceRowVersion));
        var voter = DecisionActor(h, recipient);
        var command = await DecisionInput(voter);
        var voted = await Vote(voter, command);
        Assert.Null(voted.Result.DecisionId);
        Assert.Equal(FlowInstanceStatus.Running, voted.Result.Task.InstanceStatus);

        await ConnPeople(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var root = await CreateResultConnector(h, maker, "migration-dispatch");
        var job = await DispatchJob(h, root, "migration-dispatch");
        await using var wire = await OaConnectorDispatchWireFixture.StartAsync();
        var (process, _) = StartDispatchProcess(h, root, wire, job.Id, testAssembly: baselineAssembly);
        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                Assert.True(process.ExitCode == 0, await stdout + await stderr);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        Assert.Equal("HTTP_ACK", await DispatchLastError(h, job.Id));
        var sent = Assert.Single(wire.Sends);
        var original = await DispatchAdmission(h, job.Id);
        Assert.Equal((sent.Effect, sent.Transport, sent.Digest), (original.EffectKey, original.TransportKey, original.InputSha256));
        Assert.Equal(sent.Body, original.InputBytes);
        var before = await HistoricalFacts(h, command.Input.CommandId, initial.Task, job.Id);
        Assert.Equal(5, before.Count);

        await h.Db.Database.MigrateAsync();

        Assert.Equal(applied.Append(candidate), await h.Db.Database.GetAppliedMigrationsAsync());
        Assert.True(await HasResultInbox(h));
        var after = await HistoricalFacts(h, command.Input.CommandId, initial.Task, job.Id);
        Assert.Equal(before.Count, after.Count);
        foreach (var (name, bytes) in before.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Assert.Equal(bytes, after[name]);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            output.WriteLine($"SIMULATED 迁移前后原事实 {name}，字节数={bytes.Length}，SHA-256={digest}。");
        }
        Assert.Equal(voted, await Decisions(voter).OriginalAsync(command.Input.CommandId));
        Assert.Equal(ApprovalCanonicalV2.Bytes(transferred), ApprovalCanonicalV2.Bytes(
            await originalOwner.OriginalOperationAsync("OA_TASK_TRANSFER", initial.Task, transferId)));
        var recovered = await DispatchAdmission(h, job.Id);
        Assert.Equal(original, recovered with { InputBytes = original.InputBytes });
        Assert.Equal(original.InputBytes, recovered.InputBytes);
        var entry = await ResultDispatch(maker, root, job.Id);
        Assert.Equal("HTTP_ACK", entry.GetProperty("transportState").GetString());
        Assert.Equal("SIMULATED", entry.GetProperty("adoption").GetString());
        Assert.Equal("UNPROVEN", entry.GetProperty("businessOwnerApplication").GetString());
        Assert.Null(await ReadConnectorResult(h, job.Id));
        Assert.Null(await ResumeReceipt(h, job.Id));
        Assert.Single(wire.Sends);
        Assert.Empty(wire.Queries);
        output.WriteLine($"SIMULATED {baseline} → {candidate}，APP 命令={command.Input.CommandId:D}，派发 Job={job.Id:D}，POST=1，GET=0。");
    }

    private static Task<bool> HasResultInbox(Harness h) => h.Db.Database.SqlQueryRaw<bool>(
        "SELECT to_regclass('cp6_oa_conn_v1.result_inbox') IS NOT NULL AS \"Value\"").SingleAsync();

    private sealed record HistoricalFact(string Name, byte[] Snapshot);

    private static Task<Dictionary<string, byte[]>> HistoricalFacts(Harness h, Guid command, Guid task, Guid job)
        => h.Db.Database.SqlQuery<HistoricalFact>($"""
            SELECT 'APP 审批命令' AS "Name", convert_to(to_jsonb(c)::text,'UTF8') AS "Snapshot"
            FROM cp6_approval_decision_v2.commands c
            WHERE c.tenant_id={h.Tenant} AND c.environment_id={ScopeEnvironment} AND c.id={command}
            UNION ALL
            SELECT 'APP 投票', convert_to(to_jsonb(v)::text,'UTF8') FROM cp6_approval_decision_v2.votes v
            WHERE v.tenant_id={h.Tenant} AND v.environment_id={ScopeEnvironment} AND v.command_id={command}
            UNION ALL
            SELECT 'APP 转交命令', convert_to(to_jsonb(t)::text,'UTF8') FROM cp6_native_tasks_v2.commands t
            WHERE t.tenant_id={h.Tenant} AND t.environment_id={ScopeEnvironment} AND t.task_id={task} AND t.operation='OA_TASK_TRANSFER'
            UNION ALL
            SELECT '派发准入', convert_to(to_jsonb(a)::text,'UTF8') FROM cp6_oa_conn_v1.dispatch_admissions a
            WHERE a.tenant_id={h.Tenant} AND a.environment_id={ScopeEnvironment} AND a.job_id={job}
            UNION ALL
            SELECT '派发观察', convert_to(to_jsonb(o)::text,'UTF8') FROM cp6_oa_conn_v1.dispatch_observations o
            WHERE o.tenant_id={h.Tenant} AND o.environment_id={ScopeEnvironment} AND o.job_id={job}
            """).ToDictionaryAsync(fact => fact.Name, fact => fact.Snapshot);
}
