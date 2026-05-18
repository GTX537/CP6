namespace CP6.Core.Services.Mes;

/// <summary>
/// MES 実時間通知サービス契約
/// </summary>
/// <remarks>
/// CP6.WebApi 層で SignalR ベースの実装に差し替え。
/// Service 層に依存逆転インターフェイスを置くことで、
/// CP6.Core が SignalR に依存しない設計。
/// </remarks>
public interface IMesNotifier
{
    /// <summary>実績報告 → 大屏 / ME040 リアルタイム更新</summary>
    Task NotifyProductionReportedAsync(string workOrderNo, string processCd, decimal goodQty, decimal defectQty);

    /// <summary>不良起票 → アラート</summary>
    Task NotifyDefectIssuedAsync(string defectNo, string workOrderNo, string categoryCd);

    /// <summary>設備状態変更</summary>
    Task NotifyMachineStatusChangedAsync(string machineCd, int newStatus);

    /// <summary>指図ステータス遷移</summary>
    Task NotifyWorkOrderStatusChangedAsync(string workOrderNo, int newStatus);

    /// <summary>設備停止登録</summary>
    Task NotifyDowntimeRegisteredAsync(string downtimeNo, string machineCd, int downtimeType);
}

/// <summary>NoOp 実装（テスト・SignalR 未配線時のフォールバック）</summary>
public class NoOpMesNotifier : IMesNotifier
{
    public Task NotifyProductionReportedAsync(string workOrderNo, string processCd, decimal goodQty, decimal defectQty) => Task.CompletedTask;
    public Task NotifyDefectIssuedAsync(string defectNo, string workOrderNo, string categoryCd) => Task.CompletedTask;
    public Task NotifyMachineStatusChangedAsync(string machineCd, int newStatus) => Task.CompletedTask;
    public Task NotifyWorkOrderStatusChangedAsync(string workOrderNo, int newStatus) => Task.CompletedTask;
    public Task NotifyDowntimeRegisteredAsync(string downtimeNo, string machineCd, int downtimeType) => Task.CompletedTask;
}
