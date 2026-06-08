namespace CP6.Core.Services.Wms;

public interface IMaterialShortageNotifier
{
    Task NotifyAsync(string workOrderNo, string productCd, decimal requiredQty);
}
