# CP6 项目总览

> 状态快照：2026-07-18；分支：`feat/general-role-vperm`；恢复标签：`migration-2026-07-18-ready`。

## 项目定位

CP6 是面向纸箱包装制造企业的一体化 ERP / MES / WMS 与 SaaS 中后台。项目源于日本纸箱企业核心系统刷新，现以 Vue 3 + ASP.NET Core + SQL Server 重写为独立 Web 产品。

核心业务链：

`销售接单（ERP）→ 生产执行（MES）→ 库存、拣选与发货（WMS）→ 财务/采购闭环`，并由 OA/WF、权限平台、计划中台、3D 空间底座和多租户能力支撑。

## 当前规模（仓库实扫）

| 项目 | 2026-07-18 实际值 |
|---|---:|
| WebApi Controller | 145 |
| Vue 页面 | 222 |
| `*Tests.cs` 测试文件 | 371 |
| EF Core 迁移 | 113 |
| 用户数据库 | 3：CP6DB、CP6DB_OA、CP6DB_SpaceQA |

旧文档中“66/112 Controller、110 页面、970 用例”等数字是历史快照，不应作为当前计数。

## 技术栈

- 后端：ASP.NET Core 10、EF Core 10、Dapper、SQL Server 2022、JWT、SignalR。
- 前端：Vue 3、TypeScript、Element Plus、Pinia、vue-i18n、Vite、Vitest、Playwright。
- 基础设施：Docker Compose、Redis、RabbitMQ、Kafka、cloudflared；另有 K8s 清单。
- 多语言：数据库驱动，支持简中、繁中、英语、日语、韩语。

## 仓库入口

- `README.md`：项目与文档地图。
- `docs/CODEMAP.md`：代码导航，结构有效但计数可能过时。
- `docs/00-功能盘点.md`、`00-执行计划总盘.md`、`00-product-blueprint.md`：战略入口。
- `docs/codemap-*`：各领域代码级手册。
- `docs/superpowers/specs`、`plans`、`qa`：设计、执行计划和验收证据。
- `docs/seeds`：权限键与菜单锚点的权威表。
- `migration/README.md`：换机与数据库恢复入口。
- `docs/project-memory/10-AI-Handoff.md`：AI 接手的第一读物。

## 当前开发主线

当前正在执行 `docs/superpowers/plans/2026-07-17-general-role-vperm.md`：普通角色预置 + 全模块 `v-permission` 铺设。

- T1 标准角色种子：完成。
- T2 OA/WF 前端权限：完成。
- T3 ERP 前端权限：完成。
- T4 MES、T5 FIN、T6 PUR/PLAN：待继续。
- T7 合并、部署、数据库种子验证和端到端冒烟：待完成。

## 文档真相优先级

发生冲突时按以下顺序判断：当前代码与测试 > 当前分支 Git 历史 > 对应最新 plan/spec > `project-memory/PROJECT_STATE.md` > CODEMAP/旧盘点 > archive。
