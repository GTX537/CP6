# CpListPage 契约扩展报告（Milestone C gate task）

分支 `feat/ui-template-ext`（自 main 切出）。规格：`docs/superpowers/plans/2026-07-04-ui-restyle.md` §模板缺口（Task 11 试点复盘）缺口 #1/#2/#3/#7/#8。

## What landed

### #1 total 外露
- `CpListPage` 新增 emit `total-change(n: number)`：在 `load()` 内 `total.value = res.total` 之后 emit，位于 `id !== seq` 早退之后 → 天然复用既有乱序守卫，**过期响应不 emit**。
- 试点页：`const total = ref<number>()` + `@total-change="total = $event"` → `CpPageShell :count="total"`。首载前 count=undefined，PageShell 按契约隐藏 pill；载后显示。

### #2 列级/表级字段
- `ListColumn` 增 `minWidth?: number`（→`min-width`）、`overflowTooltip?: boolean`（→`show-overflow-tooltip`）、`fixed?: 'left'|'right'`（→`fixed`）。
- `CpListPage` 增 prop `highlightCurrentRow?: boolean`，**默认 true**（withDefaults），透传 el-table——所有迁移页默认恢复原「点击行高亮」行为。
- 试点页回补：customerName `minWidth:160 + overflowTooltip:true`；`_action` `fixed:'right'`。

### #3 map 列映射
- `ListColumn` 增 `map?: (val: unknown, row: unknown) => { label: string; tone?: Tone }`。
- 契约：`map.label` 替换单元格文案（任意 kind 生效，经 `display()` 统一走 mono/num/text 呈现）；`kind:'tag'` 时渲染 `<CpTag :tone="map(...).tone">{{ label }}</CpTag>`（CpTag 显式 tone 优先于 status 查表；tone 缺省 → CpTag 兜底 muted）。`col-<prop>` 插槽优先级仍最高（插槽 > map > kind 默认）。
- 试点页：区分/ステータス 改 `kind:'tag'` + map（沿用 typeMap/statusMap i18n 词条与原 tone 逻辑）；優先度改纯 map（见下方 deviations）；三个 `col-<prop>` 插槽 + 页内 CpTag import 已删，新增 `codeLabel()` 小助手保持「未命中回退原值、null→空」的原插值语义。

### #7 共享 Tone 类型
- `CpTag.vue`（two-script-block 惯用法，与 FilterField 同款）导出 `export type Tone = 'ok'|'warn'|'danger'|'info'|'muted'`；`STATUS_TONE: Record<string, Tone>`；props `tone?: Tone`。
- 消费点全部收口：CpStatusStrip `StatusItem.tone?: Tone` + `TONE_VAR: Record<Tone,string>`；CpListPage `StatusTab.tone?: Tone` + `ListColumn.map` 签名；试点页 `statusTone(): Tone`。库公开类型无残留 string tone。

### #8 kind:'date' 落地
- 实现为 `String(val).slice(0, 10)`，null/undefined → 空串——与试点页原 `row.plannedDate?.slice(0,10)` 插槽约定一致。头注「date→暂原样」已移除。试点页 plannedDate 改 `kind:'date'`，插槽删除。

## Deviation（重要，需知悉）

控制方指示「三个码值列（含優先度）改 kind:'tag'+map」，但原页（迁移前 39d6570 与迁移后均可查证）優先度是**纯文本**（无 el-tag、无 tone）；同一指示又要求「pixel/logic-identical」。两者冲突，按后者（显式 must）裁决：把 map 契约泛化为「任意 kind 均可用 label 替换文案，仅 kind:'tag' 包 CpTag」，優先度用「纯 map、不设 kind:'tag'」——插槽照样消灭、像素零变化。该泛化同时让 130+ 页中大量「码值→纯文本」列（缺口#3 的真实形态之一）免插槽。若产品侧希望優先度也做成 pill，只需给该列补 `kind:'tag'` + tone 一行。

## TDD 证据

- 先写 8 个新测试（`CpListPage.spec.ts` 新 describe「CpListPage 契约扩展（Milestone C）」），实现前运行：**7 failed / 16 passed**（红；"slot 优先于 map" 因插槽本就优先而先绿，属既有行为的守护断言）。
- 实现后：该文件 23/23 绿。
- 新测试清单：total-change 携带正确值；total-change 过期响应不 emit（复用 deferred-promise 乱序模式）；minWidth/overflowTooltip/fixed 到达 el-table-column（props 断言）；highlightCurrentRow 默认 true + false 透传；map+kind:'tag' 渲染 label+tone；map 无 tag 仅换文案不渲染 CpTag；slot 胜 map；date slice(0,10) + null→空。
- Tone 类型编译期约束由 `npm run type-check`（vue-tsc --build，覆盖全部组件与试点页）保障。

## 验证结果

- `npm run test`：**46 files / 294 passed**（原 286 + 新 8），输出干净。
- `npm run type-check`：clean（0 error）。
- 真栈验收（backend Docker :9991 + dev server :5173，login admin/123456，出庫指示一覧）：
  - 计数 pill 显示 **37**（`.cp-page-head .cnt` 文本 "37"）。
  - 码值列 DOM 抽查 12 个 `.cp-tag`：出荷→t-warn、材料出庫→t-info、ピッキング→t-info、完了→t-ok、下書き→t-muted——与迁移前 tone 逻辑逐一一致；優先度保持纯文本（通常/急ぎ）。
  - 計画日列 2026-07-04 / 2026-06-28 等，slice 正常。
  - `td.el-table-fixed-column--right` 存在（操作列钉右）；点行后 `tr.current-row` 存在（高亮默认恢复）；客先名 cell 带 el-tooltip（溢出悬浮）。
  - console：clear 后 reload，**0 error**；仅存量 intlify flatten / vue-router 弃用 warning（main 上即有，与本次无关）。
  - 截图：`.superpowers/sdd/shots/template-ext-outbound-list.png`。

## 触及文件

- `cp6.web/src/components/base/CpTag.vue` — Tone 导出 + STATUS_TONE 强类型 + 头注
- `cp6.web/src/components/templates/CpStatusStrip.vue` — 消费 Tone + 头注
- `cp6.web/src/components/templates/CpListPage.vue` — 五项契约扩展 + 头注（含使用示例更新）
- `cp6.web/src/views/wms/OutboundOrderListView.vue` — 试点页回填（count 接线/列声明式化/删 3 插槽 + CpTag import/头注）
- `cp6.web/src/components/templates/__tests__/CpListPage.spec.ts` — +8 契约测试
- `docs/superpowers/plans/2026-07-04-ui-restyle.md` — §模板缺口 #1/#2/#3/#7/#8 标 ✅
- `.superpowers/sdd/shots/template-ext-outbound-list.png` — 真栈截图

## Self-review

- seq 守卫：emit 放在守卫早退之后、与 rows/total 赋值同一临界区，无「emit 了却没渲染」或反之的窗口。
- `display()` 对 map 列每 cell 调 map 一次、tag 列 tone 再调一次（`mapTone`）——map 要求纯函数，试点页 map 仅查 computed 字典，成本可忽略；如未来出现重 map，可在列层 memo，不改契约。
- `withDefaults` 仅给 highlightCurrentRow 设默认，其余 optional 语义不变；既有 16 测试全绿证明无回归。
- `overflowTooltip: undefined` 透传 `show-overflow-tooltip=undefined` → el-table-column 走自身默认（false），与扩展前行为一致。
- 优先度 deviation 已如上记录；其余逐条按控制方决策落地。
