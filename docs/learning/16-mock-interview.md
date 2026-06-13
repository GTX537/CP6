# 16 · 模拟面试 60 题

> 按"后端 / 前端 / 架构与 DevOps"三组各 20 道。**先自己想答案再展开**，每题都标了对应章节。
>
> 评分参考：
> - ⭐ 答出关键词
> - ⭐⭐ 答出来龙去脉
> - ⭐⭐⭐ 答出取舍 + 业界对比 + 你项目里实际怎么做

---

## A. 后端 20 题

### A1. ASP.NET Core 的 Singleton / Scoped / Transient 各自什么时候用？  → [第 02 章]
<details><summary>展开</summary>

- **Singleton**：进程一份，无状态工具（缓存包装、HTTP client、MQ producer）
- **Scoped**：每请求一份，跟 DB 相关（DbContext、Service）
- **Transient**：每次新建，极轻无状态（一般少用）

**陷阱**：Singleton 不能依赖 Scoped，否则启动报错。要在 Singleton 里用 DbContext，注入 `IServiceScopeFactory` 自己开 scope。CP6 的 `KafkaProducerService` Singleton + 用时 `_scopeFactory.CreateScope()` 取 DbContext 就是这个模式。

</details>

### A2. EF Core 的 N+1 问题是什么？怎么发现？怎么治？  → [第 03 章]
<details><summary>展开</summary>

N+1：查 1 个父对象集合，访问 N 个 navigation property → 触发 N 次额外查询。

**发现**：开 EF 日志 `LogTo(Console.WriteLine, LogLevel.Information)` 或上 MiniProfiler。

**治**：
- `Include(o => o.Details)` 主子表一起 join
- `Select(o => new { ... DetailCount = o.Details.Count() })` projection 让 DB 端聚合
- 极致用 Dapper 写聚合 SQL

</details>

### A3. EF Core 怎么处理并发更新（乐观锁）？  → [第 03 / 05 章]
<details><summary>展开</summary>

实体上加 `[Timestamp] public byte[] RowVersion`，EF Core UPDATE 时自动加 RowVersion 到 WHERE，受影响行数 0 → 抛 `DbUpdateConcurrencyException`。

API 层 catch 返回 409，让前端提示用户刷新。

CP6 的 `BaseBizEntity.RowVersion` 就是这套。

</details>

### A4. AsNoTracking 什么时候必须加？什么时候不能加？  → [第 03 章]
<details><summary>展开</summary>

- 必须加：只读列表、报表、跨页查询（避免 ChangeTracker 内存膨胀）
- 不能加：查出来准备改的（加了 SaveChanges 不会生成 UPDATE）

CP6 大部分只读路径加了，少数 dashboard 没加（改进点）。

</details>

### A5. JWT 的安全边界？为什么不能存敏感信息？  → [第 07 章]
<details><summary>展开</summary>

JWT 三段：header.payload.signature。payload 是 base64 编码**不是加密**，任何人能读。HMAC 只保证"内容未被篡改"，不保证内容机密。

只放 userId / role / 必要 claims，绝不放密码哈希、SSN、卡号。

</details>

### A6. JWT 怎么实现"主动注销"？  → [第 07 章 + 第 15 章]
<details><summary>展开</summary>

JWT 自包含 → 服务端默认无法注销已签发的。方案：

1. **黑名单**：注销时把 `jti` 加 Redis SET（带 TTL = token 剩余有效期），验签后查黑名单
2. **短期 + Refresh**：access token 5 分钟过期，注销时让 refresh token 立刻失效 → 5 分钟内泄露的 access 仍可用，但很快过期
3. **会话表**：每次发 token 写 DB，注销时改状态。失去无状态优势

CP6 当前没做主动注销。

</details>

### A7. SignalR 怎么扩展到多副本？  → [第 08 章]
<details><summary>展开</summary>

单实例：`Clients.All` 直接广播。

多副本：用户 A 连 Pod1，事件在 Pod2 发生 → 用户 A 收不到。

**解法**：

- Redis backplane：`AddSignalR().AddStackExchangeRedis(...)`，跨 Pod 通过 Redis pub/sub
- Azure SignalR Service：托管，透明跨实例

CP6 当前没启 backplane，docker-compose 单机 OK，K8s 多副本时必须配。

</details>

### A8. Repository 模式是反模式吗？  → [第 04 章]
<details><summary>展开</summary>

争议大。反对方理由：

- EF DbContext + DbSet 本身就是 Repository + UoW
- 通用仓储泄漏 IQueryable 后等于没封装
- 复杂业务（跨实体）装不进 Repository

CP6 的取舍：提供 `IRepository<T>` 只 5 个方法（不暴露 IQueryable），简单 CRUD 走它，复杂直接 LINQ on DbContext。这是务实的混合解。

</details>

### A9. 你们怎么做跨模块解耦？  → [第 06 章]
<details><summary>展开</summary>

CP6 用 Bridge Hook 模式 + IntegrationEvent 持久化 + Retry Worker + DLQ。

4 个接口：`IMesBridgeHook` / `IWmsBridgeHook` / `IErpBridgeHook` / `IOrderCancelBridgeHook`。

特性：

- **Best-effort**：失败不阻塞主业务
- **幂等**：重试不重复执行
- **可禁用**：appsettings 开关即时切 NoOp
- **持久化**：`T_IntegrationEvent` 写每次调用
- **自动重试**：BackgroundService 60s 扫 Failed
- **死信告警**：超 MaxAttempts 转 Dead + SignalR + IsAlert log
- **健康看板**：24h 成功率 + DLQ 列表 + 手动 Compensate

</details>

### A10. 为什么操作日志走 Kafka 而业务通知走 RabbitMQ？  → [第 07 章]
<details><summary>展开</summary>

Kafka：高吞吐、append-only、可保留可回放，适合日志流。
RabbitMQ：低频、确认配信、可路由可重试，适合业务通知。

CP6 选择分通道是因为两者特点不同。混用一个 MQ 总有妥协。

</details>

### A11. 怎么保证库存不超扣？  → [第 05 章]
<details><summary>展开</summary>

CP6 的 `IStockMovementService.ApplyAsync` 是唯一写入入口：

1. 校验 `AvailableQty >= 0`，否则 throw
2. 同事务写 `T_StockTransaction` 审计
3. 乐观锁 RowVersion 防并发覆盖

高并发场景升级：

- 增量 UPDATE：`SET PhysicalQty = PhysicalQty + @qty WHERE ...` + 行锁
- Redis 分布式锁（多服务实例时）
- 提前预留（Reserve）：受注就占住，发货真扣

</details>

### A12. 为什么 T_Stock 严禁直接 UPDATE？  → [第 05 章]
<details><summary>展开</summary>

不变式守护：所有库存变动必须留审计（`T_StockTransaction`），必须经校验（不超扣、不出过期），必须能触发联动（SignalR、Bridge Hook）。

直接 UPDATE 绕过这一切。所以 CP6 的约定是任何业务都通过 `StockMovementService`。Architecture-level 的强制约束 = team discipline + code review。

</details>

### A13. Bridge Hook 重试机制怎么避免雪崩？  → [第 06 章]
<details><summary>展开</summary>

- 指数退避：`NextRetryAt = now + 60s × 2^attempts`
- MaxAttempts = 5，超过转 DeadLetter（不再自动重试）
- Worker 每批 Take(50)，自然限流
- DLQ 双通道告警，运维介入

</details>

### A14. 用什么测试库？怎么测 Service？  → [第 11 章]
<details><summary>展开</summary>

xUnit + Moq + EF Core InMemory。

Service 测试：
- DbContext 用 InMemory（每用例独立 dbName）
- 外部依赖（IDeadLetterNotifier、IHubContext）用 Moq
- Service 本身真实跑

陷阱：InMemory 不支持事务、RowVersion、SQL 触发器。严肃测试用 SQL Server LocalDb 或容器化 SQL Server。

</details>

### A15. 一个慢接口怎么排查？  → [第 14 章]
<details><summary>展开</summary>

5 步：

1. 日志看 ElapsedMs（CP6 的 OperLog 有）
2. EF SQL 日志或 MiniProfiler 看 SQL 数 + 耗时
3. DB DMV 看慢查询 / 锁等待 / 缺索引
4. APM 看链路耗时分布
5. 是否走了缓存、缓存命中率多少

</details>

### A16. 你怎么处理高并发下的库存超卖？  → [第 05 / 14 章]
<details><summary>展开</summary>

层次化：

- 应用层乐观锁 RowVersion → 冲突时 409 重试
- DB 层增量 UPDATE + 行锁，避免读-改-写两次往返
- 缓存层 Redis SETNX 分布式锁
- 业务层降级：库存预占（Reserve）+ 异步实扣

不同业务量级不同方案。CP6 当前乐观锁应付中等场景够用。

</details>

### A17. 启动时迁移 vs 独立 Job 迁移？  → [第 03 章]
<details><summary>展开</summary>

CP6 选启动时 `db.Database.Migrate()`：

- 优点：简单，部署即生效
- 缺点：多副本同时启动可能冲突；迁移慢的话健康检查超时

更稳的做法：独立 Job 跑迁移：

```yaml
apiVersion: batch/v1
kind: Job
metadata: { name: cp6-migrate }
spec:
  template:
    spec:
      containers:
        - name: migrate
          image: cp6-api:latest
          command: ["dotnet", "ef", "database", "update", ...]
      restartPolicy: Never
```

主应用启动只校验版本不迁移。

</details>

### A18. async/await 的本质？  → [第 02 章]
<details><summary>展开</summary>

`await` 是个状态机生成器：编译器把 async 方法切成多个"段"，await 处把后续封装成 continuation，IO 完成时由线程池任意线程接力执行。

**对比**：
- 同步阻塞：线程在 IO 期间空等，浪费
- async/await：线程被释放回池子，能服务其他请求

**陷阱**：
- 用 `.Result` 或 `.Wait()` 退回同步，可能死锁
- async void 不能等、不能 catch，绝对禁用（除事件处理器外）
- ConfigureAwait(false) 在库代码可考虑（避免捕获 SynchronizationContext）

</details>

### A19. .NET 8 的 minimal API vs Controller？  → [第 02 章]
<details><summary>展开</summary>

- Controller：传统、有 Filter / Model Binding / 装饰器、适合复杂 API
- Minimal API：单文件、性能略好、适合简单端点（healthcheck、metrics）

CP6 用 Controller 主体 + `app.MapGet("/metrics", ...)` 简单端点。混用没问题。

</details>

### A20. 怎么实现"按配置切换实现"？  → [第 02 / 06 章]
<details><summary>展开</summary>

```csharp
if (config.GetValue<bool>("MesBridge:Enabled"))
    services.AddScoped<IMesBridgeHook, MesBridgeHook>();
else
    services.AddScoped<IMesBridgeHook, NoOpMesBridgeHook>();
```

NoOp 实现是空 / 默认值。Null Object 模式。

价值：运行时切换、不写 if-null、测试友好。

</details>

---

## B. 前端 20 题

### B1. Composition API vs Options API？  → [第 09 章]
<details><summary>展开</summary>

Composition：按"关注点"聚合代码（一个功能的状态/方法/计算属性写在一起），TypeScript 推断好，复用用 composable 函数。

Options：按"种类"分散（data/methods/computed），简单组件直观，复杂组件维护差。

CP6 全 `<script setup>`。

</details>

### B2. ref vs reactive 选哪个？  → [第 09 章]
<details><summary>展开</summary>

- 基本类型必须 ref
- 对象 ref / reactive 都行
- ref 不会失去响应性（整体赋值 OK），reactive 解构 / 重赋会丢
- 推荐统一 ref，心智简单

</details>

### B3. CP6 的动态路由怎么实现？  → [第 09 章]
<details><summary>展开</summary>

1. 后端 Sys_Menu 表存路由列表
2. 前端 `viewModules` 字典硬编码 `路径 → import` 映射
3. 登录后 API 返回该用户可见 menus
4. `addDynamicRoutes` 把交集 menus 注册到 router
5. 守卫：刷新页面后从 localStorage 恢复 menus 再 addDynamicRoutes
6. 退出登录 resetRoutes 清空动态部分

</details>

### B4. axios 拦截器有什么用？  → [第 09 章]
<details><summary>展开</summary>

- 请求拦截：附加 JWT、处理 baseURL
- 响应拦截：统一解包 `{ code, message, data }`、401 跳登录、错误提示

CP6 在响应拦截里自动解 data，业务代码直接 `const list = await http.get('/wms/stock')` 拿到 `Stock[]` 而不是 axios 包装。

</details>

### B5. Pinia store 切片怎么定？  → [第 09 章]
<details><summary>展开</summary>

按业务域：auth / dashboard / wms-stock。

跨 View 共享 → store。单 View 内部状态 → ref，别进 store。

不要"大泥球 store"。

</details>

### B6. v-for + v-if 同级有什么问题？  → [第 09 章]
<details><summary>展开</summary>

Vue 3 里 v-if 优先级高于 v-for，导致 v-if 表达式里访问不到 v-for 的 item 变量。

解决：
- 套 template：`<template v-for="x"><div v-if="x.active">`
- computed 提前过滤

</details>

### B7. SignalR 前端怎么处理重连？  → [第 08 章]
<details><summary>展开</summary>

```typescript
new HubConnectionBuilder()
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .build()
```

数组是重连延迟序列。`onreconnecting` / `onreconnected` / `onclose` 回调可加 UI 提示。

CP6 的 `signalr.ts` 还在 onclose 里 `connection = null` 让下次 getConnection 重建。

</details>

### B8. SignalR 消息背压怎么处理？  → [第 08 章]
<details><summary>展开</summary>

- 后端节流：BackgroundService 攒 200ms 一批推
- 前端节流：requestAnimationFrame 批渲染、lodash throttle
- 业务降级：只推 diff 不推全量

</details>

### B9. i18n 走 DB 还是 JSON？  → [第 10 章]
<details><summary>展开</summary>

- JSON：跟前端打包，部署简单
- DB：翻译人员后台直接改，热更新

CP6 选 DB 是因为 ERP 翻译频繁、多角色协作。

</details>

### B10. 大表格性能优化？  → [第 14 章]
<details><summary>展开</summary>

- 虚拟滚动 `el-table-v2`
- 服务端分页（不要前端 filter 全部）
- 列懒渲染（折叠列默认不渲染）
- v-memo 缓存稳定的行

</details>

### B11. 前端首屏优化清单？  → [第 14 章]
<details><summary>展开</summary>

- 路由懒加载（CP6 已做）
- Element Plus 按需引入（CP6 没做，可改进）
- 图片 WebP / 懒加载
- CDN + Brotli 压缩
- i18n 按 namespace 分包
- preload critical chunks

</details>

### B12. v-html 安全吗？  → [第 15 章]
<details><summary>展开</summary>

危险，XSS 入口。必须用时配合 DOMPurify：

```vue
<div v-html="DOMPurify.sanitize(userInput)"></div>
```

CP6 没用 v-html。

</details>

### B13. composable 怎么设计？  → [第 09 章]
<details><summary>展开</summary>

纯函数返回 reactive 对象 + 方法：

```typescript
export function useTable<T>(loader: () => Promise<T[]>) {
  const list = ref<T[]>([])
  const loading = ref(false)
  async function refresh() { /* ... */ }
  return { list, loading, refresh }
}
```

跨 View 复用纯逻辑，比 mixin 干净（不互相覆盖）。

</details>

### B14. TypeScript 用得怎样？  → [第 09 章]
<details><summary>展开</summary>

CP6 全 TS，axios 泛型、Composition API 推断、Pinia store 类型都安全。

加分项：
- 严格模式 `"strict": true`
- 用 `type` over `interface`（属人偏好）
- `as const` 字面量收窄
- 自定义类型守卫

</details>

### B15. CSRF 在 SPA + JWT 场景？  → [第 15 章]
<details><summary>展开</summary>

JWT in localStorage 天然防 CSRF（攻击者无法读 localStorage 跨域）。

但 XSS 仍危险（XSS 能读 localStorage）→ 必须严防 XSS。

CP6 没用 cookie，无需 antiforgery token。

</details>

### B16. 怎么做按钮级权限？  → [第 10 章]
<details><summary>展开</summary>

前端自定义指令：

```typescript
app.directive('permission', (el, binding) => {
  if (!auth.hasPermission(binding.value))
    el.style.display = 'none'
})

// 用法
<el-button v-permission="'wms.stock.delete'">删除</el-button>
```

后端 API 同时校验（兜底）。CP6 当前只做菜单级。

</details>

### B17. Vite 跟 Webpack 比？  → [第 09 章]
<details><summary>展开</summary>

Vite：ESM 原生 + esbuild，dev server 启动快、HMR 秒级。
Webpack：成熟生态、CommonJS 兼容、复杂配置可控。

CP6 用 Vite，开发体验显著好。生产构建用 Rollup（Vite 内置）。

</details>

### B18. Vue 错误处理？  → [第 09 章]
<details><summary>展开</summary>

`app.config.errorHandler` 全局兜底。CP6 用它吞 patch 阶段的"parentNode of null" 瞬态错误。

加 Sentry / TrackJS 上报生产错误。

</details>

### B19. 状态管理选 Pinia 还是 Vuex？  → [第 09 章]
<details><summary>展开</summary>

Pinia（Vue 官方推荐 Vue 3 状态库）：

- TypeScript 友好
- Composition API 写法
- 无 mutation，直接改 state
- DevTools 集成

CP6 选 Pinia 正确。

</details>

### B20. SSR / SSG 你考虑过吗？  → [第 09 章]
<details><summary>展开</summary>

CP6 是后台管理系统，SEO 无需求 → CSR 足够。

SSR (Nuxt 3) 适合：
- SEO 关键页面（电商商品、博客）
- 首屏 critical render path
- 复杂的 SEO 需求

CP6 用纯 CSR + 路由懒加载已经够快。

</details>

---

## C. 架构与 DevOps 20 题

### C1. 你的项目分了几层？为什么？  → [第 01 章]
<details><summary>展开</summary>

CP6: Entity / Core / WebApi / Tests + 前端 cp6.web。

依赖方向严格单向：WebApi → Core → Entity。前端通过 HTTP / SignalR。

不是教科书 DDD，是"分层 + 依赖反转"。务实选择。

</details>

### C2. 怎么处理跨模块联动？  → [第 06 章]
<details><summary>展开</summary>

CP6 的 Bridge Hook 模式 + IntegrationEvent + Retry + DLQ。已答过（A9）。

如果未来拆微服务：把 Bridge Hook 实现换成 MQ 发布即可，调用方代码不变。这是"准备 future-proof"的设计。

</details>

### C3. 怎么做数据库迁移？  → [第 03 章]
<details><summary>展开</summary>

EF Core migrations。开发期 `dotnet ef migrations add Xxx`，部署时 `db.Database.Migrate()`（CP6）或独立 Job。

破坏性迁移分三步：
1. 添加新列 nullable
2. backfill 默认值
3. ALTER NOT NULL

</details>

### C4. CI/CD 怎么走？  → [第 12 章]
<details><summary>展开</summary>

CP6 当前看到 `deploy-to-server.ps1` 和 `redeploy-cp6uk.bat`，是手动脚本。

生产推荐：
- GitHub Actions / Azure DevOps
- 测试 → build → push image → 部署 staging → 验证 → prod
- 回滚一键

</details>

### C5. 怎么做零停机部署？  → [第 12 章]
<details><summary>展开</summary>

K8s Deployment `RollingUpdate` + `maxSurge: 1, maxUnavailable: 0` + readinessProbe。

新 Pod 起来 → readiness 通过 → 加入 Service → 旧 Pod 停掉。流量永不中断。

</details>

### C6. 健康检查怎么设计？  → [第 12 章]
<details><summary>展开</summary>

CP6 当前 tcpSocket 探针（端口能连 = 健康）。

更好的做法：HTTP `/health` 端点检查 DB + Cache + MQ 都通：

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CP6Context>()
    .AddRedis(...)
    .AddRabbitMQ(...);
app.MapHealthChecks("/health");
```

分 readiness（启动完成）和 liveness（运行健康）两个端点。

</details>

### C7. 缓存策略？  → [第 14 章]
<details><summary>展开</summary>

CP6 用 `IDistributedCache`（开发期 Memory / 生产 Redis）+ `CacheService` 包装 Cache-Aside。

主动失效：改翻译时 `RemoveAsync("lang:zh-CN")` 立刻生效。

防击穿：热点 key 用 SemaphoreSlim 单飞。
防雪崩：TTL 加随机抖动。

</details>

### C8. Redis 数据怎么不丢？  → [第 12 / 14 章]
<details><summary>展开</summary>

- RDB 快照（性能好，可能丢几秒）
- AOF 写日志（一次写一次 fsync，强一致）
- 主从 + sentinel
- Cluster 模式分片

CP6 缓存场景丢了重算，不强求持久化。

</details>

### C9. Kafka vs RabbitMQ 选哪个？  → [第 07 章]
<details><summary>展开</summary>

- Kafka：高吞吐、append-only、可回放，日志流首选
- RabbitMQ：低频、确认配信、可路由，业务通知首选

CP6 双用：Kafka 操作日志 + RabbitMQ 业务通知。

</details>

### C10. K8s 三大对象的差别？  → [第 12 章]
<details><summary>展开</summary>

- **Deployment**：声明期望的 Pod 状态（副本数、镜像、策略）
- **Service**：稳定的网络入口（ClusterIP / NodePort / LoadBalancer）
- **Ingress**：L7 路由层，一个 Ingress 路由多个 Service

CP6 用 Deployment + ClusterIP Service + Nginx Ingress + HPA。

</details>

### C11. HPA 怎么工作？  → [第 12 章]
<details><summary>展开</summary>

公式：`desiredReplicas = ceil(currentReplicas × currentMetric / targetMetric)`

需要 Metrics Server。

更复杂用 Custom Metrics（业务指标如队列长度）。

</details>

### C12. 怎么管密钥？  → [第 12 / 15 章]
<details><summary>展开</summary>

环境化：

- 本地：appsettings.Local.json + .gitignore
- Docker：.env + ${VAR}
- K8s：Secret + envFrom
- 严肃 prod：Azure Key Vault / Vault + DI 集成

绝不能 Git 提交密钥。pre-commit gitleaks 兜底。

</details>

### C13. 怎么做可观测性？  → [第 13 章]
<details><summary>展开</summary>

三大支柱：

- Logs：结构化日志（Serilog）+ ELK/Loki
- Metrics：Prometheus + Grafana
- Traces：OpenTelemetry → Tempo/Jaeger

CP6 当前有 OperLog + Prometheus，缺 Trace（改进点）。

</details>

### C14. 你的架构怎么应对单点故障？  → [第 12 / 14 章]
<details><summary>展开</summary>

- API：K8s 多副本 + HPA
- DB：SQL Server AlwaysOn 主备
- Redis：sentinel / Cluster
- Kafka：3+ broker 分区副本
- 入口：Cloudflare 边缘抗 DDoS

CP6 demo 是单点（一个 DB / 一个 Redis），生产部署要分布式化。

</details>

### C15. 怎么做灰度发布？  → [第 12 章]
<details><summary>展开</summary>

- Ingress 按 header / cookie 路由 N% 流量到新版本
- Argo Rollouts 自动金丝雀
- 后端 feature flag 控制功能开关

CP6 当前没做，是改进点。

</details>

### C16. 怎么做日志收集？  → [第 13 章]
<details><summary>展开</summary>

容器日志 → Fluent Bit / Filebeat → Kafka / ES → Kibana / Grafana。

CP6 的 OperLog 是审计走 Kafka + DB，应用日志走 stdout（容器日志）需要收集（当前没看到配置）。

</details>

### C17. 你的项目 RTO / RPO 是多少？  → [第 12 / 14 章]
<details><summary>展开</summary>

RTO（Recovery Time Objective）= 故障恢复时间目标
RPO（Recovery Point Objective）= 可接受的数据丢失时间

中等业务：RTO 15 分钟 / RPO 1 小时。

需要：

- 每小时增量备份
- 异地副本
- 定期演练（CP6 当前没做）

</details>

### C18. 设计一个全新的 ERP 模块，你的步骤？  → [第 01 / 06 章]
<details><summary>展开</summary>

参考 CP6 流程：

1. **Entity**：定义 DomainModel 继承 BaseBizEntity
2. **Context**：DbSet + OnModelCreating 索引
3. **Service**：I*Service + *Service（写库存必走 IStockMovementService）
4. **采番**：用 IDocNumber / IWmsSequenceService
5. **Controller**：[Route("api/...")] 返回 `{code, message, data}`
6. **DI**：Program.cs 注册
7. **Migration**：dotnet ef migrations add
8. **Bridge Hook**：如果联动其他模块加接口
9. **前端**：api/types/view/router
10. **i18n + 菜单**：种子 SQL
11. **测试**：CP6.Tests 加用例
12. **文档**：更新 PROJECT_STRUCTURE.md

</details>

### C19. 你接手老项目，第一件事？  → [所有章]
<details><summary>展开</summary>

1. 跑起来（Docker / dev 都跑通）
2. 读 README + 项目结构图
3. 跑测试（看覆盖率 + 哪些失败）
4. 摸数据流（用户登录 → 关键业务一遍）
5. 找"约定 / 不变式"（CP6 的库存铁律、Bridge Hook 规范）
6. 看 git log 半年（理解最近痛点）
7. 列出改进点排优先级
8. 跟团队讨论，不要单方面重构

</details>

### C20. 这个项目你会怎么改进？  → [所有章]
<details><summary>展开</summary>

CP6 改进清单（实际可提的）：

- 缺 OpenTelemetry trace
- Repository OFFSET 分页 → keyset
- SignalR 多副本缺 Redis backplane
- 启动迁移 → 独立 Job
- 全局 [Authorize] + 行级数据权限
- Refresh token 机制
- Rate limit 防爆破
- Element Plus 按需引入缩 bundle
- el-table → el-table-v2 虚拟滚动
- Lang API 按 namespace 分包
- ResponseCompression
- Health check 端点细化
- 镜像切 Alpine 缩小
- 加 ESLint / Prettier / Husky pre-commit
- CI 自动化（GitHub Actions）

但答时要分**优先级**和**业务价值**，别一口气列 20 个让人觉得只会挑毛病。先答 2-3 个核心 + "如果时间允许还会做..."。

</details>

---

## 答题套路（万能模板）

任何技术问题用这个套路答，资深感会上来：

> **【是什么】** 这个东西的本质是 X。  
> **【为什么】** 它解决了 Y 问题。  
> **【怎么做】** 在 CP6 项目里我们这样实现：__（贴代码片段）__  
> **【取舍】** 我们没选 Z 方案是因为 __；如果场景变成 W，我会用 Z。  
> **【经验】** 真实踩过的坑：__。

举例 A1（DI 生命周期）：

> 【是什么】Singleton / Scoped / Transient 是 DI 容器管理对象生命周期的三种粒度。
> 【为什么】不同类型的服务有不同的状态/资源需求，统一一种会浪费或出 bug。
> 【怎么做】CP6 里 DbContext 用 Scoped，每请求一份；Cache 包装 Singleton，整个进程共享；HostedService 是 Singleton 但用 IServiceScopeFactory 取 Scoped 服务。
> 【取舍】没用 Transient 是因为业务 Service 几乎都依赖 DbContext，Transient 会让一个请求里多个 Service 用不同 DbContext，事务破裂。
> 【经验】踩过的坑：Filter 用 Filters.Add<T>() 是 Singleton 创建，CP6 用 Filters.AddService<T>() 才能从 DI 拿 Scoped。

---

## 🔗 延伸阅读

- [Cracking the Coding Interview](https://www.crackingthecodinginterview.com/) — 算法面试经典
- [System Design Interview (Alex Xu)](https://www.amazon.com/System-Design-Interview-insiders-Second/dp/B08CMF2CQF) — 架构面试经典
- [Designing Data-Intensive Applications](https://dataintensive.net/) — 分布式系统的圣经
- [The Pragmatic Programmer](https://pragprog.com/titles/tpp20/the-pragmatic-programmer-20th-anniversary-edition/) — 职业素养

---

**预祝 offer 拿到手软。**
