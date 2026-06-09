/* ============================================================
 * Gap 4.3 — 多通貨 / 為替レートマスタ frontend i18n
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Gap 4.3 FX Rate i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'erp.fxRate.title', N'汇率管理', N'匯率管理', N'FX rates', N'為替レート管理', N'환율 관리'),
  (N'erp.fxRate.subtitle', N'受注时按客户币种冻结当日汇率（基轴 JPY）', N'受注時依客戶幣別凍結當日匯率（基軸 JPY）', N'Frozen at order time by customer currency (base JPY)', N'受注時に得意先通貨の当日レートを凍結（基軸 JPY）', N'수주 시 고객 통화의 당일 환율 고정 (기축 JPY)'),
  (N'erp.fxRate.base', N'基轴', N'基軸', N'Base', N'基軸', N'기축'),
  (N'erp.fxRate.filter.currency', N'按币种筛选', N'依幣別篩選', N'Filter currency', N'通貨で絞込', N'통화 필터'),
  (N'erp.fxRate.col.currency', N'币种', N'幣別', N'Currency', N'通貨', N'통화'),
  (N'erp.fxRate.col.rateDate', N'适用日', N'適用日', N'Effective date', N'適用日', N'적용일'),
  (N'erp.fxRate.col.rate', N'汇率(JPY/外币)', N'匯率(JPY/外幣)', N'Rate (JPY/unit)', N'レート(JPY/外貨)', N'환율(JPY/외화)'),
  (N'erp.fxRate.col.remarks', N'备注', N'備註', N'Remark', N'備考', N'비고'),
  (N'erp.fxRate.col.action', N'操作', N'操作', N'Action', N'操作', N'작업'),
  (N'erp.fxRate.btn.create', N'新增汇率', N'新增匯率', N'New rate', N'新規レート', N'환율 추가'),
  (N'erp.fxRate.btn.refresh', N'刷新', N'重新整理', N'Refresh', N'更新', N'새로고침'),
  (N'erp.fxRate.btn.edit', N'编辑', N'編輯', N'Edit', N'編集', N'편집'),
  (N'erp.fxRate.btn.delete', N'删除', N'刪除', N'Delete', N'削除', N'삭제'),
  (N'erp.fxRate.btn.cancel', N'取消', N'取消', N'Cancel', N'キャンセル', N'취소'),
  (N'erp.fxRate.btn.confirm', N'确定', N'確定', N'Confirm', N'確定', N'확인'),
  (N'erp.fxRate.hint.currency', N'ISO 4217，如 USD/EUR/CNY', N'ISO 4217，如 USD/EUR/CNY', N'ISO 4217, e.g. USD/EUR/CNY', N'ISO 4217（例 USD/EUR/CNY）', N'ISO 4217 (예: USD/EUR/CNY)'),
  (N'erp.fxRate.hint.rate', N'1 外币 = ? 日元', N'1 外幣 = ? 日圓', N'1 unit = ? JPY', N'外貨1単位 = ? 円', N'1단위 = ? 엔'),
  (N'erp.fxRate.dlg.createTitle', N'新增汇率', N'新增匯率', N'New FX rate', N'為替レート新規', N'환율 추가'),
  (N'erp.fxRate.dlg.editTitle', N'编辑汇率', N'編輯匯率', N'Edit FX rate', N'為替レート編集', N'환율 편집'),
  (N'erp.fxRate.msg.required', N'币种、适用日、汇率为必填', N'幣別、適用日、匯率為必填', N'Currency, date and rate are required', N'通貨・適用日・レートは必須です', N'통화, 적용일, 환율은 필수입니다'),
  (N'erp.fxRate.msg.created', N'汇率已创建', N'匯率已建立', N'Rate created', N'レートを作成しました', N'환율이 생성되었습니다'),
  (N'erp.fxRate.msg.updated', N'汇率已更新', N'匯率已更新', N'Rate updated', N'レートを更新しました', N'환율이 업데이트되었습니다'),
  (N'erp.fxRate.msg.deleted', N'汇率已删除', N'匯率已刪除', N'Rate deleted', N'レートを削除しました', N'환율이 삭제되었습니다'),
  (N'erp.fxRate.msg.failed', N'操作失败', N'操作失敗', N'Operation failed', N'操作に失敗しました', N'작업이 실패했습니다'),
  (N'erp.fxRate.msg.deleteConfirm', N'确定删除 {cur} @ {date} 的汇率？', N'確定刪除 {cur} @ {date} 的匯率？', N'Delete rate {cur} @ {date}?', N'{cur} @ {date} のレートを削除しますか？', N'{cur} @ {date} 환율을 삭제하시겠습니까?');

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
PRINT CONCAT('=== Gap 4.3 FX Rate i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
