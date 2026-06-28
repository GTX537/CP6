SET NOCOUNT ON;
DECLARE @T uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';
DELETE FROM T_OutboundOrderDetail WHERE OutboundNo = 'OB-QA-001';
DELETE FROM T_OutboundOrder WHERE OutboundNo = 'OB-QA-001';
DELETE FROM T_Stock WHERE LocationCd IN ('A-01-01-01','A-01-01-02','A-01-02-01','A-01-02-02');
DELETE FROM T_Location WHERE LocationCd IN ('A-01-01-01','A-01-01-02','A-01-02-01','A-01-02-02');
INSERT INTO T_Location (Id,TenantId,LocationCd,WarehouseCd,LocationLevel,CapacityQty,IsPickable,IsBlocked,IsDeleted,CreateDate) VALUES
 (NEWID(),@T,'A-01-01-01','W1',5,10,1,0,0,GETDATE()),
 (NEWID(),@T,'A-01-01-02','W1',5,50,1,0,0,GETDATE()),
 (NEWID(),@T,'A-01-02-01','W1',5,10,1,1,0,GETDATE()),
 (NEWID(),@T,'A-01-02-02','W1',5,20,1,0,0,GETDATE());
INSERT INTO T_Stock (Id,TenantId,WarehouseCd,LocationCd,ProductCd,LotNo,PhysicalQty,AllocatedQty,AvailableQty,RecallFlag,OwnerType,QcStatus,IsDeleted,CreateDate) VALUES
 (NEWID(),@T,'W1','A-01-01-01','CARTON-A4','',5,0,5,0,'SELF','PENDING',0,GETDATE()),
 (NEWID(),@T,'W1','A-01-01-02','CARTON-A4','',50,0,50,0,'SELF','PENDING',0,GETDATE()),
 (NEWID(),@T,'W1','A-01-02-01','CARTON-A4','',5,0,5,0,'SELF','PENDING',0,GETDATE()),
 (NEWID(),@T,'W1','A-01-02-02','CARTON-A4','',8,5,3,0,'SELF','PENDING',0,GETDATE());
INSERT INTO T_OutboundOrder (Id,TenantId,OutboundNo,OutboundType,WarehouseCd,PlannedDate,Status,Priority,IsDeleted,CreateDate)
 VALUES (NEWID(),@T,'OB-QA-001',2,'W1',GETDATE(),3,1,0,GETDATE());
INSERT INTO T_OutboundOrderDetail (Id,TenantId,OutboundNo,[LineNo],ProductCd,RequiredQty,AllocatedQty,ShippedQty,LocationCd,IsDeleted,CreateDate)
 VALUES (NEWID(),@T,'OB-QA-001',1,'CARTON-A4',5,5,0,'A-01-02-02',0,GETDATE());
SELECT 'stock=' + CAST(COUNT(*) AS varchar) FROM T_Stock;
SELECT 'loc=' + CAST(COUNT(*) AS varchar) FROM T_Location;
SELECT 'ob=' + CAST(COUNT(*) AS varchar) FROM T_OutboundOrder WHERE OutboundNo = 'OB-QA-001';
