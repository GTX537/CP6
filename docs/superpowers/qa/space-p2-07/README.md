# Space P2-07 实时库存叠加 — gstack QA 记录

> 待用户监督跑 gstack（需真 SQL Server 隔离库 CP6DB_SpaceQA + 起主机 + 浏览器）

## 前置准备

1. 拷贝 `CP6DB_SpaceQA`（或重建迁移）到隔离 SQL Server 实例
2. 后端 `appsettings.Local.json` 指向 `CP6DB_SpaceQA`
3. 前端 Vite dev proxy 指向本地后端
4. 灌入演示种子：执行 `docs/superpowers/qa/space-p2-07/seed-stock.sql`（对 `CP6DB_SpaceQA`，TenantId=`00000000-0000-0000-0000-0000000000A1`）
5. 注：库位编码（`A-01-01-01` 等）须与已发布 Space 场景中实际 Placed 库位对齐；若不符请先在 3D 场景中 Publish 对应编码

## QA 场景清单（共 6 项）

- [ ] **场景 1 — 状态模式着色（5 态）**
  进入 `/space/viewer/{siteId}?floorId={floorId}`，默认状态模式：
  - A-01-01-01 蓝（有货，qty=5，容量 10）
  - A-01-01-02 红（满，qty=50，容量 50）
  - A-01-01-03 灰（锁定，IsBlocked=1）
  - A-01-01-04 黄（在拣，出库单 OB-QA-001 Picking+明细 AllocatedQty>ShippedQty）
  - 其余未播种库位 绿（空）

- [ ] **场景 2 — 利用率模式 + 关闭模式**
  - 点图例"利用率"按钮 → 冷暖渐变着色（低利用率偏蓝，高偏红）
  - 点"关闭" → 回灰（结构层底色），3D 可正常浏览

- [ ] **场景 3 — 刷新库存**
  - 点图例"刷新库存"按钮 → 图例数据时间戳更新（I-SPACE-703 语义）
  - 着色重新渲染

- [ ] **场景 4 — InfoCard 库存行**
  - 点击库位 A-01-01-01 → InfoCard 弹出，库存块显示：
    - 库位状态：有货
    - 库存量：5 / 10（50%）
    - 主物料：CARTON-A4
  - 点击 A-01-01-04 → 状态：在拣，库存量：8

- [ ] **场景 5 — 按物料定位**
  - SearchBox 切换到"按物料"模式，输入 `CARTON-A4` 回车
  - 复用 06 Locator：飞行到第一命中库位并高亮（A-01-01-01 或 04）
  - 若多命中：弹出提示"找到 N 个库位"

- [ ] **场景 6 — 降级（WMS 数据源断开）**
  - 停止后端 WMS 查询能力（或断库连接）
  - 触发刷新 → 提示"库存数据获取失败，显示上次快照"（W-SPACE-701）
  - 3D 结构场景照常浏览，无 crash

## 截图/日志记录

（跑完后在此补充截图路径或结果说明）

## 自动化回归结果（实施者跑）

| 门控 | 结果 |
|------|------|
| `dotnet test CP6.Tests` | 待填 |
| `vue-tsc --noEmit` | 待填 |
| `vitest run` | 待填 |
| `npm run build` | 待填 |
