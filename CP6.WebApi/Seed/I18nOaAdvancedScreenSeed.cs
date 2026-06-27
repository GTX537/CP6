using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA 电子表单信箱（Phase C）五语词条：
///   - 菜单导航：nav.735（填單）/ nav.736（表單查詢）/ nav.737（設定）
///   - 表单目录：oa.catalog.*（FormCatalog.vue）
///   - 起草发起：oa.initiate.*（FormInitiate.vue）
///   - 表单查询：oa.query.* — 暂无独立键（复用 oa.col.* 等 Phase B 已含键）
///   - 設定：oa.settings.*（InboxSettings.vue）
///   - 转交：oa.transfer.*（TransferDialog.vue）
///
/// ⚠️ Phase B seed（I18nOaInboxScreenSeed）已含的键（E-WF-001~008 / oa.formto.* /
///    oa.inst.* / nav.740/733/734 / oa.inbox.* / oa.pending.* / oa.running.* /
///    oa.done.* / oa.draft.* / oa.detail.* / oa.col.* / oa.dashboard.* /
///    oa.flowadmin.*）不在此 seed 中重复，避免 LangKey 唯一约束冲突。
/// </summary>
public static class I18nOaAdvancedScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 菜单导航标签 nav.735/736/737 ──
        new Sys_Lang { LangKey = "nav.735", ZhCN = "填單",     ZhTW = "填單",     En = "New Form",      Ja = "申請フォーム",     Ko = "양식 작성" },
        new Sys_Lang { LangKey = "nav.736", ZhCN = "表单查询", ZhTW = "表單查詢", En = "Form Search",   Ja = "フォーム検索",     Ko = "양식 검색" },
        new Sys_Lang { LangKey = "nav.737", ZhCN = "设定",     ZhTW = "設定",     En = "Settings",      Ja = "設定",             Ko = "설정" },

        // ── oa.catalog.* 填單目录 ──
        new Sys_Lang { LangKey = "oa.catalog.empty",     ZhCN = "暂无可用表单", ZhTW = "暫無可用表單", En = "No forms available",    Ja = "利用可能なフォームがありません", Ko = "사용 가능한 양식이 없습니다" },
        new Sys_Lang { LangKey = "oa.catalog.favorites", ZhCN = "我的收藏",     ZhTW = "我的收藏",     En = "Favorites",             Ja = "お気に入り",                   Ko = "즐겨찾기" },
        new Sys_Lang { LangKey = "oa.catalog.fill",      ZhCN = "填寫",         ZhTW = "填寫",         En = "Fill In",               Ja = "記入する",                     Ko = "작성하기" },

        // ── oa.initiate.* 起草发起 ──
        new Sys_Lang { LangKey = "oa.initiate.fillForm",      ZhCN = "填写表单",   ZhTW = "填寫表單",   En = "Fill in Form",       Ja = "フォームを記入",         Ko = "양식 작성" },
        new Sys_Lang { LangKey = "oa.initiate.forecastTitle", ZhCN = "流程预览",   ZhTW = "流程預覽",   En = "Flow Preview",       Ja = "フロープレビュー",       Ko = "플로우 미리보기" },
        new Sys_Lang { LangKey = "oa.initiate.noFields",      ZhCN = "暂无表单字段", ZhTW = "暫無表單欄位", En = "No form fields",   Ja = "フォームフィールドがありません", Ko = "양식 필드가 없습니다" },
        new Sys_Lang { LangKey = "oa.initiate.preview",       ZhCN = "流程预览",   ZhTW = "流程預覽",   En = "Preview Flow",       Ja = "フロー確認",             Ko = "플로우 확인" },
        new Sys_Lang { LangKey = "oa.initiate.saveDraft",     ZhCN = "保存草稿",   ZhTW = "儲存草稿",   En = "Save Draft",         Ja = "下書き保存",             Ko = "임시저장" },
        new Sys_Lang { LangKey = "oa.initiate.savedDraft",    ZhCN = "已保存草稿", ZhTW = "已儲存草稿", En = "Draft Saved",        Ja = "下書きを保存しました",   Ko = "임시저장 완료" },
        new Sys_Lang { LangKey = "oa.initiate.submit",        ZhCN = "提交",       ZhTW = "提交",       En = "Submit",             Ja = "送信",                   Ko = "제출" },
        new Sys_Lang { LangKey = "oa.initiate.submitOk",      ZhCN = "提交成功，流程已启动", ZhTW = "提交成功，流程已啟動", En = "Submitted — flow started", Ja = "送信しました。フローを開始しました", Ko = "제출 완료 — 플로우가 시작되었습니다" },

        // ── oa.settings.* 設定页 ──
        new Sys_Lang { LangKey = "oa.settings.addDelegate",  ZhCN = "添加委派",             ZhTW = "新增委派",             En = "Add Delegate",           Ja = "代理を追加",                 Ko = "대리 추가" },
        new Sys_Lang { LangKey = "oa.settings.addOk",        ZhCN = "委派已添加",           ZhTW = "委派已新增",           En = "Delegate added",         Ja = "代理を追加しました",         Ko = "대리가 추가되었습니다" },
        new Sys_Lang { LangKey = "oa.settings.delegateName", ZhCN = "委派人",               ZhTW = "委派人",               En = "Delegated By",           Ja = "委任者",                     Ko = "위임자" },
        new Sys_Lang { LangKey = "oa.settings.delegateTab",  ZhCN = "委派管理",             ZhTW = "委派管理",             En = "Delegate Management",    Ja = "代理管理",                   Ko = "대리 관리" },
        new Sys_Lang { LangKey = "oa.settings.deleteOk",     ZhCN = "已删除",               ZhTW = "已刪除",               En = "Deleted",                Ja = "削除しました",               Ko = "삭제되었습니다" },
        new Sys_Lang { LangKey = "oa.settings.hideCancelled",ZhCN = "隐藏已取消",           ZhTW = "隱藏已取消",           En = "Hide Cancelled",         Ja = "キャンセル済を非表示",       Ko = "취소됨 숨기기" },
        new Sys_Lang { LangKey = "oa.settings.pageSize",     ZhCN = "每页条数",             ZhTW = "每頁筆數",             En = "Page Size",              Ja = "ページあたり件数",           Ko = "페이지당 행수" },
        new Sys_Lang { LangKey = "oa.settings.prefSaveOk",   ZhCN = "偏好设置已保存",       ZhTW = "偏好設定已儲存",       En = "Preferences saved",      Ja = "設定を保存しました",         Ko = "환경설정이 저장되었습니다" },
        new Sys_Lang { LangKey = "oa.settings.prefTab",      ZhCN = "偏好设置",             ZhTW = "偏好設定",             En = "Preferences",            Ja = "設定",                       Ko = "환경설정" },
        new Sys_Lang { LangKey = "oa.settings.remark",       ZhCN = "备注",                 ZhTW = "備註",                 En = "Remark",                 Ja = "備考",                       Ko = "비고" },
        new Sys_Lang { LangKey = "oa.settings.remarkHint",   ZhCN = "请输入委派备注（可选）", ZhTW = "請輸入委派備註（可選）", En = "Remark (optional)",    Ja = "代理備考（任意）",           Ko = "비고 입력 (선택사항)" },
        new Sys_Lang { LangKey = "oa.settings.scope",        ZhCN = "委派范围",             ZhTW = "委派範圍",             En = "Delegate Scope",         Ja = "代理範囲",                   Ko = "위임 범위" },
        new Sys_Lang { LangKey = "oa.settings.showSummary",  ZhCN = "显示摘要",             ZhTW = "顯示摘要",             En = "Show Summary",           Ja = "サマリを表示",               Ko = "요약 표시" },
        new Sys_Lang { LangKey = "oa.settings.userHint",     ZhCN = "请选择被委派人",       ZhTW = "請選擇被委派人",       En = "Select delegatee",       Ja = "代理先を選択してください",   Ko = "피위임자를 선택하세요" },
        new Sys_Lang { LangKey = "oa.settings.validFrom",    ZhCN = "委派开始日",           ZhTW = "委派開始日",           En = "Valid From",             Ja = "委任開始日",                 Ko = "위임 시작일" },
        new Sys_Lang { LangKey = "oa.settings.validPeriod",  ZhCN = "委派期间",             ZhTW = "委派期間",             En = "Valid Period",           Ja = "委任期間",                   Ko = "위임 기간" },
        new Sys_Lang { LangKey = "oa.settings.validTo",      ZhCN = "委派结束日",           ZhTW = "委派結束日",           En = "Valid To",               Ja = "委任終了日",                 Ko = "위임 종료일" },

        // ── oa.transfer.* 转交对话框 ──
        new Sys_Lang { LangKey = "oa.transfer.title",       ZhCN = "转交待办",           ZhTW = "轉交待辦",           En = "Transfer Todo",          Ja = "タスクを転送",               Ko = "할 일 이관" },
        new Sys_Lang { LangKey = "oa.transfer.toUser",      ZhCN = "转交给",             ZhTW = "轉交給",             En = "Transfer To",            Ja = "転送先",                     Ko = "이관 대상" },
        new Sys_Lang { LangKey = "oa.transfer.userHint",    ZhCN = "请选择接收人",       ZhTW = "請選擇接收人",       En = "Select recipient",       Ja = "受信者を選択してください",   Ko = "수신자를 선택하세요" },
        new Sys_Lang { LangKey = "oa.transfer.comment",     ZhCN = "转交说明",           ZhTW = "轉交說明",           En = "Transfer Note",          Ja = "転送メモ",                   Ko = "이관 메모" },
        new Sys_Lang { LangKey = "oa.transfer.commentHint", ZhCN = "请输入转交说明（可选）", ZhTW = "請輸入轉交說明（可選）", En = "Transfer note (optional)", Ja = "転送メモ（任意）",       Ko = "이관 메모 (선택사항)" },
        new Sys_Lang { LangKey = "oa.transfer.confirm",     ZhCN = "确认转交",           ZhTW = "確認轉交",           En = "Confirm Transfer",       Ja = "転送を確認",                 Ko = "이관 확인" },
        new Sys_Lang { LangKey = "oa.transfer.ok",          ZhCN = "转交成功",           ZhTW = "轉交成功",           En = "Transferred",            Ja = "転送しました",               Ko = "이관되었습니다" },
        new Sys_Lang { LangKey = "oa.detail.transfer",      ZhCN = "转交",               ZhTW = "轉交",               En = "Transfer",               Ja = "転送",                       Ko = "이관" },
    };
}
