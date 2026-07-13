# Task T11 报告：CsrfMiddleware 豁免 /hubs 路径（SignalR negotiate 403 修复）

**Status: DONE**  ·  分支 `feat/wfs-cleanup-tickets`  ·  commit `f01d49f`（已 push）  ·  方案 B1

## 核实证据（Step A/A2 静态实证）

1. **Hub 全枚举**（`grep MapHub` on `Program.cs:2646-2649`）—— 现存 **4** 个 hub，均在 `/hubs` 前缀下：
   - `/hubs/notify`（NotifyHub）
   - `/hubs/mes`（MesHub）
   - `/hubs/wms`（WmsHub）
   - `/hubs/space`（SpaceHub，波4 新增，票面写于 07-05 未涵盖）
   → B1 用 `/hubs` **前缀**（非逐一精确路径）天然覆盖全部 4 个，含 SpaceHub，且**不触碰任何 Space 文件**（零跨模块污染达标）。
2. **403 来源实证**（中间件序）：`Program.cs:2639` `UseMiddleware<CsrfMiddleware>()` 注册于 `2646` MapHub **之前** → negotiate POST `/hubs/*/negotiate` 先过 CsrfMiddleware。CsrfMiddleware 修前豁免表仅 `/api/auth/login`（`:31`），negotiate 无 `X-CSRF-Token` 头 → `BizException("E-SEC-010", 403)`。**确认 403 来自 CSRF、发生在 negotiate**。
3. **A2**：WS upgrade 是 GET（安全方法）不被拦；仅 negotiate（POST）被拦 → 放行 hub 路径即足够。
4. **未被后续波修掉**：`grep hubs|IsExempt` on `Middleware/` 修前零命中；无既有 hub 豁免。

## 变更（B1）

- `CsrfMiddleware.cs`：内联豁免抽为可测 `internal static bool IsExempt(path)` = `PathMatches("/api/auth/login") || PathMatches("/hubs")`。复用既有 `PathMatches` 段边界匹配（`/hubsxxx` 不误豁免）。
- `CP6.WebApi.csproj`：加 `<InternalsVisibleTo Include="CP6.Tests" />`。**根因**：`IsExempt`/`PathMatches` 为 `internal`，而 WebApi 此前无 InternalsVisibleTo（仅 CP6.Core 有），票面测试直断 `CsrfMiddleware.IsExempt` 会 CS0117 编译失败 → 照 CP6.Core 既有模式暴露 WebApi internals 给测试工程。
- `CP6.Tests/Wf/CsrfHubExemptionTests.cs`：新增（票面 Theory + 补 `/hubs/space/negotiate=true`、`/api/auth/login=true` 两断言）。

## 红绿

- **红**：`IsExempt` 不存在 → `CS0117 'CsrfMiddleware' does not contain a definition for 'IsExempt'`（编译失败）。
- **绿**：`--filter CsrfHubExemptionTests` → **8/8 Passed**。
- **CSRF 闸**：`--filter Csrf` → **17/17 Passed**（含既有 SecurityMiddlewareTests，无回归）。
- **全量**：`dotnet test CP6.Tests` → **1843 Passed / 5 Skipped / 0 Failed**（基线 1835 + 新增 8 = 1843）。

## 疑虑 / 说明

- 未跑现场浏览器复验（Step A1/B1-5、DoD T11 现场复验）——本轮为静态 TDD，无起前后端隔离库。静态链路已实证（中间件序 + negotiate POST + E-SEC-010 抛点）。现场 200/`[SignalR] Connected` 复验建议部署时随冒烟一并确认。
- 未动 `signalr.ts`（B1 不需前端改动）→ 前端 vitest/type-check 无关本票，未跑。
- EF 无实体改动，未新增迁移（本票零 DbSet 变更）。
