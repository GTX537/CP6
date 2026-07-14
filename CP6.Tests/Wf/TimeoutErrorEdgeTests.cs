using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

// 复用 WfTestDb 的 SQLite 基座建库口径（GenerateCreateScript+TEXT 替换+FlowInstance rowversion 触发器）。
public class TimeoutErrorEdgeTests
{
    private static string SchemaWithApprovalTimeoutErrorEdge(Guid approver) => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver,
                          TimeoutHours = 1, TimeoutAction = "errorEdge" },
            new FlowNode { Id = "handler", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "a" },
            new FlowEdge { From = "a", To = "end" },
            new FlowEdge { From = "a", To = "handler", IsError = true },   // 失败边
        },
    });

    [Fact]
    public async Task Timeout_ErrorEdge_VoidsPendingTask_RoutesAlongErrorEdge()
    {
        using var conn = WfTestDb.NewSqliteWithSchema();
        var approver = Guid.NewGuid();
        Guid instId;
        using (var db = WfTestDb.Ctx(conn))
        {
            db.Sys_Users.Add(new CP6.Entity.DomainModels.Sys.Sys_User { Id = approver, UserName = "ap", Password = "x", RoleId = 1, Enable = true });
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "fk", FlowName = "fk", FormKey = "f",
                SchemaJson = SchemaWithApprovalTimeoutErrorEdge(approver), Version = 1, Enable = true });
            await db.SaveChangesAsync();
            var eng = WfTestDb.Engine(db);
            instId = await eng.SubmitAsync("fk", approver, "{}");   // 停在 approval "a"，生成 pending 待办
        }

        using (var db = WfTestDb.Ctx(conn))
        {
            var svc = new WfTimeoutService(db, WfTestDb.Engine(db));
            var handled = await svc.ScanOnceAsync(DateTime.UtcNow.AddHours(2), CancellationToken.None);   // 越过 DueAt
            Assert.Equal(1, handled);
        }

        using (var db = WfTestDb.Ctx(conn))
        {
            // 原 approval "a" 的待办被作废；token 已沿错误边进入 "handler" 节点并生成新 pending
            var tasksA = await db.Wf_FlowTasks.Where(t => t.InstanceId == instId && t.NodeId == "a").ToListAsync();
            Assert.All(tasksA, t => Assert.NotEqual(FlowTaskStatus.Pending, t.Status));
            var pendingHandler = await db.Wf_FlowTasks.CountAsync(t => t.InstanceId == instId && t.NodeId == "handler" && t.Status == FlowTaskStatus.Pending);
            Assert.Equal(1, pendingHandler);
            var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
            Assert.Equal(FlowInstanceStatus.Running, inst.Status);
            Assert.Contains("timeoutError", inst.VarsJson);   // 错误变量已注入
        }
    }

    [Fact]
    public async Task Timeout_Reject_ByteEquivalent_NoRegression()
    {
        // 三既有硬动作零回归的定点：reject 仍走自动驳回（不因 errorEdge case 增改而变）
        using var conn = WfTestDb.NewSqliteWithSchema();
        var approver = Guid.NewGuid();
        Guid instId;
        var schema = JsonSerializer.Serialize(new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver, TimeoutHours = 1, TimeoutAction = "reject" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
        });
        using (var db = WfTestDb.Ctx(conn))
        {
            db.Sys_Users.Add(new CP6.Entity.DomainModels.Sys.Sys_User { Id = approver, UserName = "ap", Password = "x", RoleId = 1, Enable = true });
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "fk", FlowName = "fk", FormKey = "f", SchemaJson = schema, Version = 1, Enable = true });
            await db.SaveChangesAsync();
            instId = await WfTestDb.Engine(db).SubmitAsync("fk", approver, "{}");
        }
        using (var db = WfTestDb.Ctx(conn))
        {
            await new WfTimeoutService(db, WfTestDb.Engine(db)).ScanOnceAsync(DateTime.UtcNow.AddHours(2), CancellationToken.None);
        }
        using (var db = WfTestDb.Ctx(conn))
        {
            var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
            Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);   // reject 语义不变
        }
    }
}
