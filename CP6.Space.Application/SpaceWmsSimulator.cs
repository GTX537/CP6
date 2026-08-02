namespace CP6.Space.Application;

public enum SpaceWmsSimulatorFaultMode
{
    None = 0,
    Unavailable = 1,
    Timeout = 2,
    RejectAll = 3,
    Partial = 4,
    UnknownAfterApply = 5,
}

public sealed record SpaceWmsSimulatorFaultProfile(
    SpaceWmsSimulatorFaultMode Mode,
    int ApplyCount = 0,
    TimeSpan Delay = default,
    string? ErrorCode = null)
{
    public static SpaceWmsSimulatorFaultProfile None { get; } =
        new(SpaceWmsSimulatorFaultMode.None);
}

public sealed record SpaceWmsOutboundMovement(
    string MovementId,
    string MaterialNumber,
    DateOnly OccurredOn,
    decimal Quantity);

/// <summary>
/// Test/demo control plane for the standard in-memory WMS simulator.
/// It is intentionally separate from ISpaceWmsAdapter so production
/// publishing code cannot inject faults through the adapter contract.
/// </summary>
public interface ISpaceWmsSimulatorControl
{
    void ConfigureFault(
        SpaceWmsContext context,
        SpaceWmsSimulatorFaultProfile profile);

    void SeedInventory(
        SpaceWmsContext context,
        IReadOnlyCollection<SpaceWmsInventoryItem> items);

    void SeedTasks(
        SpaceWmsContext context,
        IReadOnlyCollection<SpaceWmsTaskItem> items);

    void SeedOutboundMovements(
        SpaceWmsContext context,
        IReadOnlyCollection<SpaceWmsOutboundMovement> items);

    void Reset(SpaceWmsContext context);
}
