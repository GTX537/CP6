namespace CP6.Core.Services.Erp;

/// <summary>Safe Owner result. Unknown commit outcomes must not be automatically replayed.</summary>
public sealed class OrderPriceCorrectionException(string code, int status, Exception? inner = null)
    : InvalidOperationException(code, inner)
{
    public int Status { get; } = status;
}
