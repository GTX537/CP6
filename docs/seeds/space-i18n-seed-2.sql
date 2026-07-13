/* ============================================================
 * Space 空間管理 i18n シード 第2弾（波3 生命周期・3 画面 + 波5 追補）
 * ============================================================
 * 範囲：space.rule.* / space.publish.* / space.events.*
 * 対象テーブル：Sys_Langs（列 LangKey / ZhCN / ZhTW / En / Ja / Ko）
 * 冪等：MERGE（既存キーは UPDATE、無ければ INSERT）
 * 実行：sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/space-i18n-seed-2.sql -b
 *
 * キーの唯一の権威 = 3 コンポーネント実コード grep（Task 5 で対照済み）:
 *   SpaceCodeRuleView.vue + SegmentsEditor.vue（space.rule.*・64 件）
 *   SpacePublishView.vue（space.publish.*・47 件、波5 で goCodeRule/dupGroupTitle 追補）
 *   SpaceEventsView.vue（space.events.*・20 件）
 * ★ space.common.* は 波2 space-i18n-seed.sql で登録済みのため本ファイルには含めない
 *   （edit/delete/cancel/save/action/required/success/confirmDelete/confirm 等は再利用）。
 * 合計 131 キー = rule 64 + publish 47 + events 20。
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
PRINT '=== Space i18n シード第2弾 開始 ===';

IF OBJECT_ID('tempdb..#i18n2') IS NOT NULL DROP TABLE #i18n2;
CREATE TABLE #i18n2 (
    -- COLLATE DATABASE_DEFAULT: 一時テーブルが tempdb 既定照合順序（Latin1）を継承し
    -- Sys_Langs.LangKey（Chinese_PRC_CI_AS）との MERGE 結合で照合順序衝突（Msg 468）を起こすのを回避。
    LangKey nvarchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n2 VALUES
  -- ══ space.rule.*（SpaceCodeRuleView + SegmentsEditor・64 件）══════════════
  -- ── ページ / リスト ──
  (N'space.rule.title',            N'编码规则',   N'編碼規則',   N'Code Rules',        N'採番ルール',          N'코드 규칙'),
  (N'space.rule.create',           N'新建规则',   N'新增規則',   N'New Rule',          N'ルール新規',          N'규칙 신규'),
  (N'space.rule.preview',          N'预览',       N'預覽',       N'Preview',           N'プレビュー',          N'미리보기'),
  (N'space.rule.fld.ruleName',     N'规则名',     N'規則名稱',   N'Rule Name',         N'ルール名',            N'규칙명'),
  (N'space.rule.fld.scopeType',    N'适用范围',   N'適用範圍',   N'Scope',             N'適用範囲',            N'적용 범위'),
  (N'space.rule.fld.scopeId',      N'对象',       N'對象',       N'Target',            N'対象',                N'대상'),
  (N'space.rule.fld.segments',     N'段数',       N'段數',       N'Segments',          N'セグメント数',        N'세그먼트 수'),
  (N'space.rule.fld.isDefault',    N'默认',       N'預設',       N'Default',           N'既定',                N'기본값'),
  (N'space.rule.fld.site',         N'站点',       N'站點',       N'Site',              N'拠点',                N'거점'),
  (N'space.rule.fld.floor',        N'楼层',       N'樓層',       N'Floor',             N'フロア',              N'층'),
  (N'space.rule.fld.zone',         N'库区',       N'庫區',       N'Zone',              N'ゾーン',              N'존'),
  (N'space.rule.scope.0',          N'租户默认',   N'租戶預設',   N'Tenant Default',    N'テナント既定',        N'테넌트 기본'),
  (N'space.rule.scope.1',          N'楼层',       N'樓層',       N'Floor',             N'フロア',              N'층'),
  (N'space.rule.scope.2',          N'库区',       N'庫區',       N'Zone',              N'ゾーン',              N'존'),
  (N'space.rule.default.yes',      N'默认',       N'預設',       N'Default',           N'既定',                N'기본'),
  (N'space.rule.default.no',       N'—',          N'—',          N'—',                 N'—',                   N'—'),
  (N'space.rule.dlg.create',       N'新建编码规则', N'新增編碼規則', N'New Code Rule',   N'採番ルール新規',      N'코드 규칙 신규'),
  (N'space.rule.dlg.edit',         N'编辑编码规则', N'編輯編碼規則', N'Edit Code Rule',  N'採番ルール編集',      N'코드 규칙 편집'),
  (N'space.rule.selectSite',       N'请选择站点', N'請選擇站點', N'Select site',       N'拠点を選択',          N'거점 선택'),
  (N'space.rule.selectFloor',      N'请选择楼层', N'請選擇樓層', N'Select floor',      N'フロアを選択',        N'층 선택'),
  (N'space.rule.selectZone',       N'请选择库区', N'請選擇庫區', N'Select zone',       N'ゾーンを選択',        N'존 선택'),
  (N'space.rule.isDefaultTip',     N'设为默认将自动取消同作用域其他默认', N'設為預設將自動取消同範圍其他預設', N'Setting as default clears others in the same scope', N'既定に設定すると同一範囲の他の既定は自動解除されます', N'기본값으로 설정하면 동일 범위의 다른 기본값이 자동 해제됩니다'),
  -- ── 段エディタ ──
  (N'space.rule.seg.title',        N'段定义',     N'段定義',     N'Segment Definition', N'セグメント定義',     N'세그먼트 정의'),
  (N'space.rule.seg.add',          N'段追加',     N'新增段',     N'Add Segment',       N'セグメント追加',      N'세그먼트 추가'),
  (N'space.rule.seg.key',          N'键',         N'鍵',         N'Key',               N'キー',                N'키'),
  (N'space.rule.seg.name',         N'名称',       N'名稱',       N'Name',              N'名称',                N'이름'),
  (N'space.rule.seg.source',       N'来源',       N'來源',       N'Source',            N'ソース',              N'소스'),
  (N'space.rule.seg.width',        N'宽度',       N'寬度',       N'Width',             N'桁数',                N'자릿수'),
  (N'space.rule.seg.pad',          N'补位',       N'補位',       N'Pad',               N'埋め文字',            N'채움'),
  (N'space.rule.seg.start',        N'起始',       N'起始',       N'Start',             N'開始',                N'시작'),
  (N'space.rule.seg.step',         N'步长',       N'步長',       N'Step',              N'ステップ',            N'증가'),
  (N'space.rule.seg.sep',          N'分隔',       N'分隔',       N'Sep',               N'区切り',              N'구분자'),
  (N'space.rule.seg.upper',        N'大写',       N'大寫',       N'Upper',             N'大文字',              N'대문자'),
  (N'space.rule.seg.fixedValue',   N'固定值',     N'固定值',     N'Fixed Value',       N'固定値',              N'고정값'),
  (N'space.rule.seg.optional',     N'可选',       N'可選',       N'Optional',          N'任意',                N'선택'),
  (N'space.rule.seg.op',           N'操作',       N'操作',       N'Actions',           N'操作',                N'작업'),
  (N'space.rule.seg.del',          N'删除',       N'刪除',       N'Delete',            N'削除',                N'삭제'),
  (N'space.rule.seg.empty',        N'暂无段，请点「段追加」', N'尚無段，請點「新增段」', N'No segments. Click "Add Segment".', N'セグメントがありません。「追加」してください', N'세그먼트가 없습니다. 추가하세요'),
  -- ── 値域ドロップダウン（グループ 3 + 12 ソース）──
  (N'space.rule.src.group.fixed',  N'固定',       N'固定',       N'Fixed',             N'固定',                N'고정'),
  (N'space.rule.src.group.code',   N'码源',       N'碼源',       N'Code Source',       N'コードソース',        N'코드 소스'),
  (N'space.rule.src.group.seq',    N'序号源',     N'序號源',     N'Sequence Source',   N'連番ソース',          N'시퀀스 소스'),
  (N'space.rule.src.fixed',        N'固定值',     N'固定值',     N'Fixed',             N'固定値',              N'고정값'),
  (N'space.rule.src.site-code',    N'站点码',     N'站點碼',     N'Site Code',         N'拠点コード',          N'거점 코드'),
  (N'space.rule.src.floor-level',  N'楼层号',     N'樓層號',     N'Floor Level',       N'フロア階',            N'층 번호'),
  (N'space.rule.src.zone-code',    N'库区码',     N'庫區碼',     N'Zone Code',         N'ゾーンコード',        N'존 코드'),
  (N'space.rule.src.aisle-code',   N'巷道码',     N'巷道碼',     N'Aisle Code',        N'通路コード',          N'통로 코드'),
  (N'space.rule.src.rack-code',    N'货架码',     N'貨架碼',     N'Rack Code',         N'ラックコード',        N'랙 코드'),
  (N'space.rule.src.zone-seq',     N'库区序号',   N'庫區序號',   N'Zone Seq',          N'ゾーン連番',          N'존 시퀀스'),
  (N'space.rule.src.aisle-seq',    N'巷道序号',   N'巷道序號',   N'Aisle Seq',         N'通路連番',            N'통로 시퀀스'),
  (N'space.rule.src.rack-seq',     N'货架序号',   N'貨架序號',   N'Rack Seq',          N'ラック連番',          N'랙 시퀀스'),
  (N'space.rule.src.col',          N'列',         N'列',         N'Column',            N'列',                  N'열'),
  (N'space.rule.src.level',        N'层',         N'層',         N'Level',             N'段',                  N'단'),
  (N'space.rule.src.depth',        N'深',         N'深',         N'Depth',             N'深さ',                N'깊이'),
  -- ── 本地校验（黄条・E-3xx）──
  (N'space.rule.err.E-303',        N'缺少库区区分段（zone-code/zone-seq，或 site-code+floor-level 组合）', N'缺少庫區區分段（zone-code/zone-seq，或 site-code+floor-level 組合）', N'Missing zone-distinguishing segment (zone-code/zone-seq, or site-code+floor-level)', N'ゾーン区分セグメントがありません（zone-code/zone-seq、または site-code+floor-level）', N'존 구분 세그먼트 누락 (zone-code/zone-seq 또는 site-code+floor-level)'),
  (N'space.rule.err.E-305',        N'巷道段应设为可选（optional）', N'巷道段應設為可選（optional）', N'Aisle segment should be optional', N'通路セグメントは任意にしてください', N'통로 세그먼트는 선택으로 설정하세요'),
  (N'space.rule.err.E-306',        N'缺少库位粒度段（col/level/depth）', N'缺少庫位粒度段（col/level/depth）', N'Missing location-granularity segment (col/level/depth)', N'ロケ粒度セグメントがありません（col/level/depth）', N'로케이션 단위 세그먼트 누락 (col/level/depth)'),
  -- ── プレビューダイアログ ──
  (N'space.rule.pv.title',         N'编码预览',   N'編碼預覽',   N'Code Preview',      N'採番プレビュー',      N'코드 미리보기'),
  (N'space.rule.pv.samples',       N'样例',       N'樣例',       N'Samples',           N'サンプル',            N'샘플'),
  (N'space.rule.pv.variableLen',   N'变长对比',   N'變長對比',   N'Variable Length',   N'可変長比較',          N'가변 길이'),
  (N'space.rule.pv.withAisle',     N'含巷道',     N'含巷道',     N'With Aisle',        N'通路あり',            N'통로 포함'),
  (N'space.rule.pv.withoutAisle',  N'无巷道',     N'無巷道',     N'Without Aisle',     N'通路なし',            N'통로 미포함'),
  (N'space.rule.pv.precheck',      N'预检',       N'預檢',       N'Precheck',          N'事前チェック',        N'사전 검사'),
  (N'space.rule.pv.ok',            N'校验通过',   N'校驗通過',   N'OK',                N'問題なし',            N'정상'),
  (N'space.rule.pv.empty',         N'暂无预览',   N'尚無預覽',   N'No preview',        N'プレビューなし',      N'미리보기 없음'),
  -- ══ space.publish.*（SpacePublishView・47 件）══════════════════════════════
  (N'space.publish.title',         N'发布中心',   N'發佈中心',   N'Publish Center',    N'発行センター',        N'게시 센터'),
  (N'space.publish.site',          N'站点',       N'站點',       N'Site',              N'拠点',                N'거점'),
  (N'space.publish.floor',         N'楼层',       N'樓層',       N'Floor',             N'フロア',              N'층'),
  (N'space.publish.zone',          N'库区',       N'庫區',       N'Zone',              N'ゾーン',              N'구역'),
  (N'space.publish.selectSite',    N'请选择站点', N'請選擇站點', N'Select site',       N'拠点を選択',          N'거점 선택'),
  (N'space.publish.selectFloor',   N'请选择楼层', N'請選擇樓層', N'Select floor',      N'フロアを選択',        N'층 선택'),
  (N'space.publish.wholeFloorHint', N'整层（可空）', N'整層（可空）', N'Whole floor (optional)', N'フロア全体（任意）', N'층 전체(선택)'),
  (N'space.publish.pickFloorFirst', N'请先选择楼层', N'請先選擇樓層', N'Select a floor first', N'先にフロアを選択してください', N'먼저 층을 선택하세요'),
  (N'space.publish.precheckTitle', N'发布预检',   N'發佈預檢',   N'Pre-publish Check', N'発行プリチェック',    N'게시 사전 점검'),
  (N'space.publish.recheck',       N'重新预检',   N'重新預檢',   N'Re-check',          N'再チェック',          N'다시 점검'),
  (N'space.publish.emptyCode',     N'空码数',     N'空碼數',     N'Empty Codes',       N'空コード数',          N'빈 코드 수'),
  (N'space.publish.dupGroups',     N'重复码组数', N'重複碼組數', N'Duplicate Groups',  N'重複コード組数',      N'중복 코드 그룹 수'),
  (N'space.publish.ruleErrors',    N'规则错误数', N'規則錯誤數', N'Rule Errors',       N'ルールエラー数',      N'규칙 오류 수'),
  (N'space.publish.unplaced',      N'未落位数',   N'未落位數',   N'Unplaced Drafts',   N'未配置数',            N'미배치 수'),
  (N'space.publish.unplacedHint',  N'不阻挡发布', N'不阻擋發佈', N'Does not block publish', N'発行を妨げません', N'게시를 막지 않음'),
  (N'space.publish.genTitle',      N'生成编码',   N'生成編碼',   N'Generate Codes',    N'コード生成',          N'코드 생성'),
  (N'space.publish.mode.fillEmpty', N'仅补空码',  N'僅補空碼',   N'Fill empty only',   N'空きのみ補完',        N'빈 코드만 채움'),
  (N'space.publish.mode.rebuild',  N'全量重排',   N'全量重排',   N'Full rebuild',      N'全量再構築',          N'전체 재생성'),
  (N'space.publish.genBtn',        N'生成编码',   N'生成編碼',   N'Generate',          N'生成',                N'생성'),
  (N'space.publish.rebuildWarn',   N'全量重排将清空并重生成所有草稿码，确定继续？', N'全量重排將清空並重新生成所有草稿碼，確定繼續？', N'Full rebuild clears and regenerates all draft codes. Continue?', N'全量再構築は全ドラフトコードを消去し再生成します。続行しますか？', N'전체 재생성은 모든 초안 코드를 지우고 재생성합니다. 계속할까요?'),
  (N'space.publish.genDone',       N'已生成 {n} 个编码', N'已生成 {n} 個編碼', N'Generated {n} codes', N'{n} 件のコードを生成しました', N'코드 {n}건 생성됨'),
  (N'space.publish.publishTitle',  N'发布',       N'發佈',       N'Publish',           N'発行',                N'게시'),
  (N'space.publish.doPublish',     N'发布',       N'發佈',       N'Publish',           N'発行',                N'게시'),
  (N'space.publish.gateHint',      N'存在空码/重复/规则错误，无法发布', N'存在空碼/重複/規則錯誤，無法發佈', N'Empty/duplicate/rule errors block publish', N'空コード/重複/ルールエラーのため発行不可', N'빈 코드/중복/규칙 오류로 게시 불가'),
  (N'space.publish.publishDone',   N'已发布 {n} 个库位', N'已發佈 {n} 個庫位', N'Published {n} locations', N'{n} 件のロケーションを発行しました', N'로케이션 {n}건 게시됨'),
  (N'space.publish.conflict409',   N'数据已被他人变更，请刷新后重试', N'資料已被他人變更，請重新整理後重試', N'Data changed by another user. Please refresh and retry.', N'他のユーザーがデータを変更しました。更新して再試行してください。', N'다른 사용자가 데이터를 변경했습니다. 새로 고침 후 다시 시도하세요.'),
  (N'space.publish.adoptBtn',      N'存量采纳',   N'存量採納',   N'Adopt Existing',    N'既存取込',            N'기존 채택'),
  (N'space.publish.adoptTitle',    N'存量采纳导入', N'存量採納匯入', N'Adopt Existing Codes', N'既存コード取込',   N'기존 코드 채택'),
  (N'space.publish.adoptHint',     N'每行输入一个库位编码', N'每行輸入一個庫位編碼', N'One location code per line', N'1行につき1コードを入力', N'한 줄에 코드 하나 입력'),
  (N'space.publish.adoptPh',       N'A-01-01（每行一码）', N'A-01-01（每行一碼）', N'A-01-01 (one per line)', N'A-01-01（1行1コード）', N'A-01-01 (한 줄에 하나)'),
  (N'space.publish.adoptConfirm',  N'导入',       N'匯入',       N'Import',            N'取込',                N'가져오기'),
  (N'space.publish.adoptEmpty',    N'请至少输入一个编码', N'請至少輸入一個編碼', N'Enter at least one code', N'少なくとも1件のコードを入力してください', N'코드를 하나 이상 입력하세요'),
  (N'space.publish.imported',      N'已导入 {n} 个', N'已匯入 {n} 個', N'Imported {n}', N'{n} 件を取込',        N'{n}건 가져옴'),
  (N'space.publish.skipped',       N'跳过 {n} 个', N'跳過 {n} 個', N'Skipped {n}',      N'{n} 件をスキップ',    N'{n}건 건너뜀'),
  (N'space.publish.deactTitle',    N'停用库位',   N'停用庫位',   N'Deactivate Locations', N'ロケーション停止', N'로케이션 비활성화'),
  (N'space.publish.prefixPh',      N'输入编码前缀搜索', N'輸入編碼前綴搜尋', N'Search by code prefix', N'コード接頭辞で検索', N'코드 접두사로 검색'),
  (N'space.publish.searchBtn',     N'搜索',       N'搜尋',       N'Search',            N'検索',                N'검색'),
  (N'space.publish.noMatch',       N'无匹配库位', N'無匹配庫位', N'No matching locations', N'一致するロケーションなし', N'일치하는 로케이션 없음'),
  (N'space.publish.deactivate',    N'停用',       N'停用',       N'Deactivate',        N'停止',                N'비활성화'),
  (N'space.publish.deactivateWarn', N'确定停用库位 [{code}]？', N'確定停用庫位 [{code}]？', N'Deactivate location [{code}]?', N'ロケーション [{code}] を停止しますか？', N'로케이션 [{code}]을(를) 비활성화할까요?'),
  (N'space.publish.col.code',      N'库位编码',   N'庫位編碼',   N'Location Code',     N'ロケーションコード',  N'로케이션 코드'),
  (N'space.publish.col.status',    N'状态',       N'狀態',       N'Status',            N'状態',                N'상태'),
  (N'space.publish.status.0',      N'草稿',       N'草稿',       N'Draft',             N'下書き',              N'초안'),
  (N'space.publish.status.1',      N'已发布',     N'已發佈',     N'Published',         N'発行済',              N'게시됨'),
  (N'space.publish.status.2',      N'已停用',     N'已停用',     N'Deactivated',       N'停止済',              N'비활성'),
  -- ── 波5：错误呈现统一（重复码明细展开 + 去编码规则页引导）──
  (N'space.publish.goCodeRule',    N'去编码规则页', N'前往編碼規則頁', N'Go to Code Rules', N'採番ルールへ',        N'코드 규칙으로 이동'),
  (N'space.publish.dupGroupTitle', N'重复组 {n}（{c} 个库位）', N'重複組 {n}（{c} 個庫位）', N'Duplicate group {n} ({c} locations)', N'重複グループ {n}（{c} 件）', N'중복 그룹 {n} ({c}개 로케이션)'),
  -- ══ space.events.*（SpaceEventsView・20 件）══════════════════════════════
  (N'space.events.title',          N'集成事件监视', N'整合事件監視', N'Integration Events', N'連携イベント監視', N'연동 이벤트 모니터'),
  (N'space.events.refresh',        N'刷新',       N'重新整理',   N'Refresh',           N'更新',                N'새로고침'),
  (N'space.events.prevPage',       N'上一页',     N'上一頁',     N'Prev',              N'前へ',                N'이전'),
  (N'space.events.nextPage',       N'下一页',     N'下一頁',     N'Next',              N'次へ',                N'다음'),
  (N'space.events.pageLabel',      N'第 {page} 页', N'第 {page} 頁', N'Page {page}',     N'{page} ページ',       N'{page} 페이지'),
  (N'space.events.empty',          N'暂无集成事件', N'暫無整合事件', N'No events',        N'連携イベントなし',    N'이벤트 없음'),
  (N'space.events.detail',         N'详情',       N'詳情',       N'Detail',            N'詳細',                N'상세'),
  (N'space.events.col.sourceNo',   N'来源单号',   N'來源單號',   N'Source No',         N'ソース番号',          N'원본 번호'),
  (N'space.events.col.hookName',   N'钩子',       N'掛鉤',       N'Hook',              N'フック',              N'훅'),
  (N'space.events.col.targetModule', N'目标模块', N'目標模組',   N'Target',            N'連携先',              N'대상 모듈'),
  (N'space.events.col.status',     N'状态',       N'狀態',       N'Status',            N'ステータス',          N'상태'),
  (N'space.events.col.attempts',   N'尝试次数',   N'嘗試次數',   N'Attempts',          N'試行回数',            N'시도 횟수'),
  (N'space.events.col.createDate', N'发生时间',   N'發生時間',   N'Created',           N'発生日時',            N'발생 일시'),
  (N'space.events.col.lastError',  N'最新错误',   N'最新錯誤',   N'Last Error',        N'最終エラー',          N'최근 오류'),
  (N'space.events.status.SUCCESS', N'成功',       N'成功',       N'Success',           N'成功',                N'성공'),
  (N'space.events.status.SKIPPED', N'跳过',       N'跳過',       N'Skipped',           N'スキップ',            N'건너뜀'),
  (N'space.events.status.PENDING', N'待处理',     N'待處理',     N'Pending',           N'保留中',              N'대기'),
  (N'space.events.status.FAILED',  N'失败',       N'失敗',       N'Failed',            N'失敗',                N'실패'),
  (N'space.events.status.DEAD',    N'死信',       N'死信',       N'Dead',              N'デッド',              N'데드'),
  (N'space.events.status.COMPENSATED', N'已补偿', N'已補償',     N'Compensated',       N'補償済',              N'보상됨');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt USING #i18n2 AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET tgt.ZhCN = src.ZhCN, tgt.ZhTW = src.ZhTW, tgt.En = src.En, tgt.Ja = src.Ja, tgt.Ko = src.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT (LangKey, ZhCN, ZhTW, En, Ja, Ko) VALUES (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @ins INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @upd INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' 件 / 更新: ' + CAST(@upd AS nvarchar(10)) + N' 件（合計 131 キー想定）';
DROP TABLE #i18n2;
PRINT '=== Done ===';
