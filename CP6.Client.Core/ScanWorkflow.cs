using CP6.Client.Api;

namespace CP6.Client.Core;

public interface IScanInput
{
    Task<string> ReadAsync(string promptKey, CancellationToken ct = default);
}

public sealed class ManualScanInput : IScanInput
{
    private readonly Func<string, CancellationToken, Task<string>> _reader;
    public ManualScanInput(Func<string, CancellationToken, Task<string>> reader) => _reader = reader;
    public Task<string> ReadAsync(string promptKey, CancellationToken ct = default) => _reader(promptKey, ct);
}

public enum MoveScanStep
{
    SourceLocation,
    Product,
    Lot,
    TargetLocation,
    Quantity,
    ReadyToComplete,
    Completed,
}

public sealed class MoveScanWorkflow
{
    private bool _requiresLot;

    public MoveScanStep Step { get; private set; } = MoveScanStep.SourceLocation;
    public decimal ConfirmedQuantity { get; private set; }

    public void Reset(bool requiresLot = false)
    {
        _requiresLot = requiresLot;
        Step = MoveScanStep.SourceLocation;
        ConfirmedQuantity = 0;
    }

    public void AcceptBarcode(MobileTask task, string barcode)
    {
        var value = barcode.Trim();
        var expected = Step switch
        {
            MoveScanStep.SourceLocation => task.FromLocationCd,
            MoveScanStep.Product => task.ProductCd,
            MoveScanStep.Lot => task.LotNo,
            MoveScanStep.TargetLocation => task.ToLocationCd,
            _ => throw new InvalidOperationException("WM-SCAN-SEQUENCE"),
        };
        if (!string.Equals(expected, value, StringComparison.OrdinalIgnoreCase))
            throw new ScanMismatchException(expected ?? string.Empty, value);
        Step = Step switch
        {
            MoveScanStep.SourceLocation => MoveScanStep.Product,
            MoveScanStep.Product when _requiresLot => MoveScanStep.Lot,
            MoveScanStep.Product => MoveScanStep.TargetLocation,
            MoveScanStep.Lot => MoveScanStep.TargetLocation,
            MoveScanStep.TargetLocation => MoveScanStep.Quantity,
            _ => throw new InvalidOperationException("WM-SCAN-SEQUENCE"),
        };
    }

    public void AcceptValidated(MoveScanStep step)
    {
        if (step != Step) throw new InvalidOperationException("WM-SCAN-SEQUENCE");
        Step = Step switch
        {
            MoveScanStep.SourceLocation => MoveScanStep.Product,
            MoveScanStep.Product when _requiresLot => MoveScanStep.Lot,
            MoveScanStep.Product => MoveScanStep.TargetLocation,
            MoveScanStep.Lot => MoveScanStep.TargetLocation,
            MoveScanStep.TargetLocation => MoveScanStep.Quantity,
            _ => throw new InvalidOperationException("WM-SCAN-SEQUENCE"),
        };
    }

    public void ConfirmQuantity(MobileTask task, decimal quantity)
    {
        if (Step != MoveScanStep.Quantity) throw new InvalidOperationException("WM-SCAN-SEQUENCE");
        if (quantity <= 0 || quantity > task.Qty)
            throw new ScanQuantityException(task.Qty, quantity);
        ConfirmedQuantity = quantity;
        Step = MoveScanStep.ReadyToComplete;
    }

    public void MarkCompleted()
    {
        if (Step != MoveScanStep.ReadyToComplete) throw new InvalidOperationException("WM-SCAN-SEQUENCE");
        Step = MoveScanStep.Completed;
    }
}

public sealed class ScanMismatchException : InvalidOperationException
{
    public ScanMismatchException(string expected, string actual) : base("WM-MSG-302")
    {
        Expected = expected;
        Actual = actual;
    }
    public string Expected { get; }
    public string Actual { get; }
}

public sealed class ScanQuantityException : InvalidOperationException
{
    public ScanQuantityException(decimal expected, decimal actual) : base("WM-SCAN-QTY-MISMATCH")
    {
        Expected = expected;
        Actual = actual;
    }
    public decimal Expected { get; }
    public decimal Actual { get; }
}
