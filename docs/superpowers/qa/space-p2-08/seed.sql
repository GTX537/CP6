-- Space 08 advanced-viz demo seed (CP6DB_SpaceQA). ASCII only. Idempotent.
SET NOCOUNT ON;
DECLARE @floor uniqueidentifier = '5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F';
DECLARE @tenant uniqueidentifier =
  (SELECT TOP 1 TenantId FROM Space_Location WHERE FloorId=@floor AND LocationCode='A-01-01-01');
DECLARE @wh nvarchar(10) = N'QAWH';
DECLARE @ob nvarchar(20) = N'OB-PICK-DEMO';

IF @tenant IS NULL BEGIN PRINT 'NO TENANT - check floor/codes'; RETURN; END;

-- 1) Pick order with 4 ordered lines across the 4 real codes -----------------
DELETE FROM T_OutboundOrderDetail WHERE OutboundNo=@ob;
DELETE FROM T_OutboundOrder       WHERE OutboundNo=@ob;

INSERT INTO T_OutboundOrder (Id, TenantId, IsDeleted, CreateDate, OutboundNo, OutboundType, WarehouseCd, PlannedDate, Status, Priority)
VALUES (NEWID(), @tenant, 0, GETDATE(), @ob, 2, @wh, GETDATE(), 3, 1);

INSERT INTO T_OutboundOrderDetail (Id, TenantId, IsDeleted, CreateDate, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd)
VALUES
 (NEWID(), @tenant, 0, GETDATE(), @ob, 1, N'CARTON-A4', 10, 10, 0, N'A-01-01-01'),
 (NEWID(), @tenant, 0, GETDATE(), @ob, 2, N'CARTON-A4',  5,  5, 0, N'A-01-01-02'),
 (NEWID(), @tenant, 0, GETDATE(), @ob, 3, N'CARTON-A4',  8,  8, 0, N'A-01-02-01'),
 (NEWID(), @tenant, 0, GETDATE(), @ob, 4, N'CARTON-A4',  3,  3, 0, N'A-01-02-02');

-- 2) Stock transactions (workload heatmap): 5/3/1/2 ops today ----------------
DELETE FROM T_StockTransaction WHERE TxnNo LIKE N'TXN-DEMO-%';
DECLARE @now datetime = GETDATE();
INSERT INTO T_StockTransaction (Id, TenantId, IsDeleted, CreateDate, TxnNo, TxnType, TxnDateTime, WarehouseCd, LocationCd, ProductCd, LotNo, Qty)
VALUES
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0001', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0002', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0003', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0004', N'IN',  @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0005', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0006', N'OUT', @now, @wh, N'A-01-01-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0007', N'OUT', @now, @wh, N'A-01-01-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0008', N'IN',  @now, @wh, N'A-01-01-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0009', N'OUT', @now, @wh, N'A-01-02-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0010', N'OUT', @now, @wh, N'A-01-02-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0011', N'OUT', @now, @wh, N'A-01-02-02', N'CARTON-A4', N'', 1);

-- 3) Aisle centerline (data-driven from the 4 codes' AbsXY) -------------------
DECLARE @cy int = (SELECT AVG(AbsY) FROM Space_Location
  WHERE FloorId=@floor AND LocationCode IN (N'A-01-01-01',N'A-01-01-02',N'A-01-02-01',N'A-01-02-02') AND AbsY IS NOT NULL);
DECLARE @minx int = (SELECT MIN(AbsX)-1000 FROM Space_Location
  WHERE FloorId=@floor AND LocationCode IN (N'A-01-01-01',N'A-01-01-02',N'A-01-02-01',N'A-01-02-02') AND AbsX IS NOT NULL);
DECLARE @maxx int = (SELECT MAX(AbsX)+1000 FROM Space_Location
  WHERE FloorId=@floor AND LocationCode IN (N'A-01-01-01',N'A-01-01-02',N'A-01-02-01',N'A-01-02-02') AND AbsX IS NOT NULL);
DECLARE @line nvarchar(200) =
  N'[[' + CAST(ISNULL(@minx,0) AS nvarchar(20)) + N',' + CAST(ISNULL(@cy,0) AS nvarchar(20)) + N'],['
        + CAST(ISNULL(@maxx,10000) AS nvarchar(20)) + N',' + CAST(ISNULL(@cy,0) AS nvarchar(20)) + N']]';

-- update existing aisles of this floor whose centerline is empty
UPDATE a SET Centerline=@line
FROM Space_Aisle a JOIN Space_Zone z ON a.ZoneId=z.Id
WHERE z.FloorId=@floor AND (a.Centerline IS NULL OR a.Centerline=N'' OR a.Centerline=N'[]');

-- if floor has no aisle at all, insert one on its first zone
IF NOT EXISTS (SELECT 1 FROM Space_Aisle a JOIN Space_Zone z ON a.ZoneId=z.Id WHERE z.FloorId=@floor)
BEGIN
  DECLARE @zone uniqueidentifier = (SELECT TOP 1 Id FROM Space_Zone WHERE FloorId=@floor ORDER BY ZoneCode);
  IF @zone IS NOT NULL
    INSERT INTO Space_Aisle (Id, TenantId, IsDeleted, CreateDate, ZoneId, AisleCode, Polygon, Centerline)
    VALUES (NEWID(), @tenant, 0, GETDATE(), @zone, N'AISLE-DEMO', N'[]', @line);
END;

PRINT 'space-08 seed done';
