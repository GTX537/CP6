/* =====================================================================
 * 面接デモ用 v2：朝日飲料 100,000 枚 牛乳 1L カートン 全流程
 *   ★ 自立版：取引先 'ASAHI' を自前で作成 → どの環境でも単独で動く
 *   ★ idempotent：何度実行しても OK（demo データは全削除→再投入）
 *   ★ 既存データには触れない（'-DEMO-' or 'ASAHI'/'DW0' プレフィクスのみ）
 *
 *   ローカル実行:
 *     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB -E ^
 *            -i "D:\CP6\docs\demo-asahi-interview-v2.sql" -b
 *
 *   cp6.uk Docker 実行:
 *     scp demo-asahi-interview-v2.sql user@cp6.uk:/tmp/
 *     ssh user@cp6.uk "docker exec -i cp6-db /opt/mssql-tools18/bin/sqlcmd ^
 *       -S localhost -U sa -P 'Cp6@Docker2024!' -C -d CP6DB ^
 *       -i /tmp/demo-asahi-interview-v2.sql -b"
 * ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRAN;
  PRINT '============================================================';
  PRINT '  Asahi Beverage demo data — investigation start';
  PRINT '============================================================';

  /* ---------- 0. cleanup old demo ---------- */
  PRINT '[0] cleanup old demo data';
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
  -- BP ASAHI は MERGE で更新するので削除しない

  /* ---------- 1. BP ASAHI customer master (MERGE upsert) ---------- */
  PRINT '[1] BP master — ASAHI (Asahi Beverage)';
  MERGE T_WebBusinessPartner AS tgt
  USING (SELECT 'ASAHI' AS BpCd) AS src ON tgt.BpCd = src.BpCd
  WHEN MATCHED THEN UPDATE SET
      BpName    = N'朝日飲料株式会社',
      BpAbbrev  = N'朝日',
      Status    = 1,
      CustomerFlg = 1,
      AccountsReceivableFlg = 1, BillingFlg = 1, ReceiptFlg = 1,
      DeliveryFlg = 1, SupplierFlg = 0, AccountsPayableFlg = 0,
      PaymentScheduleFlg = 0, PaymentFlg = 0, CreditMgmtFlg = 1, MakerFlg = 0,
      PaidSupplyFlg = 0, RebuyObligationFlg = 0,
      ZipCd = '305-0031', Addr1 = N'茨城県', Addr2 = N'つくば市',
      Addr3 = N'吾妻 1-1-1', Tel = '029-123-4567',
      ModifyDate = GETDATE(), Modifier = 'demo'
  WHEN NOT MATCHED THEN INSERT
    (Id, BpCd, BpName, BpAbbrev, BaseCd, Status,
     CustomerFlg, AccountsReceivableFlg, BillingFlg, ReceiptFlg, DeliveryFlg,
     SupplierFlg, AccountsPayableFlg, PaymentScheduleFlg, PaymentFlg,
     CreditMgmtFlg, MakerFlg, PaidSupplyFlg, RebuyObligationFlg,
     SubcontractTargetFlg, SupplyPriceChangeAllowFlg, DeliveryConfirmFlg,
     PurchaseTaxPriorityFlg, PaidSupplyTaxPriorityFlg, PaidEachTimeFlg,
     PaymentTaxCalcFlg, BatchPaymentScheduleFlg, GifuInterfaceFlg, McTransferFlg,
     ZipCd, Addr1, Addr2, Addr3, Tel,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'ASAHI', N'朝日飲料株式会社', N'朝日', 'CP6', 1,
     1, 1, 1, 1, 1,
     0, 0, 0, 0,
     1, 0, 0, 0,
     0, 0, 0,
     0, 0, 0,
     0, 0, 0, 0,
     '305-0031', N'茨城県', N'つくば市', N'吾妻 1-1-1', '029-123-4567',
     0, GETDATE(), 'demo');

  /* ---------- 2. Warehouses (DW01-04) ---------- */
  PRINT '[2] Warehouses (DW01-04)';
  INSERT INTO T_Warehouse (Id, WarehouseCd, WarehouseName, WarehouseType, AllowNegative, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'DW01', N'デモ原材料倉庫',  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DW02', N'デモ半製品倉庫',  2, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DW03', N'デモ完成品倉庫',  3, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DW04', N'デモ不良品倉庫',  4, 0, 0, GETDATE(), 'demo');

  /* ---------- 3. Locations ---------- */
  PRINT '[3] Locations';
  INSERT INTO T_Location (Id, LocationCd, WarehouseCd, LocationLevel, LocationName, CapacityQty, IsPickable, IsBlocked, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'DEMO-RAW-A-01', 'DW01', 1, N'原紙ラック A-01', 5000.0,  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DEMO-WIP-B-03', 'DW02', 1, N'半製品 B-03',     2000.0,  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DEMO-FG-C-12',  'DW03', 1, N'完成品 C-12',   200000.0,  1, 0, 0, GETDATE(), 'demo'),
    (NEWID(), 'DEMO-NG-D-05',  'DW04', 1, N'不良品 D-05',    10000.0,  1, 0, 0, GETDATE(), 'demo');

  /* ---------- 4. Paper Roll ---------- */
  PRINT '[4] Paper Roll (K280 905mm 1500m)';
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
     'DEMO-RAW-A-01', 'DW01', 0, 50.00000000, N'デモ用 1500m',
     0, GETDATE(), 'demo');

  /* ---------- 5. Initial Stock ---------- */
  PRINT '[5] Initial stock (paper + ink + glue)';
  INSERT INTO T_Stock
    (Id, WarehouseCd, LocationCd, ProductCd, LotNo,
     PhysicalQty, AllocatedQty, AvailableQty, UnitCd, ReceiveDate,
     RecallFlag, OwnerType, PaperRollNo,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'DW01', 'DEMO-RAW-A-01', 'K280-905-T', 'LOT-DEMO-PAPER-001',
     1500.00000000, 0, 1500.00000000, 'M', '2026-05-15',
     0, 'SELF', 'ROLL-D-20260515-003',
     0, GETDATE(), 'demo'),
    (NEWID(), 'DW01', 'DEMO-RAW-A-01', 'INK-PANTONE-485C', 'LOT-DEMO-INK-001',
     10.0000, 0, 10.0000, 'KG', '2026-05-10',
     0, 'SELF', NULL,
     0, GETDATE(), 'demo'),
    (NEWID(), 'DW01', 'DEMO-RAW-A-01', 'GLUE-A', 'LOT-DEMO-GLUE-001',
     5.0000, 0, 5.0000, 'KG', '2026-05-12',
     0, 'SELF', NULL,
     0, GETDATE(), 'demo');

  /* ---------- 6. MES Work Order WO-DEMO-20260525-001 ---------- */
  PRINT '[6] MES WorkOrder + 5 processes';
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
    (NEWID(), 'WO-DEMO-20260525-001', 9,
     NULL, 'ASAHI',
     'P-MILK-1L-6C', N'牛乳1Lカートン 6色印刷',
     100000.00, 100200.00, 540.00,
     '2026-06-15', '2026-05-28 08:00:00', '2026-06-10 17:00:00',
     '2026-05-28 08:00:00', '2026-06-10 12:00:00',
     2, 'LOT-DEMO-WO001', N'デモ：朝日 100,000 枚 歩留り98.2%',
     0, GETDATE(), 'demo');

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

  /* ---------- 7. Material Outbound OUT-DEMO-MAT-001 ---------- */
  PRINT '[7] Material outbound + 3 detail lines';
  INSERT INTO T_OutboundOrder
    (Id, OutboundNo, OutboundType,
     WorkOrderNo, CustomerCd, CustomerName,
     WarehouseCd, PlannedDate, Status, Priority,
     Remarks, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'OUT-DEMO-MAT-001', 1,
     'WO-DEMO-20260525-001', NULL, NULL,
     'DW01', '2026-05-28', 4, 2,
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

  /* ---------- 8. Stock Transactions: OUT x3 ---------- */
  PRINT '[8] StockTxn OUT x3 (material picking)';
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

  UPDATE T_Stock SET PhysicalQty = 300.00000000, AvailableQty = 300.00000000 WHERE LotNo='LOT-DEMO-PAPER-001';
  UPDATE T_Stock SET PhysicalQty =   5.0000,     AvailableQty =   5.0000     WHERE LotNo='LOT-DEMO-INK-001';
  UPDATE T_Stock SET PhysicalQty =   3.0000,     AvailableQty =   3.0000     WHERE LotNo='LOT-DEMO-GLUE-001';
  UPDATE T_PaperRoll SET RemainingLengthM = 300.00000000, Status = 1 WHERE RollNo='ROLL-D-20260515-003';

  /* ---------- 9. QC Inspection QC-DEMO-001 ---------- */
  PRINT '[9] QC inspection (100,500 = PASS 100,200 + FAIL 300)';
  INSERT INTO T_QcInspection
    (Id, InspectionNo, ArrivalDateTime, Status, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'QC-DEMO-001', '2026-06-10 11:30:00', 2, 0, GETDATE(), 'demo');

  INSERT INTO T_QcInspectionItem
    (Id, InspectionNo, [LineNo], ProductCd,
     ExpectedQty, ReceivedQty, AcceptedQty, RejectedQty, PendingQty,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'QC-DEMO-001', 1, 'P-MILK-1L-6C',
     100000.00, 100500.00, 100200.00, 300.00, 0.00,
     0, GETDATE(), 'demo');

  /* ---------- 10. Production Receipts ---------- */
  PRINT '[10] Production receipts (good 100,200 + defect 300)';
  INSERT INTO T_InboundReceipt
    (Id, ReceiptNo, SourceType, WorkOrderNo,
     ReceiveDateTime, OperatorCd, WarehouseCd, Status, Remarks,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'RCP-DEMO-PROD-001', 'PRODUCTION', 'WO-DEMO-20260525-001',
     '2026-06-10 14:00:00', 'U-501', 'DW03', 1, N'デモ：朝日完成品入庫 良品分',
     0, GETDATE(), 'demo'),
    (NEWID(), 'RCP-DEMO-PROD-002', 'PRODUCTION', 'WO-DEMO-20260525-001',
     '2026-06-10 14:10:00', 'U-501', 'DW04', 1, N'デモ：朝日完成品入庫 不良品分',
     0, GETDATE(), 'demo');

  INSERT INTO T_InboundReceiptDetail
    (Id, ReceiptNo, [LineNo], ProductCd, ProductName, LotNo, ReceivedQty, UnitCd, LocationCd,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'RCP-DEMO-PROD-001', 1, 'P-MILK-1L-6C', N'牛乳1Lカートン 6色印刷', 'LOT-DEMO-WO001-A',  100200.00, 'PCS', 'DEMO-FG-C-12', 0, GETDATE(), 'demo'),
    (NEWID(), 'RCP-DEMO-PROD-002', 1, 'P-MILK-1L-6C', N'牛乳1Lカートン 6色印刷', 'LOT-DEMO-WO001-NG',    300.00, 'PCS', 'DEMO-NG-D-05', 0, GETDATE(), 'demo');

  INSERT INTO T_Stock
    (Id, WarehouseCd, LocationCd, ProductCd, LotNo,
     PhysicalQty, AllocatedQty, AvailableQty, UnitCd, ReceiveDate,
     RecallFlag, OwnerType, IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'DW03', 'DEMO-FG-C-12', 'P-MILK-1L-6C', 'LOT-DEMO-WO001-A',  100200.00, 100000.00,  200.00, 'PCS', '2026-06-10', 0, 'SELF', 0, GETDATE(), 'demo'),
    (NEWID(), 'DW04', 'DEMO-NG-D-05', 'P-MILK-1L-6C', 'LOT-DEMO-WO001-NG',    300.00,      0,      300.00, 'PCS', '2026-06-10', 0, 'SELF', 0, GETDATE(), 'demo');

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

  /* ---------- 11. Shipping Outbound OUT-DEMO-SHIP-001 ---------- */
  PRINT '[11] Shipping outbound 100,000 pcs';
  INSERT INTO T_OutboundOrder
    (Id, OutboundNo, OutboundType,
     WebOrderNo, CustomerCd, CustomerName,
     WarehouseCd, PlannedDate, Status, Priority,
     ShipToAddress, CarrierCd, Remarks,
     IsDeleted, CreateDate, Creator)
  VALUES
    (NEWID(), 'OUT-DEMO-SHIP-001', 2,
     'SO-DEMO-20260524-001', 'ASAHI', N'朝日飲料株式会社',
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

  /* ---------- 12. Shipping Package PKG-DEMO-001 ---------- */
  PRINT '[12] Shipping package PKG-DEMO-001 (YAMATO 20 cases 450kg)';
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

  UPDATE T_Stock SET PhysicalQty = 200.00, AllocatedQty = 0, AvailableQty = 200.00 WHERE LotNo='LOT-DEMO-WO001-A';

  /* ---------- Summary ---------- */
  PRINT '============================================================';
  PRINT '  Demo data investigation COMPLETE';
  PRINT '============================================================';
  SELECT N'BP (ASAHI)'        AS Type, COUNT(*) AS Cnt FROM T_WebBusinessPartner WHERE BpCd='ASAHI'
  UNION ALL SELECT N'Warehouse',            COUNT(*) FROM T_Warehouse        WHERE WarehouseCd LIKE 'DW0%'
  UNION ALL SELECT N'Location',             COUNT(*) FROM T_Location          WHERE LocationCd LIKE 'DEMO-%'
  UNION ALL SELECT N'PaperRoll',            COUNT(*) FROM T_PaperRoll         WHERE RollNo LIKE 'ROLL-D%'
  UNION ALL SELECT N'Stock',                COUNT(*) FROM T_Stock             WHERE LotNo LIKE 'LOT-DEMO-%'
  UNION ALL SELECT N'StockTxn',             COUNT(*) FROM T_StockTransaction  WHERE TxnNo LIKE 'TXN-DEMO-%'
  UNION ALL SELECT N'WorkOrder',            COUNT(*) FROM T_WorkOrder         WHERE WorkOrderNo LIKE 'WO-DEMO-%'
  UNION ALL SELECT N'  Process',            COUNT(*) FROM T_WorkOrderProcess  WHERE WorkOrderNo LIKE 'WO-DEMO-%'
  UNION ALL SELECT N'OutboundOrder',        COUNT(*) FROM T_OutboundOrder     WHERE OutboundNo LIKE 'OUT-DEMO-%'
  UNION ALL SELECT N'  Detail',             COUNT(*) FROM T_OutboundOrderDetail WHERE OutboundNo LIKE 'OUT-DEMO-%'
  UNION ALL SELECT N'InboundReceipt',       COUNT(*) FROM T_InboundReceipt    WHERE ReceiptNo LIKE 'RCP-DEMO-%'
  UNION ALL SELECT N'  Detail',             COUNT(*) FROM T_InboundReceiptDetail WHERE ReceiptNo LIKE 'RCP-DEMO-%'
  UNION ALL SELECT N'QC',                   COUNT(*) FROM T_QcInspection      WHERE InspectionNo LIKE 'QC-DEMO-%'
  UNION ALL SELECT N'Package',              COUNT(*) FROM T_ShippingPackage   WHERE PackageNo LIKE 'PKG-DEMO-%';

  COMMIT;
  PRINT 'COMMIT — ready to demo';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  PRINT 'ERROR: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
