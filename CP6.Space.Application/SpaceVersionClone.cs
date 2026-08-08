using CP6.Space.Domain;

namespace CP6.Space.Application;

public static class SpaceVersionCloneContract
{
    public const string ProcessorVersion = "space-clone-v2";
}

public sealed record SpaceVersionCloneRequest(
    Guid ModelId,
    string Name,
    Guid OperationId);

public sealed record SpaceVersionCloneStartResult(
    Guid ModelVersionId,
    long VersionNo,
    SpaceVersionStatus VersionStatus,
    Guid JobId,
    SpaceJobStatus JobStatus,
    bool Reused);

public sealed record SpaceVersionClonePayload(
    Guid ModelId,
    Guid SourceVersionId,
    Guid TargetVersionId,
    Guid OperationId,
    Guid? PlanningScenarioBranchId = null);

public sealed record SpaceVersionCloneCounts(
    int Sources,
    int Floors,
    int Zones,
    int Aisles,
    int Racks,
    int RackLevels,
    int Locations,
    int Elements,
    int ElementAttributes,
    int LocationExternalBindings = 0,
    int DesignAttributes = 0)
{
    public long Total =>
        (long)Sources +
        Floors +
        Zones +
        Aisles +
        Racks +
        RackLevels +
        Locations +
        Elements +
        ElementAttributes +
        LocationExternalBindings +
        DesignAttributes;
}

public interface ISpaceVersionCloneStore
{
    Task<SpaceVersionCloneStartResult> StartAsync(
        SpaceVersionCloneRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISpaceVersionCloneProcessor
{
    Task<SpaceVersionCloneCounts> ProcessAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceVersionCloneCoordinator
{
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceVersionCloneStore _store;

    public SpaceVersionCloneCoordinator(
        ISpaceExecutionContext execution,
        ISpaceVersionCloneStore store)
    {
        _execution = execution;
        _store = store;
    }

    public Task<SpaceVersionCloneStartResult> StartAsync(
        SpaceVersionCloneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_execution.TenantId == Guid.Empty || _execution.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
        }
        if (request.ModelId == Guid.Empty)
            throw new ArgumentException("Model is required.", nameof(request));
        if (request.OperationId == Guid.Empty)
            throw new ArgumentException("Clone operation is required.", nameof(request));

        return _store.StartAsync(request, cancellationToken);
    }
}
