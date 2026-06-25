SET NOCOUNT ON;
DECLARE @TB uniqueidentifier = '00000000-0000-0000-0000-0000000000B1';
DECLARE @TC uniqueidentifier = '00000000-0000-0000-0000-0000000000C1';
DECLARE @now datetime2 = SYSUTCDATETIME();
DELETE FROM Sys_RoleAction WHERE MenuId=116 AND TenantId IN (@TB,@TC);
INSERT INTO Sys_RoleAction (Id,RoleId,MenuId,ActionCode,CreateDate,TenantId) VALUES
 (NEWID(),1,116,'query',@now,@TB),(NEWID(),1,116,'edit',@now,@TB),
 (NEWID(),1,116,'query',@now,@TC),(NEWID(),1,116,'edit',@now,@TC);
SELECT RoleId,MenuId,ActionCode,CONVERT(varchar(36),TenantId) AS TenantId FROM Sys_RoleAction WHERE MenuId=116 ORDER BY TenantId;
