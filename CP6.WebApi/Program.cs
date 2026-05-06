using System.Text;
using CP6.Core.BaseProvider;
using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using Microsoft.Data.SqlClient;
using CP6.Core.Utilities;
using CP6.WebApi.Filters;
using CP6.WebApi.Hubs;

var builder = WebApplication.CreateBuilder(args);

// 1. 注册控制器（全局注册 OperLogFilter）
builder.Services.AddScoped<OperLogFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<OperLogFilter>();
});

// 1.1 注册 SignalR
builder.Services.AddSignalR();

// 2. 注册 Swagger
builder.Services.AddOpenApi();

// 3. 注册数据库上下文
builder.Services.AddDbContext<CP6Context>(options =>
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
        // EF Core 10：迁移已包含模型，但运行时偶发误报 PendingModelChangesWarning（已用 ef migrations has-pending-model-changes 确认无差异）
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// 3.1 注册 Dapper 用的 IDbConnection（每次请求新建连接）
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3.2 注册缓存（开发用 Memory，生产切 Redis 只需改这里）
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    // 生产模式：Redis（配置了连接字符串就用 Redis）
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConn;
        options.InstanceName = "CP6:";  // Key 前缀，区分不同应用
    });
}
else
{
    // 开发模式：内存缓存（零配置，行为和 Redis 一致）
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddSingleton<CacheService>();

// 3.3 注册 RabbitMQ（消息队列）
builder.Services.AddSingleton<RabbitMQService>();
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.OperLogConsumer>();

// 4. 注册仓储和服务（依赖注入）
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
builder.Services.AddScoped<IService<Article>, ArticleService>();

// 4.1 MSBBPA010 見積計算書 相关服务
builder.Services.AddScoped<IEstimateCalcService, EstimateCalcService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();

// 4.2 MSBBPA030/040 御見積書 相关服务
builder.Services.AddScoped<IQuotationService, QuotationService>();

// 4.3 MSBBPA050/060 Web 製品マスタ 相关服务
builder.Services.AddScoped<IProductService, ProductService>();
// 仕掛チェック：mcframe7 連携無し時は NoOp 実装（Phase 3 で実装差替え）
builder.Services.AddScoped<IWipCheckService, NoOpWipCheckService>();

// 4.4 MSBBPA070/080/090 Web 受注 相关服务
builder.Services.AddScoped<IOrderService, OrderService>();
// PA090 POWER EGG WF 起票：実環境では HTTP 実装に差し替え
builder.Services.AddScoped<IPowerEggWorkflowService, NoOpPowerEggWorkflowService>();

// 4.5 MSBBPA100/110/120 FSC・取引先マスタ
builder.Services.AddScoped<IBusinessPartnerService, BusinessPartnerService>();
builder.Services.AddScoped<IFscChecklistService, FscChecklistService>();

// 4.6 MSBBPA130/140/150 シート単価・版型/木型マスタ
builder.Services.AddScoped<ISheetUnitPriceService, SheetUnitPriceService>();
builder.Services.AddScoped<IPlateMoldService, PlateMoldService>();
// PA140 §8 PE API 版型発注書連携：実環境では HTTP 実装に差し替え
builder.Services.AddScoped<IPlateMoldPeApiService, NoOpPlateMoldPeApiService>();

// 5. 配置 JWT 认证
var jwt = builder.Configuration.GetSection("JWT");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!))
        };
    });
builder.Services.AddAuthorization();

// 6. 注册 CORS（SignalR 需要 AllowCredentials + 指定 Origin）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.SetIsOriginAllowed(_ => true)  // 允许所有来源
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());  // SignalR WebSocket 需要
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 7. 初始化种子数据（首次启动时自动创建）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CP6Context>();

    // Docker 环境下自动创建数据库并应用所有迁移
    db.Database.Migrate();

    if (!db.Sys_Menus.Any())
    {
        // 菜单ID和角色ID都是自定义的，手动指定
        db.Sys_Menus.AddRange(
            new Sys_Menu { MenuId = 1, MenuName = "文章管理", RoutePath = "/article", Icon = "Document", OrderNo = 1, Enable = true },
            new Sys_Menu { MenuId = 2, MenuName = "仪表盘", RoutePath = "/dashboard", Icon = "Odometer", OrderNo = 0, Enable = true },
            new Sys_Menu { MenuId = 100, MenuName = "系统管理", Icon = "Setting", OrderNo = 100, Enable = true },
            new Sys_Menu { MenuId = 101, MenuName = "角色管理", RoutePath = "/role", Icon = "UserFilled", ParentId = 100, OrderNo = 101, Enable = true },
            new Sys_Menu { MenuId = 102, MenuName = "菜单管理", RoutePath = "/menu", Icon = "Menu", ParentId = 100, OrderNo = 102, Enable = true },
            new Sys_Menu { MenuId = 103, MenuName = "权限分配", RoutePath = "/permission", Icon = "Lock", ParentId = 100, OrderNo = 103, Enable = true },
            new Sys_Menu { MenuId = 104, MenuName = "用户管理", RoutePath = "/user", Icon = "User", ParentId = 100, OrderNo = 104, Enable = true },
            new Sys_Menu { MenuId = 105, MenuName = "多语言管理", RoutePath = "/lang", Icon = "ChatLineSquare", ParentId = 100, OrderNo = 105, Enable = true },
            new Sys_Menu { MenuId = 106, MenuName = "数据字典", RoutePath = "/dict", Icon = "Collection", ParentId = 100, OrderNo = 106, Enable = true },
            new Sys_Menu { MenuId = 107, MenuName = "操作日志", RoutePath = "/operlog", Icon = "Notebook", ParentId = 100, OrderNo = 107, Enable = true },
            new Sys_Menu { MenuId = 200, MenuName = "販売管理", Icon = "ShoppingBag", OrderNo = 200, Enable = true },
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
        );

        // 管理员角色 RoleId = 1
        db.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "管理员", Description = "拥有全部权限", Enable = true, OrderNo = 0 });

        // 给管理员角色分配所有菜单
        db.Sys_RoleMenus.AddRange(
            new Sys_RoleMenu { RoleId = 1, MenuId = 1 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 2 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 100 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 101 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 102 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 103 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 104 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 105 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 106 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 107 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 200 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 201 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 202 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 203 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 204 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 205 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 206 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 207 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 208 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 209 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 210 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 211 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 212 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 213 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 214 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 215 }
        );

        // 管理员账号绑定 RoleId = 1
        if (!db.Sys_Users.Any())
        {
            db.Sys_Users.Add(new Sys_User
            {
                UserName = "admin",
                Password = "123456",
                NickName = "管理员",
                RoleId = 1,
                Enable = true,
                CreateDate = DateTime.Now
            });
        }

        db.SaveChanges();
    }
    // 补充：如果已有菜单数据但缺少用户管理菜单，追加插入
    if (!db.Sys_Menus.Any(m => m.MenuId == 107))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 107, MenuName = "操作日志", RoutePath = "/operlog", Icon = "Notebook", ParentId = 100, OrderNo = 107, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 107 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 106))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 106, MenuName = "数据字典", RoutePath = "/dict", Icon = "Collection", ParentId = 100, OrderNo = 106, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 106 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 105))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 105, MenuName = "多语言管理", RoutePath = "/lang", Icon = "ChatLineSquare", ParentId = 100, OrderNo = 105, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 105 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 104))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 104, MenuName = "用户管理", RoutePath = "/user", Icon = "User", ParentId = 100, OrderNo = 104, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 104 });
        db.SaveChanges();
    }
    // MSBBPA010 販売管理 菜单（父）+ 見積計算書 照会/登録（子）
    if (!db.Sys_Menus.Any(m => m.MenuId == 200))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 200, MenuName = "販売管理", Icon = "ShoppingBag", OrderNo = 200, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 200 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 201))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 201, MenuName = "見積計算書 照会", RoutePath = "/estimate-calc-list", Icon = "List", ParentId = 200, OrderNo = 201, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 201 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 202))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 202, MenuName = "見積計算書 登録", RoutePath = "/estimate-calc", Icon = "Money", ParentId = 200, OrderNo = 202, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 202 });
        db.SaveChanges();
    }
    // MSBBPA030 / MSBBPA040 御見積書 一覧 / 登録
    if (!db.Sys_Menus.Any(m => m.MenuId == 203))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 203, MenuName = "御見積書 一覧", RoutePath = "/quotation-list", Icon = "Tickets", ParentId = 200, OrderNo = 203, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 203 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 204))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 204, MenuName = "御見積書 登録", RoutePath = "/quotation", Icon = "EditPen", ParentId = 200, OrderNo = 204, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 204 });
        db.SaveChanges();
    }
    // MSBBPA050 / MSBBPA060 製品マスタ 登録 / 一覧
    if (!db.Sys_Menus.Any(m => m.MenuId == 205))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 205, MenuName = "製品マスタ 一覧", RoutePath = "/product-list", Icon = "Goods", ParentId = 200, OrderNo = 205, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 205 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 206))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 206, MenuName = "製品マスタ 登録", RoutePath = "/product", Icon = "Box", ParentId = 200, OrderNo = 206, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 206 });
        db.SaveChanges();
    }
    // MSBBPA070 / 080 / 090 受注入力 / 一覧 / 単価訂正
    if (!db.Sys_Menus.Any(m => m.MenuId == 207))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 207, MenuName = "受注一覧照会", RoutePath = "/order-list", Icon = "Files", ParentId = 200, OrderNo = 207, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 207 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 208))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 208, MenuName = "受注入力", RoutePath = "/order", Icon = "DocumentAdd", ParentId = 200, OrderNo = 208, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 208 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 209))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 209, MenuName = "単価訂正", RoutePath = "/order-price-correction", Icon = "PriceTag", ParentId = 200, OrderNo = 209, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 209 });
        db.SaveChanges();
    }
    // MSBBPA100 / 110 / 120 FSC・取引先マスタ
    if (!db.Sys_Menus.Any(m => m.MenuId == 210))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 210, MenuName = "FSC チェックシート", RoutePath = "/fsc-checklist", Icon = "Document", ParentId = 200, OrderNo = 210, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 210 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 211))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 211, MenuName = "取引先マスタ 一覧", RoutePath = "/business-partner-list", Icon = "OfficeBuilding", ParentId = 200, OrderNo = 211, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 211 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 212))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 212, MenuName = "取引先マスタ 登録", RoutePath = "/business-partner", Icon = "User", ParentId = 200, OrderNo = 212, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 212 });
        db.SaveChanges();
    }
    // MSBBPA130 / 140 / 150 シート単価・版型/木型マスタ
    if (!db.Sys_Menus.Any(m => m.MenuId == 213))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 213, MenuName = "シート単価マスタ", RoutePath = "/sheet-unit-price", Icon = "Coin", ParentId = 200, OrderNo = 213, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 213 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 214))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 214, MenuName = "版型/木型 一覧", RoutePath = "/plate-mold-list", Icon = "Tools", ParentId = 200, OrderNo = 214, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 214 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 215))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 215, MenuName = "版型/木型 登録", RoutePath = "/plate-mold", Icon = "Stamp", ParentId = 200, OrderNo = 215, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 215 });
        db.SaveChanges();
    }

    if (!db.Sys_Users.Any())
    {
        db.Sys_Users.Add(new Sys_User
        {
            UserName = "admin",
            Password = "123456",
            NickName = "管理员",
            Enable = true,
            CreateDate = DateTime.Now
        });
        db.SaveChanges();
    }

    // 多语言词条种子数据
    if (!db.Sys_Langs.Any())
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "app.title", ZhCN = "CP6 管理系统", ZhTW = "CP6 管理系統", En = "CP6 Admin", Ja = "CP6 管理システム", Ko = "CP6 관리 시스템" },
            new Sys_Lang { LangKey = "login.title", ZhCN = "CP6 管理系统", ZhTW = "CP6 管理系統", En = "CP6 Admin", Ja = "CP6 管理システム", Ko = "CP6 관리 시스템" },
            new Sys_Lang { LangKey = "login.username", ZhCN = "请输入用户名", ZhTW = "請輸入使用者名稱", En = "Enter username", Ja = "ユーザー名を入力", Ko = "사용자명 입력" },
            new Sys_Lang { LangKey = "login.password", ZhCN = "请输入密码", ZhTW = "請輸入密碼", En = "Enter password", Ja = "パスワードを入力", Ko = "비밀번호 입력" },
            new Sys_Lang { LangKey = "login.button", ZhCN = "登 录", ZhTW = "登 入", En = "Login", Ja = "ログイン", Ko = "로그인" },
            new Sys_Lang { LangKey = "login.success", ZhCN = "登录成功", ZhTW = "登入成功", En = "Login successful", Ja = "ログイン成功", Ko = "로그인 성공" },
            new Sys_Lang { LangKey = "login.usernameRequired", ZhCN = "请输入用户名", ZhTW = "請輸入使用者名稱", En = "Please enter username", Ja = "ユーザー名を入力してください", Ko = "사용자명을 입력하세요" },
            new Sys_Lang { LangKey = "login.passwordRequired", ZhCN = "请输入密码", ZhTW = "請輸入密碼", En = "Please enter password", Ja = "パスワードを入力してください", Ko = "비밀번호를 입력하세요" },
            new Sys_Lang { LangKey = "layout.logout", ZhCN = "退出登录", ZhTW = "登出", En = "Logout", Ja = "ログアウト", Ko = "로그아웃" },
            new Sys_Lang { LangKey = "table.search", ZhCN = "请输入关键词搜索", ZhTW = "請輸入關鍵詞搜尋", En = "Search by keyword", Ja = "キーワードで検索", Ko = "키워드로 검색" },
            new Sys_Lang { LangKey = "table.add", ZhCN = "新增", ZhTW = "新增", En = "Add", Ja = "追加", Ko = "추가" },
            new Sys_Lang { LangKey = "table.delete", ZhCN = "删除", ZhTW = "刪除", En = "Delete", Ja = "削除", Ko = "삭제" },
            new Sys_Lang { LangKey = "table.edit", ZhCN = "编辑", ZhTW = "編輯", En = "Edit", Ja = "編集", Ko = "편집" },
            new Sys_Lang { LangKey = "table.operation", ZhCN = "操作", ZhTW = "操作", En = "Action", Ja = "操作", Ko = "작업" },
            new Sys_Lang { LangKey = "table.confirmDelete", ZhCN = "确定删除吗？", ZhTW = "確定刪除嗎？", En = "Are you sure to delete?", Ja = "削除してもよろしいですか？", Ko = "삭제하시겠습니까?" },
            new Sys_Lang { LangKey = "table.confirmBatchDelete", ZhCN = "确定删除选中的 {count} 条数据吗？", ZhTW = "確定刪除選中的 {count} 筆資料嗎？", En = "Are you sure to delete {count} selected items?", Ja = "選択した {count} 件のデータを削除しますか？", Ko = "선택한 {count}개 항목을 삭제하시겠습니까?" },
            new Sys_Lang { LangKey = "table.tip", ZhCN = "提示", ZhTW = "提示", En = "Tip", Ja = "確認", Ko = "확인" },
            new Sys_Lang { LangKey = "table.addSuccess", ZhCN = "新增成功", ZhTW = "新增成功", En = "Added successfully", Ja = "追加しました", Ko = "추가되었습니다" },
            new Sys_Lang { LangKey = "table.editSuccess", ZhCN = "修改成功", ZhTW = "修改成功", En = "Updated successfully", Ja = "更新しました", Ko = "수정되었습니다" },
            new Sys_Lang { LangKey = "table.deleteSuccess", ZhCN = "删除成功", ZhTW = "刪除成功", En = "Deleted successfully", Ja = "削除しました", Ko = "삭제되었습니다" },
            new Sys_Lang { LangKey = "table.selectFirst", ZhCN = "请先选择要删除的数据", ZhTW = "請先選擇要刪除的資料", En = "Please select data to delete first", Ja = "削除するデータを選択してください", Ko = "삭제할 데이터를 먼저 선택하세요" },
            new Sys_Lang { LangKey = "table.cancel", ZhCN = "取消", ZhTW = "取消", En = "Cancel", Ja = "キャンセル", Ko = "취소" },
            new Sys_Lang { LangKey = "table.confirm", ZhCN = "确定", ZhTW = "確定", En = "OK", Ja = "確定", Ko = "확인" },
            new Sys_Lang { LangKey = "table.select", ZhCN = "请选择", ZhTW = "請選擇", En = "Select", Ja = "選択してください", Ko = "선택하세요" },
            new Sys_Lang { LangKey = "article.title", ZhCN = "标题", ZhTW = "標題", En = "Title", Ja = "タイトル", Ko = "제목" },
            new Sys_Lang { LangKey = "article.author", ZhCN = "作者", ZhTW = "作者", En = "Author", Ja = "著者", Ko = "작성자" },
            new Sys_Lang { LangKey = "article.content", ZhCN = "内容", ZhTW = "內容", En = "Content", Ja = "内容", Ko = "내용" },
            new Sys_Lang { LangKey = "article.isPublished", ZhCN = "是否发布", ZhTW = "是否發佈", En = "Published", Ja = "公開済み", Ko = "게시 여부" },
            new Sys_Lang { LangKey = "article.createDate", ZhCN = "创建时间", ZhTW = "建立時間", En = "Created At", Ja = "作成日時", Ko = "생성일" },
            new Sys_Lang { LangKey = "user.userName", ZhCN = "用户名", ZhTW = "使用者名稱", En = "Username", Ja = "ユーザー名", Ko = "사용자명" },
            new Sys_Lang { LangKey = "user.password", ZhCN = "密码", ZhTW = "密碼", En = "Password", Ja = "パスワード", Ko = "비밀번호" },
            new Sys_Lang { LangKey = "user.nickName", ZhCN = "昵称", ZhTW = "暱稱", En = "Nickname", Ja = "ニックネーム", Ko = "닉네임" },
            new Sys_Lang { LangKey = "user.role", ZhCN = "角色", ZhTW = "角色", En = "Role", Ja = "役割", Ko = "역할" },
            new Sys_Lang { LangKey = "user.enable", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "user.createDate", ZhCN = "创建时间", ZhTW = "建立時間", En = "Created At", Ja = "作成日時", Ko = "생성일" },
            new Sys_Lang { LangKey = "role.roleId", ZhCN = "角色ID", ZhTW = "角色ID", En = "Role ID", Ja = "役割ID", Ko = "역할 ID" },
            new Sys_Lang { LangKey = "role.roleName", ZhCN = "角色名称", ZhTW = "角色名稱", En = "Role Name", Ja = "役割名", Ko = "역할명" },
            new Sys_Lang { LangKey = "role.description", ZhCN = "描述", ZhTW = "描述", En = "Description", Ja = "説明", Ko = "설명" },
            new Sys_Lang { LangKey = "role.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "role.enable", ZhCN = "是否启用", ZhTW = "是否啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "role.createDate", ZhCN = "创建时间", ZhTW = "建立時間", En = "Created At", Ja = "作成日時", Ko = "생성일" },
            new Sys_Lang { LangKey = "menu.title", ZhCN = "菜单管理", ZhTW = "選單管理", En = "Menu Management", Ja = "メニュー管理", Ko = "메뉴 관리" },
            new Sys_Lang { LangKey = "menu.addTop", ZhCN = "新增顶级菜单", ZhTW = "新增頂級選單", En = "Add Top Menu", Ja = "トップメニュー追加", Ko = "최상위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.addChild", ZhCN = "添加子菜单", ZhTW = "新增子選單", En = "Add Sub Menu", Ja = "サブメニュー追加", Ko = "하위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.editMenu", ZhCN = "编辑菜单", ZhTW = "編輯選單", En = "Edit Menu", Ja = "メニュー編集", Ko = "메뉴 편집" },
            new Sys_Lang { LangKey = "menu.addTopMenu", ZhCN = "新增顶级菜单", ZhTW = "新增頂級選單", En = "Add Top Menu", Ja = "トップメニュー追加", Ko = "최상위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.addSubMenu", ZhCN = "新增子菜单", ZhTW = "新增子選單", En = "Add Sub Menu", Ja = "サブメニュー追加", Ko = "하위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.menuId", ZhCN = "菜单ID", ZhTW = "選單ID", En = "Menu ID", Ja = "メニューID", Ko = "메뉴 ID" },
            new Sys_Lang { LangKey = "menu.menuName", ZhCN = "菜单名称", ZhTW = "選單名稱", En = "Menu Name", Ja = "メニュー名", Ko = "메뉴명" },
            new Sys_Lang { LangKey = "menu.routePath", ZhCN = "路由路径", ZhTW = "路由路徑", En = "Route Path", Ja = "ルートパス", Ko = "라우트 경로" },
            new Sys_Lang { LangKey = "menu.icon", ZhCN = "图标", ZhTW = "圖示", En = "Icon", Ja = "アイコン", Ko = "아이콘" },
            new Sys_Lang { LangKey = "menu.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "menu.status", ZhCN = "状态", ZhTW = "狀態", En = "Status", Ja = "ステータス", Ko = "상태" },
            new Sys_Lang { LangKey = "menu.enabled", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "menu.disabled", ZhCN = "禁用", ZhTW = "停用", En = "Disabled", Ja = "無効", Ko = "비활성화" },
            new Sys_Lang { LangKey = "menu.operation", ZhCN = "操作", ZhTW = "操作", En = "Action", Ja = "操作", Ko = "작업" },
            new Sys_Lang { LangKey = "menu.confirmDelete", ZhCN = "确定删除该菜单吗？", ZhTW = "確定刪除該選單嗎？", En = "Are you sure to delete this menu?", Ja = "このメニューを削除しますか？", Ko = "이 메뉴를 삭제하시겠습니까?" },
            new Sys_Lang { LangKey = "menu.menuIdRequired", ZhCN = "请输入菜单ID", ZhTW = "請輸入選單ID", En = "Please enter menu ID", Ja = "メニューIDを入力してください", Ko = "메뉴 ID를 입력하세요" },
            new Sys_Lang { LangKey = "menu.menuNameRequired", ZhCN = "请输入菜单名称", ZhTW = "請輸入選單名稱", En = "Please enter menu name", Ja = "メニュー名を入力してください", Ko = "메뉴명을 입력하세요" },
            new Sys_Lang { LangKey = "menu.routePathPlaceholder", ZhCN = "如 /article", ZhTW = "如 /article", En = "e.g. /article", Ja = "例: /article", Ko = "예: /article" },
            new Sys_Lang { LangKey = "menu.iconPlaceholder", ZhCN = "如 Document, Setting", ZhTW = "如 Document, Setting", En = "e.g. Document, Setting", Ja = "例: Document, Setting", Ko = "예: Document, Setting" },
            new Sys_Lang { LangKey = "permission.title", ZhCN = "权限分配", ZhTW = "權限分配", En = "Permission Assignment", Ja = "権限設定", Ko = "권한 설정" },
            new Sys_Lang { LangKey = "permission.selectRole", ZhCN = "选择角色", ZhTW = "選擇角色", En = "Select Role", Ja = "役割を選択", Ko = "역할 선택" },
            new Sys_Lang { LangKey = "permission.menuPermission", ZhCN = "菜单权限", ZhTW = "選單權限", En = "Menu Permissions", Ja = "メニュー権限", Ko = "메뉴 권한" },
            new Sys_Lang { LangKey = "permission.save", ZhCN = "保存", ZhTW = "儲存", En = "Save", Ja = "保存", Ko = "저장" },
            new Sys_Lang { LangKey = "permission.saveSuccess", ZhCN = "权限保存成功", ZhTW = "權限儲存成功", En = "Permissions saved successfully", Ja = "権限を保存しました", Ko = "권한이 저장되었습니다" },
            new Sys_Lang { LangKey = "common.yes", ZhCN = "是", ZhTW = "是", En = "Yes", Ja = "はい", Ko = "예" },
            new Sys_Lang { LangKey = "common.no", ZhCN = "否", ZhTW = "否", En = "No", Ja = "いいえ", Ko = "아니오" },
            new Sys_Lang { LangKey = "lang.title", ZhCN = "多语言管理", ZhTW = "多語言管理", En = "Language Management", Ja = "多言語管理", Ko = "다국어 관리" },
            new Sys_Lang { LangKey = "lang.langKey", ZhCN = "词条Key", ZhTW = "詞條Key", En = "Lang Key", Ja = "キー", Ko = "키" },
            new Sys_Lang { LangKey = "lang.zhCN", ZhCN = "简体中文", ZhTW = "簡體中文", En = "Chinese (Simplified)", Ja = "簡体中国語", Ko = "중국어(간체)" },
            new Sys_Lang { LangKey = "lang.zhTW", ZhCN = "繁体中文", ZhTW = "繁體中文", En = "Chinese (Traditional)", Ja = "繁体中国語", Ko = "중국어(번체)" },
            new Sys_Lang { LangKey = "lang.en", ZhCN = "英语", ZhTW = "英語", En = "English", Ja = "英語", Ko = "영어" },
            new Sys_Lang { LangKey = "lang.ja", ZhCN = "日语", ZhTW = "日語", En = "Japanese", Ja = "日本語", Ko = "일본어" },
            new Sys_Lang { LangKey = "lang.ko", ZhCN = "韩语", ZhTW = "韓語", En = "Korean", Ja = "韓国語", Ko = "한국어" },
            // 导航菜单翻译 nav.{menuId}
            new Sys_Lang { LangKey = "nav.1", ZhCN = "文章管理", ZhTW = "文章管理", En = "Articles", Ja = "記事管理", Ko = "기사 관리" },
            new Sys_Lang { LangKey = "nav.100", ZhCN = "系统管理", ZhTW = "系統管理", En = "System", Ja = "システム管理", Ko = "시스템 관리" },
            new Sys_Lang { LangKey = "nav.101", ZhCN = "角色管理", ZhTW = "角色管理", En = "Roles", Ja = "役割管理", Ko = "역할 관리" },
            new Sys_Lang { LangKey = "nav.102", ZhCN = "菜单管理", ZhTW = "選單管理", En = "Menus", Ja = "メニュー管理", Ko = "메뉴 관리" },
            new Sys_Lang { LangKey = "nav.103", ZhCN = "权限分配", ZhTW = "權限分配", En = "Permissions", Ja = "権限設定", Ko = "권한 설정" },
            new Sys_Lang { LangKey = "nav.104", ZhCN = "用户管理", ZhTW = "使用者管理", En = "Users", Ja = "ユーザー管理", Ko = "사용자 관리" },
            new Sys_Lang { LangKey = "nav.105", ZhCN = "多语言管理", ZhTW = "多語言管理", En = "Languages", Ja = "多言語管理", Ko = "다국어 관리" },
            new Sys_Lang { LangKey = "nav.2", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            new Sys_Lang { LangKey = "dashboard.title", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            new Sys_Lang { LangKey = "dashboard.todayOps", ZhCN = "今日操作", ZhTW = "今日操作", En = "Today Ops", Ja = "本日の操作", Ko = "오늘 작업" },
            new Sys_Lang { LangKey = "dashboard.weekOps", ZhCN = "本周操作", ZhTW = "本週操作", En = "Week Ops", Ja = "今週の操作", Ko = "이번주 작업" },
            new Sys_Lang { LangKey = "dashboard.totalOps", ZhCN = "总操作数", ZhTW = "總操作數", En = "Total Ops", Ja = "総操作数", Ko = "총 작업수" },
            new Sys_Lang { LangKey = "dashboard.totalUsers", ZhCN = "用户数", ZhTW = "使用者數", En = "Users", Ja = "ユーザー数", Ko = "사용자수" },
            new Sys_Lang { LangKey = "dashboard.totalArticles", ZhCN = "文章数", ZhTW = "文章數", En = "Articles", Ja = "記事数", Ko = "기사수" },
            new Sys_Lang { LangKey = "dashboard.topControllers", ZhCN = "操作排行", ZhTW = "操作排行", En = "Top Controllers", Ja = "操作ランキング", Ko = "작업 순위" },
            new Sys_Lang { LangKey = "dashboard.trend", ZhCN = "7日趋势", ZhTW = "7日趨勢", En = "7-Day Trend", Ja = "7日間推移", Ko = "7일 추이" },
            new Sys_Lang { LangKey = "dashboard.methodDist", ZhCN = "方法分布", ZhTW = "方法分佈", En = "Method Distribution", Ja = "メソッド分布", Ko = "메서드 분포" },
            new Sys_Lang { LangKey = "dashboard.noData", ZhCN = "暂无数据", ZhTW = "暫無資料", En = "No data", Ja = "データなし", Ko = "데이터 없음" },
            new Sys_Lang { LangKey = "dashboard.count", ZhCN = "次数", ZhTW = "次數", En = "Count", Ja = "回数", Ko = "횟수" }
        );
        db.SaveChanges();
    }

    // 补充：已有数据库追加导航菜单词条
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.1"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.1", ZhCN = "文章管理", ZhTW = "文章管理", En = "Articles", Ja = "記事管理", Ko = "기사 관리" },
            new Sys_Lang { LangKey = "nav.100", ZhCN = "系统管理", ZhTW = "系統管理", En = "System", Ja = "システム管理", Ko = "시스템 관리" },
            new Sys_Lang { LangKey = "nav.101", ZhCN = "角色管理", ZhTW = "角色管理", En = "Roles", Ja = "役割管理", Ko = "역할 관리" },
            new Sys_Lang { LangKey = "nav.102", ZhCN = "菜单管理", ZhTW = "選單管理", En = "Menus", Ja = "メニュー管理", Ko = "메뉴 관리" },
            new Sys_Lang { LangKey = "nav.103", ZhCN = "权限分配", ZhTW = "權限分配", En = "Permissions", Ja = "権限設定", Ko = "권한 설정" },
            new Sys_Lang { LangKey = "nav.104", ZhCN = "用户管理", ZhTW = "使用者管理", En = "Users", Ja = "ユーザー管理", Ko = "사용자 관리" },
            new Sys_Lang { LangKey = "nav.105", ZhCN = "多语言管理", ZhTW = "多語言管理", En = "Languages", Ja = "多言語管理", Ko = "다국어 관리" },
            new Sys_Lang { LangKey = "nav.106", ZhCN = "数据字典", ZhTW = "資料字典", En = "Dictionary", Ja = "データ辞書", Ko = "데이터 사전" },
            // 字典管理页面词条
            new Sys_Lang { LangKey = "dict.typeCode", ZhCN = "类型编码", ZhTW = "類型編碼", En = "Type Code", Ja = "タイプコード", Ko = "유형 코드" },
            new Sys_Lang { LangKey = "dict.typeName", ZhCN = "类型名称", ZhTW = "類型名稱", En = "Type Name", Ja = "タイプ名", Ko = "유형명" },
            new Sys_Lang { LangKey = "dict.value", ZhCN = "字典值", ZhTW = "字典值", En = "Value", Ja = "値", Ko = "값" },
            new Sys_Lang { LangKey = "dict.label", ZhCN = "显示文本", ZhTW = "顯示文字", En = "Label", Ja = "ラベル", Ko = "라벨" },
            new Sys_Lang { LangKey = "dict.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "dict.enable", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "dict.backToTypes", ZhCN = "返回类型列表", ZhTW = "返回類型列表", En = "Back to types", Ja = "タイプ一覧に戻る", Ko = "유형 목록으로" },
            new Sys_Lang { LangKey = "dict.manageData", ZhCN = "管理字典项", ZhTW = "管理字典項", En = "Manage Items", Ja = "項目管理", Ko = "항목 관리" },
            // 操作日志词条
            new Sys_Lang { LangKey = "nav.107", ZhCN = "操作日志", ZhTW = "操作日誌", En = "Operation Log", Ja = "操作ログ", Ko = "작업 로그" },
            new Sys_Lang { LangKey = "operlog.user", ZhCN = "操作人", ZhTW = "操作人", En = "User", Ja = "操作者", Ko = "사용자" },
            new Sys_Lang { LangKey = "operlog.method", ZhCN = "方法", ZhTW = "方法", En = "Method", Ja = "メソッド", Ko = "메서드" },
            new Sys_Lang { LangKey = "operlog.url", ZhCN = "请求路径", ZhTW = "請求路徑", En = "URL", Ja = "URL", Ko = "URL" },
            new Sys_Lang { LangKey = "operlog.controller", ZhCN = "控制器", ZhTW = "控制器", En = "Controller", Ja = "コントローラー", Ko = "컨트롤러" },
            new Sys_Lang { LangKey = "operlog.status", ZhCN = "状态码", ZhTW = "狀態碼", En = "Status", Ja = "ステータス", Ko = "상태" },
            new Sys_Lang { LangKey = "operlog.elapsed", ZhCN = "耗时", ZhTW = "耗時", En = "Elapsed", Ja = "所要時間", Ko = "소요시간" },
            new Sys_Lang { LangKey = "operlog.time", ZhCN = "操作时间", ZhTW = "操作時間", En = "Time", Ja = "操作時間", Ko = "시간" },
            new Sys_Lang { LangKey = "operlog.detail", ZhCN = "详情", ZhTW = "詳情", En = "Detail", Ja = "詳細", Ko = "상세" },
            new Sys_Lang { LangKey = "operlog.requestBody", ZhCN = "请求参数", ZhTW = "請求參數", En = "Request Body", Ja = "リクエスト", Ko = "요청 본문" }
        );
        db.SaveChanges();
    }

    // 补充：已有数据库追加字典相关词条
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.106"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.106", ZhCN = "数据字典", ZhTW = "資料字典", En = "Dictionary", Ja = "データ辞書", Ko = "데이터 사전" },
            new Sys_Lang { LangKey = "dict.typeCode", ZhCN = "类型编码", ZhTW = "類型編碼", En = "Type Code", Ja = "タイプコード", Ko = "유형 코드" },
            new Sys_Lang { LangKey = "dict.typeName", ZhCN = "类型名称", ZhTW = "類型名稱", En = "Type Name", Ja = "タイプ名", Ko = "유형명" },
            new Sys_Lang { LangKey = "dict.value", ZhCN = "字典值", ZhTW = "字典值", En = "Value", Ja = "値", Ko = "값" },
            new Sys_Lang { LangKey = "dict.label", ZhCN = "显示文本", ZhTW = "顯示文字", En = "Label", Ja = "ラベル", Ko = "라벨" },
            new Sys_Lang { LangKey = "dict.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "dict.enable", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "dict.backToTypes", ZhCN = "返回类型列表", ZhTW = "返回類型列表", En = "Back to types", Ja = "タイプ一覧に戻る", Ko = "유형 목록으로" },
            new Sys_Lang { LangKey = "dict.manageData", ZhCN = "管理字典项", ZhTW = "管理字典項", En = "Manage Items", Ja = "項目管理", Ko = "항목 관리" }
        );
        db.SaveChanges();
    }

    // 补充：操作日志相关词条
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.107"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.107", ZhCN = "操作日志", ZhTW = "操作日誌", En = "Operation Log", Ja = "操作ログ", Ko = "작업 로그" },
            new Sys_Lang { LangKey = "operlog.user", ZhCN = "操作人", ZhTW = "操作人", En = "User", Ja = "操作者", Ko = "사용자" },
            new Sys_Lang { LangKey = "operlog.method", ZhCN = "方法", ZhTW = "方法", En = "Method", Ja = "メソッド", Ko = "메서드" },
            new Sys_Lang { LangKey = "operlog.url", ZhCN = "请求路径", ZhTW = "請求路徑", En = "URL", Ja = "URL", Ko = "URL" },
            new Sys_Lang { LangKey = "operlog.controller", ZhCN = "控制器", ZhTW = "控制器", En = "Controller", Ja = "コントローラー", Ko = "컨트롤러" },
            new Sys_Lang { LangKey = "operlog.status", ZhCN = "状态码", ZhTW = "狀態碼", En = "Status", Ja = "ステータス", Ko = "상태" },
            new Sys_Lang { LangKey = "operlog.elapsed", ZhCN = "耗时", ZhTW = "耗時", En = "Elapsed", Ja = "所要時間", Ko = "소요시간" },
            new Sys_Lang { LangKey = "operlog.time", ZhCN = "操作时间", ZhTW = "操作時間", En = "Time", Ja = "操作時間", Ko = "시간" },
            new Sys_Lang { LangKey = "operlog.detail", ZhCN = "详情", ZhTW = "詳情", En = "Detail", Ja = "詳細", Ko = "상세" },
            new Sys_Lang { LangKey = "operlog.requestBody", ZhCN = "请求参数", ZhTW = "請求參數", En = "Request Body", Ja = "リクエスト", Ko = "요청 본문" }
        );
        db.SaveChanges();
    }

    // 补充：仪表盘菜单和词条
    if (!db.Sys_Menus.Any(m => m.MenuId == 2))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 2, MenuName = "仪表盘", RoutePath = "/dashboard", Icon = "Odometer", OrderNo = 0, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 2 });
        db.SaveChanges();
    }
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.2"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.2", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            new Sys_Lang { LangKey = "dashboard.title", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            new Sys_Lang { LangKey = "dashboard.todayOps", ZhCN = "今日操作", ZhTW = "今日操作", En = "Today Ops", Ja = "本日の操作", Ko = "오늘 작업" },
            new Sys_Lang { LangKey = "dashboard.weekOps", ZhCN = "本周操作", ZhTW = "本週操作", En = "Week Ops", Ja = "今週の操作", Ko = "이번주 작업" },
            new Sys_Lang { LangKey = "dashboard.totalOps", ZhCN = "总操作数", ZhTW = "總操作數", En = "Total Ops", Ja = "総操作数", Ko = "총 작업수" },
            new Sys_Lang { LangKey = "dashboard.totalUsers", ZhCN = "用户数", ZhTW = "使用者數", En = "Users", Ja = "ユーザー数", Ko = "사용자수" },
            new Sys_Lang { LangKey = "dashboard.totalArticles", ZhCN = "文章数", ZhTW = "文章數", En = "Articles", Ja = "記事数", Ko = "기사수" },
            new Sys_Lang { LangKey = "dashboard.topControllers", ZhCN = "操作排行", ZhTW = "操作排行", En = "Top Controllers", Ja = "操作ランキング", Ko = "작업 순위" },
            new Sys_Lang { LangKey = "dashboard.trend", ZhCN = "7日趋势", ZhTW = "7日趨勢", En = "7-Day Trend", Ja = "7日間推移", Ko = "7일 추이" },
            new Sys_Lang { LangKey = "dashboard.methodDist", ZhCN = "方法分布", ZhTW = "方法分佈", En = "Method Distribution", Ja = "メソッド分布", Ko = "메서드 분포" },
            new Sys_Lang { LangKey = "dashboard.noData", ZhCN = "暂无数据", ZhTW = "暫無資料", En = "No data", Ja = "データなし", Ko = "데이터 없음" },
            new Sys_Lang { LangKey = "dashboard.count", ZhCN = "次数", ZhTW = "次數", En = "Count", Ja = "回数", Ko = "횟수" }
        );
        db.SaveChanges();
    }

    // 示例字典数据
    if (!db.Sys_DictTypes.Any())
    {
        db.Sys_DictTypes.AddRange(
            new Sys_DictType { TypeCode = "gender", TypeName = "性别", OrderNo = 1 },
            new Sys_DictType { TypeCode = "article_type", TypeName = "文章类型", OrderNo = 2 }
        );
        db.Sys_DictDatas.AddRange(
            new Sys_DictData { TypeCode = "gender", Value = "1", Label = "男", OrderNo = 1 },
            new Sys_DictData { TypeCode = "gender", Value = "2", Label = "女", OrderNo = 2 },
            new Sys_DictData { TypeCode = "article_type", Value = "news", Label = "新闻", OrderNo = 1 },
            new Sys_DictData { TypeCode = "article_type", Value = "tech", Label = "技术", OrderNo = 2 },
            new Sys_DictData { TypeCode = "article_type", Value = "life", Label = "生活", OrderNo = 3 }
        );
        db.SaveChanges();
    }

    // ===== MSBBPA010 見積計算書 主数据种子 =====
    if (!db.MasterBases.Any())
    {
        db.MasterBases.AddRange(
            new MasterBase { BaseCd = "01", BaseName = "岐阜事業所", IsAdminBase = true,  DiscountThreshold = 0.15m, SortOrder = 1 },
            new MasterBase { BaseCd = "02", BaseName = "東京営業所",  IsAdminBase = false, DiscountThreshold = 0.10m, SortOrder = 2 },
            new MasterBase { BaseCd = "03", BaseName = "大阪営業所",  IsAdminBase = false, DiscountThreshold = 0.10m, SortOrder = 3 }
        );
        db.SaveChanges();
    }

    if (!db.MasterStaffs.Any())
    {
        db.MasterStaffs.AddRange(
            new MasterStaff { StaffCd = "1001", StaffName = "山田 太郎",  BaseCd = "01", SortOrder = 1 },
            new MasterStaff { StaffCd = "1002", StaffName = "佐藤 花子",  BaseCd = "01", SortOrder = 2 },
            new MasterStaff { StaffCd = "2001", StaffName = "鈴木 一郎",  BaseCd = "02", SortOrder = 1 },
            new MasterStaff { StaffCd = "3001", StaffName = "田中 美咲",  BaseCd = "03", SortOrder = 1 }
        );
        db.SaveChanges();
    }

    if (!db.MasterGenericCodes.Any())
    {
        db.MasterGenericCodes.AddRange(
            // 受注区分
            new MasterGenericCode { GroupCode = "OrderType", Code = "10", Name = "通常受注", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "OrderType", Code = "20", Name = "サンプル", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "OrderType", Code = "30", Name = "商品",     SortOrder = 3 },

            // 親子区分
            new MasterGenericCode { GroupCode = "ParentChildDiv", Code = "1", Name = "親",     SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ParentChildDiv", Code = "2", Name = "子",     SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ParentChildDiv", Code = "3", Name = "部材",   SortOrder = 3 },

            // FSC 区分
            new MasterGenericCode { GroupCode = "FscDiv", Code = "0", Name = "非対象",           SortOrder = 0 },
            new MasterGenericCode { GroupCode = "FscDiv", Code = "1", Name = "FSC 100%",         SortOrder = 1 },
            new MasterGenericCode { GroupCode = "FscDiv", Code = "2", Name = "FSC MIX",          SortOrder = 2 },

            // 製品区分（大/中/小）
            // ProductCategoryBig = 大分類（独立）
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "A", Name = "段ボール箱", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "B", Name = "紙箱",       SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "C", Name = "トレー",     SortOrder = 3 },

            // ProductCategoryMid = 中分類（Attr1 = 親=大分類 Code）
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A01", Name = "A式箱",     Attr1 = "A", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A02", Name = "B式箱",     Attr1 = "A", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A03", Name = "ワンタッチ", Attr1 = "A", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B01", Name = "化粧箱",     Attr1 = "B", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B02", Name = "贈答箱",     Attr1 = "B", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C01", Name = "食品トレー", Attr1 = "C", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C02", Name = "工業トレー", Attr1 = "C", SortOrder = 2 },

            // ProductCategorySml = 小分類（Attr1 = 親=中分類 Code）
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0101", Name = "標準A式",     Attr1 = "A01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0102", Name = "半差し込み", Attr1 = "A01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0201", Name = "標準B式",     Attr1 = "A02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0301", Name = "ワンタッチ底", Attr1 = "A03", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0101", Name = "白色化粧箱", Attr1 = "B01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0102", Name = "色付化粧箱", Attr1 = "B01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0201", Name = "贈答化粧箱", Attr1 = "B02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0101", Name = "浅型トレー", Attr1 = "C01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0102", Name = "深型トレー", Attr1 = "C01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0201", Name = "工業標準",   Attr1 = "C02", SortOrder = 1 },

            // シート段
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "E",  Name = "E 段", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "B",  Name = "B 段", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "C",  Name = "C 段", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "BC", Name = "BC 段", SortOrder = 4 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "EB", Name = "EB 段", SortOrder = 5 },

            // 単位
            new MasterGenericCode { GroupCode = "Unit", Code = "PCS", Name = "枚", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "Unit", Code = "SET", Name = "セット", SortOrder = 2 },

            // 製品形状1/2
            new MasterGenericCode { GroupCode = "ProductShape1", Code = "01", Name = "A 式", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductShape1", Code = "02", Name = "B 式", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductShape2", Code = "01", Name = "通常",   SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductShape2", Code = "02", Name = "効率形状", SortOrder = 2 },

            // 物流区分
            new MasterGenericCode { GroupCode = "DistDiv", Code = "1", Name = "直送", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "DistDiv", Code = "2", Name = "倉庫経由", SortOrder = 2 },

            // M014 印刷（Attr1 = インキ性質区分：0=水性/1=油性）
            new MasterGenericCode { GroupCode = "M014", Code = "0000", Name = "なし",    Attr1 = "0", SortOrder = 0 },
            new MasterGenericCode { GroupCode = "M014", Code = "I001", Name = "オフセット", Attr1 = "1", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "M014", Code = "I002", Name = "グラビア",   Attr1 = "1", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "M014", Code = "I003", Name = "フレマル",   Attr1 = "0", SortOrder = 3 },

            // M038 工程
            new MasterGenericCode { GroupCode = "M038", Code = "0020", Name = "輪転（グラビア）1", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "M038", Code = "0021", Name = "輪転（グラビア）2", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "M038", Code = "0030", Name = "輪転（フレマル）1", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "M038", Code = "0031", Name = "輪転（フレマル）2", SortOrder = 4 },
            new MasterGenericCode { GroupCode = "M038", Code = "0050", Name = "トムソン",          SortOrder = 5 },
            new MasterGenericCode { GroupCode = "M038", Code = "0060", Name = "貼合",              SortOrder = 6 },

            // M067 段成率（%，Num1）
            new MasterGenericCode { GroupCode = "M067", Code = "E",  Name = "E 段成率", Num1 = 1.35m, SortOrder = 1 },
            new MasterGenericCode { GroupCode = "M067", Code = "B",  Name = "B 段成率", Num1 = 1.40m, SortOrder = 2 },
            new MasterGenericCode { GroupCode = "M067", Code = "C",  Name = "C 段成率", Num1 = 1.50m, SortOrder = 3 },
            new MasterGenericCode { GroupCode = "M067", Code = "BC", Name = "BC 段成率", Num1 = 1.45m, SortOrder = 4 },

            // 原紙（示意，Attr1 = 段原紙区分 1/其他；Attr2 = 坪量 g/m²）
            // Paper 原紙（Num1 = 円/m² 単価；Attr2 = 坪量 g/m²）
            new MasterGenericCode { GroupCode = "Paper", Code = "P001", Name = "K220",    Attr1 = "0", Attr2 = "220", Num1 = 32.5m, SortOrder = 1 },
            new MasterGenericCode { GroupCode = "Paper", Code = "P002", Name = "K280",    Attr1 = "0", Attr2 = "280", Num1 = 38.0m, SortOrder = 2 },
            new MasterGenericCode { GroupCode = "Paper", Code = "C001", Name = "中芯 120", Attr1 = "1", Attr2 = "120", Num1 = 22.0m, SortOrder = 3 }
        );
        db.SaveChanges();
    }

    // 幂等补种：如果存在旧的 "ProductCategory" 单层数据，迁移到 3 层级联
    if (db.MasterGenericCodes.Any(x => x.GroupCode == "ProductCategory")
        && !db.MasterGenericCodes.Any(x => x.GroupCode == "ProductCategoryBig"))
    {
        // 删除旧单层
        var legacy = db.MasterGenericCodes.Where(x => x.GroupCode == "ProductCategory").ToList();
        db.MasterGenericCodes.RemoveRange(legacy);

        // 追加 3 层级联
        db.MasterGenericCodes.AddRange(
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "A", Name = "段ボール箱", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "B", Name = "紙箱",       SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "C", Name = "トレー",     SortOrder = 3 },

            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A01", Name = "A式箱",     Attr1 = "A", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A02", Name = "B式箱",     Attr1 = "A", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A03", Name = "ワンタッチ", Attr1 = "A", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B01", Name = "化粧箱",     Attr1 = "B", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B02", Name = "贈答箱",     Attr1 = "B", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C01", Name = "食品トレー", Attr1 = "C", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C02", Name = "工業トレー", Attr1 = "C", SortOrder = 2 },

            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0101", Name = "標準A式",     Attr1 = "A01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0102", Name = "半差し込み", Attr1 = "A01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0201", Name = "標準B式",     Attr1 = "A02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0301", Name = "ワンタッチ底", Attr1 = "A03", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0101", Name = "白色化粧箱", Attr1 = "B01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0102", Name = "色付化粧箱", Attr1 = "B01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0201", Name = "贈答化粧箱", Attr1 = "B02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0101", Name = "浅型トレー", Attr1 = "C01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0102", Name = "深型トレー", Attr1 = "C01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0201", Name = "工業標準",   Attr1 = "C02", SortOrder = 1 }
        );
        db.SaveChanges();
    }
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// SignalR Hub 路由
app.MapHub<NotifyHub>("/hubs/notify");

app.Run();
