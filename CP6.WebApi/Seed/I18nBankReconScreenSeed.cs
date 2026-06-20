using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>A4 银行对账 五语词条（菜单/视图/字段/按钮 + E-A4-*/W-A4-* 错误码）。接 Program.cs i18n 链。</summary>
public static class I18nBankReconScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        new Sys_Lang { LangKey = "nav.614", ZhCN = "银行对账", ZhTW = "銀行對賬", En = "Bank Reconciliation", Ja = "銀行勘定調整", Ko = "은행 조정" },
        // ── 视图标题/页签 ──
        new Sys_Lang { LangKey = "bankrecon.workbench", ZhCN = "对账撮合台", ZhTW = "對賬撮合台", En = "Reconciliation Workbench", Ja = "照合ワークベンチ", Ko = "조정 워크벤치" },
        new Sys_Lang { LangKey = "bankrecon.statement", ZhCN = "对账会话", ZhTW = "對賬會話", En = "Bank Statement", Ja = "明細セッション", Ko = "명세 세션" },
        new Sys_Lang { LangKey = "bankrecon.profile", ZhCN = "导入模板", ZhTW = "匯入範本", En = "Import Profile", Ja = "取込テンプレート", Ko = "가져오기 템플릿" },
        // ── 字段 ──
        new Sys_Lang { LangKey = "bankrecon.field.txnDate", ZhCN = "交易日", ZhTW = "交易日", En = "Txn Date", Ja = "取引日", Ko = "거래일" },
        new Sys_Lang { LangKey = "bankrecon.field.direction", ZhCN = "方向", ZhTW = "方向", En = "Direction", Ja = "区分", Ko = "방향" },
        new Sys_Lang { LangKey = "bankrecon.field.amount", ZhCN = "金额", ZhTW = "金額", En = "Amount", Ja = "金額", Ko = "금액" },
        new Sys_Lang { LangKey = "bankrecon.field.opening", ZhCN = "期初余额", ZhTW = "期初餘額", En = "Opening Balance", Ja = "期首残高", Ko = "기초 잔액" },
        new Sys_Lang { LangKey = "bankrecon.field.closing", ZhCN = "期末余额", ZhTW = "期末餘額", En = "Closing Balance", Ja = "期末残高", Ko = "기말 잔액" },
        new Sys_Lang { LangKey = "bankrecon.field.reconciledDiff", ZhCN = "对账差额", ZhTW = "對賬差額", En = "Reconciled Diff", Ja = "調整差額", Ko = "조정 차액" },
        new Sys_Lang { LangKey = "bankrecon.field.bankAdjusted", ZhCN = "银行侧调整后余额", ZhTW = "銀行側調整後餘額", En = "Bank Adjusted Balance", Ja = "銀行側調整後残高", Ko = "은행측 조정 후 잔액" },
        new Sys_Lang { LangKey = "bankrecon.field.bookAdjusted", ZhCN = "账面侧调整后余额", ZhTW = "賬面側調整後餘額", En = "Book Adjusted Balance", Ja = "帳簿側調整後残高", Ko = "장부측 조정 후 잔액" },
        new Sys_Lang { LangKey = "bankrecon.field.internalDiff", ZhCN = "流水内部差额", ZhTW = "流水內部差額", En = "Statement Internal Diff", Ja = "明細内部差額", Ko = "명세 내부 차액" },
        // ── 按钮 ──
        new Sys_Lang { LangKey = "bankrecon.btn.autoMatch", ZhCN = "自动撮合", ZhTW = "自動撮合", En = "Auto Match", Ja = "自動照合", Ko = "자동 매칭" },
        new Sys_Lang { LangKey = "bankrecon.btn.manualMatch", ZhCN = "人工匹配", ZhTW = "人工匹配", En = "Manual Match", Ja = "手動照合", Ko = "수동 매칭" },
        new Sys_Lang { LangKey = "bankrecon.btn.unmatch", ZhCN = "拆组", ZhTW = "拆組", En = "Unmatch", Ja = "照合解除", Ko = "매칭 해제" },
        new Sys_Lang { LangKey = "bankrecon.btn.genVoucher", ZhCN = "生成凭证", ZhTW = "生成憑證", En = "Generate Voucher", Ja = "伝票生成", Ko = "전표 생성" },
        new Sys_Lang { LangKey = "bankrecon.btn.markPending", ZhCN = "标记未达", ZhTW = "標記未達", En = "Mark Pending", Ja = "未達計上", Ko = "미달 표시" },
        new Sys_Lang { LangKey = "bankrecon.btn.lock", ZhCN = "锁定", ZhTW = "鎖定", En = "Lock", Ja = "ロック", Ko = "잠금" },
        new Sys_Lang { LangKey = "bankrecon.btn.unlock", ZhCN = "解锁", ZhTW = "解鎖", En = "Unlock", Ja = "ロック解除", Ko = "잠금 해제" },
        new Sys_Lang { LangKey = "bankrecon.btn.import", ZhCN = "导入流水", ZhTW = "匯入流水", En = "Import", Ja = "明細取込", Ko = "가져오기" },
        // ── 对话框/提示 ──
        new Sys_Lang { LangKey = "bankrecon.dlg.lockConfirm", ZhCN = "锁定前请核对调节表", ZhTW = "鎖定前請核對調節表", En = "Verify reconciliation before lock", Ja = "ロック前に調整表をご確認ください", Ko = "잠금 전 조정표를 확인하세요" },
        new Sys_Lang { LangKey = "bankrecon.msg.refresh", ZhCN = "当前流水/凭证状态已变化，请刷新候选列表后重试", ZhTW = "目前流水/憑證狀態已變化，請重新整理候選清單後重試", En = "State changed, please refresh candidates and retry", Ja = "状態が変化しました。候補を更新して再試行してください", Ko = "상태가 변경되었습니다. 후보를 새로고침 후 다시 시도하세요" },
        // ── 错误码 E-A4-* ──
        new Sys_Lang { LangKey = "E-A4-IMPORT-001", ZhCN = "导入文件/模板解析失败", ZhTW = "匯入檔案/範本解析失敗", En = "Import file/profile parse failed", Ja = "取込ファイル/テンプレート解析失敗", Ko = "가져오기 파일/템플릿 파싱 실패" },
        new Sys_Lang { LangKey = "E-A4-IMPORT-002", ZhCN = "会话非开启，禁止导入/改行", ZhTW = "會話非開啟，禁止匯入/改行", En = "Session not open: import/edit forbidden", Ja = "セッション未開放：取込/編集不可", Ko = "세션 비활성: 가져오기/편집 불가" },
        new Sys_Lang { LangKey = "E-A4-MATCH-001", ZhCN = "匹配组金额不平", ZhTW = "匹配組金額不平", En = "Match group amount unbalanced", Ja = "照合グループ金額不一致", Ko = "매칭 그룹 금액 불일치" },
        new Sys_Lang { LangKey = "E-A4-MATCH-002", ZhCN = "凭证行已被其他匹配组占用", ZhTW = "憑證行已被其他匹配組佔用", En = "Journal line already occupied", Ja = "伝票行は他グループで使用中", Ko = "전표행이 이미 점유됨" },
        new Sys_Lang { LangKey = "E-A4-MATCH-003", ZhCN = "跨账户/跨币种/方向不符，禁止匹配", ZhTW = "跨賬戶/跨幣種/方向不符，禁止匹配", En = "Cross-account/currency/direction mismatch", Ja = "口座/通貨/方向不一致", Ko = "계정/통화/방향 불일치" },
        new Sys_Lang { LangKey = "E-A4-MATCH-004", ZhCN = "流水行不属同一会话/凭证行非本银行GL科目", ZhTW = "流水行不屬同一會話/憑證行非本銀行GL科目", En = "Lines not same session / not bank GL", Ja = "明細セッション不一致/銀行GL科目外", Ko = "동일 세션 아님/은행 GL 계정 아님" },
        new Sys_Lang { LangKey = "E-A4-MATCH-005", ZhCN = "流水行已被其他匹配组占用", ZhTW = "流水行已被其他匹配組佔用", En = "Statement line already occupied", Ja = "明細行は他グループで使用中", Ko = "명세행이 이미 점유됨" },
        new Sys_Lang { LangKey = "E-A4-BANKONLY-DUP", ZhCN = "该流水行已生成对账凭证", ZhTW = "該流水行已生成對賬憑證", En = "Line already has a BankRecon voucher", Ja = "当該明細は伝票生成済", Ko = "해당 명세는 전표 생성됨" },
        new Sys_Lang { LangKey = "E-A4-BANKONLY-FAIL", ZhCN = "单边项凭证生成异常", ZhTW = "單邊項憑證生成異常", En = "Bank-only voucher generation failed", Ja = "単独明細伝票の生成に失敗しました", Ko = "단독 명세 전표 생성 실패" },
        new Sys_Lang { LangKey = "E-A4-STATEMENT-LOCKED", ZhCN = "会话已锁定，禁止操作", ZhTW = "會話已鎖定，禁止操作", En = "Session locked: operation forbidden", Ja = "セッションロック済：操作不可", Ko = "세션 잠김: 작업 불가" },
        new Sys_Lang { LangKey = "E-A4-RECON-001", ZhCN = "差额不为零，禁止锁定", ZhTW = "差額不為零，禁止鎖定", En = "Diff not zero: lock forbidden", Ja = "差額がゼロでないためロック不可", Ko = "차액이 0이 아니어서 잠금 불가" },
        new Sys_Lang { LangKey = "E-A4-RECON-002", ZhCN = "会计期间已结账，禁止解锁", ZhTW = "會計期間已結賬，禁止解鎖", En = "Period closed: unlock forbidden", Ja = "会計期間締済：ロック解除不可", Ko = "회계기간 마감: 잠금 해제 불가" },
        new Sys_Lang { LangKey = "E-A4-RECON-003", ZhCN = "解锁原因必填", ZhTW = "解鎖原因必填", En = "Unlock reason required", Ja = "ロック解除理由は必須です", Ko = "잠금 해제 이유 필수" },
        new Sys_Lang { LangKey = "E-A4-RECON-LOCKED-POSTING", ZhCN = "该银行账户本期对账已锁定，禁止过账影响银行GL科目的凭证", ZhTW = "該銀行賬戶本期對賬已鎖定，禁止過賬影響銀行GL科目的憑證", En = "Recon locked: cannot post to bank GL", Ja = "当期照合ロック済：銀行GLへの転記不可", Ko = "조정 잠김: 은행 GL 전기 불가" },
        new Sys_Lang { LangKey = "E-A4-RECON-LOCKED-REVERSAL", ZhCN = "被反冲凭证已完成锁定银行对账，须先解锁对账会话", ZhTW = "被沖銷憑證已完成鎖定銀行對賬，須先解鎖對賬會話", En = "Reconciled & locked: unlock session before reversal", Ja = "照合ロック済：先にセッション解除が必要", Ko = "조정 잠김: 먼저 세션 잠금 해제 필요" },
        new Sys_Lang { LangKey = "E-A4-CONCURRENCY-001", ZhCN = "状态已变化，请刷新后重试", ZhTW = "狀態已變化，請重新整理後重試", En = "State changed, refresh and retry", Ja = "状態が変化しました。更新して再試行", Ko = "상태 변경됨. 새로고침 후 재시도" },
        new Sys_Lang { LangKey = "E-A4-PROFILE-001", ZhCN = "模板名必填", ZhTW = "範本名稱必填", En = "Profile name required", Ja = "テンプレート名は必須です", Ko = "템플릿 이름 필수" },
        new Sys_Lang { LangKey = "E-A4-PROFILE-002", ZhCN = "模板不存在", ZhTW = "範本不存在", En = "Profile not found", Ja = "テンプレートが見つかりません", Ko = "템플릿을 찾을 수 없음" },
        new Sys_Lang { LangKey = "E-A4-LINE-001", ZhCN = "仅手工行可编辑或删除", ZhTW = "僅手工行可編輯或刪除", En = "Only manual lines can be edited or deleted", Ja = "手動行のみ編集・削除可能です", Ko = "수동 행만 편집 또는 삭제 가능" },
        // ── 警告码 W-A4-* ──
        new Sys_Lang { LangKey = "W-A4-IMPORT-DUP", ZhCN = "疑似重复行（仅警告）", ZhTW = "疑似重複行（僅警告）", En = "Suspected duplicate (warning)", Ja = "重複の疑い（警告）", Ko = "중복 의심(경고)" },
        new Sys_Lang { LangKey = "W-A4-IMPORT-SKIP", ZhCN = "强重复行已跳过", ZhTW = "強重複行已跳過", En = "Strong duplicate skipped", Ja = "完全重複をスキップ", Ko = "완전 중복 건너뜀" },
        new Sys_Lang { LangKey = "W-A4-CAND-NONE", ZhCN = "无自动候选，转人工", ZhTW = "無自動候選，轉人工", En = "No candidate, manual required", Ja = "候補なし、手動へ", Ko = "후보 없음, 수동 처리" },
    };
}
