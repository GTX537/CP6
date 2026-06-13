/* ============================================================================
   CP6 WMS 拡張モジュール デモデータ投入スクリプト（冪等）
   対象: キッティング / WCS自動倉庫 / IoTセンサー
   既存の倉庫(DW01-04)・ロケ(DEMO-*)・製品(K280-905-T 等)を参照する。
   前提: docs/demo-asahi-interview-v2.sql を先に実行済みであること。

   実行: sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB -E -i "docs/demo-wms-extensions.sql" -b
   冪等: プレフィックス(KIT-DEMO- / KO-DEMO- / WCS-DEMO- / IOT-DEMO-)で
         先に DELETE してから NEWID() で再投入する。
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRAN;

    /* ───────────────── 0) クリーンアップ（プレフィックス削除） ───────────────── */
    DELETE FROM T_StockTransaction WHERE TxnNo         LIKE 'TXN-DEMO-%';
    DELETE FROM T_IotSensorReading WHERE SensorId      LIKE 'IOT-DEMO-%';
    DELETE FROM T_IotSensor        WHERE SensorId      LIKE 'IOT-DEMO-%';
    DELETE FROM T_WcsTask          WHERE TaskNo        LIKE 'WCS-DEMO-%';
    DELETE FROM T_KitOrder         WHERE KitOrderNo    LIKE 'KO-DEMO-%';
    DELETE FROM T_KitMasterComponent WHERE KitSku      LIKE 'KIT-DEMO-%';
    DELETE FROM T_KitMaster        WHERE KitSku        LIKE 'KIT-DEMO-%';

    /* ============================================================
       1) キッティング: キットマスタ + 構成品 + キット指示
       ============================================================ */

    /* 1-1) キットマスタ（ギフトBOXセット） */
    INSERT INTO T_KitMaster
        (Id, KitSku, KitName, DefaultWarehouseCd, Remarks, ActiveFlg, Creator, CreateDate, IsDeleted)
    VALUES
        (NEWID(), 'KIT-DEMO-GIFT01', 'アサヒ ギフトBOX 6本セット', 'DW03',
         'デモ用キット: 牛乳パック6本＋化粧箱', 1, 'demo', GETDATE(), 0),
        (NEWID(), 'KIT-DEMO-SAMPLE', 'サンプル配布キット', 'DW03',
         'デモ用キット: 製品サンプル詰め合わせ', 1, 'demo', GETDATE(), 0);

    /* 1-2) 構成品（既存製品を参照） */
    INSERT INTO T_KitMasterComponent
        (Id, KitSku, [LineNo], ComponentProductCd, ComponentName, RequiredQty, UnitCd, Remarks, Creator, CreateDate, IsDeleted)
    VALUES
        (NEWID(), 'KIT-DEMO-GIFT01', 1, 'P-MILK-1L-6C', '牛乳1L 6本パック', 1, 'CS',  '主構成品',  'demo', GETDATE(), 0),
        (NEWID(), 'KIT-DEMO-GIFT01', 2, 'K280-905-T',   '化粧箱（板紙）',     1, 'PCS', '外装箱',    'demo', GETDATE(), 0),
        (NEWID(), 'KIT-DEMO-SAMPLE', 1, 'P-MILK-1L-6C', '牛乳1L 6本パック', 2, 'CS',  '配布サンプル', 'demo', GETDATE(), 0),
        (NEWID(), 'KIT-DEMO-SAMPLE', 2, 'K280-905-T',   '化粧箱（板紙）',     2, 'PCS', '梱包材',    'demo', GETDATE(), 0);

    /* 1-3) キット指示（status 0=未実行 / 1=実行済 / 9=取消） */
    INSERT INTO T_KitOrder
        (Id, KitOrderNo, KitSku, KitName, Qty, Direction, WarehouseCd, KitLocationCd, KitLotNo,
         Status, OperatorCd, Remarks, ExecutedTxnNos, ExecutedAt, Creator, CreateDate, IsDeleted)
    VALUES
        (NEWID(), 'KO-DEMO-0001', 'KIT-DEMO-GIFT01', 'アサヒ ギフトBOX 6本セット', 10, 'ASSEMBLE',
         'DW03', 'DEMO-FG-C-12', 'LOT-DEMO-FG01', 0, 'demo', '出荷前組立て予定', NULL, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'KO-DEMO-0002', 'KIT-DEMO-GIFT01', 'アサヒ ギフトBOX 6本セット', 5,  'ASSEMBLE',
         'DW03', 'DEMO-FG-C-12', 'LOT-DEMO-FG01', 1, 'demo', '実行済デモ', 'KIT-EXEC-DEMO-0001', DATEADD(HOUR, -3, GETDATE()), 'demo', GETDATE(), 0),
        (NEWID(), 'KO-DEMO-0003', 'KIT-DEMO-SAMPLE', 'サンプル配布キット', 3,  'DISASSEMBLE',
         'DW03', 'DEMO-FG-C-12', NULL, 9, 'demo', '取消デモ', NULL, NULL, 'demo', GETDATE(), 0);

    /* ============================================================
       2) WCS 自動倉庫タスク（status 0=新規 1=指示済 2=実行中 3=完了 9=異常）
       ============================================================ */
    INSERT INTO T_WcsTask
        (Id, TaskNo, TaskType, Priority, Status, DeviceCd, RelatedNo, RelatedType,
         FromWarehouseCd, FromLocationCd, ToWarehouseCd, ToLocationCd, ProductCd, LotNo, Qty, UnitCd,
         CreatedAt, DispatchedAt, StartedAt, CompletedAt, ErrorMessage, Remarks, Creator, CreateDate, IsDeleted)
    VALUES
        /* 新規（未指示） */
        (NEWID(), 'WCS-DEMO-0001', 'MOVE', 2, 0, 'AGV-01', NULL, NULL,
         'DW01', 'DEMO-RAW-A-01', 'DW02', 'DEMO-WIP-B-03', 'INK-PANTONE-485C', 'LOT-DEMO-INK01', 20, 'KG',
         GETDATE(), NULL, NULL, NULL, NULL, '原材料→半製品エリア搬送', 'demo', GETDATE(), 0),
        /* 指示済 */
        (NEWID(), 'WCS-DEMO-0002', 'PICK', 1, 1, 'STK-CRANE-01', 'SO-DEMO-0001', 'OUTBOUND',
         'DW03', 'DEMO-FG-C-12', NULL, NULL, 'P-MILK-1L-6C', 'LOT-DEMO-FG01', 30, 'CS',
         GETDATE(), DATEADD(MINUTE, -20, GETDATE()), NULL, NULL, NULL, '出荷ピッキング指示済', 'demo', GETDATE(), 0),
        /* 実行中 */
        (NEWID(), 'WCS-DEMO-0003', 'PUT', 2, 2, 'AGV-02', 'IN-DEMO-0001', 'INBOUND',
         NULL, NULL, 'DW01', 'DEMO-RAW-A-01', 'GLUE-A', 'LOT-DEMO-GLUE01', 50, 'KG',
         GETDATE(), DATEADD(MINUTE, -15, GETDATE()), DATEADD(MINUTE, -10, GETDATE()), NULL, NULL, '入庫格納実行中', 'demo', GETDATE(), 0),
        /* 完了 */
        (NEWID(), 'WCS-DEMO-0004', 'COUNT', 3, 3, 'STK-CRANE-01', NULL, 'STOCKTAKE',
         'DW03', 'DEMO-FG-C-12', NULL, NULL, 'P-MILK-1L-6C', 'LOT-DEMO-FG01', 100, 'CS',
         DATEADD(HOUR, -2, GETDATE()), DATEADD(HOUR, -2, GETDATE()), DATEADD(MINUTE, -100, GETDATE()), DATEADD(MINUTE, -90, GETDATE()), NULL, '棚卸カウント完了', 'demo', GETDATE(), 0),
        /* 異常 */
        (NEWID(), 'WCS-DEMO-0005', 'MOVE', 1, 9, 'AGV-01', NULL, NULL,
         'DW02', 'DEMO-WIP-B-03', 'DW04', 'DEMO-NG-D-05', 'K280-905-T', 'LOT-DEMO-WIP01', 15, 'PCS',
         DATEADD(HOUR, -1, GETDATE()), DATEADD(MINUTE, -55, GETDATE()), DATEADD(MINUTE, -50, GETDATE()), NULL, 'E-TIMEOUT: 搬送先ロケ満杯', '異常停止デモ', 'demo', GETDATE(), 0);

    /* ============================================================
       3) IoT センサー + 計測値（TEMP/HUMID、閾値超過アラート含む）
       ============================================================ */
    INSERT INTO T_IotSensor
        (Id, SensorId, SensorType, SensorName, WarehouseCd, LocationCd, Unit,
         MinThreshold, MaxThreshold, IsEnabled, LastValue, LastReadAt, Remarks, Creator, CreateDate, IsDeleted)
    VALUES
        (NEWID(), 'IOT-DEMO-TEMP01', 'TEMP',  '完成品倉庫 温度', 'DW03', 'DEMO-FG-C-12', '℃',
         2,  10, 1, 6.4,  GETDATE(), '冷蔵帯 2〜10℃ 監視', 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-HUMID1', 'HUMID', '原材料倉庫 湿度', 'DW01', 'DEMO-RAW-A-01', '%',
         30, 60, 1, 72.0, GETDATE(), '板紙吸湿防止 30〜60% 監視', 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-TEMP02', 'TEMP',  '半製品倉庫 温度', 'DW02', 'DEMO-WIP-B-03', '℃',
         5,  25, 1, 18.2, GETDATE(), '常温帯 監視', 'demo', GETDATE(), 0);

    /* 計測値（過去6点。HUMID1 の最新は閾値超過アラート） */
    INSERT INTO T_IotSensorReading
        (Id, SensorId, ReadAt, Value, IsAlert, AlertMessage, Creator, CreateDate, IsDeleted)
    VALUES
        /* TEMP01: 正常推移 */
        (NEWID(), 'IOT-DEMO-TEMP01', DATEADD(MINUTE, -50, GETDATE()), 5.8, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-TEMP01', DATEADD(MINUTE, -40, GETDATE()), 6.0, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-TEMP01', DATEADD(MINUTE, -30, GETDATE()), 6.1, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-TEMP01', DATEADD(MINUTE, -20, GETDATE()), 6.3, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-TEMP01', DATEADD(MINUTE, -10, GETDATE()), 6.4, 0, NULL, 'demo', GETDATE(), 0),
        /* HUMID1: 上昇 → 最新で上限60%超過アラート */
        (NEWID(), 'IOT-DEMO-HUMID1', DATEADD(MINUTE, -50, GETDATE()), 55.0, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-HUMID1', DATEADD(MINUTE, -40, GETDATE()), 58.0, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-HUMID1', DATEADD(MINUTE, -30, GETDATE()), 62.0, 1, '湿度が上限(60%)を超過しました', 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-HUMID1', DATEADD(MINUTE, -20, GETDATE()), 68.0, 1, '湿度が上限(60%)を超過しました', 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-HUMID1', DATEADD(MINUTE, -10, GETDATE()), 72.0, 1, '湿度が上限(60%)を超過しました', 'demo', GETDATE(), 0),
        /* TEMP02: 正常 */
        (NEWID(), 'IOT-DEMO-TEMP02', DATEADD(MINUTE, -30, GETDATE()), 17.5, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-TEMP02', DATEADD(MINUTE, -20, GETDATE()), 18.0, 0, NULL, 'demo', GETDATE(), 0),
        (NEWID(), 'IOT-DEMO-TEMP02', DATEADD(MINUTE, -10, GETDATE()), 18.2, 0, NULL, 'demo', GETDATE(), 0);

    /* ============================================================
       4) 在庫トランザクション（帳票センターの真実源）
          IN=受入 / OUT=出荷。GETDATE() 基準の相対日付で
          「当月」「直近90日」レポートに必ず乗るようにする。
       ============================================================ */
    INSERT INTO T_StockTransaction
        (Id, TxnNo, TxnType, TxnDateTime, WarehouseCd, LocationCd, ProductCd, LotNo,
         Qty, UnitCd, UnitPrice, RelatedNo, RelatedType, OperatorCd, Remark, Creator, CreateDate, IsDeleted)
    VALUES
        /* ── IN（受入） ── */
        (NEWID(), 'TXN-DEMO-IN-001', 'IN', DATEADD(DAY, -25, GETDATE()), 'DW01', 'DEMO-RAW-A-01', 'K280-905-T',       'LOT-DEMO-RAW01',  500,  'PCS', 80,    'IN-DEMO-0001', 'INBOUND', 'demo', '板紙受入', 'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-IN-002', 'IN', DATEADD(DAY, -24, GETDATE()), 'DW01', 'DEMO-RAW-A-01', 'INK-PANTONE-485C', 'LOT-DEMO-INK01',  200,  'KG',  1200,  'IN-DEMO-0002', 'INBOUND', 'demo', 'インク受入', 'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-IN-003', 'IN', DATEADD(DAY, -23, GETDATE()), 'DW01', 'DEMO-RAW-A-01', 'GLUE-A',           'LOT-DEMO-GLUE01', 300,  'KG',  350,   'IN-DEMO-0003', 'INBOUND', 'demo', '糊受入',   'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-IN-004', 'IN', DATEADD(DAY, -20, GETDATE()), 'DW03', 'DEMO-FG-C-12',  'P-MILK-1L-6C',     'LOT-DEMO-FG01',   1000, 'CS',  600,   'IN-DEMO-0004', 'INBOUND', 'demo', '完成品入庫', 'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-IN-005', 'IN', DATEADD(DAY, -6,  GETDATE()), 'DW03', 'DEMO-FG-C-12',  'P-MILK-1L-6C',     'LOT-DEMO-FG02',   600,  'CS',  600,   'IN-DEMO-0005', 'INBOUND', 'demo', '完成品追加入庫', 'demo', GETDATE(), 0),
        /* ── OUT（出荷） ── */
        (NEWID(), 'TXN-DEMO-OUT-001', 'OUT', DATEADD(DAY, -15, GETDATE()), 'DW03', 'DEMO-FG-C-12', 'P-MILK-1L-6C',     'LOT-DEMO-FG01',  400, 'CS',  600,  'SO-DEMO-0001', 'OUTBOUND', 'demo', '出荷', 'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-OUT-002', 'OUT', DATEADD(DAY, -10, GETDATE()), 'DW03', 'DEMO-FG-C-12', 'P-MILK-1L-6C',     'LOT-DEMO-FG01',  300, 'CS',  600,  'SO-DEMO-0002', 'OUTBOUND', 'demo', '出荷', 'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-OUT-003', 'OUT', DATEADD(DAY, -8,  GETDATE()), 'DW01', 'DEMO-RAW-A-01', 'K280-905-T',      'LOT-DEMO-RAW01', 150, 'PCS', 80,   NULL,           'OUTBOUND', 'demo', '製造払出', 'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-OUT-004', 'OUT', DATEADD(DAY, -5,  GETDATE()), 'DW01', 'DEMO-RAW-A-01', 'INK-PANTONE-485C','LOT-DEMO-INK01', 50,  'KG',  1200, NULL,           'OUTBOUND', 'demo', '製造払出', 'demo', GETDATE(), 0),
        (NEWID(), 'TXN-DEMO-OUT-005', 'OUT', DATEADD(DAY, -2,  GETDATE()), 'DW03', 'DEMO-FG-C-12', 'P-MILK-1L-6C',     'LOT-DEMO-FG02',  100, 'CS',  600,  'SO-DEMO-0003', 'OUTBOUND', 'demo', '出荷', 'demo', GETDATE(), 0);

    COMMIT;

    /* ───────────────── サマリ出力 ───────────────── */
    SELECT 'T_KitMaster'          AS TableName, COUNT(*) AS Rows FROM T_KitMaster          WHERE KitSku     LIKE 'KIT-DEMO-%'
    UNION ALL SELECT 'T_KitMasterComponent', COUNT(*) FROM T_KitMasterComponent WHERE KitSku  LIKE 'KIT-DEMO-%'
    UNION ALL SELECT 'T_KitOrder',           COUNT(*) FROM T_KitOrder           WHERE KitOrderNo LIKE 'KO-DEMO-%'
    UNION ALL SELECT 'T_WcsTask',            COUNT(*) FROM T_WcsTask            WHERE TaskNo   LIKE 'WCS-DEMO-%'
    UNION ALL SELECT 'T_IotSensor',          COUNT(*) FROM T_IotSensor          WHERE SensorId LIKE 'IOT-DEMO-%'
    UNION ALL SELECT 'T_IotSensorReading',   COUNT(*) FROM T_IotSensorReading   WHERE SensorId LIKE 'IOT-DEMO-%'
    UNION ALL SELECT 'T_StockTransaction',   COUNT(*) FROM T_StockTransaction   WHERE TxnNo    LIKE 'TXN-DEMO-%';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
