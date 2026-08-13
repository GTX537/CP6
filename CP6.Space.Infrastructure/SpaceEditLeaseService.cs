using System.Data;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceEditLeaseService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceDesignAccessEvaluator access,
    ISpaceCorrelationContext? correlation = null) : ISpaceEditLeaseService
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
        return ToDto(
            versionId,
            floorLogicalId,
            lease,
            await ReadAuthoritativeUtcNowAsync(cancellationToken));
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

        EnsureExecutionContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireFloorEditLockAsync(
            versionId,
            floorLogicalId,
            cancellationToken);
        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: true,
            cancellationToken);
        var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
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
                HolderDisplayName(),
                request.ClientInstanceId,
                now,
                LeaseDuration);
            context.EditLeases.Add(lease);
        }
        else if (lease.IsExpired(now))
        {
            lease.Reassign(
                execution.ActorId,
                HolderDisplayName(),
                request.ClientInstanceId,
                now,
                LeaseDuration);
        }
        else if (lease.IsOwnedBy(execution.ActorId, request.ClientInstanceId))
        {
            lease.Renew(
                lease.LeaseId,
                execution.ActorId,
                request.ClientInstanceId,
                now,
                LeaseDuration);
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
        return ToDto(versionId, floorLogicalId, lease, now, exposeCredential: true);
    }

    public async Task<SpaceEditLeaseDto> RenewAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        ContinueSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireLeaseId(leaseId);
        RequireClientInstanceId(request.ClientInstanceId);
        EnsureExecutionContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireFloorEditLockAsync(
            versionId,
            floorLogicalId,
            cancellationToken);
        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: true,
            cancellationToken);
        var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
        var lease = await context.EditLeases.SingleOrDefaultAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.FloorLogicalId == floorLogicalId,
            cancellationToken);
        if (lease is null ||
            lease.LeaseId != leaseId ||
            lease.OwnerUserId != execution.ActorId ||
            lease.ClientInstanceId != request.ClientInstanceId ||
            lease.IsExpired(now))
        {
            throw Lost();
        }

        lease.Renew(
            leaseId,
            execution.ActorId,
            request.ClientInstanceId,
            now,
            LeaseDuration);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Lost();
        }
        return ToDto(versionId, floorLogicalId, lease, now, exposeCredential: true);
    }

    public async Task<SpaceEditLeaseDto> ReleaseAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        ContinueSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireLeaseId(leaseId);
        RequireClientInstanceId(request.ClientInstanceId);
        EnsureExecutionContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireFloorEditLockAsync(
            versionId,
            floorLogicalId,
            cancellationToken);
        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: false,
            cancellationToken);
        var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
        var lease = await context.EditLeases.SingleOrDefaultAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.FloorLogicalId == floorLogicalId,
            cancellationToken);
        if (lease is null ||
            lease.LeaseId != leaseId ||
            lease.OwnerUserId != execution.ActorId ||
            lease.ClientInstanceId != request.ClientInstanceId)
        {
            throw Lost();
        }

        lease.Release(
            leaseId,
            execution.ActorId,
            request.ClientInstanceId,
            now);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Lost();
        }
        return ToDto(versionId, floorLogicalId, lease, now, exposeCredential: true);
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

        EnsureExecutionContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireFloorEditLockAsync(
            versionId,
            floorLogicalId,
            cancellationToken);
        await EnsureScopeAsync(
            versionId,
            floorLogicalId,
            write: true,
            requireDraft: true,
            cancellationToken);
        var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
        var lease = await context.EditLeases.SingleOrDefaultAsync(candidate =>
            candidate.ModelVersionId == versionId &&
            candidate.FloorLogicalId == floorLogicalId,
            cancellationToken);
        if (lease is null ||
            lease.IsExpired(now) ||
            lease.IsOwnedBy(execution.ActorId, request.ClientInstanceId))
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
            HolderDisplayName(),
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
                correlation?.CorrelationId is { } correlationId &&
                correlationId != Guid.Empty
                    ? correlationId
                    : Guid.NewGuid(),
                execution.RequestSource,
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
        return ToDto(versionId, floorLogicalId, lease, now, exposeCredential: true);
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

    private async Task<DateTime> ReadAuthoritativeUtcNowAsync(
        CancellationToken cancellationToken)
    {
        var now = context.Database.IsSqlServer()
            ? await context.Database
                .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
                .SingleAsync(cancellationToken)
            : clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        return now;
    }

    private async Task AcquireFloorEditLockAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer())
            return;

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"cp6:space:floor-edit:{execution.TenantId:N}:" +
                    $"{versionId:N}:{floorLogicalId:N}",
        };
        await context.Database.ExecuteSqlRawAsync(
            """
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            """,
            [result, resource],
            cancellationToken);
        if (Convert.ToInt32(result.Value) < 0)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.EditLeaseHeld,
                409,
                "The floor edit session is busy.",
                recoveryAction: "retry-lease-operation",
                retryable: true);
        }
    }

    private string HolderDisplayName() =>
        string.IsNullOrWhiteSpace(execution.ActorDisplayName)
            ? execution.ActorId.ToString("D")
            : execution.ActorDisplayName!.Trim();

    private SpaceEditLeaseDto ToDto(
        Guid versionId,
        Guid floorLogicalId,
        SpaceEditLease? lease,
        DateTime now,
        bool exposeCredential = false)
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
                null,
                null,
                true,
                false,
                null);
        }
        return new SpaceEditLeaseDto(
            versionId,
            floorLogicalId,
            exposeCredential ? lease.LeaseId : null,
            lease.OwnerUserId,
            lease.HolderDisplayName,
            exposeCredential ? lease.ClientInstanceId : null,
            lease.AcquiredAtUtc,
            lease.ExpiresAtUtc,
            lease.LastRenewedAtUtc,
            false,
            lease.OwnerUserId == execution.ActorId,
            exposeCredential
                ? Convert.ToBase64String(lease.RowVersion)
                : null);
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


    private static void RequireClientInstanceId(Guid clientInstanceId)
    {
        if (clientInstanceId == Guid.Empty)
            throw Invalid("clientInstanceId", "A non-empty identity is required.");
    }
}
