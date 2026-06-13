/* ============================================================
 * Phase 6 受注取消ダイアログ i18n
 * ============================================================
 * 範囲：sales.cancel.*
 * 冪等：MERGE
 * 実行：sqlcmd -S "localhost\KOUSQLSERVER" -E -d CP6DB -f 65001 -i docs/phase6-cancel-i18n-seed.sql -b
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Phase 6 Cancel i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  -- ボタン
  (N'sales.cancel.btn',               N'取消',           N'取消',           N'Cancel order',      N'取消',           N'주문 취소'),
  -- ダイアログ ヘッダ
  (N'sales.cancel.title',             N'受注取消',       N'受注取消',       N'Cancel order',      N'受注取消',       N'주문 취소'),
  (N'sales.cancel.warningTitle',      N'注意：受注取消',  N'注意：受注取消',  N'Heads up — cancellation', N'注意：受注取消', N'주의: 주문 취소'),
  (N'sales.cancel.warningDesc',       N'取消后无法恢复。系统会自动级联取消关联的 MES 製造指図 + WMS 出庫指示，并释放库存引当。',
                                       N'取消後無法恢復。系統會自動級聯取消關聯的 MES 製造指図 + WMS 出庫指示。',
                                       N'This cannot be undone. Linked MES work orders and WMS outbound orders are cascade-cancelled and reservations released.',
                                       N'取消後は復元不可。関連の MES 製造指図と WMS 出庫指示も自動的に取消され、在庫引当も解除されます。',
                                       N'취소 후 복원할 수 없습니다. 연관된 MES 작업 지시 및 WMS 출고 지시도 자동 취소되고 재고 인수도 해제됩니다.'),
  -- フォーム
  (N'sales.cancel.reasonLabel',       N'取消理由（必填，写入审计日志）', N'取消理由（必填）', N'Reason (required, audit-logged)', N'取消理由（必須）', N'취소 사유 (필수)'),
  (N'sales.cancel.reasonPlaceholder', N'例：客户来电取消，2026/06/04', N'例：客戶來電取消', N'e.g. Customer phoned to cancel', N'例：客先電話取消', N'예: 고객 전화 취소'),
  -- NeedsDecision 画面
  (N'sales.cancel.needsDecisionTitle', N'半路状态需要确认', N'半路狀態需要確認', N'Items in flight — confirm to proceed', N'半路状態あり — 確認要', N'진행 중 항목 — 확인 필요'),
  (N'sales.cancel.relatedWO',         N'关联的 MES 製造指図', N'關聯的 MES 製造指図', N'Related MES work orders', N'関連 MES 製造指図', N'관련 MES 작업 지시'),
  (N'sales.cancel.relatedOutbound',   N'关联的 WMS 出庫指示', N'關聯的 WMS 出庫指示', N'Related WMS outbound orders', N'関連 WMS 出庫指示', N'관련 WMS 출고 지시'),
  -- 列 ヘッダ
  (N'sales.cancel.no',                N'NO',             N'NO',             N'No',                N'NO',             N'번호'),
  (N'sales.cancel.status',            N'状态',           N'狀態',           N'Status',            N'ステータス',     N'상태'),
  (N'sales.cancel.autoCancellable',   N'自动取消？',     N'自動取消？',     N'Auto-cancellable?', N'自動取消可？',   N'자동 취소?'),
  (N'sales.cancel.yes',               N'是',             N'是',             N'Yes',               N'はい',           N'예'),
  (N'sales.cancel.no_',               N'否',             N'否',             N'No',                N'いいえ',         N'아니오'),
  -- アクションボタン
  (N'sales.cancel.btnProbe',          N'下一步',         N'下一步',         N'Next',              N'次へ',           N'다음'),
  (N'sales.cancel.btnForce',          N'强制取消（不可逆）', N'強制取消（不可逆）', N'Force cancel (irreversible)', N'強制取消（不可逆）', N'강제 취소 (불가역)'),
  -- 結果
  (N'sales.cancel.successMsg',        N'取消完成',       N'取消完成',       N'Cancelled',         N'取消完了',       N'취소 완료'),
  (N'sales.cancel.outcome.Cancelled', N'取消成功',       N'取消成功',       N'Cancelled',         N'取消成功',       N'취소 성공'),
  (N'sales.cancel.outcome.PartiallyCancelled', N'部分取消（部分项无法自动取消）', N'部分取消', N'Partially cancelled', N'一部取消（半路項目あり）', N'부분 취소'),
  (N'sales.cancel.outcome.NeedsDecision', N'需要确认', N'需要確認', N'Needs decision', N'要確認', N'확인 필요'),
  (N'sales.cancel.outcome.Rejected',  N'状态机拒绝（已出荷/已取消等）', N'狀態機拒絕', N'Rejected by state machine', N'状態機拒否', N'상태 기반 거부'),
  -- WorkOrderStatus 取値ラベル
  (N'sales.cancel.wo.draft',          N'草案',           N'草案',           N'Draft',             N'下書き',         N'초안'),
  (N'sales.cancel.wo.confirmed',      N'已确定',         N'已確定',         N'Confirmed',         N'確定済',         N'확정됨'),
  (N'sales.cancel.wo.issued',         N'已发行',         N'已發行',         N'Issued',            N'発行済',         N'발행됨'),
  (N'sales.cancel.wo.inProgress',     N'进行中',         N'進行中',         N'In progress',       N'着手中',         N'진행 중'),
  (N'sales.cancel.wo.completed',      N'已完成',         N'已完成',         N'Completed',         N'完了',           N'완료'),
  (N'sales.cancel.wo.cancelled',      N'已取消',         N'已取消',         N'Cancelled',         N'取消済',         N'취소됨'),
  -- OutboundOrderStatus 取値ラベル
  (N'sales.cancel.ob.draft',          N'草案',           N'草案',           N'Draft',             N'下書き',         N'초안'),
  (N'sales.cancel.ob.confirmed',      N'已确定',         N'已確定',         N'Confirmed',         N'確定済',         N'확정됨'),
  (N'sales.cancel.ob.allocated',      N'已引当',         N'已引當',         N'Allocated',         N'引当済',         N'할당됨'),
  (N'sales.cancel.ob.picking',        N'拣货中',         N'揀貨中',         N'Picking',           N'ピッキング中',   N'피킹 중'),
  (N'sales.cancel.ob.completed',      N'已出库',         N'已出庫',         N'Shipped',           N'出庫完了',       N'출고 완료'),
  (N'sales.cancel.ob.cancelled',      N'已取消',         N'已取消',         N'Cancelled',         N'取消済',         N'취소됨'),
  -- 共通ボタン（既存の sales.btn.* に揃える — 既存にあれば MERGE で再上書きされるだけ）
  (N'sales.btn.close',                N'关闭',           N'關閉',           N'Close',             N'閉じる',         N'닫기');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt
USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET
    tgt.ZhCN = src.ZhCN, tgt.ZhTW = src.ZhTW, tgt.En = src.En,
    tgt.Ja = src.Ja, tgt.Ko = src.Ko
WHEN NOT MATCHED THEN INSERT
    (LangKey, ZhCN, ZhTW, En, Ja, Ko)
    VALUES
    (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @ins int = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @upd int = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT CONCAT('=== Phase 6 Cancel i18n 完了: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
