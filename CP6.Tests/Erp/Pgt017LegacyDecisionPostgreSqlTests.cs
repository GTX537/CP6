using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_legacy_task_decision_and_IAM_revocation_use_one_commit_order(bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "oa-inbox", "approve");
        var db = h.Db;
        var tenant = h.Tenant;
        var key = "legacy-" + tenant.ToString("N");
        var schema = """
            {"start":"start","nodes":[{"id":"start","type":"start"},{"id":"approval","type":"approval",
            "approverStrategy":"Specified","approverUserId":"__WRITER__"},{"id":"end","type":"end"}],
            "edges":[{"from":"start","to":"approval"},{"from":"approval","to":"end"}]}
            """.Replace("__WRITER__", h.User.ToString("D"), StringComparison.Ordinal);
        await LegacyDefinitionFixture.Flow(db, key, "Simulated historical decision", null, schema);
        var engine = new FlowEngine(db, new ApproverResolver(db));
        var instance = await engine.SubmitAsync(key, h.User, "{}");
        var task = await db.Wf_FlowTasks.AsNoTracking().Where(t => t.InstanceId == instance).Select(t => t.Id).SingleAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "approve");
        var path = $"api/oa/tasks/{task}/decision";
        var decision = new { Decision = "approve", Comment = "current approval" };
        var before = await Effects();
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> act;
        Task<HttpResponseMessage> change;
        FenceWait first;
        FenceWait second;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (revokeFirst)
            {
                await using var lockAction = new NpgsqlCommand(
                    """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await lockAction.ExecuteNonQueryAsync();
                change = Revoke();
                first = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                act = Act();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, act);
            }
            else
            {
                await using var lockTask = new NpgsqlCommand(
                    """SELECT 1 FROM public."Wf_FlowTask" WHERE "Id"=@task FOR UPDATE""", barrier, hold);
                lockTask.Parameters.AddWithValue("task", task);
                Assert.Equal(1, await lockTask.ExecuteScalarAsync());
                act = Act();
                first = await WaitForFence(observer, barrier.ProcessID, "%Wf_FlowTask%", null, act);
                change = Revoke();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await Effects());
            await hold.RollbackAsync();
        }
        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var response = await act;
        var after = await Effects();
        if (revokeFirst)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(response));
            Assert.Equal(before, after);
        }
        else
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            using var evidence = JsonDocument.Parse(after);
            var rows = evidence.RootElement;
            Assert.Equal(FlowTaskStatus.Approved, rows.GetProperty("task").GetProperty("Status").GetInt32());
            Assert.Equal(FlowInstanceStatus.Approved, rows.GetProperty("instance").GetProperty("Status").GetInt32());
            var approval = Assert.Single(rows.GetProperty("history").EnumerateArray(), x => x.GetProperty("Action").GetString() == "approve");
            Assert.Equal(h.User, approval.GetProperty("ActorId").GetGuid());
            var notification = Assert.Single(rows.GetProperty("outbox").EnumerateArray());
            Assert.Equal(h.User, notification.GetProperty("UserId").GetGuid());
        }
        using var stale = await Act();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(after, await Effects());
        output.WriteLine(JsonSerializer.Serialize(new { RevokeFirst = revokeFirst, first, second,
            Decision = (int)response.StatusCode, Stale = (int)stale.StatusCode, Task = task, Instance = instance }));

        Task<HttpResponseMessage> Act() => host.Send(OperationsRequest(writer, HttpMethod.Post, path, decision));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<string> Effects() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
              'task', (SELECT to_jsonb(t) FROM public."Wf_FlowTask" t WHERE t."Id"={task}),
              'instance', (SELECT to_jsonb(i) FROM public."Wf_FlowInstance" i WHERE i."Id"={instance}),
              'history', (SELECT jsonb_agg(to_jsonb(h) ORDER BY h."Id" ) FROM public."Wf_FlowHistory" h
                          WHERE h."InstanceId"={instance}),
              'outbox', COALESCE((SELECT jsonb_agg(to_jsonb(n) ORDER BY n."Id") FROM public."Wf_Notification" n
                          WHERE n."InstanceId"={instance}), '[]'::jsonb)
            )::text AS "Value"
            """).SingleAsync();
    }
}
