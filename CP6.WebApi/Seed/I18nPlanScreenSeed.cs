using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// 计划中台（Plan）P1 MRP 看板 + 计划主数据 视图 + 菜单 nav.73x + 枚举标签 + E-PLAN-* 业务错误码 词条（五语）。
/// 中文＝key（自然语言 key）。经 Program.cs 幂等合并（已存在的 key 由 GroupBy 去重，先到者留）。
/// </summary>
public static class I18nPlanScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 菜单 ──
        new Sys_Lang { LangKey = "nav.730", ZhCN = "计划中台(Plan)", ZhTW = "計劃中台(Plan)", En = "Planning", Ja = "計画(Plan)", Ko = "계획(Plan)" },
        new Sys_Lang { LangKey = "nav.731", ZhCN = "MRP运算看板", ZhTW = "MRP運算看板", En = "MRP Board", Ja = "MRP計算ボード", Ko = "MRP 보드" },
        new Sys_Lang { LangKey = "nav.732", ZhCN = "计划主数据", ZhTW = "計劃主數據", En = "Planning Policy", Ja = "計画マスタ", Ko = "계획 마스터" },

        // ── 看板视图 ──
        new Sys_Lang { LangKey = "MRP运算看板", ZhCN = "MRP运算看板", ZhTW = "MRP運算看板", En = "MRP Board", Ja = "MRP計算ボード", Ko = "MRP 보드" },
        new Sys_Lang { LangKey = "按开口受注运算", ZhCN = "按开口受注运算", ZhTW = "按開口受注運算", En = "Run from Open Orders", Ja = "受注残でMRP実行", Ko = "미출하 수주로 실행" },
        new Sys_Lang { LangKey = "运算批次", ZhCN = "运算批次", ZhTW = "運算批次", En = "MRP Run", Ja = "計算バッチ", Ko = "실행 배치" },
        new Sys_Lang { LangKey = "计划订单", ZhCN = "计划订单", ZhTW = "計劃訂單", En = "Planned Orders", Ja = "計画オーダー", Ko = "계획 오더" },
        new Sys_Lang { LangKey = "净需求", ZhCN = "净需求", ZhTW = "淨需求", En = "Net Requirements", Ja = "正味所要量", Ko = "순소요량" },
        new Sys_Lang { LangKey = "无运算结果", ZhCN = "无运算结果", ZhTW = "無運算結果", En = "No run yet", Ja = "計算結果なし", Ko = "실행 결과 없음" },

        // ── 计划主数据视图 ──
        new Sys_Lang { LangKey = "计划主数据", ZhCN = "计划主数据", ZhTW = "計劃主數據", En = "Planning Policy", Ja = "計画マスタ", Ko = "계획 마스터" },
        new Sys_Lang { LangKey = "安全库存", ZhCN = "安全库存", ZhTW = "安全庫存", En = "Safety Stock", Ja = "安全在庫", Ko = "안전 재고" },
        new Sys_Lang { LangKey = "采购提前期", ZhCN = "采购提前期", ZhTW = "採購提前期", En = "Purchase Lead Days", Ja = "購買リードタイム", Ko = "구매 리드타임" },
        new Sys_Lang { LangKey = "批量规则", ZhCN = "批量规则", ZhTW = "批量規則", En = "Lot Rule", Ja = "ロット規則", Ko = "로트 규칙" },
        new Sys_Lang { LangKey = "最小起订量", ZhCN = "最小起订量", ZhTW = "最小起訂量", En = "MOQ", Ja = "最小発注量", Ko = "최소 발주량" },
        new Sys_Lang { LangKey = "订货倍数", ZhCN = "订货倍数", ZhTW = "訂貨倍數", En = "Order Multiple", Ja = "発注倍数", Ko = "발주 배수" },
        new Sys_Lang { LangKey = "自制采购", ZhCN = "自制采购", ZhTW = "自製採購", En = "Make/Buy", Ja = "内外製区分", Ko = "자제/구매" },

        // ── 枚举：计划订单类型 ──
        new Sys_Lang { LangKey = "生产", ZhCN = "生产", ZhTW = "生產", En = "Production", Ja = "製造", Ko = "생산" },
        new Sys_Lang { LangKey = "采购", ZhCN = "采购", ZhTW = "採購", En = "Purchase", Ja = "購買", Ko = "구매" },

        // ── 枚举：计划订单状态 ──
        new Sys_Lang { LangKey = "建议", ZhCN = "建议", ZhTW = "建議", En = "Suggested", Ja = "提案", Ko = "제안" },
        new Sys_Lang { LangKey = "已转单", ZhCN = "已转单", ZhTW = "已轉單", En = "Converted", Ja = "転送済み", Ko = "전환됨" },
        new Sys_Lang { LangKey = "已忽略", ZhCN = "已忽略", ZhTW = "已忽略", En = "Ignored", Ja = "無視", Ko = "무시됨" },

        // ── 枚举：批量规则 ──
        new Sys_Lang { LangKey = "按需", ZhCN = "按需", ZhTW = "按需", En = "Lot-for-Lot", Ja = "都度", Ko = "필요량" },
        new Sys_Lang { LangKey = "最小量", ZhCN = "最小量", ZhTW = "最小量", En = "MOQ", Ja = "最小量", Ko = "최소량" },
        new Sys_Lang { LangKey = "整卷取整", ZhCN = "整卷取整", ZhTW = "整捲取整", En = "Round Roll", Ja = "巻取り単位", Ko = "롤 단위" },

        // ── 枚举：自制/采购 ──
        new Sys_Lang { LangKey = "自制", ZhCN = "自制", ZhTW = "自製", En = "Make", Ja = "内製", Ko = "자제" },

        // ── 字段：计划订单/净需求 列 ──
        new Sys_Lang { LangKey = "需求日", ZhCN = "需求日", ZhTW = "需求日", En = "Required Date", Ja = "所要日", Ko = "소요일" },
        new Sys_Lang { LangKey = "下达日", ZhCN = "下达日", ZhTW = "下達日", En = "Release Date", Ja = "手配日", Ko = "착수일" },
        new Sys_Lang { LangKey = "毛需求", ZhCN = "毛需求", ZhTW = "毛需求", En = "Gross", Ja = "総所要量", Ko = "총소요량" },
        new Sys_Lang { LangKey = "库存", ZhCN = "库存", ZhTW = "庫存", En = "On Hand", Ja = "在庫", Ko = "재고" },
        new Sys_Lang { LangKey = "运行MRP", ZhCN = "运行MRP", ZhTW = "運行MRP", En = "Run MRP", Ja = "MRP実行", Ko = "MRP 실행" },
        new Sys_Lang { LangKey = "在途", ZhCN = "在途", ZhTW = "在途", En = "In Transit", Ja = "入荷予定", Ko = "입고 예정" },
        new Sys_Lang { LangKey = "在制", ZhCN = "在制", ZhTW = "在製", En = "In WIP", Ja = "仕掛", Ko = "재공" },
        new Sys_Lang { LangKey = "已确认供给", ZhCN = "已确认供给", ZhTW = "已確認供給", En = "Firm Planned", Ja = "確定計画", Ko = "확정 계획" },

        // ── 列/通用 ──
        new Sys_Lang { LangKey = "类型", ZhCN = "类型", ZhTW = "類型", En = "Type", Ja = "種別", Ko = "유형" },
        new Sys_Lang { LangKey = "状态", ZhCN = "状态", ZhTW = "狀態", En = "Status", Ja = "ステータス", Ko = "상태" },
        new Sys_Lang { LangKey = "下游单号", ZhCN = "下游单号", ZhTW = "下游單號", En = "Downstream Doc", Ja = "下流伝票番号", Ko = "후속 전표번호" },
        new Sys_Lang { LangKey = "新增", ZhCN = "新增", ZhTW = "新增", En = "Add", Ja = "新規", Ko = "추가" },
        new Sys_Lang { LangKey = "确认删除", ZhCN = "确认删除", ZhTW = "確認刪除", En = "Confirm delete?", Ja = "削除しますか？", Ko = "삭제하시겠습니까?" },

        // ── 按钮 ──
        new Sys_Lang { LangKey = "确认", ZhCN = "确认", ZhTW = "確認", En = "Confirm", Ja = "確認", Ko = "확인" },
        new Sys_Lang { LangKey = "转单", ZhCN = "转单", ZhTW = "轉單", En = "Convert", Ja = "転送", Ko = "전환" },
        new Sys_Lang { LangKey = "忽略", ZhCN = "忽略", ZhTW = "忽略", En = "Ignore", Ja = "無視", Ko = "무시" },

        // ── E-PLAN-* 业务错误码 ──
        new Sys_Lang { LangKey = "E-PLAN-无需求: 无开口需求可运算", ZhCN = "无开口需求可运算", ZhTW = "無開口需求可運算", En = "No open demand to plan", Ja = "計算対象の需要がありません", Ko = "계획할 미충족 수요 없음" },
        new Sys_Lang { LangKey = "E-PLAN-已转单: 计划订单已转单", ZhCN = "计划订单已转单", ZhTW = "計劃訂單已轉單", En = "Planned order already converted", Ja = "計画オーダーは転送済みです", Ko = "이미 전환된 계획 오더" },
        new Sys_Lang { LangKey = "E-PLAN-状态非法: 仅建议态计划订单可确认", ZhCN = "仅建议态计划订单可确认", ZhTW = "僅建議態計劃訂單可確認", En = "Only suggested planned orders can be confirmed", Ja = "提案状態のみ確認可能", Ko = "제안 상태만 확인 가능" },
    };
}
