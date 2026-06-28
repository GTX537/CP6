# Space P3 · 08 高级可视化 — gstack 真浏览器 QA 记录

> 日期 2026-06-28。承 Plan `docs/superpowers/plans/2026-06-28-space-p2-08-advanced-viz.md`（T1~T12）。
> 隔离环境：worktree `D:\CP6-space-backend` @ `feat/space-p1-backend` + 隔离库 `CP6DB_SpaceQA`（localhost\KOUSQLSERVER，Windows 认证）。

## 环境

| 部件 | 详情 |
|---|---|
| 后端 | `dotnet run --project CP6.WebApi --urls http://localhost:5177`，读 `appsettings.Local.json` → `CP6DB_SpaceQA` |
| 前端 | vite `npm run dev` :5173，proxy `/api` → 5177 |
| 浏览器 | gstack browse（headless Chromium） |
| 登录 | admin / 123456（`POST /api/auth/login` {userName,password}，dev Csrf 关） |
| 真实数据 | Site QAWH `F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE` / Floor `5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F`；已发布编码 `A-01-01-01/02`、`A-01-02-01/02` |
| 种子 | `seed.sql`：出库单 `OB-PICK-DEMO`（4 序明细）+ 11 条 `TXN-DEMO-*` 流水（频次 5/3/1/2）+ 数据驱动中心线（floor 无 aisle → 插 `AISLE-DEMO` `[[-500,500],[2500,500]]`） |

## 后端闭环（curl，真 SQL Server）

| 端点 | 结果 |
|---|---|
| `GET /api/space/floor/{f}/pick-path?taskNo=OB-PICK-DEMO` | **200** — 4 有序拣货点，AbsXYZ 解析正确（A-01-01-01→(500,500,500)…），打包 aisle 中心线 `[[-500,500],[2500,500]]` |
| `GET /api/space/floor/{f}/workload?from=今日&to=明日` | **200** — 4 库位 opCount 5/3/1/2（= 种子频次） |
| `GET /api/space/floor/{f}/devices` | **200** — `[]`（v1 桩） |

后端 AbsXYZ join + aisle 打包 + 流水时间窗分组计次在真 SQL Server 全部跑通。

## 浏览器端到端（gstack）

1. 登录 → `/space/viewer/{site}?floorId={floor}` → 3D 场景渲染（zone 多边形 + rack/库位盒），`.advanced-panel` 挂载。
2. **拣货路径**：输入 `OB-PICK-DEMO` → 加载 → `pick-path` 200 → 面板显「**拣货路径：4 点，总距 3.0 米**」（I-SPACE-801）→ **场景出现青色路径线 + 粉色小车**（沿中心线）→ 播放/暂停/步进/重播/调速控件出现并可用。截图 `screenshots/08-qa-2-pickpath.png`。
3. **作业热图**：勾选「开启」→ `workload` 200（频次 5/3/1/2）→ **07 StockLegend 自动切到「クローズ(off)」**（与 07 着色互斥生效）→ 4 库位盒按频次冷暖着色。截图 `screenshots/08-qa-3-workload.png`。
4. **设备示意**：勾选「显示设备」→ `devices` 200 `[]`（桩）→ DeviceLayer 无图元（v1 占位预期）→ I-SPACE-803 提示（toast）。
5. 切层/卸载：路径/小车/设备清理，`workloadOn` 复位（不残留勾选亮但灰底的态）。

## 🔴🔧 QA 抓到并修复的 bug

**作业热图时间窗空窗（`51fa49a`）**：`AdvancedPanel` 默认 `from`/`to` 同为今日，后端时间窗半开 `[from,to)` → `[今日00:00, 今日00:00)` 为空窗 → 热图开了却 0 着色（curl 用 `to=明日` 才有数据，掩盖了这个 UI 默认陷阱）。**修法**：`FloorViewer` 加 `exclusiveTo(d)=d+1天`，把选择器「含当天」语义的 `to` 转成半开上界再查；toast 仍显用户所选（含当天）日期。修后同日 from/to 正常返 5/3/1/2 并着色。

## 工具/环境坑（沿用 07，本轮复现）

- 冷后端首调 Space 端点 ~5–6s（JIT），`browse` 取状态要 `sleep` 足够或重取，否则误判「未加载」。
- 合成 wheel 拉不到 near LOD + demo 盒子在大楼层极小 → 单个盒像素热色难肉眼核；着色正确性由 **后端 API + WorkloadHeatmap.apply 单测 + 盒已渲染（P1 placed 修复后）** 闭环证。
- toast TTL 3s < 冷后端 5s 调用延迟 → I-SPACE-803/802 toast 难定格截图（逻辑由 T10 代码评审确认）。
- HMR 热更新瞬间（`FloorViewer.vue?t=...`）切热图偶发一次 404（viewer 实例 mid-reload）；**整页刷新后干净复现无 404 无报错**，非生产缺陷。
- 数据空间铁律：path/cart/device mesh parent 到 `getSceneRoot()`（自带 scale 0.001 + rotation），坐标用 mm，不调 dataToWorld。

## 结论

Space P3·08 高级可视化（拣货路径动画 + 作业热图 + 设备占位）端到端在真实基础设施成立：后端 3 端点真库 200、前端三能力渲染/着色/占位均工作、与 07 互斥正确、单测 154 + 后端 1309 全绿。抓修 1 个集成 bug（热图空窗）。
