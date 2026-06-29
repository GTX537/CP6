-- Space P4 multi-floor QA seed (CP6DB_SpaceQA)
-- Adds floor F2 (Level 2) to site QAWH + a zone/rack/aisle/2 locations on F2,
-- an elevator E1 connecting F1↔F2, and a cross-floor outbound order whose LineNo
-- deliberately bounces F1→F2→F1→F2 (elevator crossed 3×) so the what-if optimizer
-- groups by floor (elevator crossed 1×) → clear savings.
-- Reuses F1's existing SP3 grid (aisles SP3-* + locations SP3-TL/SP3-TR).
-- Idempotent (IF NOT EXISTS). ASCII only. Requires SET QUOTED_IDENTIFIER ON (Space_Location filtered index).
--
-- Run (PowerShell):
--   & "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" `
--     -S "localhost\KOUSQLSERVER" -E -d CP6DB_SpaceQA `
--     -i "D:\CP6-space-backend\docs\superpowers\qa\space-p4-multifloor\seed.sql"

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @Tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';
DECLARE @Site   uniqueidentifier = 'F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE';  -- QAWH
DECLARE @F1     uniqueidentifier = '5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F';   -- existing F1 (Level 1)
DECLARE @F2     uniqueidentifier = 'F2000000-0000-0000-0000-0000000000F2';   -- new F2 (Level 2)
DECLARE @ZoneB  uniqueidentifier = 'B2000000-0000-0000-0000-00000000000B';
DECLARE @RackR2 uniqueidentifier = '0A000000-0000-0000-0000-000000000002';
DECLARE @E1     uniqueidentifier = 'E1000000-0000-0000-0000-0000000000E1';
DECLARE @Now    datetime2 = SYSUTCDATETIME();

-- ===== 1) Floor F2 (Level 2, height 6000 → stacked z = 6000 above F1) =====
IF NOT EXISTS (SELECT 1 FROM Space_Floor WHERE Id=@F2)
  INSERT INTO Space_Floor (Id, SiteId, Level, FloorCode, FloorName, Height, UnderlayOffsetX, UnderlayOffsetY, OriginX, OriginY, CreateDate, TenantId, IsDeleted)
  VALUES (@F2, @Site, 2, 'F2', '2F', 6000, 0, 0, 0, 0, @Now, @Tenant, 0);

-- ===== 2) F2 zone + rack + aisle =====
IF NOT EXISTS (SELECT 1 FROM Space_Zone WHERE Id=@ZoneB)
  INSERT INTO Space_Zone (Id, FloorId, ZoneCode, ZoneName, ZoneType, Polygon, Enable, CreateDate, TenantId, IsDeleted)
  VALUES (@ZoneB, @F2, 'B', 'B Zone', 1, '[]', 1, @Now, @Tenant, 0);

IF NOT EXISTS (SELECT 1 FROM Space_Rack WHERE Id=@RackR2)
  INSERT INTO Space_Rack (Id, ZoneId, FloorId, RackCode, X, Y, Z, RotationZ, Cols, Levels, DepthCount, CellW, CellH, CellD, Enable, CreateDate, TenantId, IsDeleted)
  VALUES (@RackR2, @ZoneB, @F2, 'R2', 0, 0, 0, 0, 1, 1, 1, 1000, 1000, 1000, 1, @Now, @Tenant, 0);

IF NOT EXISTS (SELECT 1 FROM Space_Aisle WHERE AisleCode='SP4-F2-H' AND ZoneId=@ZoneB)
  INSERT INTO Space_Aisle (Id, ZoneId, AisleCode, Polygon, Centerline, CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @ZoneB, 'SP4-F2-H', '[]', '[[0,500],[4000,500]]', @Now, @Tenant, 0);

-- ===== 3) F2 pick locations (Placed=1, on rack R2; AbsX/Y on the F2 aisle) =====
IF NOT EXISTS (SELECT 1 FROM Space_Location WHERE LocationCode='B-01-01-01' AND FloorId=@F2)
  INSERT INTO Space_Location (Id, RackId, FloorId, LocationCode, CodeOrigin, AbsX, AbsY, AbsZ, Placed, Status, [Version], CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @RackR2, @F2, 'B-01-01-01', 1, 500, 450, 500, 1, 1, 1, @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_Location WHERE LocationCode='B-01-01-02' AND FloorId=@F2)
  INSERT INTO Space_Location (Id, RackId, FloorId, LocationCode, CodeOrigin, AbsX, AbsY, AbsZ, Placed, Status, [Version], CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @RackR2, @F2, 'B-01-01-02', 1, 3500, 450, 500, 1, 1, 1, @Now, @Tenant, 0);

-- ===== 4) Elevator E1 connecting F1 (500,500 on SP3-V1) ↔ F2 (500,500 on SP4-F2-H) =====
IF NOT EXISTS (SELECT 1 FROM Space_Connector WHERE Id=@E1)
  INSERT INTO Space_Connector (Id, SiteId, ConnectorCode, ConnectorType, Name, CreateDate, TenantId, IsDeleted)
  VALUES (@E1, @Site, 'E1', 1, 'Elevator 1', @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_ConnectorStop WHERE ConnectorId=@E1 AND FloorId=@F1)
  INSERT INTO Space_ConnectorStop (Id, ConnectorId, FloorId, X, Y, CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @E1, @F1, 500, 500, @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_ConnectorStop WHERE ConnectorId=@E1 AND FloorId=@F2)
  INSERT INTO Space_ConnectorStop (Id, ConnectorId, FloorId, X, Y, CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @E1, @F2, 500, 500, @Now, @Tenant, 0);

-- ===== 5) Cross-floor outbound order (zig-zag LineNo: F1→F2→F1→F2) =====
-- Uses existing F1 SP3-TL(500,450)/SP3-TR(3500,450) + new F2 B-01-01-01/02.
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrder WHERE OutboundNo='OB-P4-CROSS')
  INSERT INTO T_OutboundOrder (Id, OutboundNo, OutboundType, WarehouseCd, PlannedDate, Status, Priority, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-P4-CROSS', 2, 'QAWH', @Now, 3, 1, @Now, 0, @Tenant);

IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-P4-CROSS' AND [LineNo]=1)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-P4-CROSS', 1, 'P4-ITEM', 1, 0, 0, 'SP3-TL', @Now, 0, @Tenant);
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-P4-CROSS' AND [LineNo]=2)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-P4-CROSS', 2, 'P4-ITEM', 1, 0, 0, 'B-01-01-02', @Now, 0, @Tenant);
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-P4-CROSS' AND [LineNo]=3)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-P4-CROSS', 3, 'P4-ITEM', 1, 0, 0, 'SP3-TR', @Now, 0, @Tenant);
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-P4-CROSS' AND [LineNo]=4)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-P4-CROSS', 4, 'P4-ITEM', 1, 0, 0, 'B-01-01-01', @Now, 0, @Tenant);

-- ===== verify =====
SELECT 'floors' AS kind, FloorCode, Level FROM Space_Floor WHERE SiteId=@Site ORDER BY Level;
SELECT 'F2 locs' AS kind, COUNT(*) AS n FROM Space_Location WHERE FloorId=@F2 AND LocationCode LIKE 'B-%';
SELECT 'connector E1 stops' AS kind, COUNT(*) AS n FROM Space_ConnectorStop WHERE ConnectorId=@E1;
SELECT 'order detail' AS kind, [LineNo], LocationCd FROM T_OutboundOrderDetail WHERE OutboundNo='OB-P4-CROSS' ORDER BY [LineNo];
