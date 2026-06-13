# CP6

本系统源于日本某大型纸箱包装企业（Crown Package）的核心系统刷新项目（基幹システム刷新PJ）。原系统是 mcframe7 ERP 平台的 Add-on，现重构为独立 Web 系统，现代化技术栈（**Vue 3 + .NET 8/10**）全新开发。**商用化路线：深耕纸箱行业，做成完整可售产品。**

---

## 现状一句话

CP6 当前是一套完整的 **「进销存 + MES」**：販売(MSBB) → 生産(MES) → 在庫物流(WMS) 正向链 + ERP→MES→WMS 闭环全通（66 控制器 / 110+ 前端页面）。下一程目标是补齐**完整 ERP + 可售 SaaS**——财务/采购/审批/权限组织/计划中台/多租户/3D 空间底座（需求文档 + 实施计划已就绪，待编码）。

详见 → **`docs/00-功能盘点.md`**（现状+缺口）、**`docs/00-执行计划总盘.md`**（16 份实施计划+执行顺序）、**`docs/00-product-blueprint.md`**（建设蓝图）。

---

## 技术栈

| 层 | 技术 |
|---|---|
| 后端 | ASP.NET Core 10 · EF Core 10 + Dapper · SQL Server · JWT · SignalR · Kafka(日志) |
| 前端 | Vue 3 + TS · Element Plus · Pinia · vue-i18n(5 语) · Vite · Playwright |
| 部署 | Docker Compose / K8s / cloudflared 隧道 |

## 代码结构（folder = namespace）

```
CP6.Entity/   实体层  DomainModels/{Common,Sys,Erp,Mes,Wms,Integration} + DTOs
CP6.Core/     核心层  Services/{...} + EFDbContext(CP6Context) + Migrations + Options + Utilities
CP6.WebApi/   API层   Controllers/{Erp,Mes,Wms,Sys,Integration} + BackgroundServices + Hubs + Filters + Observability
CP6.Tests/    测试    xUnit + EF Core InMemory
cp6.web/      前端    src/{views,api,stores,types,components,composables,i18n,router}/<module>
```
分层依赖（严格单向）：`WebApi → Core → Entity`；`cp6.web ─HTTP/WS→ WebApi`。

## 模块现状

| 模块 | 状态 |
|---|---|
| 販売管理 MSBB / 生産管理 MES / 在庫物流 WMS | ✅ 已实现（WMS 含纸器业特化：原紙/インキ/パレット/残材…） |
| ERP→MES→WMS 闭环（Bridge Hook + IntegrationEvent） | ✅ Phase 1-4 |
| 系统底座（用户/角色/菜单/字典/多语言/日志） | ✅ 已实现（薄层：单角色页面级，待 PUB 升级） |
| PUB 权限平台 / OA 审批 / 财务会计 / 采购 / 计划中台 MRP / Space 3D / 多租户 | ⏳ 文档+实施计划就绪，待编码（见执行总盘） |

---

## 文档地图（docs/，2026-06-13 整理）

| 路径 | 内容 |
|---|---|
| `docs/00-{功能盘点,执行计划总盘,product-blueprint}.md` | **战略入口三件套**：现状缺口 / 计划清单+顺序 / 建设蓝图 |
| `docs/PROJECT_STRUCTURE.md` · `DEVELOPMENT-GUIDE.md` | 项目结构参考 / 开发上手指南 |
| `docs/superpowers/specs/` · `docs/superpowers/plans/` | 设计 spec / 实施计划（16 份 2026-06-13-*） |
| `docs/{pub,finance,oa,procurement,space,approval}/` | 各新模块设计丛书 |
| `docs/detailed-spec/` | 既存 MSBB 详细规格（逆向日文設計書） |
| `docs/seeds/` · `docs/manuals/` | SQL 种子（i18n/菜单/demo）/ 用户操作手册 |
| `docs/{oa,learning,learning-basics}/` | 学习教材（OA 引擎从零讲解等） |
| `docs/archive/` | 历史任务笔记 |
| `docs/file/`（本地·gitignore） | 原始設計書源（MSBB 逆向用，大体量，不入仓库） |

---

## 快速开始

```bash
# 后端
dotnet build && dotnet run --project CP6.WebApi
# 前端
cd cp6.web && npm install && npm run dev
# 一键 WMS 种子（含菜单/i18n）
./start-wms-phase1.ps1
```
开发指南详见 `DEVELOPMENT-GUIDE.md`。
