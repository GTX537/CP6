using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

internal static class InitialAdministratorSeed
{
    internal static async Task EnsurePostgreSqlAsync(CP6Context db, IPasswordHasher hasher, string password, CancellationToken ct)
    {
        var users = db.Sys_Users.IgnoreQueryFilters();
        if (await users.AnyAsync(ct)) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The native authority fence serializes every tenant and account write. The platform
        // survivor trigger also acquires its existing root when the enabled admin is inserted.
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        var tenants = await db.Sys_Tenants.AsNoTracking().Take(2).ToArrayAsync(ct);
        if (tenants is not [{ Enable: true, ExpireDate: null, TenantCode: "DEFAULT" } tenant]
            || tenant.Id != TenantContext.DefaultTenant || await users.AnyAsync(ct)) return;

        var role = await db.Sys_Roles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            row => row.TenantId == tenant.Id && row.RoleId == 1, ct);
        if (role is null)
        {
            var existingMenus = await db.Sys_Menus.AsNoTracking().ToDictionaryAsync(menu => menu.MenuId, ct);
            db.Sys_Roles.Add(CreateRole(tenant.Id));
            AddNavigationForNewRole(db, tenant.Id, existingMenus);
        }
        else if (!role.Enable || role.RoleName != "管理员")
            throw new InvalidOperationException("POSTGRESQL_FIRST_ADMIN_ROLE_UNAVAILABLE");

        db.Sys_Users.Add(CreateAccount(tenant.Id, hasher.Hash(password), isPlatformAdmin: true));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    internal static Sys_Role CreateRole(Guid tenantId) => new()
    {
        TenantId = tenantId,
        RoleId = 1,
        RoleName = "管理员",
        Description = "拥有全部权限",
        Enable = true,
        OrderNo = 0
    };

    internal static Sys_User CreateAccount(Guid tenantId, string password, bool isPlatformAdmin = false) => new()
    {
        TenantId = tenantId,
        UserName = "admin",
        Password = password,
        NickName = "管理员",
        RoleId = 1,
        Enable = true,
        IsPlatformAdmin = isPlatformAdmin,
        CreateDate = DateTime.Now
    };

    internal static void AddNavigationForNewRole(CP6Context db, Guid tenantId,
        IReadOnlyDictionary<int, Sys_Menu>? existingMenus = null)
    {
        Sys_Menu[] defaults =
        [
            new Sys_Menu { MenuId = 2, MenuName = "仪表盘", RoutePath = "/dashboard", Icon = "Odometer", OrderNo = 0, Enable = true },
            new Sys_Menu { MenuId = 100, MenuName = "系统管理", Icon = "Setting", OrderNo = 100, Enable = true },
            new Sys_Menu { MenuId = 101, MenuName = "角色管理", RoutePath = "/role", Icon = "UserFilled", ParentId = 100, OrderNo = 101, Enable = true },
            new Sys_Menu { MenuId = 102, MenuName = "菜单管理", RoutePath = "/menu", Icon = "Menu", ParentId = 100, OrderNo = 102, Enable = true },
            new Sys_Menu { MenuId = 103, MenuName = "权限分配", RoutePath = "/permission", Icon = "Lock", ParentId = 100, OrderNo = 103, Enable = true },
            new Sys_Menu { MenuId = 104, MenuName = "用户管理", RoutePath = "/user", Icon = "User", ParentId = 100, OrderNo = 104, Enable = true },
            new Sys_Menu { MenuId = 105, MenuName = "多语言管理", RoutePath = "/lang", Icon = "ChatLineSquare", ParentId = 100, OrderNo = 105, Enable = true },
            new Sys_Menu { MenuId = 106, MenuName = "数据字典", RoutePath = "/dict", Icon = "Collection", ParentId = 100, OrderNo = 106, Enable = true },
            new Sys_Menu { MenuId = 107, MenuName = "操作日志", RoutePath = "/operlog", Icon = "Notebook", ParentId = 100, OrderNo = 107, Enable = true },
            new Sys_Menu { MenuId = 200, MenuName = "販売管理(ERP)", Icon = "ShoppingBag", OrderNo = 200, Enable = true },
            new Sys_Menu { MenuId = 201, MenuName = "見積計算書 照会", RoutePath = "/estimate-calc-list", Icon = "List", ParentId = 200, OrderNo = 201, Enable = true },
            new Sys_Menu { MenuId = 202, MenuName = "見積計算書 登録", RoutePath = "/estimate-calc", Icon = "Money", ParentId = 200, OrderNo = 202, Enable = true },
            new Sys_Menu { MenuId = 203, MenuName = "御見積書 一覧", RoutePath = "/quotation-list", Icon = "Tickets", ParentId = 200, OrderNo = 203, Enable = true },
            new Sys_Menu { MenuId = 204, MenuName = "御見積書 登録", RoutePath = "/quotation", Icon = "EditPen", ParentId = 200, OrderNo = 204, Enable = true },
            new Sys_Menu { MenuId = 205, MenuName = "製品マスタ 一覧", RoutePath = "/product-list", Icon = "Goods", ParentId = 200, OrderNo = 205, Enable = true },
            new Sys_Menu { MenuId = 206, MenuName = "製品マスタ 登録", RoutePath = "/product", Icon = "Box", ParentId = 200, OrderNo = 206, Enable = true },
            new Sys_Menu { MenuId = 207, MenuName = "受注一覧照会", RoutePath = "/order-list", Icon = "Files", ParentId = 200, OrderNo = 207, Enable = true },
            new Sys_Menu { MenuId = 208, MenuName = "受注入力", RoutePath = "/order", Icon = "DocumentAdd", ParentId = 200, OrderNo = 208, Enable = true },
            new Sys_Menu { MenuId = 209, MenuName = "単価訂正", RoutePath = "/order-price-correction", Icon = "PriceTag", ParentId = 200, OrderNo = 209, Enable = true },
            new Sys_Menu { MenuId = 210, MenuName = "FSC チェックシート", RoutePath = "/fsc-checklist", Icon = "Document", ParentId = 200, OrderNo = 210, Enable = true },
            new Sys_Menu { MenuId = 211, MenuName = "取引先マスタ 一覧", RoutePath = "/business-partner-list", Icon = "OfficeBuilding", ParentId = 200, OrderNo = 211, Enable = true },
            new Sys_Menu { MenuId = 212, MenuName = "取引先マスタ 登録", RoutePath = "/business-partner", Icon = "User", ParentId = 200, OrderNo = 212, Enable = true },
            new Sys_Menu { MenuId = 213, MenuName = "シート単価マスタ", RoutePath = "/sheet-unit-price", Icon = "Coin", ParentId = 200, OrderNo = 213, Enable = true },
            new Sys_Menu { MenuId = 214, MenuName = "版型/木型 一覧", RoutePath = "/plate-mold-list", Icon = "Tools", ParentId = 200, OrderNo = 214, Enable = true },
            new Sys_Menu { MenuId = 215, MenuName = "版型/木型 登録", RoutePath = "/plate-mold", Icon = "Stamp", ParentId = 200, OrderNo = 215, Enable = true }

        ];
        if (existingMenus is not null)
            ResolveDashboardId(defaults[0], existingMenus);

        foreach (var menu in defaults)
        {
            if (existingMenus is null || !existingMenus.TryGetValue(menu.MenuId, out var existing))
                db.Sys_Menus.Add(menu);
            else if (existing.ParentId != menu.ParentId || existing.RoutePath != menu.RoutePath
                     || (menu.RoutePath is null && existing.MenuName != menu.MenuName)
                     || (existing.MenuKey is not null && existing.MenuKey != menu.RoutePath?.Trim('/').Replace('/', '-')))
                throw new InvalidOperationException("POSTGRESQL_FIRST_ADMIN_NAVIGATION_CONFLICT");

            db.Sys_RoleMenus.Add(new Sys_RoleMenu { TenantId = tenantId, RoleId = 1, MenuId = menu.MenuId });
        }
    }

    private static void ResolveDashboardId(Sys_Menu dashboard, IReadOnlyDictionary<int, Sys_Menu> existingMenus)
    {
        var matching = existingMenus.Values.Where(menu => menu.RoutePath == dashboard.RoutePath || menu.MenuKey == "dashboard")
            .Take(2).ToArray();
        if (matching.Length > 1)
            throw new InvalidOperationException("POSTGRESQL_FIRST_ADMIN_NAVIGATION_CONFLICT");
        if (matching.Length == 1)
        {
            dashboard.MenuId = matching[0].MenuId;
            return;
        }

        if (!existingMenus.ContainsKey(dashboard.MenuId)) return;

        // Dashboard consumers use its route/key. Start above the fixed startup
        // menu numbers when a migration has already allocated 2.
        var freeId = 1000;
        while (existingMenus.ContainsKey(freeId)) freeId = checked(freeId + 1);
        dashboard.MenuId = freeId;
    }
}
