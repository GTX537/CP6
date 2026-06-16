using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>
/// OA 章09 §3/§5 设计器同构验证（B-2，OA4-D6）。铁律：设计器导出的 schema JSON（含画布坐标 x/y
/// 等设计态字段）直接喂运行时 FlowEngine 必须能跑——运行时 FlowNode 无 x/y 属性，反序列化自动忽略。
/// </summary>
public class DesignerIsomorphismTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    [Fact]
    public async Task DesignerExportedSchema_WithCoordinates_RunsOnEngine()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();

        // 模拟流程设计器导出：含坐标 x/y + 超时等设计态字段，start→n1(指定审批人)→end，
        // n1→end 边带条件 days<=3（条件流转）。
        var schemaJson = $$"""
        {
          "start": "start",
          "nodes": [
            { "id": "start", "type": "start", "x": 40, "y": 40 },
            { "id": "n1", "type": "approval", "x": 220, "y": 40, "name": "经理审批",
              "approverStrategy": "Specified", "approverUserId": "{{approver}}",
              "countersign": "all", "timeoutHours": 24, "timeoutAction": "remind" },
            { "id": "end", "type": "end", "x": 400, "y": 40 }
          ],
          "edges": [
            { "from": "start", "to": "n1" },
            { "from": "n1", "to": "end", "condition": "days <= 3" }
          ]
        }
        """;
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "designer-flow", FlowName = "设计器产出流程", FormKey = "leave",
            SchemaJson = schemaJson, Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();

        // 起流程（vars days=2 满足条件）→ n1 给指定审批人建待办（坐标/超时字段被忽略不报错）
        var instId = await Engine(db).SubmitAsync("designer-flow", Guid.NewGuid(), """{"days":2}""");
        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        Assert.Equal(approver, task.AssigneeId);
        Assert.Equal("n1", task.NodeId);

        // 审批人同意 → 条件 days<=3 成立 → 流转 end → 通过
        await Engine(db).ActAsync(task.Id, approver, approve: true);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
    }

    [Fact]
    public async Task DesignerSchema_ConditionFalse_RoutesElsewhere_AutoEnds()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        // 只有一条带条件的出边，条件不成立 → 无后继 → 引擎兜底自动结束（仍验证设计态字段不碍事）
        var schemaJson = $$"""
        {
          "start": "n1",
          "nodes": [
            { "id": "n1", "type": "approval", "x": 10, "y": 10, "approverStrategy": "Specified", "approverUserId": "{{approver}}" },
            { "id": "end", "type": "end", "x": 200, "y": 10 }
          ],
          "edges": [ { "from": "n1", "to": "end", "condition": "days <= 3" } ]
        }
        """;
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "designer-cond", FlowName = "条件流程", FormKey = "x",
            SchemaJson = schemaJson, Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();

        var instId = await Engine(db).SubmitAsync("designer-cond", Guid.NewGuid(), """{"days":9}""");
        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(task.Id, approver, true);   // days=9 不满足 days<=3
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);   // 无匹配后继 → 兜底结束
    }
}
