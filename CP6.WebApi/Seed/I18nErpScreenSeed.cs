using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// i18n 优化遗留①：ERP 旧画面硬编码日文 t() 化时补的词条（日文原文＝key，与 I18nLabelSeed 同方案）。
/// 由人工逐画面补全 5 语，经 Program.cs 幂等 upsert 入库。按模块分批增长。
/// </summary>
public static class I18nErpScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // 通用操作（受注/見積/取引先 等画面复用）
        new Sys_Lang { LangKey = "新規", ZhCN = "新建", ZhTW = "新增", En = "New", Ja = "新規", Ko = "신규" },
        new Sys_Lang { LangKey = "訂正", ZhCN = "修正", ZhTW = "修正", En = "Edit", Ja = "訂正", Ko = "정정" },
        new Sys_Lang { LangKey = "流用", ZhCN = "复用", ZhTW = "沿用", En = "Copy", Ja = "流用", Ko = "유용" },
        new Sys_Lang { LangKey = "照会", ZhCN = "查询", ZhTW = "查詢", En = "View", Ja = "照会", Ko = "조회" },
        new Sys_Lang { LangKey = "削除", ZhCN = "删除", ZhTW = "刪除", En = "Delete", Ja = "削除", Ko = "삭제" },
        new Sys_Lang { LangKey = "読込", ZhCN = "读取", ZhTW = "讀取", En = "Load", Ja = "読込", Ko = "불러오기" },
        new Sys_Lang { LangKey = "読込成功", ZhCN = "读取成功", ZhTW = "讀取成功", En = "Loaded", Ja = "読込成功", Ko = "불러오기 성공" },
        new Sys_Lang { LangKey = "保存", ZhCN = "保存", ZhTW = "儲存", En = "Save", Ja = "保存", Ko = "저장" },
        new Sys_Lang { LangKey = "戻る", ZhCN = "返回", ZhTW = "返回", En = "Back", Ja = "戻る", Ko = "뒤로" },
        new Sys_Lang { LangKey = "閉じる", ZhCN = "关闭", ZhTW = "關閉", En = "Close", Ja = "閉じる", Ko = "닫기" },
        new Sys_Lang { LangKey = "発行", ZhCN = "发行", ZhTW = "發行", En = "Issue", Ja = "発行", Ko = "발행" },
        new Sys_Lang { LangKey = "確認", ZhCN = "确认", ZhTW = "確認", En = "Confirm", Ja = "確認", Ko = "확인" },
        new Sys_Lang { LangKey = "確定登録", ZhCN = "确定登记", ZhTW = "確定登記", En = "Confirm", Ja = "確定登録", Ko = "확정 등록" },
        new Sys_Lang { LangKey = "確定取消", ZhCN = "取消确定", ZhTW = "取消確定", En = "Unconfirm", Ja = "確定取消", Ko = "확정 취소" },
        new Sys_Lang { LangKey = "+ 行追加", ZhCN = "+ 添加行", ZhTW = "+ 新增行", En = "+ Add Row", Ja = "+ 行追加", Ko = "+ 행 추가" },
        new Sys_Lang { LangKey = "候補再取得", ZhCN = "重新获取候选", ZhTW = "重新取得候選", En = "Refresh Candidates", Ja = "候補再取得", Ko = "후보 재조회" },
        new Sys_Lang { LangKey = "合計再計算", ZhCN = "合计重算", ZhTW = "合計重算", En = "Recalc Total", Ja = "合計再計算", Ko = "합계 재계산" },
        // 标签 / 区段
        new Sys_Lang { LangKey = "基本", ZhCN = "基本", ZhTW = "基本", En = "Basic", Ja = "基本", Ko = "기본" },
        new Sys_Lang { LangKey = "工程", ZhCN = "工程", ZhTW = "工程", En = "Process", Ja = "工程", Ko = "공정" },
        new Sys_Lang { LangKey = "結果", ZhCN = "结果", ZhTW = "結果", En = "Result", Ja = "結果", Ko = "결과" },
        new Sys_Lang { LangKey = "メモ", ZhCN = "备忘", ZhTW = "備忘", En = "Memo", Ja = "メモ", Ko = "메모" },
        new Sys_Lang { LangKey = "円", ZhCN = "日元", ZhTW = "日圓", En = "JPY", Ja = "円", Ko = "엔" },
        new Sys_Lang { LangKey = "③ 関連見積計算書", ZhCN = "③ 关联估算计算书", ZhTW = "③ 關聯估算計算書", En = "③ Related Estimate Sheet", Ja = "③ 関連見積計算書", Ko = "③ 관련 견적 계산서" },
        new Sys_Lang { LangKey = "④ 印刷明細", ZhCN = "④ 打印明细", ZhTW = "④ 列印明細", En = "④ Print Details", Ja = "④ 印刷明細", Ko = "④ 인쇄 명세" },
        new Sys_Lang { LangKey = "御見積書 No.", ZhCN = "报价单 No.", ZhTW = "報價單 No.", En = "Quotation No.", Ja = "御見積書 No.", Ko = "견적서 No." },
        // 状态
        new Sys_Lang { LangKey = "承認済", ZhCN = "已审批", ZhTW = "已審批", En = "Approved", Ja = "承認済", Ko = "승인 완료" },
        new Sys_Lang { LangKey = "未承認", ZhCN = "未审批", ZhTW = "未審批", En = "Unapproved", Ja = "未承認", Ko = "미승인" },
        new Sys_Lang { LangKey = "見積確定済", ZhCN = "估算已确定", ZhTW = "估算已確定", En = "Estimate Confirmed", Ja = "見積確定済", Ko = "견적 확정" },
        // 校验 / 消息
        new Sys_Lang { LangKey = "検索結果がありません", ZhCN = "无检索结果", ZhTW = "無檢索結果", En = "No search results", Ja = "検索結果がありません", Ko = "검색 결과가 없습니다" },
        new Sys_Lang { LangKey = "拠点を指定してください", ZhCN = "请指定据点", ZhTW = "請指定據點", En = "Please specify base", Ja = "拠点を指定してください", Ko = "거점을 지정하세요" },
        new Sys_Lang { LangKey = "拠点を選択してください", ZhCN = "请选择据点", ZhTW = "請選擇據點", En = "Please select base", Ja = "拠点を選択してください", Ko = "거점을 선택하세요" },
        new Sys_Lang { LangKey = "担当者を選択してください", ZhCN = "请选择担当者", ZhTW = "請選擇擔當者", En = "Please select staff", Ja = "担当者を選択してください", Ko = "담당자를 선택하세요" },
        new Sys_Lang { LangKey = "ステータスのいずれかを選択してください", ZhCN = "请至少选择一个状态", ZhTW = "請至少選擇一個狀態", En = "Please select at least one status", Ja = "ステータスのいずれかを選択してください", Ko = "상태를 하나 이상 선택하세요" },
        new Sys_Lang { LangKey = "出力フォーマットを選択してください", ZhCN = "请选择输出格式", ZhTW = "請選擇輸出格式", En = "Please select output format", Ja = "出力フォーマットを選択してください", Ko = "출력 형식을 선택하세요" },
        new Sys_Lang { LangKey = "発行する帳票を選択してください", ZhCN = "请选择要发行的报表", ZhTW = "請選擇要發行的報表", En = "Select report to issue", Ja = "発行する帳票を選択してください", Ko = "발행할 장표를 선택하세요" },
        new Sys_Lang { LangKey = "発行☑の行がありません", ZhCN = "没有勾选发行的行", ZhTW = "沒有勾選發行的行", En = "No rows checked for issue", Ja = "発行☑の行がありません", Ko = "발행 체크된 행이 없습니다" },
        new Sys_Lang { LangKey = "御見積書Noを入力してください", ZhCN = "请输入报价单 No.", ZhTW = "請輸入報價單 No.", En = "Please enter Quotation No.", Ja = "御見積書Noを入力してください", Ko = "견적서 No.를 입력하세요" },
        new Sys_Lang { LangKey = "顧客コードを入力してください", ZhCN = "请输入客户编码", ZhTW = "請輸入客戶編碼", En = "Please enter customer code", Ja = "顧客コードを入力してください", Ko = "고객 코드를 입력하세요" },
        new Sys_Lang { LangKey = "顧客・案件を設定してください", ZhCN = "请设置客户・案件", ZhTW = "請設定客戶・案件", En = "Please set customer / project", Ja = "顧客・案件を設定してください", Ko = "고객・안건을 설정하세요" },
        new Sys_Lang { LangKey = "御見積書 NO は FROM ≤ TO で指定してください", ZhCN = "报价单 NO 请按 FROM ≤ TO 指定", ZhTW = "報價單 NO 請按 FROM ≤ TO 指定", En = "Specify Quotation NO as FROM ≤ TO", Ja = "御見積書 NO は FROM ≤ TO で指定してください", Ko = "견적서 NO는 FROM ≤ TO로 지정하세요" },
        new Sys_Lang { LangKey = "御見積書作成日は FROM ≤ TO で指定してください", ZhCN = "报价单创建日请按 FROM ≤ TO 指定", ZhTW = "報價單建立日請按 FROM ≤ TO 指定", En = "Specify Quotation date as FROM ≤ TO", Ja = "御見積書作成日は FROM ≤ TO で指定してください", Ko = "견적서 작성일은 FROM ≤ TO로 지정하세요" },
        new Sys_Lang { LangKey = "同じデータが登録されています。上書きしますか。", ZhCN = "已存在相同数据。是否覆盖？", ZhTW = "已存在相同資料。是否覆寫？", En = "Same data exists. Overwrite?", Ja = "同じデータが登録されています。上書きしますか。", Ko = "동일한 데이터가 등록되어 있습니다. 덮어쓰시겠습니까?" },
        new Sys_Lang { LangKey = "明細がありません。「使用」チェック or 「行追加」で明細を追加してください。", ZhCN = "无明细。请勾选「使用」或点「添加行」添加明细。", ZhTW = "無明細。請勾選「使用」或點「新增行」新增明細。", En = "No details. Add via 'Use' check or 'Add Row'.", Ja = "明細がありません。「使用」チェック or 「行追加」で明細を追加してください。", Ko = "명세가 없습니다. '사용' 체크 또는 '행 추가'로 추가하세요." },
        new Sys_Lang { LangKey = "流用副本を生成しました", ZhCN = "已生成复用副本", ZhTW = "已生成沿用副本", En = "Copy created", Ja = "流用副本を生成しました", Ko = "복사본을 생성했습니다" },
        new Sys_Lang { LangKey = "顧客コード + 案件No（親/子/材質）を変更すると自動リフレッシュ", ZhCN = "修改客户编码 + 案件No（父/子/材质）将自动刷新", ZhTW = "修改客戶編碼 + 案件No（父/子/材質）將自動重新整理", En = "Changing Customer Code + Project No (parent/child/material) auto-refreshes", Ja = "顧客コード + 案件No（親/子/材質）を変更すると自動リフレッシュ", Ko = "고객 코드 + 안건 No(부/자/재질) 변경 시 자동 새로고침" },
        // 带 {n} 插值（值与 key 均保留 {n}）
        new Sys_Lang { LangKey = "{n} 件のチェックシートを発行しました", ZhCN = "已发行 {n} 张检查表", ZhTW = "已發行 {n} 張檢查表", En = "Issued {n} checklists", Ja = "{n} 件のチェックシートを発行しました", Ko = "체크시트 {n}건을 발행했습니다" },
        new Sys_Lang { LangKey = "{n} 件のチェックシートを発行します。よろしいですか？", ZhCN = "将发行 {n} 张检查表，确定吗？", ZhTW = "將發行 {n} 張檢查表，確定嗎？", En = "Issue {n} checklists. Continue?", Ja = "{n} 件のチェックシートを発行します。よろしいですか？", Ko = "체크시트 {n}건을 발행합니다. 계속하시겠습니까?" },
        new Sys_Lang { LangKey = "{n} 件のデータを取込みました", ZhCN = "已导入 {n} 条数据", ZhTW = "已匯入 {n} 筆資料", En = "Imported {n} records", Ja = "{n} 件のデータを取込みました", Ko = "데이터 {n}건을 가져왔습니다" },
        new Sys_Lang { LangKey = "{n} 件を更新しました", ZhCN = "已更新 {n} 条", ZhTW = "已更新 {n} 筆", En = "Updated {n} records", Ja = "{n} 件を更新しました", Ko = "{n}건을 업데이트했습니다" },
        new Sys_Lang { LangKey = "{n} 件を更新します。よろしいですか？", ZhCN = "将更新 {n} 条，确定吗？", ZhTW = "將更新 {n} 筆，確定嗎？", En = "Update {n} records. Continue?", Ja = "{n} 件を更新します。よろしいですか？", Ko = "{n}건을 업데이트합니다. 계속하시겠습니까?" },
        // QuotationView 脚本侧（回补）
        new Sys_Lang { LangKey = "御見積書", ZhCN = "报价单", ZhTW = "報價單", En = "Quotation", Ja = "御見積書", Ko = "견적서" },
        new Sys_Lang { LangKey = "キャンセル", ZhCN = "取消", ZhTW = "取消", En = "Cancel", Ja = "キャンセル", Ko = "취소" },
        new Sys_Lang { LangKey = "削除確認", ZhCN = "删除确认", ZhTW = "刪除確認", En = "Delete Confirmation", Ja = "削除確認", Ko = "삭제 확인" },
        new Sys_Lang { LangKey = "削除しました", ZhCN = "已删除", ZhTW = "已刪除", En = "Deleted", Ja = "削除しました", Ko = "삭제되었습니다" },
        new Sys_Lang { LangKey = "登録しました", ZhCN = "已登记", ZhTW = "已登記", En = "Saved", Ja = "登録しました", Ko = "등록되었습니다" },
        new Sys_Lang { LangKey = "更新しました", ZhCN = "已更新", ZhTW = "已更新", En = "Updated", Ja = "更新しました", Ko = "업데이트되었습니다" },
        new Sys_Lang { LangKey = "確定登録しました", ZhCN = "已确定登记", ZhTW = "已確定登記", En = "Confirmed", Ja = "確定登録しました", Ko = "확정 등록되었습니다" },
        new Sys_Lang { LangKey = "確定を取消しました", ZhCN = "已取消确定", ZhTW = "已取消確定", En = "Confirmation cancelled", Ja = "確定を取消しました", Ko = "확정을 취소했습니다" },
        new Sys_Lang { LangKey = "必須項目を入力してください", ZhCN = "请输入必填项", ZhTW = "請輸入必填項", En = "Please enter required fields", Ja = "必須項目を入力してください", Ko = "필수 항목을 입력하세요" },
        new Sys_Lang { LangKey = "排他制御エラー", ZhCN = "并发控制错误", ZhTW = "並發控制錯誤", En = "Concurrency Error", Ja = "排他制御エラー", Ko = "동시성 제어 오류" },
        new Sys_Lang { LangKey = "他のユーザが更新しました", ZhCN = "其他用户已更新", ZhTW = "其他使用者已更新", En = "Updated by another user", Ja = "他のユーザが更新しました", Ko = "다른 사용자가 업데이트했습니다" },
        new Sys_Lang { LangKey = "最新版を取得", ZhCN = "获取最新版", ZhTW = "取得最新版", En = "Get Latest", Ja = "最新版を取得", Ko = "최신 버전 가져오기" },
        new Sys_Lang { LangKey = "最新データを取得しました。もう一度保存してください", ZhCN = "已获取最新数据，请重新保存", ZhTW = "已取得最新資料，請重新儲存", En = "Latest data loaded. Please save again", Ja = "最新データを取得しました。もう一度保存してください", Ko = "최신 데이터를 가져왔습니다. 다시 저장하세요" },
        // ① 补：楽観排他 composable 对话框（useConflictHandler / useProductConflictHandler）
        new Sys_Lang { LangKey = "更新が競合しました。", ZhCN = "更新发生冲突。", ZhTW = "更新發生衝突。", En = "Update conflict occurred.", Ja = "更新が競合しました。", Ko = "업데이트가 충돌했습니다." },
        new Sys_Lang { LangKey = "他のユーザーが先に更新しています。最新版を読み込んで再編集してください。", ZhCN = "其他用户已先行更新，请加载最新版后重新编辑。", ZhTW = "其他使用者已先行更新，請載入最新版後重新編輯。", En = "Another user has already updated this. Please load the latest version and edit again.", Ja = "他のユーザーが先に更新しています。最新版を読み込んで再編集してください。", Ko = "다른 사용자가 먼저 업데이트했습니다. 최신 버전을 불러와 다시 편집하세요." },
        new Sys_Lang { LangKey = "最新版の取得に失敗しました", ZhCN = "获取最新版失败", ZhTW = "取得最新版失敗", En = "Failed to get the latest version", Ja = "最新版の取得に失敗しました", Ko = "최신 버전 가져오기에 실패했습니다" },
        new Sys_Lang { LangKey = "先に御見積書Noを入力して「読込」してください", ZhCN = "请先输入报价单 No. 并「读取」", ZhTW = "請先輸入報價單 No. 並「讀取」", En = "Enter Quotation No. and 'Load' first", Ja = "先に御見積書Noを入力して「読込」してください", Ko = "먼저 견적서 No.를 입력하고 '불러오기' 하세요" },
        new Sys_Lang { LangKey = "確定する見積計算書にチェックを入れてから、もう一度「確定登録」を押してください", ZhCN = "请先勾选要确定的估算计算书，再点「确定登记」", ZhTW = "請先勾選要確定的估算計算書，再點「確定登記」", En = "Check estimate sheets to confirm, then click 'Confirm'", Ja = "確定する見積計算書にチェックを入れてから、もう一度「確定登録」を押してください", Ko = "확정할 견적 계산서를 체크한 후 '확정 등록'을 누르세요" },
        new Sys_Lang { LangKey = "確定する見積計算書を1件以上選択してください", ZhCN = "请至少选择一件要确定的估算计算书", ZhTW = "請至少選擇一件要確定的估算計算書", En = "Select at least one estimate sheet to confirm", Ja = "確定する見積計算書を1件以上選択してください", Ko = "확정할 견적 계산서를 1건 이상 선택하세요" },
        new Sys_Lang { LangKey = "御見積書 {no} を論理削除します。削除後は復旧できません。よろしいですか？", ZhCN = "将逻辑删除报价单 {no}，删除后无法恢复，确定吗？", ZhTW = "將邏輯刪除報價單 {no}，刪除後無法復原，確定嗎？", En = "Logically delete Quotation {no}. Cannot be recovered. Continue?", Ja = "御見積書 {no} を論理削除します。削除後は復旧できません。よろしいですか？", Ko = "견적서 {no}를 논리 삭제합니다. 복구할 수 없습니다. 계속하시겠습니까?" },
        new Sys_Lang { LangKey = "御見積書 {no} の確定を取消します。よろしいですか？", ZhCN = "将取消报价单 {no} 的确定，确定吗？", ZhTW = "將取消報價單 {no} 的確定，確定嗎？", En = "Cancel confirmation of Quotation {no}. Continue?", Ja = "御見積書 {no} の確定を取消します。よろしいですか？", Ko = "견적서 {no}의 확정을 취소합니다. 계속하시겠습니까?" },
        new Sys_Lang { LangKey = "発行しました：{files}", ZhCN = "已发行：{files}", ZhTW = "已發行：{files}", En = "Issued: {files}", Ja = "発行しました：{files}", Ko = "발행했습니다: {files}" },
        new Sys_Lang { LangKey = "No={no} が未登録です", ZhCN = "No={no} 未登记", ZhTW = "No={no} 未登記", En = "No={no} not found", Ja = "No={no} が未登録です", Ko = "No={no}는 미등록입니다" },
        new Sys_Lang { LangKey = "Q=御見積書 / SC=提出用計算書 / C=計算書（カンマ区切り）", ZhCN = "Q=报价单 / SC=提交用计算书 / C=计算书（逗号分隔）", ZhTW = "Q=報價單 / SC=提交用計算書 / C=計算書（逗號分隔）", En = "Q=Quotation / SC=Submission Calc / C=Calc (comma-separated)", Ja = "Q=御見積書 / SC=提出用計算書 / C=計算書（カンマ区切り）", Ko = "Q=견적서 / SC=제출용 계산서 / C=계산서(쉼표 구분)" },
    };
}
