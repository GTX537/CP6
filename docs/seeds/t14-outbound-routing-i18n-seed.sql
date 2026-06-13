/* ============================================================
 * T14 / Gap 4.2 — Outbound multi-warehouse routing frontend i18n
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== T14 Outbound Routing i18n seed start ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'wms.outboundRouting.title', N'出库路由规则', N'出庫路由規則', N'Outbound routing rules', N'出庫ルーティングルール', N'출고 라우팅 규칙'),
  (N'wms.outboundRouting.subtitle', N'按客户/品类配置优先出库仓库（规则→仓库优先级→FEFO）', N'依客戶/品類設定優先出庫倉庫（規則→倉庫優先級→FEFO）', N'Preferred warehouse by customer/category (rule → priority → FEFO)', N'得意先/品類別の優先出庫倉庫（ルール→倉庫優先度→FEFO）', N'고객/품목별 우선 출고 창고 (규칙→우선순위→FEFO)'),
  (N'wms.outboundRouting.any', N'任意', N'任意', N'Any', N'すべて', N'전체'),
  (N'wms.outboundRouting.on', N'启用', N'啟用', N'On', N'有効', N'사용'),
  (N'wms.outboundRouting.off', N'停用', N'停用', N'Off', N'無効', N'미사용'),
  (N'wms.outboundRouting.type.1', N'材料出库', N'材料出庫', N'Material', N'材料出庫', N'자재 출고'),
  (N'wms.outboundRouting.type.2', N'出货', N'出貨', N'Shipping', N'出荷', N'출하'),
  (N'wms.outboundRouting.type.3', N'内部调拨', N'內部調撥', N'Transfer', N'社内振替', N'내부 이전'),
  (N'wms.outboundRouting.col.sortOrder', N'顺序', N'順序', N'Order', N'評価順', N'순서'),
  (N'wms.outboundRouting.col.ruleName', N'规则名称', N'規則名稱', N'Rule name', N'ルール名', N'규칙 이름'),
  (N'wms.outboundRouting.col.customerCd', N'客户CD', N'客戶CD', N'Customer', N'得意先CD', N'고객 코드'),
  (N'wms.outboundRouting.col.productPrefix', N'产品前缀', N'產品前綴', N'Product prefix', N'製品CD接頭辞', N'제품 접두사'),
  (N'wms.outboundRouting.col.outboundType', N'出库区分', N'出庫區分', N'Outbound type', N'出庫区分', N'출고 구분'),
  (N'wms.outboundRouting.col.target', N'目标仓库', N'目標倉庫', N'Target WH', N'引当先倉庫', N'대상 창고'),
  (N'wms.outboundRouting.col.enabled', N'状态', N'狀態', N'Enabled', N'有効', N'상태'),
  (N'wms.outboundRouting.col.remarks', N'备注', N'備註', N'Remark', N'備考', N'비고'),
  (N'wms.outboundRouting.col.action', N'操作', N'操作', N'Action', N'操作', N'작업'),
  (N'wms.outboundRouting.btn.create', N'新增规则', N'新增規則', N'New rule', N'新規ルール', N'규칙 추가'),
  (N'wms.outboundRouting.btn.refresh', N'刷新', N'重新整理', N'Refresh', N'更新', N'새로고침'),
  (N'wms.outboundRouting.btn.edit', N'编辑', N'編輯', N'Edit', N'編集', N'편집'),
  (N'wms.outboundRouting.btn.delete', N'删除', N'刪除', N'Delete', N'削除', N'삭제'),
  (N'wms.outboundRouting.btn.cancel', N'取消', N'取消', N'Cancel', N'キャンセル', N'취소'),
  (N'wms.outboundRouting.btn.confirm', N'确定', N'確定', N'Confirm', N'確定', N'확인'),
  (N'wms.outboundRouting.hint.sortOrder', N'数值越小越优先', N'數值越小越優先', N'Lower runs first', N'小さいほど優先', N'작을수록 우선'),
  (N'wms.outboundRouting.dlg.createTitle', N'新增路由规则', N'新增路由規則', N'New routing rule', N'ルーティングルール新規', N'라우팅 규칙 추가'),
  (N'wms.outboundRouting.dlg.editTitle', N'编辑路由规则', N'編輯路由規則', N'Edit routing rule', N'ルーティングルール編集', N'라우팅 규칙 편집'),
  (N'wms.outboundRouting.msg.required', N'规则名称与目标仓库为必填', N'規則名稱與目標倉庫為必填', N'Rule name and target warehouse are required', N'ルール名と引当先倉庫は必須です', N'규칙 이름과 대상 창고는 필수입니다'),
  (N'wms.outboundRouting.msg.created', N'规则已创建', N'規則已建立', N'Rule created', N'ルールを作成しました', N'규칙이 생성되었습니다'),
  (N'wms.outboundRouting.msg.updated', N'规则已更新', N'規則已更新', N'Rule updated', N'ルールを更新しました', N'규칙이 업데이트되었습니다'),
  (N'wms.outboundRouting.msg.deleted', N'规则已删除', N'規則已刪除', N'Rule deleted', N'ルールを削除しました', N'규칙이 삭제되었습니다'),
  (N'wms.outboundRouting.msg.deleteConfirm', N'确定删除规则「{name}」？', N'確定刪除規則「{name}」？', N'Delete rule "{name}"?', N'ルール「{name}」を削除しますか？', N'규칙 "{name}"을(를) 삭제하시겠습니까?'),
  (N'wms.outboundRouting.preview.title', N'候选仓库预览', N'候選倉庫預覽', N'Candidate warehouse preview', N'候補倉庫プレビュー', N'후보 창고 미리보기'),
  (N'wms.outboundRouting.preview.productCd', N'产品CD', N'產品CD', N'Product', N'製品CD', N'제품 코드'),
  (N'wms.outboundRouting.preview.customerCd', N'客户CD', N'客戶CD', N'Customer', N'得意先CD', N'고객 코드'),
  (N'wms.outboundRouting.preview.outboundType', N'出库区分', N'出庫區分', N'Outbound type', N'出庫区分', N'출고 구분'),
  (N'wms.outboundRouting.preview.fallback', N'兜底仓库', N'兜底倉庫', N'Fallback WH', N'フォールバック倉庫', N'대체 창고'),
  (N'wms.outboundRouting.preview.btn', N'预览', N'預覽', N'Preview', N'プレビュー', N'미리보기'),
  (N'wms.outboundRouting.preview.order', N'引当顺序', N'引當順序', N'Allocation order', N'引当順', N'할당 순서'),
  (N'wms.outboundRouting.preview.needProduct', N'请输入产品CD', N'請輸入產品CD', N'Enter product code', N'製品CDを入力してください', N'제품 코드를 입력하세요');

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
PRINT CONCAT('=== T14 Outbound Routing i18n complete: INSERT=', @ins, ' UPDATE=', @upd, ' ===');

DROP TABLE #i18n;
