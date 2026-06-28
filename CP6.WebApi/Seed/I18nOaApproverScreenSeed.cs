using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>审批人解析高级策略画面词条(②③①)：oa.designer.strategy.*/oa.approverMap.*/nav.739/E-WF-014/015。
/// 去重避开 I18nOaInbox/Advanced/Designer/SerialSign seed 已有键。
/// 已排除(已含于 I18nOaDesignerScreenSeed)：oa.designer.approverUser/approverRole/approverLevels/userHint/strategy.{directManager,deptLeader,role,specified,starter}
/// 已排除(已含于 I18nOaSerialSignScreenSeed)：oa.designer.stage.remove/stagesSection/stage.*</summary>
public static class I18nOaApproverScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 新审批策略枚举 (NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.strategy.formField", ZhCN = "表单字段指定", ZhTW = "表單欄位指定", En = "Form Field", Ja = "フォーム項目指定", Ko = "양식 필드 지정" },
        new() { LangKey = "oa.designer.strategy.dataMap",   ZhCN = "数据映射",     ZhTW = "資料映射",     En = "Data Map",   Ja = "データマップ",     Ko = "데이터 매핑" },
        new() { LangKey = "oa.designer.strategy.group",     ZhCN = "混合组",       ZhTW = "混合組",       En = "Group",      Ja = "混合グループ",     Ko = "혼합 그룹" },

        // ── 新字段标签 (NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.approverField",      ZhCN = "审批人字段",   ZhTW = "審批人欄位",   En = "Approver Field",    Ja = "承認者項目",   Ko = "승인자 필드" },
        new() { LangKey = "oa.designer.approverFieldHint",  ZhCN = "表单中存审批人 UserId 的字段名", ZhTW = "表單中存審批人 UserId 的欄位名", En = "Form field holding approver UserId", Ja = "承認者UserIdを保持するフォーム項目", Ko = "승인자 UserId를 담는 양식 필드" },
        new() { LangKey = "oa.designer.approverMapKey",     ZhCN = "映射键",       ZhTW = "映射鍵",       En = "Map Key",           Ja = "マップキー",   Ko = "매핑 키" },

        // ── When 门控 / Filter 候选过滤 (NodePropertyPanel.vue 高级参数区) ──
        new() { LangKey = "oa.designer.approverWhen",       ZhCN = "适用条件",     ZhTW = "適用條件",     En = "When (condition)",  Ja = "適用条件",     Ko = "적용 조건" },
        new() { LangKey = "oa.designer.approverWhenHint",   ZhCN = "对表单字段求值,为真才采用本规则。如 amount > 10000", ZhTW = "對表單欄位求值,為真才採用本規則。如 amount > 10000", En = "Evaluated over form fields; rule applies only if true. e.g. amount > 10000", Ja = "フォーム項目で評価し真なら適用。例 amount > 10000", Ko = "양식 필드로 평가, 참일 때만 적용. 예 amount > 10000" },
        new() { LangKey = "oa.designer.approverFilter",     ZhCN = "候选过滤",     ZhTW = "候選過濾",     En = "Candidate Filter",  Ja = "候補フィルタ", Ko = "후보 필터" },
        new() { LangKey = "oa.designer.approverFilterHint", ZhCN = "逐候选求值,保留通过者。可用 user.deptId/starter.deptId 等", ZhTW = "逐候選求值,保留通過者。可用 user.deptId/starter.deptId 等", En = "Per-candidate filter; keep those passing. e.g. user.deptId == starter.deptId", Ja = "候補ごとに評価し通過者を残す。例 user.deptId == starter.deptId", Ko = "후보별 평가, 통과자만 유지. 예 user.deptId == starter.deptId" },

        // ── Group 混合组成员 (NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.member",             ZhCN = "成员",         ZhTW = "成員",         En = "Member",            Ja = "メンバー",     Ko = "구성원" },
        new() { LangKey = "oa.designer.addMember",          ZhCN = "加成员",       ZhTW = "加成員",       En = "Add Member",        Ja = "メンバー追加", Ko = "구성원 추가" },

        // ── 前端校验消息 (designerModel.ts validateClient) ──
        new() { LangKey = "oa.designer.errApproverConfig",  ZhCN = "审批人高级配置不完整", ZhTW = "審批人進階配置不完整", En = "Advanced approver config incomplete", Ja = "承認者の詳細設定が不完全です", Ko = "고급 승인자 구성이 불완전합니다" },

        // ── 审批人映射维护页 (ApproverMapView.vue) ──
        new() { LangKey = "oa.approverMap.key",         ZhCN = "映射键",   ZhTW = "映射鍵",   En = "Map Key",      Ja = "マップキー",   Ko = "매핑 키" },
        new() { LangKey = "oa.approverMap.matchValue",  ZhCN = "匹配值",   ZhTW = "匹配值",   En = "Match Value",  Ja = "一致値",       Ko = "일치 값" },
        new() { LangKey = "oa.approverMap.approverUser",ZhCN = "审批用户", ZhTW = "審批用戶", En = "Approver User",Ja = "承認ユーザー", Ko = "승인 사용자" },
        new() { LangKey = "oa.approverMap.approverRole",ZhCN = "审批角色", ZhTW = "審批角色", En = "Approver Role", Ja = "承認ロール",   Ko = "승인 역할" },
        new() { LangKey = "oa.approverMap.enable",      ZhCN = "启用",     ZhTW = "啟用",     En = "Enable",       Ja = "有効",         Ko = "사용" },
        new() { LangKey = "oa.approverMap.addRow",      ZhCN = "新增映射", ZhTW = "新增映射", En = "Add Mapping",  Ja = "マッピング追加",Ko = "매핑 추가" },

        // ── 菜单导航 nav.739 ──
        new() { LangKey = "nav.739",  ZhCN = "审批人映射", ZhTW = "審批人映射", En = "Approver Mapping",  Ja = "承認者マッピング", Ko = "승인자 매핑" },

        // ── 后端错误码 ──
        new() { LangKey = "E-WF-014", ZhCN = "审批人高级配置非法",     ZhTW = "審批人進階配置非法",     En = "Invalid advanced approver config",           Ja = "承認者の詳細設定が不正です",             Ko = "고급 승인자 구성이 잘못되었습니다" },
        new() { LangKey = "E-WF-015", ZhCN = "审批人映射重复或非法",   ZhTW = "審批人映射重複或非法",   En = "Duplicate or invalid approver mapping",      Ja = "承認者マッピングが重複または不正です",   Ko = "승인자 매핑이 중복되거나 잘못되었습니다" },
    };
}
