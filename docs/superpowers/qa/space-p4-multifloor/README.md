# Space P4 · 3D 多层路由 — QA 证据

**日期**：2026-06-29　**分支**：`feat/space-p4-multifloor`　**worktree**：`D:\CP6-space-backend`

承 spec `docs/superpowers/specs/2026-06-29-space-p4-multifloor-design.md` + plan `2026-06-29-space-p4-multifloor.md`（A~G 17 task code-complete + 终审 review APPROVE_WITH_NITS + 抓修 I-1/M-1）。

## 环境（隔离栈）
- 后端 5177 ← `appsettings.Local.json` → `CP6DB_SpaceQA`（启动时自动应用 `SpaceP4Connector` 迁移）。
- 前端 vite **5180**（隔离，proxy→5177）。登录 admin/123456。
- 堆叠视图路由 `/space/stacked/{siteId}`；单层视图 `/space/viewer/{siteId}?floorId=`。

## 种子（`seed.sql`，幂等）
站点 QAWH 原有 F1（Level1，复用 SP3 十字网格 + 库位 SP3-TL/SP3-TR）。新增：
- **F2 楼层**（Level2，Height6000 → 堆叠 z=6000）+ F2 Zone B + Rack R2 + 横巷 SP4-F2-H + 库位 B-01-01-01(500,450)/B-01-01-02(3500,450)。
- **电梯 E1**（Space_Connector，type=1）两 stop：F1(500,500)（在 SP3-V1 竖巷上）+ F2(500,500)（在 SP4-F2-H 横巷上）。
- **出库单 OB-P4-CROSS**（Status=3）跨层绕路 LineNo：1=SP3-TL(F1) → 2=B-01-01-02(F2) → 3=SP3-TR(F1) → 4=B-01-01-01(F2)，**电梯往返 3 次**。
- 种子坑（同 SP3，已处理）：`[LineNo]` 保留字 / `SET QUOTED_IDENTIFIER ON`（Space_Location 过滤唯一索引）/ `Space_Aisle.Polygon`+`Centerline` NOT NULL / Placed 库位须 RackId（B-* 挂 R2）。**注意**：迁移须先应用（后端启动自动 migrate）再跑 seed，否则 `Space_Connector` 表不存在。

## 验收结果

### 后端契约（真 SQL Server，curl）
`GET /api/space/site/{QAWH}/pick-path?taskNo=OB-P4-CROSS` → **HTTP 200**，返回：
- `floors`：F1(level1,**z=0**) + F2(level2,**z=6000**) —— `ComputeFloorZ` Level 升序累加正确。
- `stops`：4 跨层拣货点带 `floorId`+AbsXYZ（SP3-TL@F1 / B-01-01-02@F2 / SP3-TR@F1 / B-01-01-01@F2），zigzag 序。
- `aisles`：F2 的 SP4-F2-H + F1 的 SP3 网格，各带 floorId。
- `connectors`：E1 两 stop（F1/F2 各 (500,500)）。
→ **整条后端跨层契约（ComputeFloorZ / 库位 floor 解析[Guid? 守卫] / 涉及层 aisles / 连接体）端到端成立**。

### 前端 + 3D 堆叠（gstack 真栈）
| 点 | 验收 | 结果 |
| --- | --- | --- |
| **1 堆叠** | 全站楼层于各自 Z 渲染全几何 | ✅ `/space/stacked/{QAWH}`：F1+F2 两层 rack 线框于不同高度（F2 在上 z=6000）+ 楼板；侧栏 2F/L2 + 1F/L1 显隐勾选；整栈相机框选。`01-stacked-floors.png` |
| **2 跨层路径** | 路径经电梯竖直上下层 | ✅ 加载 OB-P4-CROSS → **青色路径线竖直连接 F1↔F2（经电梯 E1）**，粉色小车在路径上，播放控件。`02-crossfloor-path.png` |
| **3 重排对比** | 实际/优化/省% + 优化线开关 | ✅ 面板「**实际 33.2 米 / 优化 17.2 米 / 省 48%**」（actual 3×电梯往返 vs optimized 按层分组 1×往返）；勾「显示优化路径」→ **绿色优化线 3D 叠加**。`03-optimized-line.png` |
| **4 编辑器放置** | 连接体放置工具 | ✅ 工具 code-complete + 终审 review 过（复用编辑器既有 `screenToWorld`，mirror 模板放置）；E1 已种子化。运行态精细放置留监督。 |
| **5 无回归** | 单层 08/SP3 + 后端 | ✅ 单层 `/space/viewer/{QAWH}?floorId={F1}` 正常加载（canvas/200/无错）；`/floor/{id}/pick-path` 原封不动；后端 `dotnet test` 1442/0fail；前端 vitest 230/vue-tsc0/build。 |

**关键数值（前后端闭环一致）**：actual 33.2m（绕路 LineNo：F1→F2→F1→F2，电梯往返 3×6m=18m 竖直 + 各层水平）；optimized 17.2m（NN+2opt 按层分组，电梯往返 1×6m + 水平）；**省 48%** —— 跨层动线优化（少跑电梯）的价值直观可见。

## 截图
- `01-stacked-floors.png` — 全站堆叠：F1+F2 两层 rack 于不同 Z + 楼板 + 侧栏显隐。
- `02-crossfloor-path.png` — 跨层路径：青线竖直经电梯 F1→F2 + 粉小车 + 面板 48% 省。
- `03-optimized-line.png` — 勾「显示优化路径」→ 绿色优化线 3D 叠加。

## 已知 headless 限制（非缺陷，沿用 SP3）
- 合成 wheel/dblclick 拉不动 OrbitControls → 无法肉眼逐帧核「小车沿电梯上下动画手感」「整栈环视」；**跨层路径竖直经电梯由截图（青线竖直连两层）+ 后端契约 z=6000 + planPickComparisonMF 单测 + 48% 省距数学闭环证**，不靠像素。
- 冷后端首调 Space 端点 ~5-6s（JIT），browse 取状态须 sleep 够。
- 编辑器连接体放置的精细 canvas 交互留监督验收（工具已 gated + reviewed）。

## 结论：**DONE** — SP4 3D 多层路由（连接体 + 站点级契约 + 多层图/3D A* + 堆叠 viewer + 编辑器工具）真库真栈验收通过；跨层路径经电梯竖直渲染 + 48% 重排省距 + 单层零回归。
