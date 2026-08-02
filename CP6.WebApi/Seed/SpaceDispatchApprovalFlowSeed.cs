using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.WebApi.Seed;

/// <summary>
/// Creates the fail-closed single-step OA binding for Space dispatch assignments.
/// Production can replace the specified approver in the OA designer.
/// </summary>
public static class SpaceDispatchApprovalFlowSeed
{
    private const string FlowKey = "space-dispatch-assignment";
    private const string BizType = "SPACE_DISPATCH_ASSIGNMENT";

    public static void Seed(CP6Context db, Guid? approverUserId = null)
    {
        var approver = approverUserId
            ?? db.Sys_Users.Where(value => value.UserName == "admin" && value.Enable)
                .Select(value => (Guid?)value.Id)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Space dispatch approval seed requires an enabled approver");
        if (approver == Guid.Empty)
            throw new InvalidOperationException(
                "Space dispatch approval seed approver cannot be empty");

        var head = db.Wf_FlowDefs.FirstOrDefault(value => value.FlowKey == FlowKey);
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
                        Name = "Space dispatch assignment approval",
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
                FlowName = "Space dispatch assignment approval",
                FormKey = "SpaceDispatchAssignment",
                SchemaJson = JsonSerializer.Serialize(schema),
                Version = 1,
                Enable = true,
            };
            db.Wf_FlowDefs.Add(head);
        }

        if (!db.Wf_FlowDefVersions.Any(value =>
                value.FlowDefId == head.Id &&
                value.Status == WfDefinitionVersionStatus.Published))
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

        var binding = db.Wf_ApprovalBindings.FirstOrDefault(
            value => value.BizType == BizType);
        if (binding is null)
        {
            db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
            {
                Id = Guid.NewGuid(),
                BizType = BizType,
                FlowKey = FlowKey,
                Enable = true,
                DetailRoute = "/space/viewer",
            });
        }
        else
        {
            binding.FlowKey = FlowKey;
            binding.Enable = true;
            binding.DetailRoute ??= "/space/viewer";
        }

        db.SaveChanges();
    }
}
