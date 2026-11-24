using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plan;

internal sealed partial class DemandSourceIntake
{
    private const string CapabilityMenu = "service";
    internal const string ReceiveCapability = "planning.source.receive";
    internal const string ResultReadCapability = "planning.source.result.read";

    private static DemandSourceIntakeException AccessDenied() => new("ACCESS_DENIED", 403);

    private async Task<OrderReadOwnerV1.Access> RequireCapability(string action, CancellationToken ct)
    {
        try
        {
            return await orderRead.ParticipateDisclosureAsync(CapabilityMenu, action, ct);
        }
        catch (CurrentPermissionUnavailableException)
        {
            throw AccessDenied();
        }
        catch (OrderPriceCorrectionException error) when (error.Status is 401 or 403)
        {
            throw AccessDenied();
        }
    }

    internal static bool AllowsOriginalDisclosure(OrderReadOwnerV1.Access access, Guid sourceActor) =>
        sourceActor != Guid.Empty && access.SubjectId == sourceActor && access.Price && access.Amount;

    private static async Task<bool> CanDisclose(OrderReadOwnerV1.Access access, SalesSource origin, CancellationToken ct)
    {
        if (!AllowsOriginalDisclosure(access, origin.ActorId)) return false;
        var number = await access.Headers.Where(order => order.Id == origin.OrderId)
            .Select(order => order.WebOrderNo).SingleOrDefaultAsync(ct);
        return number is not null && await access.Rows.AnyAsync(
            line => line.Id == origin.LineId && line.WebOrderNo == number, ct);
    }

    private async Task RequireFinalAccess(string action, OrderReadOwnerV1.Access initial, SalesSource? visible,
        CancellationToken ct)
    {
        var current = await RequireCapability(action, ct);
        if (current.SubjectId != initial.SubjectId || current.ReadScopeVersion != initial.ReadScopeVersion ||
            visible is not null && !await CanDisclose(current, visible, ct))
            throw AccessDenied();
        await RequireActiveTenant(ct);
    }
}
