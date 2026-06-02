namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS 業務NO 採番サービス。
/// 形式（全社統一）：{Prefix}{yyyyMM}{NNNN}（永不重置／例：TXN2026050001）
/// </summary>
public interface IWmsSequenceService
{
    /// <summary>次番採番</summary>
    /// <param name="prefix">採番プレフィックス（IN/RC/OUT/SHIP/TXN 等）</param>
    /// <param name="date">日付（既定 = 今日）</param>
    Task<string> NextAsync(string prefix, DateTime? date = null);
}
