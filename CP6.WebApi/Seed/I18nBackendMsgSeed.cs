using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// i18n 优化遗留：后端控制器 return 型响应里的硬编码错误/提示文案（原文＝key，经
/// LocalizedControllerBase 的 Localizer 索引器按请求 culture 解析）。前端 http 拦截器/业务页显示 res.message。
/// 注：SignalR 推送消息(SignalRWmsNotifier)条件拼接较复杂，另行处理。
/// </summary>
public static class I18nBackendMsgSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        new Sys_Lang { LangKey = "业务键已存在", ZhCN = "业务键已存在", ZhTW = "業務鍵已存在", En = "Business key already exists", Ja = "業務キーが既に存在します", Ko = "업무 키가 이미 존재합니다" },
        new Sys_Lang { LangKey = "E10008: 検索結果がありません", ZhCN = "E10008: 无检索结果", ZhTW = "E10008: 無檢索結果", En = "E10008: No search results", Ja = "E10008: 検索結果がありません", Ko = "E10008: 검색 결과가 없습니다" },
        new Sys_Lang { LangKey = "E10034: すでに更新済みです。再検索してください", ZhCN = "E10034: 已被更新，请重新检索", ZhTW = "E10034: 已被更新，請重新檢索", En = "E10034: Already updated. Please search again", Ja = "E10034: すでに更新済みです。再検索してください", Ko = "E10034: 이미 업데이트되었습니다. 다시 검색하세요" },
        new Sys_Lang { LangKey = "E10034: すでに更新済みです", ZhCN = "E10034: 已被更新", ZhTW = "E10034: 已被更新", En = "E10034: Already updated", Ja = "E10034: すでに更新済みです", Ko = "E10034: 이미 업데이트되었습니다" },
        new Sys_Lang { LangKey = "空文件", ZhCN = "空文件", ZhTW = "空檔案", En = "Empty file", Ja = "空のファイル", Ko = "빈 파일" },
        new Sys_Lang { LangKey = "発行履歴が見つかりません", ZhCN = "找不到发行履历", ZhTW = "找不到發行履歷", En = "Issue history not found", Ja = "発行履歴が見つかりません", Ko = "발행 이력을 찾을 수 없습니다" },
        new Sys_Lang { LangKey = "groupCode 不能为空", ZhCN = "groupCode 不能为空", ZhTW = "groupCode 不能為空", En = "groupCode cannot be empty", Ja = "groupCode は空にできません", Ko = "groupCode는 비워둘 수 없습니다" },
        new Sys_Lang { LangKey = "見積計算書NOが未登録です。", ZhCN = "报价计算书NO未登记。", ZhTW = "報價計算書NO未登記。", En = "Quotation calc No. is not registered.", Ja = "見積計算書NOが未登録です。", Ko = "견적 계산서 No.가 등록되지 않았습니다." },
        new Sys_Lang { LangKey = "更新対象が、他の処理によって更新されています。最新情報を取得してください。", ZhCN = "更新对象已被其他处理更新，请获取最新信息。", ZhTW = "更新對象已被其他處理更新，請取得最新資訊。", En = "The target has been updated by another process. Please fetch the latest information.", Ja = "更新対象が、他の処理によって更新されています。最新情報を取得してください。", Ko = "갱신 대상이 다른 처리에 의해 갱신되었습니다. 최신 정보를 가져오세요." },
        new Sys_Lang { LangKey = "排他锁冲突", ZhCN = "排他锁冲突", ZhTW = "排他鎖衝突", En = "Exclusive lock conflict", Ja = "排他ロック競合", Ko = "배타 잠금 충돌" },
        new Sys_Lang { LangKey = "手配NO 1〜3 のいずれかを指定してください。", ZhCN = "请指定手配NO 1〜3 中的任一项。", ZhTW = "請指定手配NO 1〜3 中的任一項。", En = "Please specify one of arrangement No. 1-3.", Ja = "手配NO 1〜3 のいずれかを指定してください。", Ko = "수배 No. 1〜3 중 하나를 지정하세요." },
        new Sys_Lang { LangKey = "E10008: 製品マスタが見つかりません", ZhCN = "E10008: 找不到产品主数据", ZhTW = "E10008: 找不到產品主資料", En = "E10008: Product master not found", Ja = "E10008: 製品マスタが見つかりません", Ko = "E10008: 제품 마스터를 찾을 수 없습니다" },
        new Sys_Lang { LangKey = "E10010: 発行対象がありません", ZhCN = "E10010: 无发行对象", ZhTW = "E10010: 無發行對象", En = "E10010: No issue target", Ja = "E10010: 発行対象がありません", Ko = "E10010: 발행 대상이 없습니다" },
        new Sys_Lang { LangKey = "E10036: 数量 FROM≦TO の関係で指定してください", ZhCN = "E10036: 数量请按 FROM≦TO 的关系指定", ZhTW = "E10036: 數量請按 FROM≦TO 的關係指定", En = "E10036: Specify quantity with FROM≤TO relationship", Ja = "E10036: 数量 FROM≦TO の関係で指定してください", Ko = "E10036: 수량을 FROM≤TO 관계로 지정하세요" },
        new Sys_Lang { LangKey = "E10036: 金額 FROM≦TO の関係で指定してください", ZhCN = "E10036: 金额请按 FROM≦TO 的关系指定", ZhTW = "E10036: 金額請按 FROM≦TO 的關係指定", En = "E10036: Specify amount with FROM≤TO relationship", Ja = "E10036: 金額 FROM≦TO の関係で指定してください", Ko = "E10036: 금액을 FROM≤TO 관계로 지정하세요" },
        new Sys_Lang { LangKey = "E10036: FROM≦TO の関係で指定してください", ZhCN = "E10036: 请按 FROM≦TO 的关系指定", ZhTW = "E10036: 請按 FROM≦TO 的關係指定", En = "E10036: Specify with FROM≤TO relationship", Ja = "E10036: FROM≦TO の関係で指定してください", Ko = "E10036: FROM≤TO 관계로 지정하세요" },
        new Sys_Lang { LangKey = "W10002: 更新対象が他の処理によって更新されています", ZhCN = "W10002: 更新对象已被其他处理更新", ZhTW = "W10002: 更新對象已被其他處理更新", En = "W10002: The target has been updated by another process", Ja = "W10002: 更新対象が他の処理によって更新されています", Ko = "W10002: 갱신 대상이 다른 처리에 의해 갱신되었습니다" },
        new Sys_Lang { LangKey = "受注が見つかりません", ZhCN = "找不到受订", ZhTW = "找不到受訂", En = "Order not found", Ja = "受注が見つかりません", Ko = "수주를 찾을 수 없습니다" },
        new Sys_Lang { LangKey = "取消理由（reason）は必須です", ZhCN = "取消理由（reason）为必填", ZhTW = "取消理由（reason）為必填", En = "Cancel reason is required", Ja = "取消理由（reason）は必須です", Ko = "취소 사유(reason)는 필수입니다" },
        new Sys_Lang { LangKey = "見積計算書が見つかりません", ZhCN = "找不到报价计算书", ZhTW = "找不到報價計算書", En = "Quotation calc not found", Ja = "見積計算書が見つかりません", Ko = "견적 계산서를 찾을 수 없습니다" },
        new Sys_Lang { LangKey = "製品マスタが未登録です。", ZhCN = "产品主数据未登记。", ZhTW = "產品主資料未登記。", En = "Product master is not registered.", Ja = "製品マスタが未登録です。", Ko = "제품 마스터가 등록되지 않았습니다." },
        new Sys_Lang { LangKey = "表单定义不存在", ZhCN = "表单定义不存在", ZhTW = "表單定義不存在", En = "Form definition does not exist", Ja = "フォーム定義が存在しません", Ko = "양식 정의가 존재하지 않습니다" },
        new Sys_Lang { LangKey = "御見積書NOが未登録です。", ZhCN = "报价单NO未登记。", ZhTW = "報價單NO未登記。", En = "Quotation No. is not registered.", Ja = "御見積書NOが未登録です。", Ko = "견적서 No.가 등록되지 않았습니다." },
        new Sys_Lang { LangKey = "流程定义不存在", ZhCN = "流程定义不存在", ZhTW = "流程定義不存在", En = "Process definition does not exist", Ja = "プロセス定義が存在しません", Ko = "프로세스 정의가 존재하지 않습니다" },
        new Sys_Lang { LangKey = "流程实例不存在", ZhCN = "流程实例不存在", ZhTW = "流程實例不存在", En = "Process instance does not exist", Ja = "プロセスインスタンスが存在しません", Ko = "프로세스 인스턴스가 존재하지 않습니다" },
        new Sys_Lang { LangKey = "E10097: ファイルが選択されていません", ZhCN = "E10097: 未选择文件", ZhTW = "E10097: 未選擇檔案", En = "E10097: No file selected", Ja = "E10097: ファイルが選択されていません", Ko = "E10097: 파일이 선택되지 않았습니다" },
        new Sys_Lang { LangKey = "用户名不存在", ZhCN = "用户名不存在", ZhTW = "使用者名稱不存在", En = "Username does not exist", Ja = "ユーザー名が存在しません", Ko = "사용자 이름이 존재하지 않습니다" },
        new Sys_Lang { LangKey = "密码错误", ZhCN = "密码错误", ZhTW = "密碼錯誤", En = "Incorrect password", Ja = "パスワードが正しくありません", Ko = "비밀번호가 올바르지 않습니다" },
        new Sys_Lang { LangKey = "账号已被禁用", ZhCN = "账号已被禁用", ZhTW = "帳號已被停用", En = "Account is disabled", Ja = "アカウントが無効化されています", Ko = "계정이 비활성화되었습니다" },
        new Sys_Lang { LangKey = "类型编码已存在", ZhCN = "类型编码已存在", ZhTW = "類型編碼已存在", En = "Type code already exists", Ja = "タイプコードが既に存在します", Ko = "유형 코드가 이미 존재합니다" },
        new Sys_Lang { LangKey = "保存成功", ZhCN = "保存成功", ZhTW = "儲存成功", En = "Saved successfully", Ja = "保存しました", Ko = "저장되었습니다" },
        new Sys_Lang { LangKey = "用户名已存在", ZhCN = "用户名已存在", ZhTW = "使用者名稱已存在", En = "Username already exists", Ja = "ユーザー名が既に存在します", Ko = "사용자 이름이 이미 존재합니다" },
        new Sys_Lang { LangKey = "E10023: すでに更新済みです。再検索してください。版型・木型No:{0} Rev:{1}", ZhCN = "E10023: 已被更新，请重新检索。版型·木型No:{0} Rev:{1}", ZhTW = "E10023: 已被更新，請重新檢索。版型·木型No:{0} Rev:{1}", En = "E10023: Already updated. Please search again. Plate/Mold No:{0} Rev:{1}", Ja = "E10023: すでに更新済みです。再検索してください。版型・木型No:{0} Rev:{1}", Ko = "E10023: 이미 업데이트되었습니다. 다시 검색하세요. 판형·목형No:{0} Rev:{1}" },
        // SignalR WMS 业务推送通知(SignalRWmsNotifier)：Title + Message(位置参数)
        new Sys_Lang { LangKey = "入庫完了", ZhCN = "入库完成", ZhTW = "入庫完成", En = "Inbound Completed", Ja = "入庫完了", Ko = "입고 완료" },
        new Sys_Lang { LangKey = "出荷完了", ZhCN = "出货完成", ZhTW = "出貨完成", En = "Shipment Completed", Ja = "出荷完了", Ko = "출하 완료" },
        new Sys_Lang { LangKey = "棚卸差異あり", ZhCN = "盘点有差异", ZhTW = "盤點有差異", En = "Stocktake Discrepancy", Ja = "棚卸差異あり", Ko = "재고조사 차이 있음" },
        new Sys_Lang { LangKey = "棚卸完了", ZhCN = "盘点完成", ZhTW = "盤點完成", En = "Stocktake Completed", Ja = "棚卸完了", Ko = "재고조사 완료" },
        new Sys_Lang { LangKey = "入庫受領 {0}（倉庫 {1}）が完了しました。", ZhCN = "入库收货 {0}（仓库 {1}）已完成。", ZhTW = "入庫收貨 {0}（倉庫 {1}）已完成。", En = "Inbound receipt {0} (warehouse {1}) is completed.", Ja = "入庫受領 {0}（倉庫 {1}）が完了しました。", Ko = "입고 수령 {0}(창고 {1})이(가) 완료되었습니다." },
        new Sys_Lang { LangKey = "出荷指示 {0} の出荷が完了しました。", ZhCN = "出货指示 {0} 的出货已完成。", ZhTW = "出貨指示 {0} 的出貨已完成。", En = "Shipment for outbound order {0} is completed.", Ja = "出荷指示 {0} の出荷が完了しました。", Ko = "출하 지시 {0}의 출하가 완료되었습니다." },
        new Sys_Lang { LangKey = "出荷指示 {0} の出荷が完了しました（荷姿 {1}）。", ZhCN = "出货指示 {0} 的出货已完成（包装 {1}）。", ZhTW = "出貨指示 {0} 的出貨已完成（包裝 {1}）。", En = "Shipment for outbound order {0} is completed (package {1}).", Ja = "出荷指示 {0} の出荷が完了しました（荷姿 {1}）。", Ko = "출하 지시 {0}의 출하가 완료되었습니다(포장 {1})." },
        new Sys_Lang { LangKey = "棚卸 {0} が完了しました（差異 {1} 件）。", ZhCN = "盘点 {0} 已完成（差异 {1} 项）。", ZhTW = "盤點 {0} 已完成（差異 {1} 項）。", En = "Stocktake {0} is completed ({1} discrepancies).", Ja = "棚卸 {0} が完了しました（差異 {1} 件）。", Ko = "재고조사 {0}이(가) 완료되었습니다(차이 {1}건)." },
    };
}
