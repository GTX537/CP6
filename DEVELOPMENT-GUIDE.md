# CP6 全栈项目开发流程

> 面向 MOM 系统 .NET 开发岗位面试的全栈练手项目，覆盖分布式微服务核心技术栈。

---

## 零、Git 分支与交付纪律

CP6 采用“分支优先、验证后合并”的开发方式：

1. `main` 仅用于集成和发布准备。除非有必须直接在 `main` 操作的明确技术原因，否则所有开发、修复、重构和文档任务都从最新 `main` 创建独立任务分支。
2. 一个分支只处理一个任务。根工作区存在其他未提交改动时，使用独立 Git worktree 隔离，不覆盖、不暂存、不提交其他人的工作。
3. 任务分支必须完成与风险相称的测试、构建、格式、契约和迁移检查；必需门禁未通过，不得并入 `main`。
4. 合并前审查相对 `main` 的完整 diff，排除敏感信息、机器专属配置、调试残留和范围漂移。
5. 测试通过后才提交并合并到 `main`；合并后执行必要冒烟验证，再把 `main` 推送到远端。
6. 只有远端 `main` 已包含任务提交且验证证据已记录，任务才算完成。不得 force-push 或重写共享历史。

直接在 `main` 开发属于例外，必须在开始前说明不可使用分支的原因，并在提交或项目状态记录中保留依据。“改动很小”或“更方便”不构成例外。面向自动化开发代理的完整约束见仓库根目录 `AGENTS.md`。

---

## 一、项目架构总览

```
D:\CP6\
├── CP6.Entity/          # 实体层 — 数据模型 + DTO
├── CP6.Core/            # 核心层 — 通用仓储 + 服务 + 工具类 + EF 迁移
├── CP6.WebApi/          # API 层 — 控制器 + 过滤器 + Hub + 后台服务
├── CP6.Tests/           # 测试层 — xUnit + Moq
├── cp6.web/             # 前端 — Vue 3 + Element Plus + Vite
├── k8s/                 # Kubernetes 部署清单
├── docker-compose.yml   # Docker 编排
└── CP6.slnx             # .NET 解决方案文件
```

### 分层依赖关系
```
CP6.WebApi  →  CP6.Core  →  CP6.Entity
CP6.Tests   →  CP6.WebApi / CP6.Core
cp6.web（独立前端项目，通过 HTTP/WebSocket 与 API 通信）
```

### 技术选型
| 层次 | 技术 | 版本 | 用途 |
|------|------|------|------|
| 前端框架 | Vue 3 + TypeScript | 3.5 | 组件化 SPA |
| UI 组件库 | Element Plus | 2.13 | 表格/表单/弹窗 |
| 状态管理 | Pinia | 3.0 | 全局状态 |
| 国际化 | vue-i18n | 11.3 | 5 语言动态切换 |
| HTTP 客户端 | axios | 1.14 | API 请求 + 拦截器 |
| 实时通信 | @microsoft/signalr | 10.0 | WebSocket 推送 |
| 后端框架 | ASP.NET Core | 8.0 | RESTful API |
| ORM | EF Core + Dapper | 8.0 / 2.1 | CRUD + 复杂 SQL |
| 认证 | JWT Bearer | 8.0 | 无状态身份验证 |
| 缓存 | IDistributedCache | 8.0 | Memory / Redis 双模式 |
| 消息队列 | RabbitMQ.Client | 7.2 | 异步消息解耦 |
| 数据库 | SQL Server | 2022 | 关系型存储 |
| 容器化 | Docker + Compose | - | 多服务编排 |
| 编排 | Kubernetes | 1.35 | 集群部署 + 自动扩缩 |
| 测试 | xUnit + Moq | 2.9 / 4.20 | 单元测试 + Mock |

---

## 二、后端搭建流程

### Phase 1：初始化解决方案

```bash
# 1. 创建解决方案和项目
dotnet new sln -n CP6
dotnet new classlib -n CP6.Entity -f net8.0
dotnet new classlib -n CP6.Core -f net8.0
dotnet new webapi -n CP6.WebApi -f net8.0

# 2. 添加项目引用
dotnet add CP6.Core reference CP6.Entity
dotnet add CP6.WebApi reference CP6.Core

# 3. 添加到解决方案
dotnet sln add CP6.Entity CP6.Core CP6.WebApi
```

### Phase 2：实体层 (CP6.Entity)

```
CP6.Entity/
├── DomainModels/
│   ├── BaseEntity.cs        # 抽象基类（Id, Creator, CreateDate...）
│   ├── Article.cs           # 文章表
│   ├── Sys_User.cs          # 用户表
│   ├── Sys_Role.cs          # 角色表
│   ├── Sys_Menu.cs          # 菜单表（树形，MenuId + ParentId）
│   ├── Sys_RoleMenu.cs      # 角色-菜单关联表
│   ├── Sys_Lang.cs          # 多语言翻译表
│   ├── Sys_DictType.cs      # 字典类型表
│   ├── Sys_DictData.cs      # 字典数据表
│   └── Sys_OperLog.cs       # 操作日志表
└── DTOs/
    └── LoginRequest.cs      # 登录请求 DTO
```

**关键设计：**
- BaseEntity 用 `Guid` 作为主键，统一 Creator/CreateDate 审计字段
- Sys_Menu 用 `int MenuId`（非 Guid），支持树形结构 ParentId
- Sys_OperLog 记录完整请求信息（Method, Path, Body, StatusCode, ElapsedMs）

### Phase 3：核心层 (CP6.Core)

#### 3.1 安装 NuGet 包
```bash
cd CP6.Core
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Dapper
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
dotnet add package RabbitMQ.Client
dotnet add package System.IdentityModel.Tokens.Jwt
```

#### 3.2 DbContext（EF Core Code-First）
```
CP6.Core/EFDbContext/
└── CP6Context.cs    # 9 个 DbSet + OnModelCreating 配置
```

**面试要点：** Code-First 流程 → 定义 Entity → 注册 DbSet → Add-Migration → Update-Database

#### 3.3 通用仓储模式（Generic Repository）
```
CP6.Core/BaseProvider/
├── IRepository.cs       # 接口：FindAsync, GetPageListAsync, AddAsync...
├── RepositoryBase.cs    # EF Core 实现（Where + OrderBy + Pagination）
├── IService.cs          # 服务接口
└── ServiceBase.cs       # 默认实现，子类可 override
```

**面试要点：** 一套代码搞定所有 Entity 的 CRUD，新增表只需：
1. 定义 Entity
2. DbContext 加 DbSet
3. Controller 注入 `IService<新Entity>`

#### 3.4 工具类
```
CP6.Core/Utilities/
├── JwtHelper.cs         # JWT 生成（Claims + HMAC-SHA256）
├── CacheService.cs      # IDistributedCache 封装（Cache-Aside 模式）
└── RabbitMQService.cs   # MQ 发布（durable + persistent + 降级策略）
```

**CacheService 核心方法：**
```csharp
// Cache-Aside：缓存有 → 返回；缓存无 → 调 factory → 写缓存 → 返回
public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration)
```

**RabbitMQService 降级策略：**
```csharp
if (_mq.IsConnected)
    await _mq.PublishAsync(queue, log);   // MQ 可用 → 异步发送
else
{
    _context.Sys_OperLogs.Add(log);       // MQ 不可用 → 同步写 DB
    await _context.SaveChangesAsync();
}
```

#### 3.5 EF Core 迁移
```bash
# 4 次迁移，渐进式建库
dotnet ef migrations add Init                    # 基础表结构
dotnet ef migrations add AddSysLang              # 多语言支持
dotnet ef migrations add AddDictTables           # 字典管理
dotnet ef migrations add AddOperLog              # 操作日志
dotnet ef database update                        # 应用到数据库
```

### Phase 4：API 层 (CP6.WebApi)

#### 4.1 安装 NuGet 包
```bash
cd CP6.WebApi
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

#### 4.2 Program.cs 注册顺序（422 行，项目核心）

```csharp
// ===== 服务注册 =====
builder.Services.AddControllers(o => o.Filters.Add<OperLogFilter>());  // 全局过滤器
builder.Services.AddSignalR();                                          // SignalR
builder.Services.AddDbContext<CP6Context>(...);                        // EF Core
builder.Services.AddScoped<IDbConnection>(_ => new SqlConnection(connStr)); // Dapper
builder.Services.AddSingleton<CacheService>();                         // 缓存
builder.Services.AddSingleton<RabbitMQService>();                      // MQ
builder.Services.AddHostedService<OperLogConsumer>();                   // 后台消费者
builder.Services.AddAuthentication().AddJwtBearer(...);                // JWT
builder.Services.AddCors(...);                                         // CORS

// ===== 中间件管道 =====
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotifyHub>("/hubs/notify");                                 // SignalR 端点

// ===== 数据库初始化 =====
db.Database.Migrate();       // Docker 环境自动建库
// 种子数据：菜单、角色、用户、翻译、字典...
```

#### 4.3 控制器（9 个，共 902 行）
```
CP6.WebApi/Controllers/
├── AuthController.cs        # POST /api/auth/login → JWT Token
├── ArticleController.cs     # CRUD /api/article（带分页 + 关键字搜索）
├── DashboardController.cs   # GET /api/dashboard（Dapper 聚合 + 缓存）
├── DictController.cs        # CRUD /api/dict（字典管理 + 缓存失效）
├── LangController.cs        # CRUD /api/lang（多语言 + 缓存失效）
├── MenuController.cs        # CRUD /api/menu（树形菜单）
├── OperLogController.cs     # GET /api/operlog（操作日志查询）
├── RoleController.cs        # CRUD /api/role（角色 + 权限分配）
└── UserController.cs        # CRUD /api/user（用户管理）
```

#### 4.4 操作日志过滤器（OperLogFilter）
```
请求进入 → OnActionExecutionAsync
  ├── 启动 Stopwatch 计时
  ├── 读取 RequestBody
  ├── 调用 next()（执行 Controller Action）
  ├── 获取 StatusCode + ElapsedMs
  ├── 跳过条件：GET / /api/auth / /api/operlog
  └── 记录：MQ 发送 或 直接写 DB
```

#### 4.5 SignalR Hub + 后台消费者
```
MQ 消息流：
  OperLogFilter → RabbitMQ(cp6.operlog)
                    ↓
  OperLogConsumer(BackgroundService)
    ├── 写入 DB（IServiceScopeFactory 创建 Scoped CP6Context）
    ├── SignalR 推送（IHubContext<NotifyHub>.Clients.All.SendAsync）
    └── 手动 ACK（BasicAckAsync）
```

---

## 三、前端搭建流程

### Phase 1：初始化项目

```bash
npm create vue@latest cp6.web
# 选项：TypeScript ✓, Vue Router ✓, Pinia ✓

cd cp6.web
npm install element-plus @element-plus/icons-vue
npm install axios
npm install vue-i18n
npm install @microsoft/signalr
```

### Phase 2：项目结构
```
cp6.web/src/
├── api/                     # API 接口定义
│   ├── http.ts              # axios 实例 + 拦截器
│   ├── article.ts           # 文章 API
│   ├── dashboard.ts         # 仪表盘 API
│   ├── dict.ts              # 字典 API
│   ├── lang.ts              # 多语言 API
│   ├── menu.ts              # 菜单 API
│   ├── operlog.ts           # 操作日志 API
│   ├── role.ts              # 角色 API
│   └── user.ts              # 用户 API
├── i18n/
│   └── index.ts             # vue-i18n 初始化 + 动态加载
├── router/
│   └── index.ts             # 路由 + 动态菜单 + 守卫
├── stores/
│   └── counter.ts           # Pinia 状态管理
├── utils/
│   └── signalr.ts           # SignalR 单例连接
├── views/                   # 页面组件
│   ├── LoginView.vue        # 登录页
│   ├── LayoutView.vue       # 主布局（侧边栏 + 头部 + 内容区）
│   ├── DashboardView.vue    # 仪表盘（统计卡片 + 趋势图 + 实时告警）
│   ├── ArticleView.vue      # 文章管理
│   ├── UserView.vue         # 用户管理
│   ├── RoleView.vue         # 角色管理
│   ├── MenuView.vue         # 菜单管理（树形）
│   ├── PermissionView.vue   # 权限分配（角色-菜单勾选）
│   ├── LangView.vue         # 多语言管理
│   ├── DictView.vue         # 字典管理
│   └── OperLogView.vue      # 操作日志
├── App.vue                  # 根组件
└── main.ts                  # 入口：initI18n → createApp → 注册插件
```

### Phase 3：核心机制

#### 3.1 axios 拦截器 (http.ts)
```typescript
// 请求拦截：自动附加 JWT Token
instance.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// 响应拦截：401 → 清除 Token → 跳转登录页
instance.interceptors.response.use(res => res, error => {
  if (error.response?.status === 401) {
    localStorage.removeItem('token')
    router.push('/login')
  }
})
```

#### 3.2 动态路由 (router/index.ts)
```typescript
// 登录成功 → API 返回用户菜单列表 → 动态注册路由
export function addDynamicRoutes(menus: Menu[]) {
  const viewModules: Record<string, Component> = {
    '/dashboard': () => import('@/views/DashboardView.vue'),
    '/article':   () => import('@/views/ArticleView.vue'),
    // ... 9 个视图映射
  }
  menus.forEach(menu => {
    const component = viewModules[menu.routePath]
    if (component) {
      router.addRoute('layout', {
        path: menu.routePath,
        component,
        meta: { title: menu.menuName }
      })
    }
  })
}
```

#### 3.3 多语言 (i18n/index.ts)
```
流程：
1. initI18n() → 从 localStorage 读取语言偏好
2. loadLang('zh-CN') → GET /api/lang/zh-CN → 扁平 key 转嵌套对象
3. changeLang('en') → 切换语言 → 保存到 localStorage
4. 模板中使用 {{ $t('login.title') }}
```

#### 3.4 SignalR 实时推送 (signalr.ts + DashboardView.vue)
```typescript
// 单例连接
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/notify')
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .build()

// DashboardView 中监听事件
connection.on('NewOperLog', (log) => {
  showRealtimeAlert(log)    // 顶部告警横幅
  refreshDashboard()        // 自动刷新统计数据
})
```

### Phase 4：Vite 开发配置 (vite.config.ts)
```typescript
export default defineConfig({
  server: {
    port: 5173,
    host: '0.0.0.0',
    proxy: {
      '/api': { target: 'http://localhost:9991' },
      '/hubs': { target: 'http://localhost:9991', ws: true }  // WebSocket
    }
  }
})
```

---

## 四、Docker 容器化

### 构建流程
```
1. API 多阶段构建：
   SDK 镜像(~800MB) → 编译 → Runtime 镜像(~100MB) → 最终镜像

2. Web 多阶段构建：
   Node 镜像 → npm build → Nginx 镜像(~25MB) → 最终镜像

3. docker-compose 编排 5 个服务：
   cp6-db(SQL Server) → cp6-redis → cp6-mq → cp6-api → cp6-web
   ↑ healthcheck + depends_on 控制启动顺序
```

### 关键命令
```bash
docker compose up -d --build    # 一键启动
docker compose logs -f cp6-api  # 查看 API 日志
docker compose down             # 停止并删除
```

### 踩坑记录
| 问题 | 原因 | 解决 |
|------|------|------|
| credential-desktop 报错 | Docker config 残留 | 清空 credsStore |
| Error 4060 数据库不存在 | 容器首次启动无 DB | 加 db.Database.Migrate() |
| CORS 不兼容 SignalR | AllowAnyOrigin + AllowCredentials 冲突 | SetIsOriginAllowed + AllowCredentials |
| curl 中文乱码 | Windows 编码问题 | 用 ASCII 测试数据 |

---

## 五、Kubernetes 部署

### 资源清单（9 个 YAML）
```
k8s/
├── namespace.yaml       # Namespace: cp6（资源隔离）
├── configmap.yaml       # ConfigMap: 连接字符串 + nginx.conf
├── secret.yaml          # Secret: SA密码 + MQ密码（Base64）
├── db-deployment.yaml   # SQL Server: Deployment + PVC(2Gi) + Service
├── redis-deployment.yaml# Redis: Deployment + PVC(1Gi) + Service
├── mq-deployment.yaml   # RabbitMQ: Deployment + PVC(1Gi) + Service
├── api-deployment.yaml  # API: Deployment(2副本) + Service + HPA
├── web-deployment.yaml  # Web: Deployment(2副本) + NodePort Service
└── ingress.yaml         # Ingress: L7 路由（/api → API, / → Web）
```

### Docker Compose vs Kubernetes 对照
| Docker Compose | Kubernetes | 说明 |
|---|---|---|
| services | Deployment + Pod | 容器管理单元 |
| environment | ConfigMap + Secret | 配置注入 |
| volumes | PVC + PV | 持久化存储 |
| ports | Service (ClusterIP/NodePort) | 服务发现 |
| healthcheck | readinessProbe / livenessProbe | 健康检查 |
| depends_on | initContainers | 启动顺序 |
| 无 | HPA | 自动水平扩缩容 |
| 无 | Ingress | L7 路由 + 域名 |
| 无 | Namespace | 多环境隔离 |

### 部署命令
```bash
minikube start --driver=docker --memory=4096 --cpus=2
minikube addons enable ingress
minikube addons enable metrics-server

# 构建镜像到 minikube（Windows PowerShell）
minikube docker-env | Invoke-Expression
docker build -t cp6-api:latest -f CP6.WebApi/Dockerfile .
docker build -t cp6-web:latest -f cp6.web/Dockerfile .

# 一键部署
cd k8s && .\deploy.bat

# 访问前端
minikube service cp6-web -n cp6
```

### 踩坑记录
| 问题 | 原因 | 解决 |
|------|------|------|
| exec 探针变量不替换 | K8S probe 不支持 $(ENV) | 改用 tcpSocket 探针 |
| DB Pod CrashLoop | 两个 Pod 争抢同一 PVC | strategy: Recreate |
| initContainer 找不到 sqlcmd | mssql-tools 镜像变更 | 改用 busybox + nc -z |
| Ingress snippet 被拒 | 安全策略禁用 | 用标准 annotation |

---

## 六、测试

### 测试项目搭建
```bash
dotnet new xunit -n CP6.Tests -f net8.0
dotnet add CP6.Tests reference CP6.WebApi CP6.Core
dotnet add CP6.Tests package Moq
dotnet add CP6.Tests package Microsoft.EntityFrameworkCore.InMemory
```

### 测试文件
```
CP6.Tests/
├── TestHelper.cs            # InMemory DbContext + MemoryCache 工厂
├── CacheServiceTests.cs     # 5 个测试：Cache-Aside 模式验证
└── OperLogFilterTests.cs    # 5 个测试：MQ/DB 降级 + 跳过规则
```

### 测试模式：AAA (Arrange-Act-Assert)
```csharp
[Fact]
public async Task POST_WhenMqDisconnected_ShouldWriteToDb()
{
    // Arrange — 准备 InMemory DB + Mock MQ(disconnected)
    var context = TestHelper.CreateInMemoryContext();
    var mockMq = CreateMockMq(isConnected: false);

    // Act — 执行 Filter
    var filter = new OperLogFilter(context, mockMq.Object);
    await filter.OnActionExecutionAsync(actionContext, next);

    // Assert — 验证写入了 DB
    Assert.Equal(1, context.Sys_OperLogs.Count());
}
```

---

## 七、完整数据流

```
用户操作浏览器
    ↓
Vue 前端（axios + JWT Token）
    ↓ HTTP / WebSocket
Nginx 反向代理（Docker: cp6-web / K8S: Ingress）
    ↓
ASP.NET Core API（cp6-api:5000）
    ├── OperLogFilter 拦截 → RabbitMQ（异步）→ OperLogConsumer → DB + SignalR 推送
    ├── Controller → EF Core → SQL Server（CRUD）
    ├── Controller → Dapper → SQL Server（聚合查询）
    ├── CacheService → Redis / MemoryCache（缓存层）
    └── SignalR Hub → WebSocket → 前端实时更新
```

---

## 八、开发时间线

| 阶段 | 内容 | 核心知识点 |
|------|------|-----------|
| Day 1 | 解决方案搭建 + Entity + DbContext + Migration | EF Core Code-First |
| Day 1 | 通用仓储 + JWT 认证 + 基础 CRUD | Generic Repository + JWT |
| Day 2 | 前端搭建 + 动态路由 + 多语言 + RBAC | Vue 3 + vue-router + i18n |
| Day 3 | 操作日志 Filter | ActionFilter + 审计 |
| Day 3 | Dapper 仪表盘 + Redis 缓存 | Dapper + Cache-Aside |
| Day 4 | Docker 容器化 | Multi-stage Build + Compose |
| Day 4 | RabbitMQ 消息队列 | Producer-Consumer + 降级 |
| Day 5 | SignalR 实时推送 | WebSocket + IHubContext |
| Day 5 | 单元测试 | xUnit + Moq + InMemory |
| Day 6 | Kubernetes 部署 | 9 个核心概念 + HPA |
