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
    /// 冪等は<b>材料行単位</b>：本工单に既存の ISSUE 反冲移動がある材料のみスキップし、
    /// 未反冲の材料は重放で続伝する（一部の料が失敗＝棚卸凍結等でも残料が永久未反冲にならない）。
    /// 行失敗しても<b>途中で断链せず</b>全料を試行し、成功料は各自即時落库する。
    /// 但し失敗料が残れば末尾で例外を投げる（呼出側 C.2 闸で本轮成本归集をスキップさせるため）。
    /// </summary>
    Task BackflushAsync(string workOrderNo, string? userName);
}
