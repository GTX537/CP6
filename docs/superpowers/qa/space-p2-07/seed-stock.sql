-- 演示：5 态各一例（库位编码替换为 QA 中实际已发布编码）
DECLARE @T uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';
-- 有货
INSERT INTO T_Stock (Id,TenantId,WarehouseCd,LocationCd,ProductCd,LotNo,PhysicalQty,AllocatedQty,AvailableQty,QcStatus,OwnerType,IsDeleted,CreateDate)
VALUES (NEWID(),@T,'W1','A-01-01-01','CARTON-A4','',5,0,5,'PENDING','SELF',0,GETDATE());
-- 满（量>=容量）
INSERT INTO T_Stock (Id,TenantId,WarehouseCd,LocationCd,ProductCd,LotNo,PhysicalQty,AllocatedQty,AvailableQty,QcStatus,OwnerType,IsDeleted,CreateDate)
VALUES (NEWID(),@T,'W1','A-01-01-02','CARTON-A4','',50,0,50,'PENDING','SELF',0,GETDATE());
-- Location 容量 + 锁定示例
UPDATE T_Location SET CapacityQty=10 WHERE LocationCd='A-01-01-01';
UPDATE T_Location SET CapacityQty=50 WHERE LocationCd='A-01-01-02';
UPDATE T_Location SET IsBlocked=1 WHERE LocationCd='A-01-01-03';   -- 锁定
-- 在拣：出库单 Picking + 明细
DECLARE @OB nvarchar(20)='OB-QA-001';
INSERT INTO T_OutboundOrder (Id,TenantId,OutboundNo,OutboundType,WarehouseCd,PlannedDate,Status,Priority,IsDeleted,CreateDate)
VALUES (NEWID(),@T,@OB,2,'W1',GETDATE(),3,1,0,GETDATE());            -- Status=3 Picking
INSERT INTO T_OutboundOrderDetail (Id,TenantId,OutboundNo,LineNo,ProductCd,RequiredQty,AllocatedQty,ShippedQty,LocationCd,IsDeleted,CreateDate)
VALUES (NEWID(),@T,@OB,1,'CARTON-A4',5,5,0,'A-01-01-04',0,GETDATE());
INSERT INTO T_Stock (Id,TenantId,WarehouseCd,LocationCd,ProductCd,LotNo,PhysicalQty,AllocatedQty,AvailableQty,QcStatus,OwnerType,IsDeleted,CreateDate)
VALUES (NEWID(),@T,'W1','A-01-01-04','CARTON-A4','',8,5,3,'PENDING','SELF',0,GETDATE());
