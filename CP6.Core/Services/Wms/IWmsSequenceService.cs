namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS 業務NO 採番サービス。
/// 形式：{Prefix}{yyyyMMdd}-{NNNNN}（例：TXN20260522-00001）
/// </summary>
public interface IWmsSequenceService
{
    /// <summary>次番採番</summary>
    /// <param name="prefix">採番プレフィックス（IN/RC/OUT/SHIP/TXN 等）</param>
    /// <param name="date">日付（既定 = 今日）</param>
    Task<string> NextAsync(string prefix, DateTime? date = null);
}
