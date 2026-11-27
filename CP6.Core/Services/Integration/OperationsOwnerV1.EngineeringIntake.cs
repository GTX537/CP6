using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

public sealed partial class OperationsOwnerV1
{
    // 主体来自服务登记和当前凭据门。仅用于工程受理的同库调用。
    internal async Task<OperationsAccessV1> EngineeringIntakeAccessAsync(Guid subject, EngineeringIntakeCallerV1 caller, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null || subject == Guid.Empty || caller.TenantId != db.CurrentTenantId)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        var access = new OperationsAccessV1(db, this, await Read(EngineeringRequirement, "query", ct), storedSubjectSource: Read)
        {
            IntakeCaller = caller
        };
        return access;

        Task<Authority> Read(string resource, string action, CancellationToken token) =>
            action == "query" && resource is EngineeringRequirement or UomProfile or ControlledDocument or FileContent
            || resource == EngineeringRequirement && action is "add" or "edit"
                ? AuthorityCoreAsync(resource, action, subject, true, token)
                : throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
    }

    internal async Task ValidateEngineeringIntakeAccessAsync(OperationsAccessV1 access, CancellationToken ct)
    {
        await access.ValidateFinalAsync(ct);
        foreach (var original in access.Authorities)
        {
            var current = await AuthorityCoreAsync(original.Resource, original.Action, access.ActorId, true, ct);
            RequireUnchangedAuthority(original, current);
        }
    }
}

internal sealed partial class OperationsAccessV1
{
    internal EngineeringIntakeCallerV1? IntakeCaller { get; init; }
}
