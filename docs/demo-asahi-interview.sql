/* =====================================================================
 * 面接デモ用：朝日飲料 100,000 枚 牛乳 1L カートン 全流程データ
 *   - 既存顧客 BP001 を「朝日飲料株式会社」に流用
 *   - 全 demo データは NO に "-DEMO-" を含む → 一括クリーンアップ可能
 *   - スクリプト冒頭で旧 demo データを削除 → 再実行可能
 *
 *   実行: sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -i demo-asahi-interview.sql -b
 * ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRAN;
  PRINT '════════════════════════════════════════════════════════════';
  PRINT '  朝日飲料デモデータ 投入開始';
  PRINT '════════════════════════════════════════════════════════════';

  /* ───────── 0. 旧 demo データ クリーンアップ ───────── */
  PRINT '[0] 旧 demo データ削除';
  DELETE FROM T_StockTransaction      WHERE TxnNo        LIKE 'TXN-DEMO-%';
  DELETE FROM T_ShippingPackage       WHERE PackageNo    LIKE 'PKG-DEMO-%';
  DELETE FROM T_InboundReceiptDetail  WHERE ReceiptNo    LIKE 'RCP-DEMO-%';
  DELETE FROM T_InboundReceipt        WHERE ReceiptNo    LIKE 'RCP-DEMO-%';
  DELETE FROM T_OutboundOrderDetail   WHERE OutboundNo   LIKE 'OUT-DEMO-%';
  DELETE FROM T_OutboundOrder         WHERE OutboundNo   LIKE 'OUT-DEMO-%';
  DELETE FROM T_QcInspectionItem      WHERE InspectionNo LIKE 'QC-DEMO-%';
  DELETE FROM T_QcInspection          WHERE InspectionNo LIKE 'QC-DEMO-%';
  DELETE FROM T_WorkOrderProcess      WHERE WorkOrderNo  LIKE 'WO-DEMO-%';
  DELETE FROM T_WorkOrder             WHERE WorkOrderNo  LIKE 'WO-DEMO-%';
  DELETE FROM T_PaperRoll             WHERE RollNo       LIKE 'ROLL-D%';
  DELETE FROM T_Stock                 WHERE LotNo        LIKE 'LOT-DEMO-%';
  DELETE FROM T_Location              WHERE LocationCd   LIKE 'DEMO-%';
  DELETE FROM T_Warehouse             WHERE WarehouseCd  IN ('DW01','DW02','DW03','DW04');

  /* ───────── 1. 倉庫 4 件（原材料/半製品/完成品/不良品）───────── */
  PRINT '[1] 倉庫マスタ';
  INSERT INTO T_Warehouse (Id, WarehouseCd, WarehouseName, WarehouseType, AllowNegative, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'DW01', N'デモ原材料倉庫',  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DW02', N'デモ半製品倉庫',  2, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DW03', N'デモ完成品倉庫',  3, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DW04', N'デモ不良品倉庫',  4, 0, 0, GETDATE(), 'demo');

  /* ───────── 2. ロケーション 4 件 ───────── */
  PRINT '[2] ロケーションマスタ';
  INSERT INTO T_Location (Id, LocationCd, WarehouseCd, LocationLevel, LocationName, CapacityQty, IsPickable, IsBlocked, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'DEMO-RAW-A-01', 'DW01', 1, N'原紙ラック A-01', 5000.0,  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DEMO-WIP-B-03', 'DW02', 1, N'半製品 B-03',     2000.0,  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DEMO-FG-C-12',  'DW03', 1, N'完成品 C-12',   200000.0,  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DEMO-NG-D-05',  'DW04', 1, N'不良品 D-05',    10000.0,  1, 0, 0, GETDATE(), 'demo');

  /* ───────── 3. 原紙ロール（K280 905mm 1500m）───────── */
  PRINT '[3] 原紙ロール';
  INSERT INTO T_PaperRoll
    (Id, RollNo, PaperGrade, WidthMm, BasisWeight, GrainDirection,
     OriginalLengthM, RemainingLengthM, CoreDiameterInch,
     MfgDate, MfgLotNo, SupplierRollNo,
     LocationCd, WarehouseCd, Status, DisposeThresholdM, Remarks,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'ROLL-D-20260515-003', 'K280', 905, 280.00, 'T',
     1500.00000000, 1500.00000000, 3.00,
     '2026-05-10', 'OJI-A-20260510', 'OJI-345678',
     'DEMO-RAW-A-01', 'DW01', 0, 50.00000000, N'デモ用初期在庫 1500m',
     0, GETDATE(), 'demo');

  /* ───────── 4. 在庫実況 (初期在庫：原紙のみ。後で OUT/IN で更新)───────── */
  PRINT '[4] 初期在庫（原紙のみ）';
  -- 原紙在庫は PaperRoll とは別に Stock 行も用意（出庫対象）
  INSERT INTO T_Stock
    (Id, WarehouseCd, LocationCd, ProductCd, LotNo,
     PhysicalQty, AllocatedQty, AvailableQty, UnitCd, ReceiveDate,
     RecallFlag, OwnerType, PaperRollNo,
     IsDeleted, CreateDate, Creator)
  VALUES
    -- 原紙在庫 1500m
    (NEWID(), 'DW01', 'DEMO-RAW-A-01', 'K280-905-T', 'LOT-DEMO-PAPER-001',
     1500.00000000, 0, 1500.00000000, 'M', '2026-05-15',
     0, 'SELF', 'ROLL-D-20260515-003',
     0, GETDATE(), 'demo'),
    -- インキ在庫 10kg
    (NEWID(), 'DW01', 'DEMO-RAW-A-01', 'INK-PANTONE-485C', 'LOT-DEMO-INK-001',
     10.0000, 0, 10.0000, 'KG', '2026-05-10',
     0, 'SELF', NULL,
     0, GETDATE(), 'demo'),
    -- 接着剤在庫 5kg
    (NEWID(), 'DW01', 'DEMO-RAW-A-01', 'GLUE-A', 'LOT-DEMO-GLUE-001',
     5.0000, 0, 5.0000, 'KG', '2026-05-12',
     0, 'SELF', NULL,
     0, GETDATE(), 'demo');

  /* ───────── 5. MES 製造指図 WO-DEMO-20260525-001 ───────── */
  PRINT '[5] MES 製造指図 + 5 工程';
  INSERT INTO T_WorkOrder
    (Id, WorkOrderNo, Status,
     WebOrderNo, CustomerCd,
     ProductCd, ProductName,
     ProductionQty, CompletedQty, DefectQty,
     DeliveryDate, PlanStartDate, PlanEndDate,
     ActualStartDate, ActualEndDate,
     Priority, LotNo, Remarks,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'WO-DEMO-20260525-001', 9,           -- 9=完成
     NULL, 'BP001',
     'P-MILK-1L-6C', N'牛乳1Lカートン 6色印刷',
     100000.00, 100200.00, 540.00,
     '2026-06-15', '2026-05-28 08:00:00', '2026-06-10 17:00:00',
     '2026-05-28 08:00:00', '2026-06-10 12:00:00',
     2, 'LOT-DEMO-WO001', N'デモ：朝日 100,000 枚 歩留り98.2%',
     0, GETDATE(), 'demo');

  -- 5 工程
  INSERT INTO T_WorkOrderProcess
    (Id, WorkOrderNo, ProcessCd, TaskCd, SortOrder, ProcessStatus,
     GoodQty, DefectQty,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'WO-DEMO-20260525-001', 'OP10', 'PRINT', 1, 9, 101800, 240, 0, GETDATE(), 'demo'),
    (NEWID(), 'WO-DEMO-20260525-001', 'OP20', 'VARN',  2, 9, 101800,   0, 0, GETDATE(), 'demo'),
    (NEWID(), 'WO-DEMO-20260525-001', 'OP30', 'DIE',   3, 9, 101200, 600, 0, GETDATE(), 'demo'),
    (NEWID(), 'WO-DEMO-20260525-001', 'OP40', 'FOLD',  4, 9, 100950, 250, 0, GETDATE(), 'demo'),
    (NEWID(), 'WO-DEMO-20260525-001', 'OP50', 'QC',    5, 9, 100500, 450, 0, GETDATE(), 'demo');

  /* ───────── 6. 材料出庫指示 OUT-DEMO-MAT-001 ───────── */
  PRINT '[6] 材料出庫指示 + 3 明細';
  INSERT INTO T_OutboundOrder
    (Id, OutboundNo, OutboundType,
     WorkOrderNo, CustomerCd, CustomerName,
     WarehouseCd, PlannedDate, Status, Priority,
     Remarks, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'OUT-DEMO-MAT-001', 1,             -- 1=材料出庫
     'WO-DEMO-20260525-001', NULL, NULL,
     'DW01', '2026-05-28', 4, 2,                 -- 4=Completed
     N'デモ：朝日 WO 向け材料領料', 0, GETDATE(), 'demo');

  INSERT INTO T_OutboundOrderDetail
    (Id, OutboundNo, [LineNo], ProductCd, ProductName,
     RequiredQty, AllocatedQty, ShippedQty,
     LotNo, LocationCd, UnitCd,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'OUT-DEMO-MAT-001', 1, 'K280-905-T',        N'原紙 K280',           1200.00, 1200.00, 1200.00, 'LOT-DEMO-PAPER-001', 'DEMO-RAW-A-01', 'M',  0, GETDATE(), 'demo'),
    (NEWID(), 'OUT-DEMO-MAT-001', 2, 'INK-PANTONE-485C',  N'インキ Pantone 485C',    5.0000,  5.0000,  5.0000, 'LOT-DEMO-INK-001',   'DEMO-RAW-A-01', 'KG', 0, GETDATE(), 'demo'),
    (NEWID(), 'OUT-DEMO-MAT-001', 3, 'GLUE-A',            N'接着剤',                 2.0000,  2.0000,  2.0000, 'LOT-DEMO-GLUE-001',  'DEMO-RAW-A-01', 'KG', 0, GETDATE(), 'demo');

  /* ───────── 7. 在庫トランザクション：OUT 3 件（材料領料）───────── */
  PRINT '[7] StockTxn OUT 3 件';
  INSERT INTO T_StockTransaction
    (Id, TxnNo, TxnType, TxnDateTime,
     WarehouseCd, LocationCd, ProductCd, LotNo, Qty, UnitCd,
     RelatedNo, RelatedType, OperatorCd, Remark,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'TXN-DEMO-001', 'OUT', '2026-05-28 08:15:00',
     'DW01', 'DEMO-RAW-A-01', 'K280-905-T',       'LOT-DEMO-PAPER-001', 1200.00000000, 'M',
     'OUT-DEMO-MAT-001', 'OUTBOUND', 'U-201', N'デモ：印刷向け 原紙領料',
     0, GETDATE(), 'demo'),
    (NEWID(), 'TXN-DEMO-002', 'OUT', '2026-05-28 08:20:00',
     'DW01', 'DEMO-RAW-A-01', 'INK-PANTONE-485C', 'LOT-DEMO-INK-001',      5.0000, 'KG',
     'OUT-DEMO-MAT-001', 'OUTBOUND', 'U-201', N'デモ：印刷向け インキ領料',
     0, GETDATE(), 'demo'),
    (NEWID(), 'TXN-DEMO-003', 'OUT', '2026-05-28 08:25:00',
     'DW01', 'DEMO-RAW-A-01', 'GLUE-A',           'LOT-DEMO-GLUE-001',     2.0000, 'KG',
     'OUT-DEMO-MAT-001', 'OUTBOUND', 'U-201', N'デモ：印刷向け 接着剤領料',
     0, GETDATE(), 'demo');

  -- Stock 残量更新
  UPDATE T_Stock SET PhysicalQty = 300.00000000, AvailableQty = 300.00000000 WHERE LotNo='LOT-DEMO-PAPER-001';
  UPDATE T_Stock SET PhysicalQty =   5.0000,     AvailableQty =   5.0000     WHERE LotNo='LOT-DEMO-INK-001';
  UPDATE T_Stock SET PhysicalQty =   3.0000,     AvailableQty =   3.0000     WHERE LotNo='LOT-DEMO-GLUE-001';
  -- 原紙ロール残米更新
  UPDATE T_PaperRoll SET RemainingLengthM = 300.00000000, Status = 1 WHERE RollNo='ROLL-D-20260515-003';

  /* ───────── 8. QC 受入検品 QC-DEMO-001 ───────── */
  PRINT '[8] QC 受入検品 (100,500 中 100,200 PASS / 300 FAIL)';
  INSERT INTO T_QcInspection
    (Id, InspectionNo, ArrivalDateTime, Status,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'QC-DEMO-001', '2026-06-10 11:30:00', 2,       -- 2=Judged
     0, GETDATE(), 'demo');

  INSERT INTO T_QcInspectionItem
    (Id, InspectionNo, [LineNo], ProductCd,
     ExpectedQty, ReceivedQty, AcceptedQty, RejectedQty, PendingQty,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'QC-DEMO-001', 1, 'P-MILK-1L-6C',
     100000.00, 100500.00, 100200.00, 300.00, 0.00,
     0, GETDATE(), 'demo');

  /* ───────── 9. 完成品入庫 RCP-DEMO-PROD-001 ───────── */
  PRINT '[9] 完成品入庫 (良品 100,200 + 不良品 300)';
  INSERT INTO T_InboundReceipt
    (Id, ReceiptNo, SourceType, WorkOrderNo,
     ReceiveDateTime, OperatorCd, WarehouseCd, Status, Remarks,
     IsDeleted, CreateDate, Creator)
  VALUES
    -- 良品
    (NEWID(), 'RCP-DEMO-PROD-001', 'PRODUCTION', 'WO-DEMO-20260525-001',
     '2026-06-10 14:00:00', 'U-501', 'DW03', 1, N'デモ：朝日完成品入庫 良品分',
     0, GETDATE(), 'demo'),
    -- 不良品
    (NEWID(), 'RCP-DEMO-PROD-002', 'PRODUCTION', 'WO-DEMO-20260525-001',
     '2026-06-10 14:10:00', 'U-501', 'DW04', 1, N'デモ：朝日完成品入庫 不良品分',
     0, GETDATE(), 'demo');

  INSERT INTO T_InboundReceiptDetail
    (Id, ReceiptNo, [LineNo], ProductCd, ProductName, LotNo, ReceivedQty, UnitCd, LocationCd,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'RCP-DEMO-PROD-001', 1, 'P-MILK-1L-6C', N'牛乳1Lカートン 6色印刷', 'LOT-DEMO-WO001-A',  100200.00, 'PCS', 'DEMO-FG-C-12', 0, GETDATE(), 'demo'),
    (NEWID(), 'RCP-DEMO-PROD-002', 1, 'P-MILK-1L-6C', N'牛乳1Lカートン 6色印刷', 'LOT-DEMO-WO001-NG',    300.00, 'PCS', 'DEMO-NG-D-05', 0, GETDATE(), 'demo');

  -- 完成品在庫を立てる
  INSERT INTO T_Stock
    (Id, WarehouseCd, LocationCd, ProductCd, LotNo,
     PhysicalQty, AllocatedQty, AvailableQty, UnitCd, ReceiveDate,
     RecallFlag, OwnerType, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'DW03', 'DEMO-FG-C-12', 'P-MILK-1L-6C', 'LOT-DEMO-WO001-A',  100200.00, 100000.00,  200.00, 'PCS', '2026-06-10', 0, 'SELF', 0, GETDATE(), 'demo'),
    (NEWID(), 'DW04', 'DEMO-NG-D-05', 'P-MILK-1L-6C', 'LOT-DEMO-WO001-NG',    300.00,      0,      300.00, 'PCS', '2026-06-10', 0, 'SELF', 0, GETDATE(), 'demo');

  -- IN トランザクション
  INSERT INTO T_StockTransaction
    (Id, TxnNo, TxnType, TxnDateTime,
     WarehouseCd, LocationCd, ProductCd, LotNo, Qty, UnitCd,
     RelatedNo, RelatedType, OperatorCd, Remark,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'TXN-DEMO-004', 'IN', '2026-06-10 14:00:00',
     'DW03', 'DEMO-FG-C-12', 'P-MILK-1L-6C', 'LOT-DEMO-WO001-A', 100200.00, 'PCS',
     'RCP-DEMO-PROD-001', 'INBOUND', 'U-501', N'デモ：朝日完成品 良品入庫',
     0, GETDATE(), 'demo'),
    (NEWID(), 'TXN-DEMO-005', 'IN', '2026-06-10 14:10:00',
     'DW04', 'DEMO-NG-D-05', 'P-MILK-1L-6C', 'LOT-DEMO-WO001-NG',  300.00, 'PCS',
     'RCP-DEMO-PROD-002', 'INBOUND', 'U-501', N'デモ：朝日完成品 不良品入庫',
     0, GETDATE(), 'demo');

  /* ───────── 10. 出荷指示 OUT-DEMO-SHIP-001 ───────── */
  PRINT '[10] 出荷指示 100,000 枚';
  INSERT INTO T_OutboundOrder
    (Id, OutboundNo, OutboundType,
     WebOrderNo, CustomerCd, CustomerName,
     WarehouseCd, PlannedDate, Status, Priority,
     ShipToAddress, CarrierCd, Remarks,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'OUT-DEMO-SHIP-001', 2,
     'SO-DEMO-20260524-001', 'BP001', N'朝日飲料株式会社',
     'DW03', '2026-06-12', 4, 2,
     N'茨城県つくば市 朝日飲料 茨城工場', 'YAMATO', N'デモ：朝日向け出荷 完了済',
     0, GETDATE(), 'demo');

  INSERT INTO T_OutboundOrderDetail
    (Id, OutboundNo, [LineNo], ProductCd, ProductName,
     RequiredQty, AllocatedQty, ShippedQty,
     LotNo, LocationCd, UnitCd, UnitPrice,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'OUT-DEMO-SHIP-001', 1, 'P-MILK-1L-6C', N'牛乳1Lカートン 6色印刷',
     100000.00, 100000.00, 100000.00,
     'LOT-DEMO-WO001-A', 'DEMO-FG-C-12', 'PCS', 3.07,
     0, GETDATE(), 'demo');

  /* ───────── 11. 梱包 PKG-DEMO-001 ───────── */
  PRINT '[11] 梱包 PKG-DEMO-001 (20 ケース 450kg YAMATO)';
  INSERT INTO T_ShippingPackage
    (Id, PackageNo, OutboundNo, CaseQty,
     TotalWeightKg, TotalVolumeM3,
     CarrierCd, TrackingNo, DepartureTime, Remarks,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'PKG-DEMO-001', 'OUT-DEMO-SHIP-001', 20,
     450.000, 2.4000,
     'YAMATO', 'YAMATO-DEMO-20260612-345678', '2026-06-12 15:00:00',
     N'デモ：朝日向け配送 PKG',
     0, GETDATE(), 'demo');

  -- 出荷 OUT トランザクション
  INSERT INTO T_StockTransaction
    (Id, TxnNo, TxnType, TxnDateTime,
     WarehouseCd, LocationCd, ProductCd, LotNo, Qty, UnitCd,
     RelatedNo, RelatedType, OperatorCd, Remark,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'TXN-DEMO-006', 'OUT', '2026-06-12 11:00:00',
     'DW03', 'DEMO-FG-C-12', 'P-MILK-1L-6C', 'LOT-DEMO-WO001-A', 100000.00, 'PCS',
     'OUT-DEMO-SHIP-001', 'OUTBOUND', 'U-301', N'デモ：朝日向け出荷確定',
     0, GETDATE(), 'demo');

  -- 出荷後在庫を残 200 に
  UPDATE T_Stock SET PhysicalQty = 200.00, AllocatedQty = 0, AvailableQty = 200.00 WHERE LotNo='LOT-DEMO-WO001-A';

  /* ───────── 12. 完了レポート ───────── */
  PRINT '════════════════════════════════════════════════════════════';
  PRINT '  デモデータ投入完了';
  PRINT '════════════════════════════════════════════════════════════';
  SELECT N'倉庫 (DW01-04)'             AS 種別, COUNT(*) AS 件数 FROM T_Warehouse        WHERE WarehouseCd LIKE 'DW0%'
  UNION ALL SELECT N'ロケ (DEMO-*)',        COUNT(*) FROM T_Location          WHERE LocationCd LIKE 'DEMO-%'
  UNION ALL SELECT N'原紙ロール',           COUNT(*) FROM T_PaperRoll         WHERE RollNo LIKE 'ROLL-D%'
  UNION ALL SELECT N'在庫行 Stock',         COUNT(*) FROM T_Stock             WHERE LotNo LIKE 'LOT-DEMO-%'
  UNION ALL SELECT N'StockTxn',             COUNT(*) FROM T_StockTransaction  WHERE TxnNo LIKE 'TXN-DEMO-%'
  UNION ALL SELECT N'製造指図 + 工程',      COUNT(*) FROM T_WorkOrder         WHERE WorkOrderNo LIKE 'WO-DEMO-%'
  UNION ALL SELECT N'  └ 工程',             COUNT(*) FROM T_WorkOrderProcess  WHERE WorkOrderNo LIKE 'WO-DEMO-%'
  UNION ALL SELECT N'出庫指示',             COUNT(*) FROM T_OutboundOrder     WHERE OutboundNo LIKE 'OUT-DEMO-%'
  UNION ALL SELECT N'  └ 明細',             COUNT(*) FROM T_OutboundOrderDetail WHERE OutboundNo LIKE 'OUT-DEMO-%'
  UNION ALL SELECT N'入庫実績',             COUNT(*) FROM T_InboundReceipt    WHERE ReceiptNo LIKE 'RCP-DEMO-%'
  UNION ALL SELECT N'  └ 明細',             COUNT(*) FROM T_InboundReceiptDetail WHERE ReceiptNo LIKE 'RCP-DEMO-%'
  UNION ALL SELECT N'QC 検品',              COUNT(*) FROM T_QcInspection      WHERE InspectionNo LIKE 'QC-DEMO-%'
  UNION ALL SELECT N'梱包',                 COUNT(*) FROM T_ShippingPackage   WHERE PackageNo LIKE 'PKG-DEMO-%';

  COMMIT;
  PRINT '✓ COMMIT 完了 — 画面で確認可能';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  PRINT '✗ エラー: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
