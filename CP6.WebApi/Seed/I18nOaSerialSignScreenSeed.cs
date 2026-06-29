using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// WFS 串簽功能五语词条 (T12)：
///   - 退回选择器对话框：oa.detail.sendback.* / oa.sendback.*
///   - 流程时间线：oa.timeline.sentBack
///   - 设计器串簽档位面板：oa.designer.stagesSection / oa.designer.stage.*
///   - 设计器校验消息：oa.designer.errStageInvalid
///   - 后端错误码：E-WF-011 / E-WF-012 / E-WF-013
///
/// ⚠️ Phase B seed（I18nOaInboxScreenSeed）已含：E-WF-001~008 / oa.detail.* 基础操作 / oa.transfer.*
/// ⚠️ Phase C′ seed（I18nOaDesignerScreenSeed）已含：oa.designer.countersign（会签规则）/
///    oa.designer.allowReject / oa.designer.nodeProps / oa.designer.strategy.* 等
/// ⚠️ 本 seed 所有键均为新增，无重复。
/// </summary>
public static class I18nOaSerialSignScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 退回操作按钮 (FormDetail.vue) ──
        new Sys_Lang { LangKey = "oa.detail.sendback",           ZhCN = "退回",               ZhTW = "退回",               En = "Send Back",                        Ja = "差し戻し",                   Ko = "반려" },
        new Sys_Lang { LangKey = "oa.detail.sendback.prevStage", ZhCN = "退回上一档",         ZhTW = "退回上一檔",         En = "To Previous Stage",                Ja = "前の段階へ差し戻し",         Ko = "이전 단계로 반려" },
        new Sys_Lang { LangKey = "oa.detail.sendback.starter",   ZhCN = "退回发起人",         ZhTW = "退回發起人",         En = "To Initiator",                     Ja = "申請者へ差し戻し",           Ko = "신청자에게 반려" },
        new Sys_Lang { LangKey = "oa.detail.sendback.node",      ZhCN = "退回到指定节点",     ZhTW = "退回到指定節點",     En = "To Specific Node",                 Ja = "指定ノードへ差し戻し",       Ko = "지정 노드로 반려" },
        new Sys_Lang { LangKey = "oa.detail.sendbackOk",         ZhCN = "已退回",             ZhTW = "已退回",             En = "Sent back",                        Ja = "差し戻しました",             Ko = "반려했습니다" },

        // ── 退回对话框字段标签 (SendBackDialog.vue) ──
        new Sys_Lang { LangKey = "oa.sendback.target",           ZhCN = "退回目标",           ZhTW = "退回目標",           En = "Target",                           Ja = "差し戻し先",                 Ko = "반려 대상" },
        new Sys_Lang { LangKey = "oa.sendback.selectNode",       ZhCN = "选择节点",           ZhTW = "選擇節點",           En = "Select Node",                      Ja = "ノードを選択",               Ko = "노드 선택" },
        new Sys_Lang { LangKey = "oa.sendback.nodeHint",         ZhCN = "请选择目标节点",     ZhTW = "請選擇目標節點",     En = "Select target node",               Ja = "対象ノードを選択してください", Ko = "대상 노드를 선택하세요" },

        // ── 流程时间线 (FlowTimeline.vue) ──
        new Sys_Lang { LangKey = "oa.timeline.sentBack",         ZhCN = "已退回",             ZhTW = "已退回",             En = "Sent Back",                        Ja = "差し戻し",                   Ko = "반려됨" },

        // ── 设计器串簽档位面板 (NodePropertyPanel.vue) ──
        new Sys_Lang { LangKey = "oa.designer.stagesSection",          ZhCN = "串簽档位",           ZhTW = "串簽檔位",           En = "Serial Stages",                    Ja = "直列承認段階",               Ko = "직렬 결재 단계" },
        new Sys_Lang { LangKey = "oa.designer.stage.enable",           ZhCN = "启用串簽",           ZhTW = "啟用串簽",           En = "Enable Serial Signing",            Ja = "直列承認を有効化",           Ko = "직렬 결재 사용" },
        new Sys_Lang { LangKey = "oa.designer.stage.add",              ZhCN = "加档",               ZhTW = "加檔",               En = "Add Stage",                        Ja = "段階を追加",                 Ko = "단계 추가" },
        new Sys_Lang { LangKey = "oa.designer.stage.kind.fixed",       ZhCN = "固定",               ZhTW = "固定",               En = "Fixed",                            Ja = "固定",                       Ko = "고정" },
        new Sys_Lang { LangKey = "oa.designer.stage.kind.managerChain",ZhCN = "逐级",               ZhTW = "逐級",               En = "Manager Chain",                    Ja = "上長連鎖",                   Ko = "단계별 상사" },
        new Sys_Lang { LangKey = "oa.designer.stage.maxLevels",        ZhCN = "最大级数",           ZhTW = "最大級數",           En = "Max Levels",                       Ja = "最大階層",                   Ko = "최대 레벨" },
        new Sys_Lang { LangKey = "oa.designer.stage.maxLevelsTip",     ZhCN = "从第1级主管起逐级展开，最多N个运行档", ZhTW = "從第1級主管起逐級展開，最多N個運行檔", En = "Expands per level from the 1st manager, up to N stages", Ja = "1次上長から段階展開、最大N段階", Ko = "1차 상사부터 단계별 전개, 최대 N단계" },
        new Sys_Lang { LangKey = "oa.designer.stage.approverLevelsTip",ZhCN = "取第N级主管，本档仅1个运行档",       ZhTW = "取第N級主管，本檔僅1個運行檔",       En = "The Nth-level manager, a single stage",                  Ja = "N次上長を取得、1段階のみ",       Ko = "N차 상사, 단일 단계" },
        new Sys_Lang { LangKey = "oa.designer.stage.countersign",      ZhCN = "会签",               ZhTW = "會簽",               En = "Countersign",                      Ja = "合議",                       Ko = "합의" },
        new Sys_Lang { LangKey = "oa.designer.stage.name",             ZhCN = "档名",               ZhTW = "檔名",               En = "Stage Name",                       Ja = "段階名",                     Ko = "단계명" },
        new Sys_Lang { LangKey = "oa.designer.stage.moveUp",           ZhCN = "上移",               ZhTW = "上移",               En = "Move Up",                          Ja = "上へ",                       Ko = "위로" },
        new Sys_Lang { LangKey = "oa.designer.stage.moveDown",         ZhCN = "下移",               ZhTW = "下移",               En = "Move Down",                        Ja = "下へ",                       Ko = "아래로" },
        new Sys_Lang { LangKey = "oa.designer.stage.remove",           ZhCN = "删除",               ZhTW = "刪除",               En = "Remove",                           Ja = "削除",                       Ko = "삭제" },

        // ── 设计器校验消息 (NodePropertyPanel.vue / designerModel.ts) ──
        new Sys_Lang { LangKey = "oa.designer.errStageInvalid",        ZhCN = "串簽档配置不完整",   ZhTW = "串簽檔配置不完整",   En = "Serial stage config incomplete",   Ja = "直列段階の設定が不完全です", Ko = "직렬 단계 설정이 불완전합니다" },

        // ── 后端错误码 ──
        new Sys_Lang { LangKey = "E-WF-011",                           ZhCN = "串簽档配置非法",                                        ZhTW = "串簽檔配置非法",                                        En = "Invalid serial-stage configuration",                      Ja = "直列段階の設定が不正です",           Ko = "직렬 단계 설정이 잘못되었습니다" },
        new Sys_Lang { LangKey = "E-WF-012",                           ZhCN = "退回目标非法",                                          ZhTW = "退回目標非法",                                          En = "Invalid send-back target",                                Ja = "差し戻し先が不正です",               Ko = "반려 대상이 잘못되었습니다" },
        new Sys_Lang { LangKey = "E-WF-013",                           ZhCN = "该关卡审批人缺失，流程已挂起待指派",                    ZhTW = "該關卡審批人缺失，流程已掛起待指派",                    En = "Stage approver missing; instance suspended for assignment",Ja = "段階の承認者が見つからず、案件は割当待ちで保留されました", Ko = "단계 승인자가 없어 결재가 보류되었습니다" },
    };
}
