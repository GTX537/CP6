SET NOCOUNT ON;
DECLARE @TB uniqueidentifier = '00000000-0000-0000-0000-0000000000B1';
DECLARE @TC uniqueidentifier = '00000000-0000-0000-0000-0000000000C1';
DECLARE @pwd nvarchar(max) = (SELECT Password FROM Sys_Users WHERE UserName='admin');
DECLARE @now datetime2 = SYSUTCDATETIME();

DELETE FROM Sys_TenantSsoConfigs WHERE TenantId IN (@TB,@TC);
DELETE FROM Sys_Users WHERE UserName IN ('sso_admB','sso_admC','sso_userC') OR Email='sso.jit@example.com';
DELETE FROM Sys_Tenants WHERE Id IN (@TB,@TC);

INSERT INTO Sys_Tenants (Id,TenantCode,TenantName,Enable,CreateDate,Remark)
VALUES (@TB,'TENANTB',N'QA租户B-SSO非强制',1,@now,N'T10 QA'),
       (@TC,'TENANTC',N'QA租户C-SSO强制',1,@now,N'T10 QA');

INSERT INTO Sys_Users (Id,UserName,Password,NickName,RoleId,Enable,Email,TenantId,FailedLoginCount,MustChangePassword,PasswordChangedAt,AllowPasswordFallback,CreateDate)
VALUES
 (NEWID(),'sso_admB',@pwd,N'B租户管理员',1,1,'admb@tenantb.test',@TB,0,0,@now,1,@now),
 (NEWID(),'sso_admC',@pwd,N'C租户管理员(break-glass)',1,1,'admc@tenantc.test',@TC,0,0,@now,1,@now),
 (NEWID(),'sso_userC',@pwd,N'C租户普通用户',3,1,'userc@tenantc.test',@TC,0,0,@now,0,@now);

SELECT TenantCode, CONVERT(varchar(36),Id) AS TenantId FROM Sys_Tenants WHERE Id IN (@TB,@TC);
SELECT UserName, CONVERT(varchar(36),TenantId) AS TenantId, RoleId, AllowPasswordFallback FROM Sys_Users WHERE UserName IN ('sso_admB','sso_admC','sso_userC');
