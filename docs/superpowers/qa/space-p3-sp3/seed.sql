-- Space P3 SP3 pathfinding QA seed (CP6DB_SpaceQA)
-- Adds a multi-aisle cross-grid on demo floor F1 + 4 spread-out pick locations
-- + one outbound order whose [LineNo] order deliberately zig-zags, so the
-- what-if optimizer shows a clear savings (actual ~15.3m -> optimized ~9.3m, ~39%).
-- Idempotent: every insert guarded by IF NOT EXISTS. ASCII only (sqlcmd safe).
--
-- Run (PowerShell):
--   & "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" `
--     -S "localhost\KOUSQLSERVER" -E -d CP6DB_SpaceQA `
--     -i "D:\CP6-space-backend\docs\superpowers\qa\space-p3-sp3\seed.sql"

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;  -- required: Space_Location has a filtered unique index (WHERE LocationCode IS NOT NULL)
SET ANSI_NULLS ON;

DECLARE @Tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1'; -- admin tenant (DEV-visible)
DECLARE @Floor  uniqueidentifier = '5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F'; -- demo floor F1
DECLARE @Zone   uniqueidentifier = '4D426CAE-AAE6-4CC5-91A1-370549113A59'; -- zone A (on floor F1)
DECLARE @Rack   uniqueidentifier = '0A000000-0000-0000-0000-000000000001'; -- rack R1 on F1 (scene needs every Placed loc to have a RackId)
DECLARE @Now    datetime2 = SYSUTCDATETIME();

-- ===== 1) Cross-grid aisles (centerlines in XY mm; intersections connect the grid) =====
-- H1 y=500, H2 y=3500, V1 x=500, V2 x=3500 -> 4 connected corners (500/3500 x 500/3500).
IF NOT EXISTS (SELECT 1 FROM Space_Aisle WHERE AisleCode='SP3-H1' AND ZoneId=@Zone)
  INSERT INTO Space_Aisle (Id, ZoneId, AisleCode, Polygon, Centerline, CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Zone, 'SP3-H1', '[]', '[[0,500],[4000,500]]', @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_Aisle WHERE AisleCode='SP3-H2' AND ZoneId=@Zone)
  INSERT INTO Space_Aisle (Id, ZoneId, AisleCode, Polygon, Centerline, CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Zone, 'SP3-H2', '[]', '[[0,3500],[4000,3500]]', @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_Aisle WHERE AisleCode='SP3-V1' AND ZoneId=@Zone)
  INSERT INTO Space_Aisle (Id, ZoneId, AisleCode, Polygon, Centerline, CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Zone, 'SP3-V1', '[]', '[[500,0],[500,4000]]', @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_Aisle WHERE AisleCode='SP3-V2' AND ZoneId=@Zone)
  INSERT INTO Space_Aisle (Id, ZoneId, AisleCode, Polygon, Centerline, CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Zone, 'SP3-V2', '[]', '[[3500,0],[3500,4000]]', @Now, @Tenant, 0);

-- ===== 2) Pick locations at the 4 grid corners (Placed=1 so pick-path resolves AbsXYZ) =====
-- TL(500,450) TR(3500,450) BL(500,3550) BR(3500,3550) -- all sit on a vertical aisle.
IF NOT EXISTS (SELECT 1 FROM Space_Location WHERE LocationCode='SP3-TL' AND FloorId=@Floor)
  INSERT INTO Space_Location (Id, RackId, FloorId, LocationCode, CodeOrigin, AbsX, AbsY, AbsZ, Placed, Status, [Version], CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Rack, @Floor, 'SP3-TL', 1, 500, 450, 500, 1, 1, 1, @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_Location WHERE LocationCode='SP3-TR' AND FloorId=@Floor)
  INSERT INTO Space_Location (Id, RackId, FloorId, LocationCode, CodeOrigin, AbsX, AbsY, AbsZ, Placed, Status, [Version], CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Rack, @Floor, 'SP3-TR', 1, 3500, 450, 500, 1, 1, 1, @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_Location WHERE LocationCode='SP3-BL' AND FloorId=@Floor)
  INSERT INTO Space_Location (Id, RackId, FloorId, LocationCode, CodeOrigin, AbsX, AbsY, AbsZ, Placed, Status, [Version], CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Rack, @Floor, 'SP3-BL', 1, 500, 3550, 500, 1, 1, 1, @Now, @Tenant, 0);
IF NOT EXISTS (SELECT 1 FROM Space_Location WHERE LocationCode='SP3-BR' AND FloorId=@Floor)
  INSERT INTO Space_Location (Id, RackId, FloorId, LocationCode, CodeOrigin, AbsX, AbsY, AbsZ, Placed, Status, [Version], CreateDate, TenantId, IsDeleted)
  VALUES (NEWID(), @Rack, @Floor, 'SP3-BR', 1, 3500, 3550, 500, 1, 1, 1, @Now, @Tenant, 0);

-- ===== 3) Outbound order with a deliberately zig-zag [LineNo] order =====
-- [LineNo] 1=TL 2=BR 3=TR 4=BL : actual route crosses the grid twice (~15.3m).
-- Optimizer reorders to the perimeter TL->TR->BR->BL (~9.3m), ~39% savings.
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrder WHERE OutboundNo='OB-SP3-CROSS')
  INSERT INTO T_OutboundOrder (Id, OutboundNo, OutboundType, WarehouseCd, PlannedDate, Status, Priority, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-SP3-CROSS', 2, 'QAWH', @Now, 3, 1, @Now, 0, @Tenant);

IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-SP3-CROSS' AND [LineNo]=1)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-SP3-CROSS', 1, 'SP3-ITEM', 1, 0, 0, 'SP3-TL', @Now, 0, @Tenant);
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-SP3-CROSS' AND [LineNo]=2)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-SP3-CROSS', 2, 'SP3-ITEM', 1, 0, 0, 'SP3-BR', @Now, 0, @Tenant);
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-SP3-CROSS' AND [LineNo]=3)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-SP3-CROSS', 3, 'SP3-ITEM', 1, 0, 0, 'SP3-TR', @Now, 0, @Tenant);
IF NOT EXISTS (SELECT 1 FROM T_OutboundOrderDetail WHERE OutboundNo='OB-SP3-CROSS' AND [LineNo]=4)
  INSERT INTO T_OutboundOrderDetail (Id, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd, CreateDate, IsDeleted, TenantId)
  VALUES (NEWID(), 'OB-SP3-CROSS', 4, 'SP3-ITEM', 1, 0, 0, 'SP3-BL', @Now, 0, @Tenant);

-- ===== verify =====
SELECT 'aisles' AS kind, COUNT(*) AS n FROM Space_Aisle WHERE ZoneId=@Zone AND AisleCode LIKE 'SP3-%';
SELECT 'locations' AS kind, COUNT(*) AS n FROM Space_Location WHERE FloorId=@Floor AND LocationCode LIKE 'SP3-%';
SELECT 'detail' AS kind, [LineNo], LocationCd FROM T_OutboundOrderDetail WHERE OutboundNo='OB-SP3-CROSS' ORDER BY [LineNo];
