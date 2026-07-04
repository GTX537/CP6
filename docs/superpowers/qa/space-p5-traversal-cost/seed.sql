-- Space P5 traversal-cost QA seed (CP6DB_SpaceQA)
-- =====================================================================
-- This is the LAST seed in a 4-part chain. Run order against CP6DB_SpaceQA:
--   0) THIS file's "Bootstrap" section below  (site QAWH / floor F1 / zone A / rack R1)
--   1) docs/superpowers/qa/space-p3-sp3/seed.sql        (F1 cross-grid + SP3-* locs + OB-SP3-CROSS zig-zag order)
--   2) docs/superpowers/qa/space-p4-multifloor/seed.sql (F2 + elevator E1 + OB-P4-CROSS cross-floor order)
--   3) THIS file's "E1 cost" section below              (Space P5: gives elevator E1 its time cost)
--
-- WHY a bootstrap section: on the ORIGINAL QA machine the QAWH site chain
-- (site/F1/zone A/rack R1) was created by hand via the editor UI, so the sp3/sp4
-- seeds referenced those GUIDs but never created them. On a FRESH Docker volume
-- (this machine) the QA db is bootstrapped from scratch, so we must materialize
-- that chain here before the sp3/sp4 seeds can resolve their FK GUIDs.
--
-- The GUIDs below MUST match the ones hard-coded in the sp3/sp4 seeds:
--   Tenant 00000000-0000-0000-0000-0000000000A1 (default/admin tenant, DEV-visible)
--   Site QAWH   F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE
--   Floor F1    5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F  (Level 1)
--   Zone  A     4D426CAE-AAE6-4CC5-91A1-370549113A59  (on F1)
--   Rack  R1    0A000000-0000-0000-0000-000000000001  (on F1 / Zone A)
--
-- Idempotent (every insert guarded by IF NOT EXISTS). ASCII only (sqlcmd safe).
-- SET QUOTED_IDENTIFIER ON required (Space_Location has a filtered unique index).
--
-- Run (this machine — Docker SQL Server in WSL2):
--   PW from C:\CP6\.env (MSSQL_SA_PASSWORD)
--   wsl -d Ubuntu -u root -- sh -c \
--     "docker exec -i cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<PW>' -C -d CP6DB_SpaceQA \
--       < /mnt/c/CP6/docs/superpowers/qa/space-p5-traversal-cost/seed.sql"

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @Tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';
DECLARE @Site   uniqueidentifier = 'F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE'; -- QAWH
DECLARE @F1     uniqueidentifier = '5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F'; -- floor F1 (Level 1)
DECLARE @ZoneA  uniqueidentifier = '4D426CAE-AAE6-4CC5-91A1-370549113A59'; -- zone A on F1
DECLARE @RackR1 uniqueidentifier = '0A000000-0000-0000-0000-000000000001'; -- rack R1 on F1 / zone A
DECLARE @Now    datetime2 = SYSUTCDATETIME();

-- =====================================================================
-- BOOTSTRAP: QAWH site chain (site -> floor F1 -> zone A -> rack R1)
-- Must run BEFORE the sp3 + sp4 seeds.
-- =====================================================================

-- 1) Site QAWH
IF NOT EXISTS (SELECT 1 FROM Space_Site WHERE Id=@Site)
  INSERT INTO Space_Site (Id, SiteCode, SiteName, Enable, CreateDate, TenantId, IsDeleted)
  VALUES (@Site, 'QAWH', 'QA Warehouse', 1, @Now, @Tenant, 0);

-- 2) Floor F1 (Level 1, height 6000)
IF NOT EXISTS (SELECT 1 FROM Space_Floor WHERE Id=@F1)
  INSERT INTO Space_Floor (Id, SiteId, Level, FloorCode, FloorName, Height, UnderlayOffsetX, UnderlayOffsetY, OriginX, OriginY, CreateDate, TenantId, IsDeleted)
  VALUES (@F1, @Site, 1, 'F1', '1F', 6000, 0, 0, 0, 0, @Now, @Tenant, 0);

-- 3) Zone A on F1
IF NOT EXISTS (SELECT 1 FROM Space_Zone WHERE Id=@ZoneA)
  INSERT INTO Space_Zone (Id, FloorId, ZoneCode, ZoneName, ZoneType, Polygon, Enable, CreateDate, TenantId, IsDeleted)
  VALUES (@ZoneA, @F1, 'A', 'A Zone', 1, '[]', 1, @Now, @Tenant, 0);

-- 4) Rack R1 on F1 / Zone A (SP3 locations need a valid RackId)
IF NOT EXISTS (SELECT 1 FROM Space_Rack WHERE Id=@RackR1)
  INSERT INTO Space_Rack (Id, ZoneId, FloorId, RackCode, X, Y, Z, RotationZ, Cols, Levels, DepthCount, CellW, CellH, CellD, Enable, CreateDate, TenantId, IsDeleted)
  VALUES (@RackR1, @ZoneA, @F1, 'R1', 0, 0, 0, 0, 1, 1, 1, 1000, 1000, 1000, 1, @Now, @Tenant, 0);

-- =====================================================================
-- E1 COST (Space P5): give elevator E1 its traversal time cost.
-- Requires the sp4 seed to have created connector E1 first.
--   WaitSec           = boarding/door fixed cost (charged once on the vertical edge)
--   TravelSecPerFloor = per-level travel cost (x |Level diff| of the two stops)
-- Baseline QA values: 20s wait + 6s/floor.
-- =====================================================================
UPDATE Space_Connector SET WaitSec=20, TravelSecPerFloor=6 WHERE ConnectorCode='E1';

-- --- Cost-effect variant (acceptance #3): make the elevator expensive so the
--     what-if optimizer changes the optimized order / savings%. Run this, reload
--     the stacked viewer, capture 02-cost-effect.png, then restore to 20/6 above.
-- UPDATE Space_Connector SET WaitSec=120, TravelSecPerFloor=60 WHERE ConnectorCode='E1';

-- ===== verify =====
SELECT 'site' AS kind, SiteCode, SiteName FROM Space_Site WHERE Id=@Site;
SELECT 'floors' AS kind, FloorCode, Level FROM Space_Floor WHERE SiteId=@Site ORDER BY Level;
SELECT 'connector E1 cost' AS kind, ConnectorCode, WaitSec, TravelSecPerFloor FROM Space_Connector WHERE ConnectorCode='E1';
