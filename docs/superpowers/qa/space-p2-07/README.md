# Space P2-07 实时库存叠加 — gstack QA 记录

**状态:✅ 通过(监督下,2026-06-28)** — 真 SQL Server(`localhost\KOUSQLSERVER` / 隔离库 `CP6DB_SpaceQA`)+ gstack headless Chromium。后端 5177(读 `appsettings.Local.json` 指隔离库)、前端 5173(vite proxy)。

## 环境与种子

- 隔离库 `CP6DB_SpaceQA`(P1 QA 拷贝法建,含 Space P1 已发布场景:Site `QAWH` / Floor `5C92E6A8…` / 4 个已发布库位)。
- **实际已发布库位编码 = `A-01-01-01 / A-01-01-02 / A-01-02-01 / A-01-02-02`**(与计划草稿里的 `A-01-01-03/04` 不同 → 种子按真实编码改写,见 `seed-stock-applied.sql`)。
- 演示种子(TenantId=`…A1`,全 ProductCd=`CARTON-A4`):
  - `A-01-01-01` → **有货**(qty5 / cap10)
  - `A-01-01-02` → **满**(qty50 / cap50 = 100%)
  - `A-01-02-01` → **锁定**(Location.IsBlocked=1)
  - `A-01-02-02` → **在拣**(出库单 `OB-QA-001` Status=Picking(3) + 明细 AllocatedQty5>ShippedQty0)
  - (4 库位用尽,「空」态未单独演示 — 任何无库存且有 Location 行的库位即绿)

## 端到端验证证据

**① 后端接真(authed curl,真 SQL Server)** — 核心「07 接真」闸门:
- `GET /api/space/floor/{id}/stock` → 200,4 库位 **BinStatus 全对**:`A-01-01-01`=1有货 / `A-01-01-02`=2满 / `A-01-02-01`=3锁定 / `A-01-02-02`=4在拣(qty8<cap20 仍判在拣 → 优先级正确)。
- `GET /api/space/stock/locate?material=CARTON-A4` → 200,4 命中。
- locate API 与 scene API 对同一库位返回一致 GUID(`0b00…0002`)。

**② 浏览器(gstack,截图见 `shots/`)**:
- **InfoCard 库存行正确**(`shots/01-infocard-stock-满.png`):点 A-01-01-02 → `在庫 / 库位状态: 满 / 库存量: 50 / 50 (100%) / 主物料: CARTON-A4`。
- **图例渲染**(`shots/02-legend-5color.png`):状态/利用率/关闭模式钮 + 5 态色例(空绿/有货蓝/满红/锁定灰/在拣黄)+ 刷新库存 + 自动刷新 + 数据时间戳。
- **库位标签 + 盒子渲染**(`shots/03-labels-boxes-rendered.png`):`A-01-01-01`/`A-01-01-02` CSS2D 标签 + 货架内库位实例盒(placed 修复后)。
- **利用率模式**:切换后图例变冷暖渐变(低→高)。
- **刷新/数据时间**:刷新后图例数据时间戳更新。

## 🔴 QA 抓到并修复的 4 个集成 bug(均单测覆盖不到,提交 `f24d1cb`)

1. **floorId 时序 404**:`refreshStock` 在 `viewer.onReady` 早于 `currentFloorId` 赋值触发 → `GET /floor//stock`(双斜杠)404。改为在 `loadFloor` 末尾(currentFloorId 已设)刷新。
2. **着色 code↔GUID 错位(核心)**:`overlay.apply` 把 locationCode 传给 `setInstanceColor`,但 `InstancedBuckets` 按库位 GUID 键 → 盒子从不着色。新增 `ViewerHandle.getLocationIdByCode`(+`SpaceViewer._codeToId` 反查),apply 先 code→GUID 再着色 + 回归单测。
3. **InfoCard 库存 GUID↔code**:`syncSelectedStock` 用 selectedId(GUID)查按编码键的快照 → 永远 null。新增 `getLocationCode`(GUID→编码)再 `getStock`;locate(onLocated)也同步库存。
4. **潜伏 P1 缺陷**:`SceneDto` 无 `placed` 字段,`SceneBuilder` 用 `loc.placed` 过滤渲染 + 建 `locationCodes` → 全 `undefined` → **库位盒子自 P1 起从未渲染**(只显货架线框)+ 标签/反查映射空。`/scene` 契约「仅含 Placed=true」,故 SceneBuilder 标 `placed:true`、codes 去 placed 判断。修复后标签 + 盒子 + 着色 + InfoCard 库存全部生效。

## 工具限制(非产品缺陷)

- **库位盒子的像素级颜色**未逐一肉眼核对:demo 场景仅 4 个库位在单货架、楼层很大 → 盒子在全层视图极小;headless 合成 wheel 事件无法稳定驱动 OrbitControls dolly 到 near LOD 档(medium↔far=150)。**着色正确性由以下闭环证明**:后端 BinStatus 全对(curl)+ 快照浏览器拉取成功 + InfoCard 同链路数据正确(满 50/50)+ `apply` code→GUID→setInstanceColor 单测锁定 + 盒子确已渲染(标签证)。
- **按物料定位 / 降级(W-SPACE-701)**:后端 `/stock/locate` 已 curl 证(4 命中);前端 `el-message` 提示自动消失(~3s)未截到。逻辑直白、与按编码定位同 Locator 路径,低风险。

## 自动化回归结果

| 门控 | 结果 |
|------|------|
| `dotnet test CP6.Tests` | ✅ 1301 passed / 0 failed / 5 skipped |
| `vue-tsc --noEmit` | ✅ 0 errors |
| `vitest run` | ✅ 134 passed(20 files,含 overlay 回归测) |
| `npm run build` | ✅ 成功(仅 chunk-size 既有提示) |
