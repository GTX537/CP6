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

    private async Task<OrderReadOwnerV1.Access> RequireCapability(string action, CancellationToken ct) =>
        await FindCapability(action, ct) ?? throw AccessDenied();

    private async Task<OrderReadOwnerV1.Access?> FindCapability(string action, CancellationToken ct)
    {
        try
        {
            return await orderRead.ParticipateDisclosureAsync(CapabilityMenu, action, ct);
        }
        catch (OrderPriceCorrectionException error) when (error.Status == 403 && error.Message == "ACTION_FORBIDDEN")
        {
            return null;
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

    internal sealed record ReceiveSlot(string Action, OrderReadOwnerV1.Access Access, SalesSource Origin,
        Inbox? Prior, bool CanReadResult);

    // 同一事务先核当前能力和可见的原来源，再查看 Planning 原操作槽。
    internal static async Task<ReceiveSlot> InspectAuthorizedReceive(
        SourceResultQuery query, long version,
        Func<string, CancellationToken, Task<OrderReadOwnerV1.Access?>> findCapability,
        Func<OrderReadOwnerV1.Access, CancellationToken, Task<SalesSource?>> findVisibleOrigin,
        Func<CancellationToken, Task<Inbox?>> observePlanningHistory,
        CancellationToken ct)
    {
        var receive = await findCapability(ReceiveCapability, ct);
        var resultRead = await findCapability(ResultReadCapability, ct);
        var disclosure = resultRead ?? receive ?? throw AccessDenied();
        if (receive is not null && resultRead is not null &&
            (!SameScope(receive, resultRead) || receive.Price != resultRead.Price || receive.Amount != resultRead.Amount))
            throw AccessDenied();

        var origin = await findVisibleOrigin(disclosure, ct) ?? throw AccessDenied();
        if (!origin.Matches(query, version)) throw Conflict();

        var prior = resultRead is null ? null : await observePlanningHistory(ct);
        var applicable = prior is null ? receive : resultRead;
        if (applicable is null || prior is not null && prior.ActorId != applicable.SubjectId)
            throw AccessDenied();
        return new(prior is null ? ReceiveCapability : ResultReadCapability, applicable, origin, prior, resultRead is not null);
    }

    private async Task<SalesSource?> FindVisibleOrigin(string operation, OrderReadOwnerV1.Access access, CancellationToken ct)
    {
        var origin = await FindSalesOrigin(operation, ct);
        if (origin is null || !AllowsOriginalDisclosure(access, origin.ActorId)) return null;

        await OrderSourceFacts.LockNative(db, origin.OrderId, ct);
        return await CanDisclose(access, origin, ct) ? origin : null;
    }

    private static bool SameScope(OrderReadOwnerV1.Access left, OrderReadOwnerV1.Access right) =>
        left.SubjectId == right.SubjectId && left.ReadScopeVersion == right.ReadScopeVersion;

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
        if (!SameScope(current, initial) ||
            visible is not null && !await CanDisclose(current, visible, ct))
            throw AccessDenied();
        await RequireActiveTenant(ct);
    }
}
