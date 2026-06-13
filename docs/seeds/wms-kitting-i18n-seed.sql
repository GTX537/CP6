/* ============================================================
 * WMS MSBBWM140 キッティング i18n シードデータ
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WMS Kitting i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  (N'wms.kit.title',                N'套件主数据',          N'套件主資料',          N'Kit Master',          N'キット品マスタ',      N'키트 마스터'),
  (N'wms.kit.titleNew',             N'套件主数据 新建',     N'套件主資料 新增',     N'New Kit Master',      N'キット品マスタ 登録', N'키트 마스터 신규'),
  (N'wms.kit.orderTitle',           N'套件组装/拆解 指示',  N'套件組裝/拆解 指示',  N'Kit Order',           N'キット組立/バラシ 指示', N'키트 조립/분해 지시'),
  (N'wms.kit.orderTitleNew',        N'套件指示 新建',       N'套件指示 新增',       N'New Kit Order',       N'キット指示 登録',     N'키트 지시 신규'),
  (N'wms.kit.tab.master',           N'套件主数据',          N'套件主資料',          N'Kit Master',          N'キット品マスタ',      N'키트 마스터'),
  (N'wms.kit.tab.order',            N'组装指示',            N'組裝指示',            N'Kit Order',           N'組立指示',            N'조립 지시'),
  (N'wms.kit.fld.kitSku',           N'套件SKU',             N'套件SKU',             N'Kit SKU',             N'キットSKU',           N'키트SKU'),
  (N'wms.kit.fld.kitName',          N'套件名',              N'套件名',              N'Kit Name',            N'キット名',            N'키트명'),
  (N'wms.kit.fld.defaultWh',        N'默认仓库',            N'預設倉庫',            N'Default Warehouse',   N'既定保管倉庫',        N'기본 보관 창고'),
  (N'wms.kit.fld.active',           N'有效',                N'有效',                N'Active',              N'有効',                N'활성'),
  (N'wms.kit.fld.orderNo',          N'指示NO',              N'指示NO',              N'Order No',            N'指示NO',              N'지시NO'),
  (N'wms.kit.fld.direction',        N'方向',                N'方向',                N'Direction',           N'方向',                N'방향'),
  (N'wms.kit.fld.kitLoc',           N'套件货位',            N'套件庫位',            N'Kit Location',        N'キットロケ',          N'키트 로케'),
  (N'wms.kit.fld.kitLot',           N'套件批次',            N'套件批次',            N'Kit Lot',             N'キットロット',        N'키트 로트'),
  (N'wms.kit.fld.executedAt',       N'执行日时',            N'執行日時',            N'Executed At',         N'実行日時',            N'실행 일시'),
  (N'wms.kit.dir.assemble',         N'组装 (部品→套件)',    N'組裝 (部品→套件)',    N'Assemble',            N'組立 (部品→キット)',  N'조립 (부품→키트)'),
  (N'wms.kit.dir.disassemble',      N'拆解 (套件→部品)',    N'拆解 (套件→部品)',    N'Disassemble',         N'バラシ (キット→部品)', N'분해 (키트→부품)'),
  (N'wms.kit.status.draft',         N'草稿',                N'草稿',                N'Draft',               N'下書き',              N'초안'),
  (N'wms.kit.status.executed',      N'已执行',              N'已執行',              N'Executed',            N'実行済',              N'실행됨'),
  (N'wms.kit.status.cancelled',     N'已取消',              N'已取消',              N'Cancelled',           N'取消',                N'취소됨'),
  (N'wms.kit.bom.title',            N'BOM 构成 (部品)',     N'BOM 構成 (部品)',     N'BOM Components',      N'構成部品 (BOM)',      N'구성 부품 (BOM)'),
  (N'wms.kit.bom.componentCd',      N'部品CD',              N'部品CD',              N'Component CD',        N'部品CD',              N'부품CD'),
  (N'wms.kit.bom.componentName',    N'部品名',              N'部品名',              N'Component Name',      N'部品名',              N'부품명'),
  (N'wms.kit.bom.requiredQty',      N'每套件需要量',        N'每套件需要量',        N'Required Qty per Kit', N'1キット必要量',      N'1키트당 필요량'),
  (N'wms.kit.btn.execute',          N'执行',                N'執行',                N'Execute',             N'実行',                N'실행'),
  (N'wms.kit.msg.executeAsk',       N'要执行套件指示吗？库存将被实际变动', N'要執行套件指示嗎？庫存將被實際變動', N'Execute kit order? Stock will be applied.', N'キット指示を実行しますか？在庫が変動します', N'키트 지시를 실행하시겠습니까? 재고가 변동됩니다'),
  (N'wms.kit.msg.kitLotAutoGen',    N'空时自动生成 KIT-YYYYMMDD-XXXX', N'空時自動生成 KIT-YYYYMMDD-XXXX', N'(empty=auto KIT-YYYYMMDD-XXXX)', N'空欄=自動 KIT-YYYYMMDD-XXXX', N'(비어 있음=자동 KIT-YYYYMMDD-XXXX)'),
  (N'wms.kit.msg.kitLotRequiredDisassemble', N'拆解必填：要拆解的批次NO', N'拆解必填：要拆解的批次NO', N'Required: lot to disassemble', N'バラシ時必須：取出すロットNO', N'분해 시 필수: 분해할 로트NO'),
  (N'wms.kit.msg.txnCount',         N'已发出 {n} 笔库存事务', N'已發出 {n} 筆庫存事務', N'{n} stock transactions issued', N'{n} 件の在庫トランザクション発行', N'{n}건의 재고 트랜잭션 발행');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET tgt.ZhCN=src.ZhCN, tgt.ZhTW=src.ZhTW, tgt.En=src.En, tgt.Ja=src.Ja, tgt.Ko=src.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT (LangKey,ZhCN,ZhTW,En,Ja,Ko) VALUES (src.LangKey,src.ZhCN,src.ZhTW,src.En,src.Ja,src.Ko)
OUTPUT $action INTO @actionLog;
DECLARE @ins INT=(SELECT COUNT(*) FROM @actionLog WHERE act='INSERT');
DECLARE @upd INT=(SELECT COUNT(*) FROM @actionLog WHERE act='UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' / 更新: ' + CAST(@upd AS nvarchar(10));
DROP TABLE #i18n;
PRINT '=== Done ===';
