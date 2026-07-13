using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>内核 hardening 画面词条：inclusive 网关（palette/节点/面板）+ 分支驳回策略 + 前端校验镜像
/// + 后端错误码 E-WF-019/020/021。键面以 cp6.web/src/views/oa/designer 实际引用为权威
/// （InclusiveGatewayNode.vue / NodePropertyPanel.vue / designerModel.ts validateClient）。
/// 去重：12 键在既有 I18nOa* seed 中均无重复（落地前 grep 复核）。</summary>
public static class I18nOaKernelHardeningScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 节点名（InclusiveGatewayNode.vue）──
        new() { LangKey = "oa.designer.gw.inclusiveSplit",          ZhCN = "包容分叉",         ZhTW = "包容分叉",         En = "Inclusive Split",                    Ja = "包含分岐",                     Ko = "포괄 분기" },
        new() { LangKey = "oa.designer.gw.inclusiveJoin",           ZhCN = "包容汇聚",         ZhTW = "包容匯聚",         En = "Inclusive Join",                     Ja = "包含合流",                     Ko = "포괄 합류" },

        // ── 分支驳回策略（NodePropertyPanel.vue）──
        new() { LangKey = "oa.designer.gw.branchReject",            ZhCN = "分支驳回策略",     ZhTW = "分支駁回策略",     En = "Branch Reject Policy",               Ja = "分岐却下ポリシー",             Ko = "분기 반려 정책" },
        new() { LangKey = "oa.designer.gw.branchReject.cascade",    ZhCN = "整单驳回（默认）", ZhTW = "整單駁回（預設）", En = "Reject whole instance (default)",    Ja = "全体却下（既定）",             Ko = "전체 반려(기본)" },
        new() { LangKey = "oa.designer.gw.branchReject.prune",      ZhCN = "仅剪除本分支",     ZhTW = "僅剪除本分支",     En = "Prune this branch only",             Ja = "この分岐のみ剪定",             Ko = "해당 분기만 제거" },
        new() { LangKey = "oa.designer.gw.branchRejectHint",        ZhCN = "剪枝：驳回只终止本分支，兄弟分支继续；全部分支被剪时按上一层策略处理", ZhTW = "剪枝：駁回只終止本分支，兄弟分支繼續；全部分支被剪時按上一層策略處理", En = "Prune: rejection ends only this branch; siblings continue. If every branch is pruned, the parent policy applies.", Ja = "剪定：却下は当該分岐のみ終了し、兄弟分岐は継続します。全分岐が剪定された場合は上位ポリシーを適用します。", Ko = "가지치기: 반려 시 해당 분기만 종료되고 형제 분기는 계속 진행됩니다. 모든 분기가 제거되면 상위 정책이 적용됩니다." },

        // ── 前端校验消息（designerModel.ts validateClient 镜像）──
        new() { LangKey = "oa.designer.errInclusiveDefault",        ZhCN = "包容分叉需至少2条出边且恰好一条无条件默认边", ZhTW = "包容分叉需至少2條出邊且恰好一條無條件預設邊", En = "Inclusive split needs >=2 outgoing edges with exactly one unconditional default edge", Ja = "包含分岐には2本以上の出力エッジと、条件なしのデフォルトエッジがちょうど1本必要です", Ko = "포괄 분기는 2개 이상의 출력 엣지와 정확히 1개의 무조건 기본 엣지가 필요합니다" },
        new() { LangKey = "oa.designer.errInclusivePair",           ZhCN = "包容分叉/汇聚未正确成对",                     ZhTW = "包容分叉/匯聚未正確成對",                     En = "Inclusive split/join are not correctly paired",  Ja = "包含分岐/合流が正しく対になっていません",        Ko = "포괄 분기/합류가 올바르게 짝지어지지 않았습니다" },
        new() { LangKey = "oa.designer.errBranchReject",            ZhCN = "分支驳回策略配置非法",                       ZhTW = "分支駁回策略配置非法",                       En = "Invalid branch reject policy configuration",     Ja = "分岐却下ポリシーの設定が不正です",              Ko = "분기 반려 정책 설정이 잘못되었습니다" },

        // ── 后端错误码（FlowSchemaValidator / SendBackAsync）──
        new() { LangKey = "E-WF-019", ZhCN = "不能退回到兄弟分支内部",             ZhTW = "不能退回到兄弟分支內部",             En = "Cannot send back into a sibling branch",                       Ja = "兄弟分岐内への差し戻しはできません",             Ko = "형제 분기 내부로 반려할 수 없습니다" },
        new() { LangKey = "E-WF-020", ZhCN = "包容分叉出边配置非法（需恰好一条默认边）", ZhTW = "包容分叉出邊配置非法（需恰好一條預設邊）", En = "Invalid inclusive split edges (exactly one default edge required)", Ja = "包含分岐の出力エッジ設定が不正です（デフォルトエッジがちょうど1本必要）", Ko = "포괄 분기 출력 엣지 설정이 잘못되었습니다(기본 엣지 1개 필요)" },
        new() { LangKey = "E-WF-021", ZhCN = "包容网关配对或驳回策略配置非法",     ZhTW = "包容網關配對或駁回策略配置非法",     En = "Invalid inclusive gateway pairing or branch-reject policy",    Ja = "包含ゲートウェイの対応関係または却下ポリシーの設定が不正です", Ko = "포괄 게이트웨이 페어링 또는 반려 정책 설정이 잘못되었습니다" },
    };
}
