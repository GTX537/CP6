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
    public async Task Timeout_ErrorEdge_SiblingTokenOnSameNode_Survives()
    {
        // 终审 Important#1（B-T1 审查 Minor#2）：畸形但合法 schema——parallelSplit 双出边汇入同一 approval 节点
        // → 两 token 同 NodeId 各持 Pending 待办。其一超时走 errorEdge 时，作废谓词若缺 TokenId 过滤，
        // 会把兄弟 token 的待办一并误废却只路由本 token → 兄弟支永挂。断言：兄弟待办存活 + 兄弟 token 仍 Active 停原节点，
        // 超时支正常沿错误边进 handler 并生成新待办。
        using var conn = WfTestDb.NewSqliteWithSchema();
        var approver = Guid.NewGuid();
        var schema = JsonSerializer.Serialize(new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "split", Type = "parallelSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver,
                              TimeoutHours = 1, TimeoutAction = "errorEdge" },
                new FlowNode { Id = "handler", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "split" },
                new FlowEdge { From = "split", To = "a" },   // 双边汇入同一 approval → 两 token 同 NodeId
                new FlowEdge { From = "split", To = "a" },
                new FlowEdge { From = "a", To = "end" },
                new FlowEdge { From = "a", To = "handler", IsError = true },
            },
        });

        Guid instId, dueTaskId, siblingTaskId;
        using (var db = WfTestDb.Ctx(conn))
        {
            db.Sys_Users.Add(new CP6.Entity.DomainModels.Sys.Sys_User { Id = approver, UserName = "ap", Password = "x", RoleId = 1, Enable = true });
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "fk2", FlowName = "fk2", FormKey = "f",
                SchemaJson = schema, Version = 1, Enable = true });
            await db.SaveChangesAsync();
            instId = await WfTestDb.Engine(db).SubmitAsync("fk2", approver, "{}");

            // 两枚同节点 Pending 待办（各属一 token）；把其一 DueAt 推远 → 本轮扫描只处理另一枚（错峰到期）
            var tasks = await db.Wf_FlowTasks
                .Where(t => t.InstanceId == instId && t.NodeId == "a" && t.Status == FlowTaskStatus.Pending)
                .OrderBy(t => t.Id).ToListAsync();
            Assert.Equal(2, tasks.Count);
            dueTaskId = tasks[0].Id;
            siblingTaskId = tasks[1].Id;
            tasks[1].DueAt = DateTime.UtcNow.AddHours(100);
            await db.SaveChangesAsync();
        }

        using (var db = WfTestDb.Ctx(conn))
        {
            var handled = await new WfTimeoutService(db, WfTestDb.Engine(db))
                .ScanOnceAsync(DateTime.UtcNow.AddHours(2), CancellationToken.None);
            Assert.Equal(1, handled);   // 只有到期的那枚被处理
        }

        using (var db = WfTestDb.Ctx(conn))
        {
            var dueTask = await db.Wf_FlowTasks.SingleAsync(t => t.Id == dueTaskId);
            var sibling = await db.Wf_FlowTasks.SingleAsync(t => t.Id == siblingTaskId);
            Assert.Equal(FlowTaskStatus.Cancelled, dueTask.Status);          // 超时支待办被作废
            Assert.Equal(FlowTaskStatus.Pending, sibling.Status);            // ★ 兄弟待办存活（修复前被误废 → 红）

            var dueToken = await db.Wf_FlowTokens.SingleAsync(t => t.Id == dueTask.TokenId);
            var siblingToken = await db.Wf_FlowTokens.SingleAsync(t => t.Id == sibling.TokenId);
            Assert.Equal("handler", dueToken.NodeId);                        // 超时支已沿错误边路由（路由的是本 token）
            Assert.Equal(FlowTokenStatus.Active, dueToken.Status);
            Assert.Equal("a", siblingToken.NodeId);                          // ★ 兄弟 token 仍停原节点
            Assert.Equal(FlowTokenStatus.Active, siblingToken.Status);

            var pendingHandler = await db.Wf_FlowTasks.CountAsync(
                t => t.InstanceId == instId && t.NodeId == "handler" && t.Status == FlowTaskStatus.Pending);
            Assert.Equal(1, pendingHandler);                                 // 错误边落点生成新待办
            var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
            Assert.Equal(FlowInstanceStatus.Running, inst.Status);
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
