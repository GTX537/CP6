using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.WebApi.Seed;

/// <summary>
/// A5 预算审批流程种子（spec §8，幂等：按 FlowKey/BizType 判存）。
/// 建一个单审批节点（Specified 策略指定管理员 / 测试可自行指定 ApproverUserId）的审批流程并绑定到 A5_Budget。
/// </summary>
public static class A5BudgetFlowSeed
{
    private const string FlowKey = "budget-approve";
    private const string BizType = "A5_Budget";

    public static void Seed(CP6Context db, Guid? approverUserId = null)
    {
        if (!db.Wf_FlowDefs.Any(f => f.FlowKey == FlowKey))
        {
            var schema = new FlowSchema
            {
                Nodes =
                {
                    new FlowNode
                    {
                        Id = "n1",
                        Type = "approval",
                        Name = "预算审批",
                        ApproverStrategy = "Specified",
                        ApproverUserId = approverUserId ?? Guid.Empty,   // 实际部署时替换为真实管理员 Id
                    },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "end" } },
            };

            db.Wf_FlowDefs.Add(new Wf_FlowDef
            {
                Id = Guid.NewGuid(),
                FlowKey = FlowKey,
                FlowName = "预算审批",
                FormKey = "BudgetApproval",
                SchemaJson = JsonSerializer.Serialize(schema),
                Version = 1,
                Enable = true,
            });
        }

        if (!db.Wf_ApprovalBindings.Any(x => x.BizType == BizType))
        {
            db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
            {
                Id = Guid.NewGuid(),
                BizType = BizType,
                FlowKey = FlowKey,
                Enable = true,
            });
        }

        db.SaveChanges();
    }
}
