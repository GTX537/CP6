# Space P3 · SP3 拣货路径规划做真 — QA 证据

**日期**：2026-06-29　**分支**：`feat/space-p3-pathfinding`　**worktree**：`D:\CP6-space-backend`

承 spec `docs/superpowers/specs/2026-06-29-space-p3-sp3-pathfinding-design.md`（v1.1）+ plan `2026-06-29-space-p3-sp3-pathfinding.md`。

## 环境（隔离栈，未碰 D:\CP6 / 5173）

- 后端 5177 ← `CP6.WebApi/appsettings.Local.json` → 隔离库 **`CP6DB_SpaceQA`**（`localhost\KOUSQLSERVER`，Windows 认证）。
- 前端 vite **5180**（独立端口，proxy `/api`→5177；避开并发 wfs-B 会话的 5173）。
- 登录 admin / 123456（dev Csrf 关）。viewer 路由 `/space/viewer/{siteId}?floorId={floorId}`。
- 演示 Site QAWH `F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE` / Floor F1 `5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F`。

## 种子（`seed.sql`，幂等）

多巷十字网格 + 4 个 spread-out 拣货库位 + 一张「明显绕路」出库单：

- **网格 aisle**：`SP3-H1`(y=500) / `SP3-H2`(y=3500) / `SP3-V1`(x=500) / `SP3-V2`(x=3500) → 4 个连通交叉口角点 (500/3500 × 500/3500)。
- **库位**（`Placed=1`，挂 rack R1 `0A00…0001`，否则 `/scene` 因 `RackId!.Value` 崩）：`SP3-TL`(500,450) `SP3-TR`(3500,450) `SP3-BL`(500,3550) `SP3-BR`(3500,3550)。
- **出库单 `OB-SP3-CROSS`**：LineNo **故意绕路** 1=TL → 2=BR → 3=TR → 4=BL（来回横跨网格）。优化序应回到周长 TL→TR→BR→BL。

种子坑（已在 seed.sql 处理）：① `LineNo` 是 T-SQL 保留字 → `[LineNo]`；② `Space_Location` 有过滤唯一索引 → 须 `SET QUOTED_IDENTIFIER ON`；③ `Space_Aisle.Polygon` 与 `Centerline` 均 NOT NULL（Polygon 给 `'[]'`）；④ `/scene` 要求每个 Placed 库位有 `RackId`（挂 R1）。

运行：`& "…\SQLCMD.EXE" -S "localhost\KOUSQLSERVER" -E -d CP6DB_SpaceQA -i "…\seed.sql"`

## 验收结果（gstack headless Chromium，真库真栈）

| 点 | 验收 | 结果 |
| --- | --- | --- |
| **A 真交叉口图** | 拣货路径沿巷道走、不穿货架；无 `W-SPACE-801`(degraded) | ✅ pick-path API 200；planner 算出 **actual=15.3m**（绕网格走）而非直连对角线和 ~11.7m → **证明路径沿真交叉口图绕行**（v1 会退化直连）。`degradedPairCount=0`，无 degraded 提示。 |
| **B 重排对比 (what-if)** | 面板显示「实际/优化/省%」+ 优化线开关 | ✅ 面板：`拣货路径：4 点，总距 15.3 米` + **`实际 15.3 米 / 优化 9.3 米 / 省 39%`**；`显示优化路径` 复选框仅 pathLoaded 时出现；**勾选 → 绿色优化线叠加**（`02-optimized-on.png`），**取消 → 绿线消失**（`03-optimized-off.png`，青色 actual + 小车仍在）。 |
| **C A\*** | 路径与 dijkstra 同形 | ✅ 单测 `astar` 与 dijkstra 最短距离等价；真栈路径沿网格渲染（青线+粉小车）。 |
| **无回归** | 07/08 + 既有单巷 pick-path | ✅ `/scene` 200、`/stock` 200、`/workload` 200、`/devices` 200、既有 `OB-PICK-DEMO`(单巷) pick-path 200 数据正确；面板 07 图例(空/有货/满/锁定/在拣)+08 作业热图/设备区 全渲染。 |

**关键数值验证**：actual 路径 LineNo 绕路序 TL→BR→TR→BL 的网格路程 = 6.1+3.1+6.1 ≈ **15.3m**；优化周长序 TL→TR→BR→BL = 3.1×3 ≈ **9.3m**；省距 **39%**。与纯逻辑单测（`planPickComparison` deterministic 2700/1300 用例）同构，前后端闭环一致。

## 截图

- `01-actual-path.png` — 加载 OB-SP3-CROSS：青色 actual 路径 + 粉色小车，面板统计。
- `02-optimized-on.png` — 勾「显示优化路径」：绿色优化线叠加 + 复选框选中。
- `03-optimized-off.png` — 取消勾选：绿线消失，仅青色 actual + 小车，复选框未选中。

## 已知 headless 限制（非缺陷，沿用 07/08/SP2）

- 演示网格仅 4×4m，在大楼层视图里渲染很小；合成 wheel/double-click **拉不动 OrbitControls 相机**，故路径细节像素级肉眼核受限。**路径沿巷道（非对角穿越）由 actual=15.3m≠直连11.7m 的距离数学 + 单测交叉口连通用例闭环证明**，不依赖像素核。
- toast TTL 3s < 冷后端首调 5-6s，I/W toast 难定格（已用 API 状态码 + 面板文本替代）。

## 结论：**DONE** — SP3 三特性（A 真交叉口图 / B 重排对比 / C A\*）真库真栈验收通过，零回归。
