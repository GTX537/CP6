using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.WebApi.Seed;

/// <summary>
/// 采购 PR/PO 审批流程种子（仿 <see cref="A5BudgetFlowSeed"/>，幂等：按 FlowKey/BizType 判存）。
/// 各建一个单审批节点（Specified 策略指定管理员）的流程并绑定 PUR_PO / PUR_PR。
/// 配了绑定后，PR/PO 送审走真实 OA 审批；缺失/停用绑定一律 fail-closed。
/// </summary>
public static class PurApprovalFlowSeed
{
    public static void Seed(CP6Context db, Guid? approverUserId = null)
    {
        var approver = approverUserId
            ?? db.Sys_Users.Where(u => u.UserName == "admin").Select(u => (Guid?)u.Id).FirstOrDefault()
            ?? throw new InvalidOperationException("PUR approval seed requires an enabled approver");
        if (approver == Guid.Empty) throw new InvalidOperationException("PUR approval seed approver cannot be empty");

        SeedFlow(db, "po-approve", "采购订单审批", "PUR_PO", approver, null);
        SeedFlow(db, "pr-approve", "采购申请审批", "PUR_PR", approver, "/pur/pr?prNo={bizId}");
        db.SaveChanges();
    }

    private static void SeedFlow(
        CP6Context db, string flowKey, string flowName, string bizType, Guid approver, string? detailRoute)
    {
        var head = db.Wf_FlowDefs.FirstOrDefault(f => f.FlowKey == flowKey);
        if (head == null)
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
            head = new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowName, FormKey = flowKey,
                SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
            };
            db.Wf_FlowDefs.Add(head);
        }
        if (!db.Wf_FlowDefVersions.Any(x =>
                x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Published))
        {
            db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion
            {
                Id = Guid.NewGuid(), FlowDefId = head.Id, Version = Math.Max(1, head.Version),
                Status = WfDefinitionVersionStatus.Published,
                FlowNameSnapshot = head.FlowName, SchemaJson = head.SchemaJson,
                PublishedAtUtc = DateTime.UtcNow, PublishedBy = approver,
            });
        }

        var binding = db.Wf_ApprovalBindings.FirstOrDefault(x => x.BizType == bizType);
        if (binding == null)
        {
            db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
            {
                Id = Guid.NewGuid(), BizType = bizType, FlowKey = flowKey,
                Enable = true, DetailRoute = detailRoute,
            });
        }
        else if (bizType == "PUR_PR" && string.IsNullOrWhiteSpace(binding.DetailRoute))
        {
            binding.DetailRoute = detailRoute;
        }
    }
}
