# CP6 全栈学习丛书

> **定位**：不是"项目说明书"（看 [`PROJECT_STRUCTURE.md`](../PROJECT_STRUCTURE.md)），不是"从零搭建教程"（看 [`DEVELOPMENT-GUIDE.md`](../../DEVELOPMENT-GUIDE.md)），而是 —— **拿 CP6 的真实代码当教材，学高级全栈思维**。
>
> 每章拆 1~2 个真实文件，讲：**为什么这么写、不这么写会出什么事、面试官会怎么问、业界其他方案怎么对比**。
>
> 作者视角：你已经会基础语法，正在从"能跑就行"过渡到"知道为什么这样设计、会被问的时候答得出"。

---

## 怎么用这套书

- **从前往后看**：是一条 ERP/MES/WMS 一体化系统的全栈学习路径。
- **挑章节看**：每章独立可读，最后一节都是自检题 + 延伸阅读。
- **面试前**：直接翻 [16. 模拟面试 60 题](./16-mock-interview.md)。
- **遇到问题**：每章 ⚠️ 踩坑记录是项目里真实跌过的跤。

每章固定结构：

| 段落 | 用途 |
|---|---|
| 📍 学习目标 | 学完这章你能答出哪些问题 |
| 🔎 真实代码切片 | 直接引用 CP6 的源码片段（带文件:行号） |
| 💡 资深视角 | "为什么这么写"、"业界其他方案对比" |
| ⚠️ 踩坑记录 | 项目里真实踩过/规避过的坑 |
| 🧪 自检题 | 面试可能问的形式，3~5 道 |
| 🔗 延伸阅读 | 官方文档、经典文章 |

---

## 章节目录

### Part 0 · 总览
- [00. 怎么读这套丛书](#怎么用这套书)（本页）

### Part 1 · 架构层（看清骨架）
- [01. 分层架构与依赖方向](./01-architecture-layering.md) — `.slnx` + 4 个 csproj
- [02. DI 容器与 Program.cs 编排术](./02-di-and-program.md) — `Program.cs`
- [03. EF Core + Dapper 混用之道](./03-ef-and-dapper.md) — `CP6Context.cs` + Dashboard
- [04. 通用仓储 + Service 模板](./04-repository-service-pattern.md) — `RepositoryBase` / `ServiceBase`

### Part 2 · 领域层（看清业务）
- [05. 领域不变式：库存这一道铁律](./05-stock-invariant.md) — `IStockMovementService` + `T_Stock`
- [06. 跨模块联动：Bridge Hook 模式](./06-bridge-hook-pattern.md) — 4 个 `I*BridgeHook` + DLQ

### Part 3 · API 与实时（看清边界）
- [07. JWT 认证 + 全局过滤器审计](./07-jwt-and-operlog-filter.md) — `AuthController` + `OperLogFilter`
- [08. SignalR 实时推送 + 背压](./08-signalr-and-backpressure.md) — `Hubs/` + `BackgroundServices/`

### Part 4 · 前端（看清左半边）
- [09. Vue3 + TS + Pinia + 动态路由](./09-vue3-frontend.md) — `main.ts` + `router/index.ts`
- [10. DB 驱动 i18n + 菜单权限](./10-i18n-rbac.md) — `Sys_Langs` + `Sys_Menu`

### Part 5 · 测试与运维（看清右半边）
- [11. 测试金字塔：xUnit + Moq + InMemory](./11-testing.md) — `CP6.Tests/`
- [12. DevOps：Docker / Compose / K8s / cloudflared](./12-devops.md) — `docker-compose.yml` + `k8s/`
- [13. 可观测性：日志 / 指标 / 追踪](./13-observability.md) — `OperLogFilter` + `BridgeMetricsCollector`

### Part 6 · 横向（看清取舍）
- [14. 性能与扩展性清单](./14-performance.md)
- [15. 安全清单](./15-security.md)
- [16. 模拟面试 60 题](./16-mock-interview.md)

---

## 三条角色路线（按 90 分钟通读）

| 你想应聘什么 | 必读章节 | 选读 |
|---|---|---|
| **高级后端** | 01 → 02 → 03 → 04 → 05 → 06 → 07 → 13 → 16 | 11, 14, 15 |
| **高级前端** | 01 → 02 → 07 → 08 → 09 → 10 → 12 → 16 | 13, 14, 15 |
| **架构师 / Staff** | 01 → 02 → 06 → 08 → 12 → 13 → 14 → 15 → 16 | 05, 07 |

---

## 写在前面：CP6 这套代码值得当教材的几个理由

1. **真实业务边界**：ERP↔MES↔WMS 不是 todo-list，是会卡库存、卡审批、卡发票的真实业务流。
2. **跨模块联动有"接缝"概念**：四个 Bridge Hook + IntegrationEvent + DLQ + 健康看板，把"模块解耦"这件事做完了一个完整闭环。
3. **基础设施齐**：Docker Compose + K8s + cloudflared 都在仓库里，不是 PPT。
4. **测试覆盖到 290+**：每个领域 Service 都能跑用例，不是纸面架构。
5. **文档线索完整**：需求规格、ER 图、菜单/i18n 种子 SQL 都在 `docs/`，可以反向理解为什么代码长这样。

但也有缺点（这本书会一并指出）：
- 部分文件夹有冗余命名（如 `Wms/PlateMoldService` 与 `PlateMoldService` 同名不同域）
- 一些 BackgroundService 缺指标埋点
- 前端的"动态路由 + 刷新重建"那段逻辑比较脆

资深开发者看代码不该只看亮点，也要识别可改进面 —— 这本书会给你这种"批判式阅读"的脚手架。

---

*生成于 2026-06-09，与项目代码同步。后续若有大改动，章节末"延伸阅读"会贴更新链接。*
