/* ============================================================
 * WMS Phase 5 (MSBBWM100/150/170) i18n シードデータ
 * ============================================================
 * 範囲：wms.qc.* + wms.rma.* + wms.expiry.*
 * 冪等：MERGE
 * 実行：sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/wms-phase5-i18n-seed.sql -b
 * ============================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT '=== WMS Phase 5 i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

-- ─── 期限管理（wms.expiry.*） ───
INSERT INTO #i18n VALUES
  (N'wms.expiry.title',            N'有效期管理(FEFO)',   N'有效期管理(FEFO)',   N'Expiry / FEFO',          N'賞味期限管理(FEFO)',  N'유통기한 관리(FEFO)'),
  (N'wms.expiry.fld.daysWithin',   N'多少天内',          N'多少天內',          N'Within Days',            N'残日数(内)',         N'잔여 일수(이내)'),
  (N'wms.expiry.col.daysLeft',     N'剩余天数',          N'剩餘天數',          N'Days Left',              N'残日数',             N'잔여 일수'),
  (N'wms.expiry.col.overdue',      N'已过期',            N'已過期',            N'Overdue',                N'期限切れ',           N'기한 초과'),
  (N'wms.expiry.col.lossAmount',   N'损失金额',          N'損失金額',          N'Loss Amount',            N'損失金額',           N'손실 금액'),
  (N'wms.expiry.col.totalLoss',    N'总损失',            N'總損失',            N'Total Loss',             N'累計損失',           N'누계 손실'),
  (N'wms.expiry.btn.dispose',      N'批量废弃',          N'批量廢棄',          N'Dispose',                N'一括廃棄',           N'일괄 폐기'),
  (N'wms.expiry.msg.confirmDispose', N'确认废弃所选 {n} 件库存？该操作不可撤销', N'確認廢棄所選 {n} 件庫存？此操作不可撤銷', N'Dispose {n} selected stocks? Cannot be undone', N'選択中の {n} 件を廃棄しますか？取消不可', N'선택한 {n} 건을 폐기하시겠습니까? 취소 불가'),
  (N'wms.expiry.msg.reasonAsk',    N'请输入废弃原因',    N'請輸入廢棄原因',    N'Enter dispose reason',   N'廃棄理由を入力',     N'폐기 사유 입력'),
  (N'wms.expiry.msg.disposed',     N'件已废弃',          N'件已廢棄',          N'item(s) disposed',       N'件 廃棄完了',         N'건 폐기 완료');

-- ─── QC 入荷検品（wms.qc.*） ───
INSERT INTO #i18n VALUES
  (N'wms.qc.title',                N'入货检验',          N'入貨檢驗',          N'QC Inspection',          N'入荷検品',           N'입하 검수'),
  (N'wms.qc.fld.inspectionNo',     N'检验NO',            N'檢驗NO',            N'Inspection No',          N'検品NO',             N'검수NO'),
  (N'wms.qc.fld.arrivalDateTime',  N'到货日时',          N'到貨日時',          N'Arrival',                N'到着日時',           N'도착 일시'),
  (N'wms.qc.fld.inspector',        N'检验员',            N'檢驗員',            N'Inspector',              N'検品者',             N'검수자'),
  (N'wms.qc.fld.judgement',        N'判定',              N'判定',              N'Judgement',              N'判定',               N'판정'),
  (N'wms.qc.fld.judgementReason',  N'判定理由',          N'判定理由',          N'Judgement Reason',       N'判定理由',           N'판정 사유'),
  (N'wms.qc.fld.generatedReceipt', N'生成入库NO',        N'生成入庫NO',        N'Generated Receipt',      N'自動入庫NO',         N'자동 입고NO'),
  (N'wms.qc.fld.acceptWh',         N'合格入库仓库',      N'合格入庫倉庫',      N'Accept Warehouse',       N'合格入庫倉庫',       N'합격 입고 창고'),
  (N'wms.qc.status.created',       N'已创建',            N'已建立',            N'Created',                N'作成',               N'생성'),
  (N'wms.qc.status.inspecting',    N'检验中',            N'檢驗中',            N'Inspecting',             N'検品中',             N'검수 중'),
  (N'wms.qc.status.judged',        N'已判定',            N'已判定',            N'Judged',                 N'判定済',             N'판정 완료'),
  (N'wms.qc.status.cancelled',     N'已取消',            N'已取消',            N'Cancelled',              N'取消',               N'취소됨'),
  (N'wms.qc.judge.pass',           N'合格',              N'合格',              N'Pass',                   N'合格',               N'합격'),
  (N'wms.qc.judge.conditional',    N'有条件合格',        N'有條件合格',        N'Conditional Pass',       N'条件付合格',         N'조건부 합격'),
  (N'wms.qc.judge.hold',           N'保留',              N'保留',              N'Hold',                   N'保留',               N'보류'),
  (N'wms.qc.judge.fail',           N'不合格',            N'不合格',            N'Fail',                   N'不合格',             N'불합격'),
  (N'wms.qc.judge.return',         N'即返品',            N'即退品',            N'Immediate Return',       N'即返品',             N'즉시 반품'),
  (N'wms.qc.col.receivedQty',      N'到货量',            N'到貨量',            N'Received',               N'到着数',             N'도착 수량'),
  (N'wms.qc.col.acceptedQty',      N'合格数',            N'合格數',            N'Accepted',               N'合格数',             N'합격 수량'),
  (N'wms.qc.col.rejectedQty',      N'不良数',            N'不良數',            N'Rejected',               N'不良数',             N'불량 수량'),
  (N'wms.qc.col.pendingQty',       N'保留数',            N'保留數',            N'Pending',                N'保留数',             N'보류 수량'),
  (N'wms.qc.col.defectReason',     N'不良理由',          N'不良理由',          N'Defect Reason',          N'不良理由',           N'불량 사유'),
  (N'wms.qc.btn.fromInbound',      N'从入库预定生成',    N'從入庫預定生成',    N'From Inbound Order',     N'入庫予定から作成',   N'입고 예정에서 생성'),
  (N'wms.qc.btn.judge',            N'最终判定',          N'最終判定',          N'Judge',                  N'最終判定',           N'최종 판정'),
  (N'wms.qc.msg.passAutoReceipt',  N'合格时将自动生成入库实绩并反映库存', N'合格時將自動生成入庫實績並反映庫存', N'On PASS, an inbound receipt will be auto-generated and stock applied', N'合格時に入庫実績を自動生成し在庫を反映します', N'합격 시 입고 실적이 자동 생성되어 재고가 반영됩니다');

-- ─── RMA 返品（wms.rma.*） ───
INSERT INTO #i18n VALUES
  (N'wms.rma.title',               N'退货管理(RMA)',    N'退貨管理(RMA)',    N'Returns (RMA)',          N'返品管理(RMA)',     N'반품 관리(RMA)'),
  (N'wms.rma.titleNew',            N'退货管理 新建',    N'退貨管理 新增',    N'New RMA',                N'返品管理 登録',     N'반품 신규'),
  (N'wms.rma.fld.no',              N'RMA NO',           N'RMA NO',           N'RMA No',                 N'RMA NO',            N'RMA NO'),
  (N'wms.rma.fld.appliedDate',     N'申请日',           N'申請日',           N'Applied Date',           N'申請日',             N'신청일'),
  (N'wms.rma.fld.originalShipping', N'原出货NO',        N'原出貨NO',         N'Original Shipping',      N'元出荷NO',           N'원 출하NO'),
  (N'wms.rma.fld.returnReason',    N'退货原因',         N'退貨原因',         N'Return Reason',          N'返品理由',           N'반품 사유'),
  (N'wms.rma.col.condition',       N'商品状态',         N'商品狀態',         N'Condition',              N'商品状態',           N'상품 상태'),
  (N'wms.rma.col.judgement',       N'判定',             N'判定',             N'Judgement',              N'判定',               N'판정'),
  (N'wms.rma.col.destLoc',         N'振分目的地',       N'振分目的地',       N'Destination',            N'振分先ロケ',         N'배분 목적지'),
  (N'wms.rma.cond.new',            N'全新未使用',       N'全新未使用',       N'New / Unused',           N'新品未使用',         N'신품 미사용'),
  (N'wms.rma.cond.open',           N'已开封',           N'已開封',           N'Opened',                 N'開封済',             N'개봉됨'),
  (N'wms.rma.cond.damaged',        N'已损坏',           N'已損壞',           N'Damaged',                N'破損',               N'파손됨'),
  (N'wms.rma.judge.resell',        N'再销售',           N'再銷售',           N'Resell',                 N'再販可',             N'재판매'),
  (N'wms.rma.judge.repair',        N'修理',             N'修理',             N'Repair',                 N'要修理',             N'수리'),
  (N'wms.rma.judge.scrap',         N'废弃',             N'廢棄',             N'Scrap',                  N'廃棄',               N'폐기'),
  (N'wms.rma.judge.supplierReturn', N'返厂',            N'返廠',             N'Supplier Return',        N'仕入先返品',         N'공급사 반품'),
  (N'wms.rma.status.applied',      N'申请受理',         N'申請受理',         N'Applied',                N'申請受付',           N'신청 접수'),
  (N'wms.rma.status.authorized',   N'已发RMA号',        N'已發RMA號',        N'Authorized',             N'RMA発行済',          N'RMA 발급됨'),
  (N'wms.rma.status.received',     N'已退货入库',       N'已退貨入庫',       N'Received',               N'返品入庫済',         N'반품 입고됨'),
  (N'wms.rma.status.inspecting',   N'检验中',           N'檢驗中',           N'Inspecting',             N'検査中',             N'검사 중'),
  (N'wms.rma.status.judged',       N'已判定',           N'已判定',           N'Judged',                 N'判定済',             N'판정 완료'),
  (N'wms.rma.status.closed',       N'后处理完成',       N'後處理完成',       N'Closed',                 N'後処理完了',         N'후처리 완료'),
  (N'wms.rma.status.cancelled',    N'已取消',           N'已取消',           N'Cancelled',              N'取消',               N'취소됨'),
  (N'wms.rma.btn.receive',         N'退货入库',         N'退貨入庫',         N'Receive',                N'返品入庫',           N'반품 입고'),
  (N'wms.rma.btn.startInspection', N'开始检验',         N'開始檢驗',         N'Start Inspection',       N'検査開始',           N'검사 시작'),
  (N'wms.rma.btn.judge',           N'判定与振分',       N'判定與振分',       N'Judge & Dispose',        N'判定+振分',          N'판정+배분'),
  (N'wms.rma.btn.close',           N'后处理完成',       N'後處理完成',       N'Close',                  N'クローズ',           N'클로즈'),
  (N'wms.rma.msg.judgementRequired', N'所有明细行必须设置判定',  N'所有明細行必須設置判定',  N'All rows require Judgement', N'全明細に判定を設定してください', N'모든 명세에 판정을 설정하세요');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt
USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET
    tgt.ZhCN = src.ZhCN, tgt.ZhTW = src.ZhTW, tgt.En = src.En, tgt.Ja = src.Ja, tgt.Ko = src.Ko
WHEN NOT MATCHED BY TARGET THEN
    INSERT (LangKey, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @ins INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @upd INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' 件 / 更新: ' + CAST(@upd AS nvarchar(10)) + N' 件';

DROP TABLE #i18n;
PRINT '=== WMS Phase 5 i18n シード完了 ===';
PRINT N'  ※ Cache-Aside で 1時間キャッシュされる為、即時反映には API 再起動が必要。';
