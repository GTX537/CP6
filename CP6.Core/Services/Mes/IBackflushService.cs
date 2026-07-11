namespace CP6.Core.Services.Mes;

/// <summary>
/// 完工点 原料反冲サービス（F1 財務油路 波C.1）。
///
/// 指図の全工程完了時、BOM（ProductMaterial）定额/尺寸算法で原料消費量を算出し、
/// 原料倉から OUT 在庫移動（RelatedType="ISSUE"）を発行する。同時に実績消費量
/// （WorkOrderMaterial.ActualQty）を回写し、成本归集（CostCollectService）の料成本を実消費に対齐する。
/// 拍板①：在庫不足でも負在庫記帳＋告警で通し、報工本体は止めない（账实差异は棚卸で吸収）。
/// </summary>
public interface IBackflushService
{
    /// <summary>
    /// 指図番号 <paramref name="workOrderNo"/> の完成品に対し、BOM 定额の原料を反冲する。
    /// 冪等：同一指図に既存の ISSUE 反冲移動があれば何もしない。
    /// </summary>
    Task BackflushAsync(string workOrderNo, string? userName);
}
