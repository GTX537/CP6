using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.WebApi.Seed;

/// <summary>
/// Creates the single-step OA approval binding used by WMS production feature changes.
/// Production may replace the specified approver through the OA designer, but the
/// binding remains fail-closed and always resolves to a published workflow version.
/// </summary>
public static class WmsFeatureApprovalFlowSeed
{
    private const string FlowKey = "wms-feature-flag-change";
    private const string BizType = "WMS_FEATURE_FLAG_CHANGE";

    public static void Seed(CP6Context db, Guid? approverUserId = null)
    {
        var approver = approverUserId
            ?? db.Sys_Users.Where(x => x.UserName == "admin" && x.Enable)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "WMS feature approval seed requires an enabled approver");
        if (approver == Guid.Empty)
            throw new InvalidOperationException(
                "WMS feature approval seed approver cannot be empty");

        var head = db.Wf_FlowDefs.FirstOrDefault(x => x.FlowKey == FlowKey);
        if (head is null)
        {
            var schema = new FlowSchema
            {
                Nodes =
                {
                    new FlowNode
                    {
                        Id = "approval",
                        Type = "approval",
                        Name = "WMS production feature approval",
                        ApproverStrategy = "Specified",
                        ApproverUserId = approver,
                    },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "approval", To = "end" } },
            };
            head = new Wf_FlowDef
            {
                Id = Guid.NewGuid(),
                FlowKey = FlowKey,
                FlowName = "WMS production feature approval",
                FormKey = "WmsFeatureFlagChange",
                SchemaJson = JsonSerializer.Serialize(schema),
                Version = 1,
                Enable = true,
            };
            db.Wf_FlowDefs.Add(head);
        }

        if (!db.Wf_FlowDefVersions.Any(x =>
                x.FlowDefId == head.Id
                && x.Status == WfDefinitionVersionStatus.Published))
        {
            db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion
            {
                Id = Guid.NewGuid(),
                FlowDefId = head.Id,
                Version = Math.Max(1, head.Version),
                Status = WfDefinitionVersionStatus.Published,
                FlowNameSnapshot = head.FlowName,
                SchemaJson = head.SchemaJson,
                PublishedAtUtc = DateTime.UtcNow,
                PublishedBy = approver,
            });
        }

        var binding = db.Wf_ApprovalBindings.FirstOrDefault(x => x.BizType == BizType);
        if (binding is null)
        {
            db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
            {
                Id = Guid.NewGuid(),
                BizType = BizType,
                FlowKey = FlowKey,
                Enable = true,
                DetailRoute = "/wms/production",
            });
        }
        else
        {
            binding.FlowKey = FlowKey;
            binding.Enable = true;
            binding.DetailRoute ??= "/wms/production";
        }

        db.SaveChanges();
    }
}
