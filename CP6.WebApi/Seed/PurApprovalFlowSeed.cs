using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.WebApi.Seed;

/// <summary>
/// 采购 PR/PO 审批流程种子（仿 <see cref="A5BudgetFlowSeed"/>，幂等：按 FlowKey/BizType 判存）。
/// 各建一个单审批节点（Specified 策略指定管理员）的流程并绑定 PUR_PO / PUR_PR。
/// 配了绑定后，PR/PO 送审走真实 OA 审批（不再自动通过）；删除/禁用绑定即回退自动放行。
/// </summary>
public static class PurApprovalFlowSeed
{
    public static void Seed(CP6Context db, Guid? approverUserId = null)
    {
        var approver = approverUserId
            ?? db.Sys_Users.Where(u => u.UserName == "admin").Select(u => (Guid?)u.Id).FirstOrDefault()
            ?? Guid.Empty;

        SeedFlow(db, "po-approve", "采购订单审批", "PUR_PO", approver);
        SeedFlow(db, "pr-approve", "采购申请审批", "PUR_PR", approver);
        db.SaveChanges();
    }

    private static void SeedFlow(CP6Context db, string flowKey, string flowName, string bizType, Guid approver)
    {
        if (!db.Wf_FlowDefs.Any(f => f.FlowKey == flowKey))
        {
            var schema = new FlowSchema
            {
                Nodes =
                {
                    new FlowNode { Id = "n1", Type = "approval", Name = flowName, ApproverStrategy = "Specified", ApproverUserId = approver },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "end" } },
            };
            db.Wf_FlowDefs.Add(new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowName, FormKey = flowKey,
                SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
            });
        }

        if (!db.Wf_ApprovalBindings.Any(x => x.BizType == bizType))
        {
            db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding { Id = Guid.NewGuid(), BizType = bizType, FlowKey = flowKey, Enable = true });
        }
    }
}
