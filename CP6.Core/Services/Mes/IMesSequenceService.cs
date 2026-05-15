namespace CP6.Core.Services.Mes;

/// <summary>
/// MES 採番管理サービス
/// </summary>
/// <remarks>
/// 仕様書 §9 No.10 T_MesSequence
/// WO=指図 / PR=実績 / QC=検査 / DF=不良
/// </remarks>
public interface IMesSequenceService
{
    /// <summary>採番取得（プレフィックス + 日付 + 4桁連番）</summary>
    /// <param name="seqKey">WO / PR / QC / DF</param>
    /// <param name="date">採番日（NULL=今日）</param>
    Task<string> NextAsync(string seqKey, DateTime? date = null);
}
