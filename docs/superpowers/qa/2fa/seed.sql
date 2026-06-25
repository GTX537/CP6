-- 2FA QA seed (SQL Server, CP6DB). Idempotent.
-- Gives the default-tenant admin an email (for email-OTP path) and adds a
-- fresh 2FA-OFF user `tfaforce` in the DEFAULT tenant (copies admin's BCrypt
-- hash for password "123456") to exercise the forced-enroll flow.
SET NOCOUNT ON;

UPDATE Sys_Users SET Email = 'admin@default.test'
WHERE UserName = 'admin' AND Email IS NULL;

DELETE FROM Sys_Users WHERE UserName = 'tfaforce';
INSERT INTO Sys_Users
  (Id, UserName, Password, NickName, RoleId, Enable, CreateDate, TenantId,
   FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, Email)
SELECT NEWID(), 'tfaforce', Password, N'强制用户', RoleId, 1, SYSDATETIME(), TenantId,
       0, 0, 0, 0, 'tfaforce@default.test'
FROM Sys_Users WHERE UserName = 'admin';

-- Tenant 2FA mode is toggled per test via the policy API or directly:
--   UPDATE Sys_Tenants SET TwoFactorMode = 2 WHERE TenantCode = 'DEFAULT';  -- forced
--   UPDATE Sys_Tenants SET TwoFactorMode = 0 WHERE TenantCode = 'DEFAULT';  -- restore

-- Restore clean dev state after QA:
--   UPDATE Sys_Tenants SET TwoFactorMode = 0 WHERE TenantCode = 'DEFAULT';
--   UPDATE Sys_Users SET TwoFactorEnabled=0, TwoFactorSecret=NULL, TwoFactorEnrolledAt=NULL
--     WHERE UserName IN ('admin','tfaforce');
