using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>OEE 計算・分析サービス契約</summary>
public interface IOeeService
{
    /// <summary>OEE 日次データ検索</summary>
    Task<List<OeeDailyDto>> SearchAsync(OeeSearchQuery query);

    /// <summary>本日各設備のリアルタイム OEE（再計算 + 取得）</summary>
    Task<List<OeeDailyDto>> CalculateTodayAsync(string? machineCd = null);

    /// <summary>指定日 OEE 再計算（バッチ用途）</summary>
    Task<int> RecalculateAsync(OeeRecalcRequest req, string? userName);

    /// <summary>過去 N 日 OEE 推移（設備別折線）</summary>
    Task<Dictionary<string, List<OeeDailyDto>>> GetTrendAsync(int days, string? machineCd);
}
