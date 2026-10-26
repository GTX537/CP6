using CP6.Core.Services.Integration;
using CP6.Entity.DTOs;
namespace CP6.Core.Services.Erp;

public partial class OrderService
{
    internal Task<string> CreateToolLinkedAsync(OperationsAccessV1 root, OrderDto draft, CancellationToken ct)
        => WriteOwner.EnlistToolCreate(root, draft, async (frozen, actor) =>
        {
            frozen.Status = 0; frozen.McTransferFlg = false; frozen.McOrderNo = null;
            foreach (var d in frozen.Details)
            { d.Status = 0; d.ApprovalStatus = 0; d.WfApprovalFlg = false; d.McTransferFlg = false; d.McOrderNo = null; d.McOrderDetailNo = null; }
            return await CreateCoreAsync(frozen, actor, null, deferBridges: true);
        }, ct);
}
