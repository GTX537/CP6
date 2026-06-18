using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// A2 工艺路线完善 主数据画面（工作中心 + 工序费率）视图 + 菜单 nav.31x + 字段标签 + W-A2-*/E-A2-* 业务错误码 词条（五语）。
/// 中文＝key（自然语言 key）。经 Program.cs 幂等合并（已存在的 key 由 GroupBy 去重，先到者留）。
/// </summary>
public static class I18nA2ScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 菜单（nav.31x，对应 MES 组 300 子项 314/315） ──
        new Sys_Lang { LangKey = "nav.314", ZhCN = "工作中心", ZhTW = "工作中心", En = "Work Center", Ja = "ワークセンター", Ko = "워크센터" },
        new Sys_Lang { LangKey = "nav.315", ZhCN = "工序费率", ZhTW = "工序費率", En = "Process Cost Rate", Ja = "工程レート", Ko = "공정 비율" },

        // ── 视图标题 / 字段标签 ──
        new Sys_Lang { LangKey = "工作中心", ZhCN = "工作中心", ZhTW = "工作中心", En = "Work Center", Ja = "ワークセンター", Ko = "워크센터" },
        new Sys_Lang { LangKey = "工序费率", ZhCN = "工序费率", ZhTW = "工序費率", En = "Process Cost Rate", Ja = "工程レート", Ko = "공정 비율" },
        new Sys_Lang { LangKey = "日可用产能", ZhCN = "日可用产能", ZhTW = "日可用產能", En = "Daily Capacity (h)", Ja = "日次能力(h)", Ko = "일 가용 능력(h)" },
        new Sys_Lang { LangKey = "产能", ZhCN = "产能", ZhTW = "產能", En = "Capacity", Ja = "能力", Ko = "능력" },
        new Sys_Lang { LangKey = "人工费率", ZhCN = "人工费率", ZhTW = "人工費率", En = "Labor Rate", Ja = "人件費レート", Ko = "인건비 비율" },
        new Sys_Lang { LangKey = "制造费率", ZhCN = "制造费率", ZhTW = "製造費率", En = "Overhead Rate", Ja = "製造間接費レート", Ko = "제조 간접비 비율" },

        // ── 标准工时 / 实绩工时 字段（spec §3，预留供路线·实绩画面共用） ──
        new Sys_Lang { LangKey = "段取工时", ZhCN = "段取工时", ZhTW = "段取工時", En = "Setup Hours", Ja = "段取時間", Ko = "준비 시간" },
        new Sys_Lang { LangKey = "单件工时", ZhCN = "单件工时", ZhTW = "單件工時", En = "Per-Unit Hours", Ja = "単品工数", Ko = "단위 공수" },
        new Sys_Lang { LangKey = "标准人数", ZhCN = "标准人数", ZhTW = "標準人數", En = "Std Headcount", Ja = "標準人数", Ko = "표준 인원" },
        new Sys_Lang { LangKey = "标准机时", ZhCN = "标准机时", ZhTW = "標準機時", En = "Std Machine Hours", Ja = "標準機械時間", Ko = "표준 기계 시간" },
        new Sys_Lang { LangKey = "标准人工工时", ZhCN = "标准人工工时", ZhTW = "標準人工工時", En = "Std Labor Hours", Ja = "標準人工時間", Ko = "표준 인공 시간" },
        new Sys_Lang { LangKey = "实际机时", ZhCN = "实际机时", ZhTW = "實際機時", En = "Actual Machine Hours", Ja = "実績機械時間", Ko = "실제 기계 시간" },
        new Sys_Lang { LangKey = "实际人工工时", ZhCN = "实际人工工时", ZhTW = "實際人工工時", En = "Actual Labor Hours", Ja = "実績人工時間", Ko = "실제 인공 시간" },

        // ── E-2 成本单展示：工/费 实际/标准列头 + 明细行追溯列头（料工费成本做真画面）──
        new Sys_Lang { LangKey = "实际人工", ZhCN = "实际人工", ZhTW = "實際人工", En = "Actual Labor", Ja = "実績人件費", Ko = "실제 인건비" },
        new Sys_Lang { LangKey = "实际制造费用", ZhCN = "实际制造费用", ZhTW = "實際製造費用", En = "Actual Overhead", Ja = "実績製造間接費", Ko = "실제 제조 간접비" },
        new Sys_Lang { LangKey = "成本明细行", ZhCN = "成本明细行", ZhTW = "成本明細行", En = "Cost Detail Lines", Ja = "原価明細行", Ko = "원가 명세 행" },
        new Sys_Lang { LangKey = "要素", ZhCN = "要素", ZhTW = "要素", En = "Element", Ja = "要素", Ko = "요소" },
        new Sys_Lang { LangKey = "工序", ZhCN = "工序", ZhTW = "工序", En = "Process", Ja = "工程", Ko = "공정" },
        new Sys_Lang { LangKey = "直接材料", ZhCN = "直接材料", ZhTW = "直接材料", En = "Direct Material", Ja = "直接材料", Ko = "직접 재료" },
        new Sys_Lang { LangKey = "工时", ZhCN = "工时", ZhTW = "工時", En = "Hours", Ja = "工数", Ko = "공수" },
        new Sys_Lang { LangKey = "标准工时", ZhCN = "标准工时", ZhTW = "標準工時", En = "Std Hours", Ja = "標準工数", Ko = "표준 공수" },
        new Sys_Lang { LangKey = "实际金额", ZhCN = "实际金额", ZhTW = "實際金額", En = "Actual Amount", Ja = "実績金額", Ko = "실제 금액" },
        new Sys_Lang { LangKey = "标准金额", ZhCN = "标准金额", ZhTW = "標準金額", En = "Std Amount", Ja = "標準金額", Ko = "표준 금액" },
        new Sys_Lang { LangKey = "工时来源", ZhCN = "工时来源", ZhTW = "工時來源", En = "Hour Source", Ja = "工数ソース", Ko = "공수 출처" },
        new Sys_Lang { LangKey = "警告码", ZhCN = "警告码", ZhTW = "警告碼", En = "Warning Code", Ja = "警告コード", Ko = "경고 코드" },

        // ── W-A2-* 迁移/校验告警 + E-A2-* 业务错误码（与 Service 抛出消息整串对齐） ──
        new Sys_Lang { LangKey = "E-A2-WC-001: 工作中心CD必填", ZhCN = "工作中心CD必填", ZhTW = "工作中心CD必填", En = "Work center code required", Ja = "ワークセンターCDは必須です", Ko = "워크센터 코드 필수" },
        new Sys_Lang { LangKey = "E-A2-WC-001: 工作中心不存在", ZhCN = "工作中心不存在", ZhTW = "工作中心不存在", En = "Work center not found", Ja = "ワークセンターが存在しません", Ko = "워크센터 없음" },
        new Sys_Lang { LangKey = "E-A2-WC-003: 日可用产能不可为负", ZhCN = "日可用产能不可为负", ZhTW = "日可用產能不可為負", En = "Daily capacity cannot be negative", Ja = "日次能力は負数不可", Ko = "일 가용 능력은 음수 불가" },
        new Sys_Lang { LangKey = "E-A2-RATE-001: 费率生效区间重叠", ZhCN = "费率生效区间重叠", ZhTW = "費率生效區間重疊", En = "Rate effective period overlaps", Ja = "レートの有効期間が重複しています", Ko = "비율 유효 기간 중복" },
        new Sys_Lang { LangKey = "E-A2-RATE-001: 费率不存在", ZhCN = "费率不存在", ZhTW = "費率不存在", En = "Rate not found", Ja = "レートが存在しません", Ko = "비율 없음" },
        new Sys_Lang { LangKey = "E-A2-RATE-003: 费率不可为负", ZhCN = "费率不可为负", ZhTW = "費率不可為負", En = "Rate cannot be negative", Ja = "レートは負数不可", Ko = "비율은 음수 불가" },
        new Sys_Lang { LangKey = "E-A2-RATE-004: 失效日早于生效日", ZhCN = "失效日早于生效日", ZhTW = "失效日早於生效日", En = "Valid-to is earlier than valid-from", Ja = "失効日が有効日より前です", Ko = "실효일이 발효일보다 빠름" },
    };
}
