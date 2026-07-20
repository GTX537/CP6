/* ============================================================
 * Space P2.5 货场分析 / 控制塔：菜单、权限与界面 i18n
 * Target: SQL Server / CP6DB
 * Idempotent: MenuKey + TenantId/MenuId/ActionCode + LangKey
 * Prerequisite: docs/seeds/space-menu-seed.sql (parent MenuId 900)
 * ============================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRY
BEGIN TRANSACTION;

DECLARE @MenuId INT = 907;

IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 900)
    THROW 51000, 'Space parent menu 900 is missing. Run space-menu-seed.sql first.', 1;

IF EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = @MenuId AND MenuKey <> N'space-control-tower')
    THROW 51001, 'MenuId 907 is already occupied by another menu.', 1;

IF EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuKey = N'space-control-tower')
    SELECT @MenuId = MenuId FROM Sys_Menus WHERE MenuKey = N'space-control-tower';
ELSE
    INSERT INTO Sys_Menus
        (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES
        (@MenuId, N'货场控制塔', N'/space/control-tower', N'space-control-tower', N'DataAnalysis', 900, 907, 1, SYSDATETIME());

-- Keep the canonical route stable when this seed is re-applied.
UPDATE Sys_Menus
SET MenuName = N'货场控制塔', RoutePath = N'/space/control-tower', Icon = N'DataAnalysis',
    ParentId = 900, OrderNo = 907, Enable = 1
WHERE MenuId = @MenuId;

INSERT INTO Sys_RoleMenus (TenantId, RoleId, MenuId)
SELECT t.Id, 1, @MenuId
FROM Sys_Tenants t
WHERE NOT EXISTS
(
    SELECT 1 FROM Sys_RoleMenus rm
    WHERE rm.TenantId = t.Id AND rm.RoleId = 1 AND rm.MenuId = @MenuId
);

DECLARE @Actions TABLE
(
    ActionCode NVARCHAR(50) NOT NULL,
    ActionName NVARCHAR(100) NOT NULL,
    Sort INT NOT NULL
);
INSERT INTO @Actions VALUES
    (N'view',   N'查看控制塔', 1),
    (N'manage', N'管理分析设置与重算', 2);

INSERT INTO Sys_MenuAction (Id, MenuId, ActionCode, ActionName, Sort, CreateDate, TenantId)
SELECT NEWID(), @MenuId, a.ActionCode, a.ActionName, a.Sort, SYSDATETIME(), t.Id
FROM @Actions a
CROSS JOIN Sys_Tenants t
WHERE NOT EXISTS
(
    SELECT 1 FROM Sys_MenuAction ma
    WHERE ma.TenantId = t.Id AND ma.MenuId = @MenuId AND ma.ActionCode = a.ActionCode
);

INSERT INTO Sys_RoleAction (Id, RoleId, MenuId, ActionCode, CreateDate, TenantId)
SELECT NEWID(), 1, @MenuId, a.ActionCode, SYSDATETIME(), t.Id
FROM @Actions a
CROSS JOIN Sys_Tenants t
WHERE NOT EXISTS
(
    SELECT 1 FROM Sys_RoleAction ra
    WHERE ra.TenantId = t.Id AND ra.RoleId = 1
      AND ra.MenuId = @MenuId AND ra.ActionCode = a.ActionCode
);

IF OBJECT_ID('tempdb..#spaceP25I18n') IS NOT NULL DROP TABLE #spaceP25I18n;
CREATE TABLE #spaceP25I18n
(
    LangKey NVARCHAR(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
    ZhCN NVARCHAR(500), ZhTW NVARCHAR(500), En NVARCHAR(500), Ja NVARCHAR(500), Ko NVARCHAR(500)
);

INSERT INTO #spaceP25I18n VALUES
    (N'space.home.controlTower', N'控制塔', N'控制塔', N'Control Tower', N'コントロールタワー', N'컨트롤 타워'),
    (N'space.controlTower.title', N'货场控制塔', N'貨場控制塔', N'Yard Control Tower', N'ヤード・コントロールタワー', N'야드 컨트롤 타워'),
    (N'space.controlTower.subtitle', N'选择站点进入实时三维运营视图', N'選擇站點進入即時三維營運視圖', N'Select a site to open its live 3D operations view', N'サイトを選択してリアルタイム3D運用ビューを開きます', N'사이트를 선택해 실시간 3D 운영 화면을 엽니다'),
    (N'space.controlTower.refresh', N'刷新', N'重新整理', N'Refresh', N'更新', N'새로 고침'),
    (N'space.controlTower.noPermission', N'没有控制塔查看权限', N'沒有控制塔檢視權限', N'You do not have permission to view the control tower', N'コントロールタワーを表示する権限がありません', N'컨트롤 타워 조회 권한이 없습니다'),
    (N'space.controlTower.retry', N'重试', N'重試', N'Retry', N'再試行', N'다시 시도'),
    (N'space.controlTower.open', N'进入控制塔', N'進入控制塔', N'Open Control Tower', N'コントロールタワーを開く', N'컨트롤 타워 열기'),
    (N'space.controlTower.empty', N'暂无可用站点', N'暫無可用站點', N'No sites are available', N'利用可能なサイトがありません', N'사용 가능한 사이트가 없습니다'),
    (N'space.controlTower.loadFailed', N'站点加载失败，请稍后重试', N'站點載入失敗，請稍後重試', N'Failed to load sites; please try again', N'サイトの読み込みに失敗しました。再試行してください', N'사이트를 불러오지 못했습니다. 다시 시도하세요');

MERGE Sys_Langs AS target
USING #spaceP25I18n AS source
ON target.LangKey = source.LangKey AND target.TenantId IS NULL
WHEN MATCHED THEN UPDATE SET
    target.ZhCN = source.ZhCN, target.ZhTW = source.ZhTW, target.En = source.En,
    target.Ja = source.Ja, target.Ko = source.Ko
WHEN NOT MATCHED BY TARGET THEN
    INSERT (LangKey, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (source.LangKey, source.ZhCN, source.ZhTW, source.En, source.Ja, source.Ko);

DROP TABLE #spaceP25I18n;
COMMIT TRANSACTION;
PRINT 'Space P2.5 analytics/control-tower seed completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT MenuId, MenuName, RoutePath, MenuKey
FROM Sys_Menus WHERE MenuKey = N'space-control-tower';
SELECT TenantId, MenuId, ActionCode
FROM Sys_MenuAction
WHERE MenuId = (SELECT MenuId FROM Sys_Menus WHERE MenuKey = N'space-control-tower')
ORDER BY TenantId, ActionCode;
