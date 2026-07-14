using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// WFS 三期波⑥「子流程(subFlow)」画面词条 + 错误码五语种子（Sys_Lang，ZhCN/ZhTW/En/Ja/Ko）。
///
/// 键面以 cp6.web 实际 <c>t()</c> 消费为权威（波③/④/⑤口径，非以 brief 草稿键名为准）：
///  - 节点/面板（E-T1/E-T2）：<c>oa.designer.subflow.*</c> 12 键——SubFlowNode.vue（title + 动态
///    <c>policy.{all|any}</c> 全域）/ NodePropertyPanel.vue（target/targetHint/varsIn/varsOut/varsHint/
///    multi/collectionVar/policy/policy.all/policy.any/policyHint）。
///  - 前端校验（E-T1）：<c>oa.designer.errSubFlowConfig</c>（designerModel.ts validateClient）。
///  - 收件箱互链（E-T3）：<c>oa.detail.parentFlow</c> / <c>oa.detail.subFlows</c>（FormDetail.vue）。
///    注：<c>oa.detail.subIndex</c> 非 t() 键——FormDetail.vue 以 <c>#{{ s.subIndex }}</c> 直渲数据属性，
///    故不入种（入之即孤儿，与「零缺零孤儿」冲突）。子实例状态标签复用既有 <c>oa.inst.*</c>，本文件不重复放。
///  - 后端错误码：<c>E-WF-025</c>（子流程配置无效：引用/映射/策略/集合，SubFlowRefValidator/SubFlowNodeHandler/
///    FlowSchemaValidator/WfServiceJobService/FlowEngine.SubFlow.cs 抛）/<c>E-WF-026</c>（引用成环或嵌套深度超限）。
///    BizException/InvalidOperationException 码即 key，与 I18nOaServiceTask/FlowTrigger 同体例。
///
/// 去重：全部 17 键在既有 I18nOa*/I18nTenant* 等 seed 中均无重复（已全库 grep 核实，SeedLangs insert-only 安全）。
/// vue-i18n 特殊字符律：值内禁裸 <c>{ } @ |</c>（否则 message 编译失败致画面空白，Program.cs 同步块只兜 I18nLabelSeed）——
/// 故 varsHint 以文字描述映射格式，不写字面 JSON 花括号（沿 ServiceTask paramsHint 先例）。
///
/// 接入：Program.cs i18n concat 链尾（波⑤ <c>I18nOaEngineInfraScreenSeed</c> 之后）追加 <c>.Concat(...Items)</c>。
/// </summary>
public static class I18nOaSubFlowScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 节点 / 面板（SubFlowNode.vue + NodePropertyPanel.vue）──
        new() { LangKey = "oa.designer.subflow.title",         ZhCN = "子流程",         ZhTW = "子流程",         En = "Sub-flow",            Ja = "サブフロー",           Ko = "하위 플로우" },
        new() { LangKey = "oa.designer.subflow.target",        ZhCN = "目标流程",       ZhTW = "目標流程",       En = "Target Flow",         Ja = "対象フロー",           Ko = "대상 플로우" },
        new() { LangKey = "oa.designer.subflow.targetHint",    ZhCN = "引用一个已发布流程作为子步骤（不能引用当前流程自身）", ZhTW = "引用一個已發布流程作為子步驟（不能引用當前流程自身）", En = "Reference a published flow as a sub-step (cannot reference this flow itself)", Ja = "公開済みフローをサブステップとして参照します（自分自身は参照できません）", Ko = "게시된 플로우를 하위 단계로 참조합니다(자기 자신은 참조할 수 없음)" },
        new() { LangKey = "oa.designer.subflow.varsIn",        ZhCN = "传入变量映射",   ZhTW = "傳入變數對應",   En = "Input Variable Map",  Ja = "入力変数マッピング",   Ko = "입력 변수 매핑" },
        new() { LangKey = "oa.designer.subflow.varsOut",       ZhCN = "回注变量映射",   ZhTW = "回注變數對應",   En = "Output Variable Map", Ja = "出力変数マッピング",   Ko = "출력 변수 매핑" },
        new() { LangKey = "oa.designer.subflow.varsHint",      ZhCN = "JSON 变量映射，键为变量名、值为 $.表单路径；多实例回注为数组", ZhTW = "JSON 變數對應，鍵為變數名、值為 $.表單路徑；多實例回注為陣列", En = "JSON variable map: key is the variable name, value is a $.form-path; multi-instance writes back an array", Ja = "JSON 変数マッピング。キーは変数名、値は $.フォームパス。複数インスタンスでは配列で書き戻します", Ko = "JSON 변수 매핑: 키는 변수 이름, 값은 $.양식 경로. 다중 인스턴스는 배열로 기록합니다" },
        new() { LangKey = "oa.designer.subflow.multi",         ZhCN = "多实例",         ZhTW = "多實例",         En = "Multi-instance",      Ja = "マルチインスタンス",   Ko = "다중 인스턴스" },
        new() { LangKey = "oa.designer.subflow.collectionVar", ZhCN = "集合变量",       ZhTW = "集合變數",       En = "Collection Variable", Ja = "コレクション変数",     Ko = "컬렉션 변수" },
        new() { LangKey = "oa.designer.subflow.policy",        ZhCN = "完成策略",       ZhTW = "完成策略",       En = "Completion Policy",   Ja = "完了ポリシー",         Ko = "완료 정책" },
        new() { LangKey = "oa.designer.subflow.policy.all",    ZhCN = "全部通过",       ZhTW = "全部通過",       En = "All approved",        Ja = "全件承認",             Ko = "전체 승인" },
        new() { LangKey = "oa.designer.subflow.policy.any",    ZhCN = "任一通过",       ZhTW = "任一通過",       En = "Any approved",        Ja = "いずれか承認",         Ko = "하나라도 승인" },
        new() { LangKey = "oa.designer.subflow.policyHint",    ZhCN = "全部通过：任一驳回或撤回即走错误处置；任一通过：首个通过即恢复并撤回其余", ZhTW = "全部通過：任一駁回或撤回即走錯誤處置；任一通過：首個通過即恢復並撤回其餘", En = "All: any rejection or withdrawal triggers error handling. Any: the first approval resumes and withdraws the rest.", Ja = "全件承認：いずれかが却下または取下げされるとエラー処理へ。いずれか承認：最初の承認で再開し残りを取り下げます。", Ko = "전체 승인: 하나라도 반려 또는 철회되면 오류 처리. 하나라도 승인: 첫 승인 시 재개하고 나머지는 철회합니다." },

        // ── 前端校验消息（designerModel.ts validateClient 镜像 E-WF-025 静态部分）──
        new() { LangKey = "oa.designer.errSubFlowConfig",      ZhCN = "子流程配置不完整或非法", ZhTW = "子流程配置不完整或非法", En = "Sub-flow config incomplete or invalid", Ja = "サブフローの設定が不完全または不正です", Ko = "하위 플로우 구성이 불완전하거나 잘못되었습니다" },

        // ── 收件箱父子互链（FormDetail.vue）──
        new() { LangKey = "oa.detail.parentFlow",              ZhCN = "父流程",         ZhTW = "父流程",         En = "Parent Flow",         Ja = "親フロー",             Ko = "상위 플로우" },
        new() { LangKey = "oa.detail.subFlows",                ZhCN = "子流程实例",     ZhTW = "子流程實例",     En = "Sub-flow Instances",  Ja = "サブフローインスタンス", Ko = "하위 플로우 인스턴스" },

        // ── 后端错误码（FlowSchemaValidator / SubFlowRefValidator / SubFlowNodeHandler / WfServiceJobService）──
        new() { LangKey = "E-WF-025", ZhCN = "子流程配置无效（引用/映射/策略/集合）", ZhTW = "子流程配置無效（引用/對應/策略/集合）", En = "Invalid sub-flow configuration (reference/map/policy/collection)", Ja = "サブフロー設定が無効です（参照/マッピング/ポリシー/コレクション）", Ko = "하위 플로우 구성이 잘못되었습니다(참조/매핑/정책/컬렉션)" },
        new() { LangKey = "E-WF-026", ZhCN = "子流程引用成环或嵌套深度超限",         ZhTW = "子流程引用成環或嵌套深度超限",         En = "Sub-flow reference cycle or nesting depth exceeded",              Ja = "サブフロー参照が循環しているか、ネスト深度が上限を超えています",   Ko = "하위 플로우 참조가 순환하거나 중첩 깊이가 초과되었습니다" },
    };
}
