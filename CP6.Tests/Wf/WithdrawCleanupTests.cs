using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>
/// Fix3：撤回(WithdrawAsync)终止实例时，除清在途待办外，还须级联清理读模型 ——
/// Active token → Cancelled、Pending 传签履历 → Voided，否则未来收件箱会显示幻影待签。
/// </summary>
public class WithdrawCleanupTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedFlowAsync(CP6Context db, Guid approver)
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
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Withdraw_CancelsActiveTokens_AndVoidsPendingFormTos()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var starter = Guid.NewGuid();
        var instId = await Engine(db).SubmitAsync("leave", starter, "{}");

        // 提交后：1 Active token + 1 Pending 传签履历
        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.Equal(1, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending));

        await new TaskCenterService(db).WithdrawAsync(instId, starter);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Withdrawn, inst.Status);
        // token 清场：零残留 Active
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        // 履历清场：零 Pending，原行作废
        Assert.Equal(0, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Voided));
    }
}
