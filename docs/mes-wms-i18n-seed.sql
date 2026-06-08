/* ============================================================
 * MES + WMS ナビゲーションメニュー i18n 词条 シードデータ
 * ============================================================
 * 対象テーブル：Sys_Langs
 *
 * 背景：
 *   - LayoutView.vue は `nav.{MenuId}` を i18n キーとしてメニュー名を翻訳。
 *   - 該当キーが Sys_Langs に無い場合は `Sys_Menus.MenuName`（単一言語、現状は日本語）に
 *     fallback するため、言語切替が効かなくなる。
 *   - 本スクリプトで MES (nav.300~312) + WMS (nav.400~483) を 5 言語（zh-CN / zh-TW / en / ja / ko）で補完。
 *
 * 冪等性：
 *   - MERGE 文。LangKey 一致時は 5 列を上書き、無ければ INSERT。
 *   - 何度実行しても安全。
 *
 * 実行：
 *   sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/mes-wms-i18n-seed.sql -b
 *
 * 反映：
 *   実行後、Lang API のキャッシュを失効させる必要がある。
 *     1) バックエンドを再起動（最も簡単）
 *     2) または /api/lang 管理画面で何か編集して保存（InvalidateLangCache が走る）
 * ============================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT '=== MES + WMS i18n シード開始 ===';

-- 一時テーブルに全行をロード → 一括 MERGE
IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN    nvarchar(500),
    ZhTW    nvarchar(500),
    En      nvarchar(500),
    Ja      nvarchar(500),
    Ko      nvarchar(500)
);

/* ─────────────────────────────────────────────────────
 * MES (MSBBME010 ~ 090 + Phase 4) — nav.300 ~ 312
 * ─────────────────────────────────────────────────── */
INSERT INTO #i18n VALUES
  (N'nav.300', N'制造执行(MES)',     N'製造執行(MES)',       N'Manufacturing (MES)',         N'製造執行(MES)',                N'제조실행(MES)'),
  (N'nav.301', N'生产计划看板',       N'生產計劃看板',         N'Planning Board',              N'生産計画ボード',                N'생산계획 보드'),
  (N'nav.302', N'制造指令 录入',      N'製造指令 錄入',         N'Work Order Entry',            N'製造指図 入力',                N'제조 지시 입력'),
  (N'nav.303', N'制造指令 一览',      N'製造指令 一覽',         N'Work Order List',             N'製造指図 一覧',                N'제조 지시 목록'),
  (N'nav.304', N'制造实绩 录入',      N'製造實績 錄入',         N'Production Result Entry',     N'製造実績 入力',                N'생산 실적 입력'),
  (N'nav.305', N'制造实绩 一览',      N'製造實績 一覽',         N'Production Result List',      N'製造実績 一覧',                N'생산 실적 목록'),
  (N'nav.306', N'质量检查 录入',      N'品質檢查 錄入',         N'Quality Inspection Entry',    N'品質検査 入力',                N'품질 검사 입력'),
  (N'nav.307', N'质量检查 一览',      N'品質檢查 一覽',         N'Quality Inspection List',     N'品質検査 一覧',                N'품질 검사 목록'),
  (N'nav.308', N'不良品管理',         N'不良品管理',            N'Defect Management',           N'不良品管理',                    N'불량품 관리'),
  (N'nav.309', N'MES 仪表盘',         N'MES 儀表盤',            N'MES Dashboard',               N'MESダッシュボード',            N'MES 대시보드'),
  (N'nav.310', N'设备管理',           N'設備管理',              N'Machine Management',          N'設備管理',                      N'설비 관리'),
  (N'nav.311', N'OEE 分析',           N'OEE 分析',              N'OEE Analysis',                N'OEE 分析',                      N'OEE 분석'),
  (N'nav.312', N'Control Tower 大屏', N'Control Tower 大屏',    N'Control Tower',               N'コントロールタワー',            N'컨트롤 타워');

/* ─────────────────────────────────────────────────────
 * WMS Core (MSBBWM010 ~ 090) — nav.400 ~ 416
 * ─────────────────────────────────────────────────── */
INSERT INTO #i18n VALUES
  (N'nav.400', N'仓库管理(WMS)',     N'倉庫管理(WMS)',         N'Warehouse (WMS)',             N'倉庫管理(WMS)',                N'창고관리(WMS)'),
  (N'nav.401', N'仓库主数据',         N'倉庫主資料',            N'Warehouse Master',            N'倉庫マスタ',                    N'창고 마스터'),
  (N'nav.402', N'库位管理',           N'庫位管理',              N'Location Management',         N'ロケーション管理',              N'로케이션 관리'),
  (N'nav.403', N'库存查询',           N'庫存查詢',              N'Stock Inquiry',               N'在庫照会',                      N'재고 조회'),
  (N'nav.404', N'入库预定 一览',      N'入庫預定 一覽',         N'Inbound Order List',          N'入庫予定 一覧',                N'입고 예정 목록'),
  (N'nav.405', N'入库预定 登记',      N'入庫預定 登記',         N'Inbound Order Entry',         N'入庫予定 登録',                N'입고 예정 등록'),
  (N'nav.406', N'入库实绩 录入',      N'入庫實績 錄入',         N'Inbound Receipt',             N'入庫実績 入力',                N'입고 실적 입력'),
  (N'nav.407', N'出库指令 一览',      N'出庫指令 一覽',         N'Outbound Order List',         N'出庫指示 一覧',                N'출고 지시 목록'),
  (N'nav.408', N'出库指令 登记',      N'出庫指令 登記',         N'Outbound Order Entry',        N'出庫指示 登録',                N'출고 지시 등록'),
  (N'nav.409', N'成品入库',           N'成品入庫',              N'Product Inbound',             N'製品入庫',                      N'제품 입고'),
  (N'nav.410', N'发货指令 一览',      N'發貨指令 一覽',         N'Shipping Order List',         N'出荷指示 一覧',                N'출하 지시 목록'),
  (N'nav.411', N'发货指令 登记',      N'發貨指令 登記',         N'Shipping Order Entry',        N'出荷指示 登録',                N'출하 지시 등록'),
  (N'nav.412', N'拣货作业',           N'揀貨作業',              N'Picking',                     N'ピッキング作業',                N'피킹 작업'),
  (N'nav.413', N'打包·发货确定',      N'打包·發貨確定',         N'Packaging & Shipping',        N'梱包・出荷確定',                N'포장·출하 확정'),
  (N'nav.414', N'盘点 一览',          N'盤點 一覽',             N'Stocktake List',              N'棚卸 一覧',                     N'재고 실사 목록'),
  (N'nav.415', N'盘点 作业',          N'盤點 作業',             N'Stocktake',                   N'棚卸 作業',                     N'재고 실사 작업'),
  (N'nav.416', N'WMS 仪表盘',         N'WMS 儀表盤',            N'WMS Dashboard',               N'WMSダッシュボード',            N'WMS 대시보드');

/* ─────────────────────────────────────────────────────
 * WMS 拡張 (Phase WM-5 ~ 7) — nav.420 ~ 428
 * ─────────────────────────────────────────────────── */
INSERT INTO #i18n VALUES
  (N'nav.420', N'WMS 扩展功能',       N'WMS 擴展功能',          N'WMS Advanced',                N'WMS 拡張機能',                  N'WMS 확장 기능'),
  (N'nav.421', N'入货检验(QC)',       N'入貨檢驗(QC)',          N'Inbound QC Inspection',       N'入荷検品(QC)',                 N'입하 검수(QC)'),
  (N'nav.422', N'货位优化',           N'貨位優化',              N'Slotting Optimization',       N'スロッティング最適化',          N'슬로팅 최적화'),
  (N'nav.423', N'补货指令',           N'補貨指令',              N'Replenishment',               N'補充指示',                      N'보충 지시'),
  (N'nav.424', N'越库直通',           N'越庫直通',              N'Cross-Docking',               N'クロスドッキング',              N'크로스 도킹'),
  (N'nav.425', N'套件组装',           N'套件組裝',              N'Kitting / Assembly',          N'キッティング・組立',            N'키팅·조립'),
  (N'nav.426', N'退货管理(RMA)',      N'退貨管理(RMA)',         N'Returns (RMA)',               N'返品管理(RMA)',                N'반품 관리(RMA)'),
  (N'nav.427', N'批次追溯·回收',      N'批次追溯·回收',         N'Lot Traceability / Recall',   N'ロット追溯・回収',              N'로트 추적·회수'),
  (N'nav.428', N'有效期管理(FEFO)',   N'有效期管理(FEFO)',      N'Expiry / FEFO',               N'賞味期限管理(FEFO)',           N'유통기한 관리(FEFO)');

/* ─────────────────────────────────────────────────────
 * WMS 業界特化（紙器包装業） — nav.440 ~ 447
 * ─────────────────────────────────────────────────── */
INSERT INTO #i18n VALUES
  (N'nav.440', N'行业特化(纸器)',     N'行業特化(紙器)',        N'Industry-Specific (Paper)',   N'業界特化(紙器)',                N'업종 특화(지기)'),
  (N'nav.441', N'原纸卷管理',         N'原紙捲管理',            N'Paper Roll Management',       N'原紙ロール管理',                N'원지 롤 관리'),
  (N'nav.442', N'残料·边角料管理',    N'殘料·邊角料管理',       N'Remnants & Scraps',           N'残材・端材管理',                N'잔재·자투리 관리'),
  (N'nav.443', N'印版·木型仓库',      N'印版·木型倉庫',         N'Plate/Mold Storage',          N'印版・木型倉庫',                N'인판·목형 창고'),
  (N'nav.444', N'油墨·胶水管理',      N'油墨·膠水管理',         N'Ink & Adhesive',              N'インキ・接着剤管理',            N'잉크·접착제 관리'),
  (N'nav.445', N'托盘管理',           N'棧板管理',              N'Pallet Management',           N'パレット管理',                  N'팔레트 관리'),
  (N'nav.446', N'客户寄存库存(VMI)',  N'客戶寄存庫存(VMI)',     N'Customer Consignment (VMI)',  N'客先預り在庫(VMI)',            N'고객 위탁 재고(VMI)'),
  (N'nav.447', N'试作·样品库存',      N'試作·樣品庫存',         N'Prototype & Samples',         N'試作・サンプル在庫',            N'시작·샘플 재고');

/* ─────────────────────────────────────────────────────
 * WMS 連携・モバイル — nav.460 ~ 464
 * ─────────────────────────────────────────────────── */
INSERT INTO #i18n VALUES
  (N'nav.460', N'对接·移动端',        N'對接·行動端',           N'Integration & Mobile',        N'連携・モバイル',                N'연동·모바일'),
  (N'nav.461', N'移动作业指令',       N'移動作業指令',          N'Mobile Task',                 N'モバイル作業指示',              N'모바일 작업 지시'),
  (N'nav.462', N'WCS/自动库连接',     N'WCS/自動倉連接',        N'WCS / AS-RS Integration',     N'WCS/自動倉庫連携',             N'WCS/자동창고 연동'),
  (N'nav.463', N'承运商对接',         N'承運商對接',            N'Carrier Integration',         N'配送業者連携',                  N'운송업체 연동'),
  (N'nav.464', N'IoT 温湿度监控',     N'IoT 溫濕度監控',        N'IoT Temp/Humidity',           N'IoT温湿度モニタ',              N'IoT 온습도 모니터');

/* ─────────────────────────────────────────────────────
 * WMS 帳票分析 — nav.480 ~ 483
 * ─────────────────────────────────────────────────── */
INSERT INTO #i18n VALUES
  (N'nav.480', N'报表分析',           N'報表分析',              N'Reports & Analytics',         N'帳票分析',                      N'장표 분석'),
  (N'nav.481', N'报表中心',           N'報表中心',              N'Report Center',               N'帳票センター',                  N'장표 센터'),
  (N'nav.482', N'接口健康监控',       N'介面健康監控',          N'Bridge Health',               N'連携ヘルス監視',                N'연계 상태 모니터'),
  (N'nav.483', N'库存滞留报表',       N'庫存滯留報表',          N'Stock Dwell Report',          N'在庫滞留レポート',              N'재고 체류 리포트');

/* ─────────────────────────────────────────────────────
 * MERGE：既存は更新、無ければ追加
 * ─────────────────────────────────────────────────── */
DECLARE @actionLog TABLE (act nvarchar(10));

MERGE Sys_Langs AS tgt
USING #i18n AS src
   ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET
    tgt.ZhCN = src.ZhCN,
    tgt.ZhTW = src.ZhTW,
    tgt.En   = src.En,
    tgt.Ja   = src.Ja,
    tgt.Ko   = src.Ko
WHEN NOT MATCHED BY TARGET THEN
    INSERT (LangKey, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @inserted INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @updated  INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT N'  追加: ' + CAST(@inserted AS nvarchar(10)) + N' 件 / 更新: ' + CAST(@updated AS nvarchar(10)) + N' 件';

DROP TABLE #i18n;

PRINT '=== MES + WMS i18n シード完了 ===';
PRINT '';
PRINT N'  確認SQL: SELECT LangKey, ZhCN, En, Ja FROM Sys_Langs WHERE LangKey LIKE ''nav.3%'' OR LangKey LIKE ''nav.4%'' ORDER BY LangKey;';
PRINT N'';
PRINT N'  ※ Cache-Aside で 1時間 キャッシュされる為、即時反映には API 再起動が必要。';
