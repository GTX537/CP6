using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>A3 固定资产五语词条（菜单/枚举/字段/错误码，spec §10）。代码只放 key，语义点分 key。</summary>
public static class I18nA3ScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 通用操作提示（自然语言 key，fin 模块惯例；操作成功/操作失败 其它 fin 视图亦在用但此前缺词条）──
        new Sys_Lang { LangKey = "操作成功", ZhCN = "操作成功", ZhTW = "操作成功", En = "Operation succeeded", Ja = "操作が成功しました", Ko = "작업이 성공했습니다" },
        new Sys_Lang { LangKey = "操作失败", ZhCN = "操作失败", ZhTW = "操作失敗", En = "Operation failed", Ja = "操作に失敗しました", Ko = "작업이 실패했습니다" },
        new Sys_Lang { LangKey = "试算", ZhCN = "试算", ZhTW = "試算", En = "Trial Run", Ja = "試算", Ko = "시산" },

        // ── 页头标题 + 副标题（仿 fin 视图 page-header 风格）──
        new Sys_Lang { LangKey = "资产分类", ZhCN = "资产分类", ZhTW = "資產分類", En = "Asset Category", Ja = "資産分類", Ko = "자산 분류" },
        new Sys_Lang { LangKey = "资产卡片", ZhCN = "资产卡片", ZhTW = "資產卡片", En = "Asset Card", Ja = "資産カード", Ko = "자산 카드" },
        new Sys_Lang { LangKey = "折旧计提", ZhCN = "折旧计提", ZhTW = "折舊計提", En = "Depreciation", Ja = "減価償却", Ko = "감가상각" },
        new Sys_Lang { LangKey = "资产处置", ZhCN = "资产处置", ZhTW = "資產處置", En = "Asset Disposal", Ja = "資産処分", Ko = "자산 처분" },
        new Sys_Lang { LangKey = "资产分类与默认折旧规则（科目锚点）", ZhCN = "资产分类与默认折旧规则（科目锚点）", ZhTW = "資產分類與預設折舊規則（科目錨點）", En = "Asset categories & default depreciation rules (account anchors)", Ja = "資産分類と既定の減価償却ルール（勘定アンカー）", Ko = "자산 분류 및 기본 감가상각 규칙(계정 앵커)" },
        new Sys_Lang { LangKey = "固定资产卡片台账（原值/累折/净值）", ZhCN = "固定资产卡片台账（原值/累折/净值）", ZhTW = "固定資產卡片台帳（原值/累折/淨值）", En = "Fixed-asset card ledger (cost / accum. deprec. / NBV)", Ja = "固定資産カード台帳（取得原価/減価償却累計/簿価）", Ko = "고정자산 카드 원장(취득원가/감가상각누계/장부가)" },
        new Sys_Lang { LangKey = "按期试算→计提→过账（汇总凭证）", ZhCN = "按期试算→计提→过账（汇总凭证）", ZhTW = "按期試算→計提→過帳（彙總憑證）", En = "Period preview → accrue → post depreciation (summary voucher)", Ja = "期次ごとの試算→計上→転記（集計仕訳）", Ko = "기간별 시산→계상→전기(집계 전표)" },
        new Sys_Lang { LangKey = "出售/报废/转让/盘亏（清理结转损益）", ZhCN = "出售/报废/转让/盘亏（清理结转损益）", ZhTW = "出售/報廢/轉讓/盤虧（清理結轉損益）", En = "Sale / scrap / transfer / loss disposal (clearing carry-over)", Ja = "売却/除却/譲渡/棚卸減耗（清算振替損益）", Ko = "매각/폐기/양도/감모 처분(청산 대체 손익)" },

        // ── 菜单 ──
        new Sys_Lang { LangKey = "nav.615", ZhCN = "资产分类", ZhTW = "資產分類", En = "Asset Category", Ja = "資産分類", Ko = "자산 분류" },
        new Sys_Lang { LangKey = "nav.616", ZhCN = "资产卡片", ZhTW = "資產卡片", En = "Asset Card", Ja = "資産カード", Ko = "자산 카드" },
        new Sys_Lang { LangKey = "nav.617", ZhCN = "折旧计提", ZhTW = "折舊計提", En = "Depreciation", Ja = "減価償却", Ko = "감가상각" },
        new Sys_Lang { LangKey = "nav.618", ZhCN = "资产处置", ZhTW = "資產處置", En = "Asset Disposal", Ja = "資産処分", Ko = "자산 처분" },

        // ── 折旧方法枚举 ──
        new Sys_Lang { LangKey = "asset.method.1", ZhCN = "直线法", ZhTW = "直線法", En = "Straight-Line", Ja = "定額法", Ko = "정액법" },
        new Sys_Lang { LangKey = "asset.method.2", ZhCN = "双倍余额递减", ZhTW = "雙倍餘額遞減", En = "Double Declining", Ja = "定率法(倍率)", Ko = "이중체감법" },
        new Sys_Lang { LangKey = "asset.method.3", ZhCN = "年数总和", ZhTW = "年數總和", En = "Sum-of-Years", Ja = "級数法", Ko = "연수합계법" },
        new Sys_Lang { LangKey = "asset.method.4", ZhCN = "工作量法", ZhTW = "工作量法", En = "Units of Production", Ja = "生産高比例法", Ko = "생산량비례법" },

        // ── 资产状态枚举 ──
        new Sys_Lang { LangKey = "asset.status.0", ZhCN = "草稿", ZhTW = "草稿", En = "Draft", Ja = "下書き", Ko = "초안" },
        new Sys_Lang { LangKey = "asset.status.1", ZhCN = "在用", ZhTW = "在用", En = "In Use", Ja = "使用中", Ko = "사용 중" },
        new Sys_Lang { LangKey = "asset.status.2", ZhCN = "已提足", ZhTW = "已提足", En = "Fully Depreciated", Ja = "償却済", Ko = "상각완료" },
        new Sys_Lang { LangKey = "asset.status.3", ZhCN = "已处置", ZhTW = "已處置", En = "Disposed", Ja = "処分済", Ko = "처분완료" },

        // ── 处置类型枚举 ──
        new Sys_Lang { LangKey = "asset.disposalType.1", ZhCN = "出售", ZhTW = "出售", En = "Sale", Ja = "売却", Ko = "매각" },
        new Sys_Lang { LangKey = "asset.disposalType.2", ZhCN = "报废", ZhTW = "報廢", En = "Scrap", Ja = "除却", Ko = "폐기" },
        new Sys_Lang { LangKey = "asset.disposalType.3", ZhCN = "转让", ZhTW = "轉讓", En = "Transfer", Ja = "譲渡", Ko = "양도" },
        new Sys_Lang { LangKey = "asset.disposalType.4", ZhCN = "盘亏", ZhTW = "盤虧", En = "Inventory Loss", Ja = "棚卸減耗", Ko = "재고손실" },

        // ── 批次状态枚举 ──
        new Sys_Lang { LangKey = "asset.runStatus.0", ZhCN = "草稿", ZhTW = "草稿", En = "Draft", Ja = "下書き", Ko = "초안" },
        new Sys_Lang { LangKey = "asset.runStatus.1", ZhCN = "已过账", ZhTW = "已過帳", En = "Posted", Ja = "記帳済", Ko = "전기완료" },
        new Sys_Lang { LangKey = "asset.runStatus.2", ZhCN = "已反冲", ZhTW = "已沖銷", En = "Reversed", Ja = "赤伝済", Ko = "역분개" },

        // ── 关键字段标签 ──
        new Sys_Lang { LangKey = "asset.field.assetNo", ZhCN = "资产编号", ZhTW = "資產編號", En = "Asset No.", Ja = "資産番号", Ko = "자산번호" },
        new Sys_Lang { LangKey = "asset.field.originalValue", ZhCN = "原值", ZhTW = "原值", En = "Original Value", Ja = "取得価額", Ko = "취득가액" },
        new Sys_Lang { LangKey = "asset.field.accumulated", ZhCN = "累计折旧", ZhTW = "累計折舊", En = "Accum. Deprec.", Ja = "減価償却累計", Ko = "감가상각누계" },
        new Sys_Lang { LangKey = "asset.field.netValue", ZhCN = "净值", ZhTW = "淨值", En = "Net Book Value", Ja = "帳簿価額", Ko = "장부가액" },
        new Sys_Lang { LangKey = "asset.field.salvage", ZhCN = "残值", ZhTW = "殘值", En = "Salvage", Ja = "残存価額", Ko = "잔존가액" },
        new Sys_Lang { LangKey = "asset.field.proceeds", ZhCN = "处置价款", ZhTW = "處置價款", En = "Proceeds", Ja = "処分収入", Ko = "처분대금" },
        new Sys_Lang { LangKey = "asset.field.code", ZhCN = "编码", ZhTW = "編碼", En = "Code", Ja = "コード", Ko = "코드" },
        new Sys_Lang { LangKey = "asset.field.category", ZhCN = "资产分类", ZhTW = "資產分類", En = "Category", Ja = "資産分類", Ko = "분류" },
        new Sys_Lang { LangKey = "asset.field.period", ZhCN = "会计期间", ZhTW = "會計期間", En = "Period", Ja = "会計期間", Ko = "회계기간" },
        new Sys_Lang { LangKey = "asset.field.method", ZhCN = "折旧方法", ZhTW = "折舊方法", En = "Method", Ja = "償却方法", Ko = "상각방법" },
        new Sys_Lang { LangKey = "asset.field.life", ZhCN = "可用月数", ZhTW = "可用月數", En = "Useful Life (mo)", Ja = "耐用月数", Ko = "내용월수" },
        new Sys_Lang { LangKey = "asset.field.salvageRate", ZhCN = "残值率", ZhTW = "殘值率", En = "Salvage Rate", Ja = "残価率", Ko = "잔존율" },
        new Sys_Lang { LangKey = "asset.field.assetAccount", ZhCN = "固定资产科目", ZhTW = "固定資產科目", En = "Asset Account", Ja = "固定資産勘定", Ko = "유형자산 계정" },
        new Sys_Lang { LangKey = "asset.field.accumAccount", ZhCN = "累计折旧科目", ZhTW = "累計折舊科目", En = "Accum. Deprec. Account", Ja = "減価償却累計勘定", Ko = "감가상각누계 계정" },
        new Sys_Lang { LangKey = "asset.field.expenseAccount", ZhCN = "折旧费用科目", ZhTW = "折舊費用科目", En = "Deprec. Expense Account", Ja = "減価償却費勘定", Ko = "감가상각비 계정" },
        new Sys_Lang { LangKey = "asset.field.acquisitionDate", ZhCN = "购置日", ZhTW = "購置日", En = "Acquisition Date", Ja = "取得日", Ko = "취득일" },
        new Sys_Lang { LangKey = "asset.field.totalWorkload", ZhCN = "预计总工作量", ZhTW = "預計總工作量", En = "Total Workload", Ja = "予定総作業量", Ko = "총 작업량" },
        new Sys_Lang { LangKey = "asset.field.openingImport", ZhCN = "期初建卡", ZhTW = "期初建卡", En = "Opening Import", Ja = "期首取込", Ko = "기초도입" },
        new Sys_Lang { LangKey = "asset.field.depreciatedPeriods", ZhCN = "已提期数", ZhTW = "已提期數", En = "Depreciated Periods", Ja = "償却済期数", Ko = "상각경과기" },
        new Sys_Lang { LangKey = "asset.field.amount", ZhCN = "金额", ZhTW = "金額", En = "Amount", Ja = "金額", Ko = "금액" },
        new Sys_Lang { LangKey = "asset.field.opening", ZhCN = "期初累计", ZhTW = "期初累計", En = "Opening Accum.", Ja = "期首累計", Ko = "기초누계" },
        new Sys_Lang { LangKey = "asset.field.closing", ZhCN = "期末累计", ZhTW = "期末累計", En = "Closing Accum.", Ja = "期末累計", Ko = "기말누계" },
        new Sys_Lang { LangKey = "asset.field.runNo", ZhCN = "批次号", ZhTW = "批次號", En = "Run No.", Ja = "バッチ番号", Ko = "배치 번호" },
        new Sys_Lang { LangKey = "asset.field.runStatus", ZhCN = "批次状态", ZhTW = "批次狀態", En = "Run Status", Ja = "状態", Ko = "상태" },
        new Sys_Lang { LangKey = "asset.field.count", ZhCN = "资产数", ZhTW = "資產數", En = "Count", Ja = "件数", Ko = "건수" },
        new Sys_Lang { LangKey = "asset.field.assetCard", ZhCN = "资产卡片", ZhTW = "資產卡片", En = "Asset Card", Ja = "資産カード", Ko = "자산 카드" },
        new Sys_Lang { LangKey = "asset.field.disposalNo", ZhCN = "处置单号", ZhTW = "處置單號", En = "Disposal No.", Ja = "処分番号", Ko = "처분 번호" },
        new Sys_Lang { LangKey = "asset.field.disposalType", ZhCN = "处置类型", ZhTW = "處置類型", En = "Disposal Type", Ja = "処分タイプ", Ko = "처분 유형" },
        new Sys_Lang { LangKey = "asset.field.disposalDate", ZhCN = "处置日", ZhTW = "處置日", En = "Disposal Date", Ja = "処分日", Ko = "처분일" },
        new Sys_Lang { LangKey = "asset.field.taxAmount", ZhCN = "税额", ZhTW = "稅額", En = "Tax Amount", Ja = "税額", Ko = "세액" },
        new Sys_Lang { LangKey = "asset.field.disposalExpense", ZhCN = "清理费用", ZhTW = "清理費用", En = "Disposal Expense", Ja = "処分費用", Ko = "처분비용" },
        new Sys_Lang { LangKey = "asset.field.netGainLoss", ZhCN = "净损益", ZhTW = "淨損益", En = "Net Gain/Loss", Ja = "純損益", Ko = "순손익" },
        new Sys_Lang { LangKey = "asset.field.bank", ZhCN = "银行账户", ZhTW = "銀行帳戶", En = "Bank Account", Ja = "銀行口座", Ko = "은행계좌" },
        new Sys_Lang { LangKey = "asset.field.reason", ZhCN = "原因", ZhTW = "原因", En = "Reason", Ja = "理由", Ko = "사유" },

        // ── 操作动作 ──
        new Sys_Lang { LangKey = "asset.action.run", ZhCN = "计提折旧", ZhTW = "計提折舊", En = "Run Depreciation", Ja = "減価償却計算", Ko = "감가상각 계산" },
        new Sys_Lang { LangKey = "asset.action.post", ZhCN = "过账", ZhTW = "過帳", En = "Post", Ja = "記帳", Ko = "전기" },
        new Sys_Lang { LangKey = "asset.action.reverse", ZhCN = "反冲", ZhTW = "沖銷", En = "Reverse", Ja = "赤伝", Ko = "역분개" },
        new Sys_Lang { LangKey = "asset.action.reverseReason", ZhCN = "请输入反冲原因", ZhTW = "請輸入沖銷原因", En = "Reverse reason", Ja = "赤伝理由を入力", Ko = "역분개 사유 입력" },
        new Sys_Lang { LangKey = "asset.action.confirm", ZhCN = "确认", ZhTW = "確認", En = "Confirm", Ja = "確定", Ko = "확정" },
        new Sys_Lang { LangKey = "asset.action.activate", ZhCN = "启用", ZhTW = "啟用", En = "Activate", Ja = "有効化", Ko = "활성화" },
        new Sys_Lang { LangKey = "asset.action.schedule", ZhCN = "折旧计划", ZhTW = "折舊計劃", En = "Depreciation Schedule", Ja = "償却計画", Ko = "상각계획" },
        new Sys_Lang { LangKey = "asset.action.runs", ZhCN = "折旧批次", ZhTW = "折舊批次", En = "Depreciation Runs", Ja = "償却バッチ", Ko = "상각 배치" },

        // ── 错误码 FA001~FA012 ──
        new Sys_Lang { LangKey = "FA001", ZhCN = "资产分类账务科目未配置", ZhTW = "資產分類帳務科目未配置", En = "Category accounts not configured", Ja = "資産分類の勘定科目が未設定", Ko = "자산 분류 계정 미설정" },
        new Sys_Lang { LangKey = "FA002", ZhCN = "资产已处置或已有进行中处置单", ZhTW = "資產已處置或已有進行中處置單", En = "Asset already disposed or has an active disposal", Ja = "資産は処分済みまたは処分中です", Ko = "이미 처분되었거나 진행 중인 처분이 있습니다" },
        new Sys_Lang { LangKey = "FA003", ZhCN = "本期已存在折旧批次", ZhTW = "本期已存在折舊批次", En = "Depreciation batch already exists for the period", Ja = "当期の減価償却バッチが既に存在します", Ko = "해당 기간 감가상각 배치가 이미 있습니다" },
        new Sys_Lang { LangKey = "FA004", ZhCN = "资产尚未起折", ZhTW = "資產尚未起折", En = "Asset depreciation not yet started", Ja = "償却開始前です", Ko = "상각 미개시" },
        new Sys_Lang { LangKey = "FA005", ZhCN = "累计折旧已达上限", ZhTW = "累計折舊已達上限", En = "Accumulated depreciation reached cap", Ja = "償却上限に達しています", Ko = "상각 한도 도달" },
        new Sys_Lang { LangKey = "FA006", ZhCN = "数据不一致或对象不存在", ZhTW = "資料不一致或物件不存在", En = "Data inconsistent or not found", Ja = "データ不整合または存在しません", Ko = "데이터 불일치 또는 없음" },
        new Sys_Lang { LangKey = "FA007", ZhCN = "会计期间未开启或已结账", ZhTW = "會計期間未開啟或已結帳", En = "Period not open or already closed", Ja = "会計期間が未開設または締め済み", Ko = "기간 미개시 또는 마감됨" },
        new Sys_Lang { LangKey = "FA008", ZhCN = "工作量法缺总量或本期未录量", ZhTW = "工作量法缺總量或本期未錄量", En = "Units-of-production total/period workload missing", Ja = "生産高比例法の総量/当期量が未入力", Ko = "생산량비례법 총량/당기량 누락" },
        new Sys_Lang { LangKey = "FA009", ZhCN = "状态不可再过账或需先反冲", ZhTW = "狀態不可再過帳或需先沖銷", En = "Invalid status; reverse first", Ja = "状態が不正、先に赤伝が必要", Ko = "상태 오류, 먼저 역분개 필요" },
        new Sys_Lang { LangKey = "FA010", ZhCN = "有价款或清理费用但未指定收/付款账户", ZhTW = "有價款或清理費用但未指定收/付款帳戶", En = "Proceeds/expense present but no bank account specified", Ja = "代金/費用ありだが入出金口座が未指定", Ko = "대금/비용 있으나 은행계좌 미지정" },
        new Sys_Lang { LangKey = "FA011", ZhCN = "反冲次序冲突：关联单据未先反冲", ZhTW = "沖銷次序衝突：關聯單據未先沖銷", En = "Reversal order conflict: reverse linked document first", Ja = "赤伝順序エラー：関連伝票を先に赤伝してください", Ko = "역분개 순서 충돌: 연관 전표 먼저 역분개" },
        new Sys_Lang { LangKey = "FA012", ZhCN = "资产分类被引用或有下级，不可删除", ZhTW = "資產分類被引用或有下級，不可刪除", En = "Category referenced or has children; cannot delete", Ja = "分類は参照中または下位あり、削除不可", Ko = "분류 참조 중 또는 하위 존재, 삭제 불가" },
    };
}
