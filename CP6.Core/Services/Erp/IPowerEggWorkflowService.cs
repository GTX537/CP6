using CP6.Entity.DomainModels;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Erp;

/// <summary>
/// POWER EGG WF 起票サービス（PA090 単価訂正 §第三部分 3 — 46 項目送信）
/// </summary>
/// <remarks>
/// 仕様書 §PE更新入出力仕様 NO.1：単価変更が発生した受注明細について
/// POWER EGG にワークフロー申請を起票し、承認後に MSLAB0020 バッチが
/// mcframe7 へ反映する。本環境では HTTP POST 用のインターフェースのみ提供。
/// </remarks>
public interface IPowerEggWorkflowService
{
    /// <summary>単価訂正 WF 起票（46項目）</summary>
    /// <param name="entity">単価変更のあった受注明細（変更後値で渡す）</param>
    /// <param name="applicantStaffCd">申請者（売上担当者）</param>
    /// <returns>true=起票成功 / false=起票失敗（環境無し時もここに含む）</returns>
    Task<bool> RequestPriceCorrectionAsync(OrderDetail entity, string? applicantStaffCd, CancellationToken ct = default);
}

/// <summary>
/// POWER EGG WF 起票 NoOp 実装（連携環境無し時）
/// </summary>
/// <remarks>
/// 本クラスは「起票したことにする」スタブで、操作ログのみ書込み。
/// 本番環境では Configuration で HTTP エンドポイントを設定し、実装を差し替える。
/// </remarks>
public class NoOpPowerEggWorkflowService : IPowerEggWorkflowService
{
    private readonly Microsoft.Extensions.Logging.ILogger<NoOpPowerEggWorkflowService> _logger;

    public NoOpPowerEggWorkflowService(Microsoft.Extensions.Logging.ILogger<NoOpPowerEggWorkflowService> logger)
    {
        _logger = logger;
    }

    public Task<bool> RequestPriceCorrectionAsync(OrderDetail entity, string? applicantStaffCd, CancellationToken ct = default)
    {
        // 仕様書 §PE 1〜46 項目を組み立て（実環境では HTTP POST）
        var subject = $"単価訂正申請 {entity.HaibaiNo1}/{entity.HaibaiNo2}/{entity.HaibaiNo3}";
        _logger.LogInformation(
            "[PE-WF stub] 件名={Subject} 申請者={Applicant} WebOrderNo={WebOrderNo}-{DetailNo} " +
            "個別単価={IndPrice} セット単価={SetPrice} 特値={Special} 仮単価={Provisional} 理由={Reason}",
            subject, applicantStaffCd ?? "(未設定)",
            entity.WebOrderNo, entity.WebOrderDetailNo,
            entity.IndividualUnitPrice, entity.SetUnitPrice,
            entity.SpecialPriceFlg, entity.ProvisionalPriceFlg, entity.PriceChangeReason);
        return Task.FromResult(true);
    }
}
