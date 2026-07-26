using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>OA 章05 §2/§3 审批服务（A-1/A-2）：按绑定起流程 + 防重 + 状态查询。</summary>
public class ApprovalServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    /// <summary>种一个单审批节点（指定审批人）的流程 + 业务绑定。</summary>
    private static async Task SeedFlowAndBindingAsync(CP6Context db, string flowKey, string bizType, Guid approver)
    {
        var schema = new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        };
        var head = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = bizType,
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        };
        db.Wf_FlowDefs.Add(head);
        db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published,
            FlowNameSnapshot = head.FlowName, SchemaJson = head.SchemaJson,
        });
        db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
        {
            Id = Guid.NewGuid(), BizType = bizType, FlowKey = flowKey, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    private static ApprovalService Svc(CP6Context db) =>
        new(db, new FlowEngine(db, new ApproverResolver(db)));

    [Fact]
    public async Task Submit_NoBinding_Throws()
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Svc(db).SubmitAsync("PO", "PO-001", Guid.NewGuid(), new { amount = 100 }));
    }

    [Fact]
    public async Task Submit_WithBinding_StartsFlow_TagsBizTypeAndId()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAndBindingAsync(db, "po-approval", "PO", approver);

        var instId = await Svc(db).SubmitAsync("PO", "PO-001", Guid.NewGuid(), new { amount = 100 });

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(instId, inst.Id);
        Assert.Equal("PO", inst.BizType);
        Assert.Equal("PO-001", inst.BizId);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
        Assert.Contains("amount", inst.VarsJson);   // formSnapshot 落 VarsJson
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == approver));
    }

    [Fact]
    public async Task Submit_Duplicate_WhileRunning_Throws()
    {
        using var db = NewDb();
        await SeedFlowAndBindingAsync(db, "po-approval", "PO", Guid.NewGuid());

        await Svc(db).SubmitAsync("PO", "PO-001", Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Svc(db).SubmitAsync("PO", "PO-001", Guid.NewGuid()));   // 同单据进行中 → 防重
    }

    [Fact]
    public async Task Submit_SameBizType_DifferentBizId_Allowed()
    {
        using var db = NewDb();
        await SeedFlowAndBindingAsync(db, "po-approval", "PO", Guid.NewGuid());

        await Svc(db).SubmitAsync("PO", "PO-001", Guid.NewGuid());
        await Svc(db).SubmitAsync("PO", "PO-002", Guid.NewGuid());   // 不同单据互不影响

        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task GetStatus_NoInstance_None()
    {
        using var db = NewDb();
        Assert.Equal(ApprovalStatus.None, await Svc(db).GetStatusAsync("PO", "PO-404"));
    }

    [Fact]
    public async Task GetStatus_ReflectsRunningInstance()
    {
        using var db = NewDb();
        await SeedFlowAndBindingAsync(db, "po-approval", "PO", Guid.NewGuid());
        await Svc(db).SubmitAsync("PO", "PO-001", Guid.NewGuid());

        Assert.Equal(ApprovalStatus.Running, await Svc(db).GetStatusAsync("PO", "PO-001"));
    }
}
