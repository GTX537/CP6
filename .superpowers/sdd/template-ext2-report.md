# 模板契约扩展二轮报告 — FilterField date·number·valueFormat + CpListPage paginated·reload + 批次1回填

日期：2026-07-04 ｜ 分支：feat/ui-migrate-wms ｜ 执行：Claude Fable 5

## 范围（计划 §模板缺口「WMS 迁移批次1 复盘」缺口 #9/#10/#11/#12/#13）

### 契约扩展（模板组件）

**CpFilterBar（`cp6.web/src/components/templates/CpFilterBar.vue`）**
- `FilterField.type` 增 `'date'`：单日 el-date-picker 透传（clearable），恢复独立起/止字段形态，单侧留空即开区间查询（#13）。
- `FilterField.valueFormat?: string`：透传 el-date-picker `value-format`，date 与 daterange 通用（#9）。**opt-in 无默认值**——若给 daterange 设默认 `YYYY-MM-DD` 会静默改变既有消费者拿到的返回类型（Date→string），故由页面显式声明。
- `FilterField.type` 增 `'number'` + `min?/max?/step?`：el-input-number 透传（#10）。model 值为 number；清空为 null/undefined。
- 头注同步更新（字段语义 + opt-in 理由都写进契约说明）。

**CpListPage（`cp6.web/src/components/templates/CpListPage.vue`）**
- `paginated?: boolean`（默认 true）：false 时 pager 不渲染、page 锁 1、fetch 收 `size=UNPAGED_SIZE(1000)`（常量在实现处注释文档化，头注亦标明）（#11）。
- `defineExpose({ reload })`——**仅暴露 reload**。`reload()` 直接复用内部 `load()`，保留当前 filters / page / statusKey（#12）。已知边界（头注记录在案）：删除当前页最后一行后 reload 仍停留原 page 显示空页，不做自动页码收拢——与原页 reload() 行为一致，本轮明确不加。

### 批次1页面回填（4 页，代偿全部拆除）

| 页面 | 变更 |
|---|---|
| `views/wms/InboundOrderListView.vue` | daterange 合并字段拆回两个独立 `date` 字段（arrivalFrom/arrivalTo，valueFormat:'YYYY-MM-DD'，标签沿用原页 予定入荷 From/To 词条）；**删除 ymd() 本地时区格式化与 tuple 拆分代偿**，fetch 直接收字符串。单侧开区间查询恢复。 |
| `views/wms/ExpiryView.vue` | 「N日以内」text→`number`(min1/max365，placeholder 30)；字段初值空、fetch 侧 `?? 30` 缺省语义保留（`f.days == null` 兼容 el-input-number 清空的 null）。`:paginated="false"` 恢复单表滚动+跨全量勾选一括廃棄（fetch 包装 slice 在 page=1/size=1000 下等于全量透传，概览指标本就按全量算，不受影响）。廃棄成功 → `listRef.reload()`。reloadKey 已删。 |
| `views/wms/CrossDockView.vue` | reloadKey+`:key` 重挂载 → `ref="listRef"` + `reloadList()`（新建 @saved / 実行 / 取消 三处）。筛选/页码在刷新后保留。 |
| `views/wms/WarehouseListView.vue` | 同上（新建/編集 @saved / 削除）。 |

### 测试（TDD：先红后绿）

- 新增 10 条：CpFilterBar 6（date 渲染非 range / valueFormat 输入得字符串 / 无 valueFormat 不透传 / daterange 透传 valueFormat / number min·max·step 透传 / number v-model 数值型）+ CpListPage 4（paginated=false 隐 pager+size1000+page1 / 缺省 true 照常 / reload() 保 filters·page·statusKey / exposed 仅 reload）。
- 先运行确认 9 条失败（红）→ 实现 → 全绿。
- 测试实现备注：① el-input-number 在 prop 不回写时会自行重同步 emit null，number 变更测试改用真实 v-model 宿主组件；② VTU 的 `vm` 代理 setupState，「仅暴露 reload」断言改查 `vm.$.exposed` 的键集。
- 全量：`npm run test` **304 passed**（294+10）；`npm run type-check` clean。

### 真栈验收（gstack browse，admin 会话，5173→Docker api 9991）

- **InboundOrderList**：单侧 from-only 查询 `GET …?pageSize=500&arrivalFrom=2026-06-01`（含 2026-06-21 行）/ `arrivalFrom=2026-07-01`（0 行空态）；to-only `arrivalTo=2026-06-30` 同样生效。日期以字符串直达后端，无 Date 序列化。截图 `shots/ext2-inbound-from-only.png`。
- **Expiry**：number spinner 渲染（增减按钮在）、999 输入 UI 即钳到 365、days=30 缺省与 days=365 查询均正确发出；pager 不渲染。截图 `shots/ext2-expiry-number-nopager.png`。⚠️ QA 库无带赏味期限的库存（365 日也 0 行），跨全量勾选与廃棄后 reload 无法真栈演示——契约本身由单测覆盖（size=1000 + reload 保上下文）。
- **CrossDock**：新建保存 → `POST /wms/cross-dock 200` → 自动 `GET …?productCd=EXT2`（**筛选保留在刷新请求里**，旧方案重挂载会丢）；建成行可查（`shots/ext2-crossdock-created.png`）。
- **Warehouse**：筛选 warehouseCd=DW01（4 行→1 行）→ 編集保存 → `PUT 200` → 自动 `GET …?warehouseCd=DW01`，**筛选值与窄化结果均存活**。截图 `shots/ext2-warehouse-filter-survives.png`。
- Console 无 error（仅既有 intlify flatten / router next() 弃用 warning，全站固有）。

### 发现的既有缺陷（非本轮引入，未修）

**CrossDock 実行/取消 一直是坏的**：后端序列化为 `xDockNo`（大写 D），前端类型/页面全用 `xdockNo` → 行内 `row.xdockNo` 为 undefined，`POST /wms/cross-dock/undefined/execute → 400`；一覧的单号列也因此空白。迁移前原页（git 299dbc2~1 第 127 行 `row.xdockNo!`）就是同样写法——批次1迁移忠实保留了这个上游 bug。修复涉及 API 类型/字段名契约（`types/wms/wms.ts` + 页面），超出本轮「契约扩展+代偿拆除」范围，建议开 follow-up 票（前端对齐 `xDockNo` 或后端 JsonPropertyName）。

### 文档

- 计划文档 §模板缺口 批次1 复盘：#9/#10/#11/#12/#13 原地标 ✅ 已实现 + 一行落地说明。
- 遗留 QA 数据：cross-dock XD2026070001（計画状态，EXT2-PROD）留在 QA 库，无碍。

### 约束遵守

- 仅 token/组件契约；未动后端、路由、i18n 机制；页面改动仅限 4 个回填页；单 commit。
