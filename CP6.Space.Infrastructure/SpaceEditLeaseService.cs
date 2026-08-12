using System.Data;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceEditLeaseService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceDesignAccessEvaluator access) : ISpaceEditLeaseService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(90);

    public async Task<SpaceEditLeaseDto> GetAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default)
    {
        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: false,
            requireDraft: false,
            cancellationToken);
        var lease = await context.EditLeases
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId,
                cancellationToken);
        return ToDto(versionId, floorLogicalId, lease, RequireUtcNow());
    }

    public async Task<SpaceEditLeaseDto> AcquireAsync(
        Guid versionId,
        Guid floorLogicalId,
        AcquireSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ClientInstanceId == Guid.Empty)
            throw Invalid("clientInstanceId", "A non-empty identity is required.");

        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: true,
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var now = RequireUtcNow();
        var lease = await context.EditLeases.SingleOrDefaultAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.FloorLogicalId == floorLogicalId,
            cancellationToken);

        if (lease is null)
        {
            lease = SpaceEditLease.Create(
                execution.TenantId,
                versionId,
                floorLogicalId,
                execution.ActorId,
                request.ClientInstanceId,
                now,
                LeaseDuration);
            context.EditLeases.Add(lease);
        }
        else if (lease.IsExpired(now))
        {
            lease.Reassign(
                execution.ActorId,
                request.ClientInstanceId,
                now,
                LeaseDuration);
        }
        else if (lease.IsOwnedBy(execution.ActorId, request.ClientInstanceId))
        {
            lease.Renew(lease.LeaseId, execution.ActorId, now, LeaseDuration);
        }
        else
        {
            throw Held(lease);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new SpaceProblemException(
                SpaceErrorCodes.EditLeaseHeld,
                409,
                "This floor is being edited in another session.",
                recoveryAction: "refresh-lease-state",
                retryable: true);
        }
        return ToDto(versionId, floorLogicalId, lease, now);
    }

    public async Task<SpaceEditLeaseDto> RenewAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        RequireLeaseId(leaseId);
        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: true,
            cancellationToken);
        var now = RequireUtcNow();
        var lease = await context.EditLeases.SingleOrDefaultAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.FloorLogicalId == floorLogicalId,
            cancellationToken);
        if (lease is null ||
            lease.LeaseId != leaseId ||
            lease.OwnerUserId != execution.ActorId ||
            lease.IsExpired(now))
        {
            throw Lost();
        }

        lease.Renew(leaseId, execution.ActorId, now, LeaseDuration);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Lost();
        }
        return ToDto(versionId, floorLogicalId, lease, now);
    }

    public async Task<SpaceEditLeaseDto> ReleaseAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        RequireLeaseId(leaseId);
        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: false,
            cancellationToken);
        var now = RequireUtcNow();
        var lease = await context.EditLeases.SingleOrDefaultAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.FloorLogicalId == floorLogicalId,
            cancellationToken);
        if (lease is null ||
            lease.LeaseId != leaseId ||
            lease.OwnerUserId != execution.ActorId)
        {
            throw Lost();
        }

        lease.Release(leaseId, execution.ActorId, now);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Lost();
        }
        return ToDto(versionId, floorLogicalId, lease, now);
    }

    public async Task<SpaceEditLeaseDto> TakeoverAsync(
        Guid versionId,
        Guid floorLogicalId,
        TakeoverSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ClientInstanceId == Guid.Empty)
            throw Invalid("clientInstanceId", "A non-empty identity is required.");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw Invalid("reason", "A takeover reason of at most 500 characters is required.");

        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: true,
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var now = RequireUtcNow();
        var lease = await context.EditLeases.SingleOrDefaultAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.FloorLogicalId == floorLogicalId,
            cancellationToken);
        if (lease is null || lease.IsExpired(now) || lease.OwnerUserId == execution.ActorId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.EditLeaseTakeoverDenied,
                409,
                "There is no active lease owned by another editor to take over.",
                recoveryAction: "acquire-or-renew-lease");
        }

        var previousLeaseId = lease.LeaseId;
        var previousOwnerUserId = lease.OwnerUserId;
        lease.Reassign(
            execution.ActorId,
            request.ClientInstanceId,
            now,
            LeaseDuration);
        context.EditLeaseTakeoverAudits.Add(
            SpaceEditLeaseTakeoverAudit.Create(
                execution.TenantId,
                versionId,
                floorLogicalId,
                previousLeaseId,
                previousOwnerUserId,
                lease.LeaseId,
                execution.ActorId,
                request.ClientInstanceId,
                reason!,
                now));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new SpaceProblemException(
                SpaceErrorCodes.EditLeaseTakeoverDenied,
                409,
                "The lease changed while takeover was being applied.",
                recoveryAction: "refresh-lease-state",
                retryable: true);
        }
        return ToDto(versionId, floorLogicalId, lease, now);
    }

    private async Task EnsureScopeAsync(
        Guid versionId,
        Guid floorLogicalId,
        bool write,
        bool requireDraft,
        CancellationToken cancellationToken)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty || floorLogicalId == Guid.Empty)
            throw NotFound();

        var scope = await (
                from version in context.Versions.AsNoTracking()
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { Version = version, Model = model })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        if (scope.Model.Mode != SpaceModelMode.DesignV1 ||
            scope.Model.CutoverState != SpaceModelCutoverState.DesignV1)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DesignApiDisabled,
                404,
                "The Design API is not enabled for this Site.",
                recoveryAction: "use-legacy-api");
        }
        access.EnsureSiteAccess(scope.Model.SiteId, write);
        if (requireDraft && scope.Version.Status != SpaceVersionStatus.Draft)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "Only a Draft version can hold an edit lease.",
                recoveryAction: "open-or-create-draft");
        }
        var floorExists = await context.FloorRevisions.AsNoTracking().AnyAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.LogicalId == floorLogicalId,
            cancellationToken);
        if (!floorExists)
            throw NotFound();
    }

    private void EnsureExecutionContext()
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot access edit leases.",
                recoveryAction: "use-published-runtime");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private SpaceEditLeaseDto ToDto(
        Guid versionId,
        Guid floorLogicalId,
        SpaceEditLease? lease,
        DateTime now)
    {
        if (lease is null || lease.IsExpired(now))
        {
            return new SpaceEditLeaseDto(
                versionId,
                floorLogicalId,
                null,
                null,
                null,
                null,
                null,
                true,
                false,
                null);
        }
        return new SpaceEditLeaseDto(
            versionId,
            floorLogicalId,
            lease.LeaseId,
            lease.OwnerUserId,
            lease.ClientInstanceId,
            lease.ExpiresAtUtc,
            lease.LastRenewedAtUtc,
            false,
            lease.OwnerUserId == execution.ActorId,
            Convert.ToBase64String(lease.RowVersion));
    }

    private static SpaceProblemException Held(SpaceEditLease lease) => new(
        SpaceErrorCodes.EditLeaseHeld,
        409,
        "This floor is being edited in another session.",
        $"Lease holder {lease.OwnerUserId:D}; expires at {lease.ExpiresAtUtc:O}.",
        "wait-or-request-takeover",
        retryable: true);

    private static SpaceProblemException Lost() => new(
        SpaceErrorCodes.EditLeaseLost,
        409,
        "The edit lease is no longer valid.",
        recoveryAction: "export-recovery-draft-or-reacquire",
        retryable: true);

    private static SpaceProblemException Invalid(string field, string detail) => new(
        SpaceErrorCodes.RequestInvalid,
        422,
        "The edit lease request is invalid.",
        $"{field}: {detail}",
        "correct-request");

    private static SpaceProblemException NotFound() => new(
        SpaceErrorCodes.LogicalIdNotFound,
        404,
        "The Space version or floor was not found.",
        recoveryAction: "refresh-space-model");

    private static void RequireLeaseId(Guid leaseId)
    {
        if (leaseId == Guid.Empty)
            throw Invalid("leaseId", "A non-empty identity is required.");
    }
}
