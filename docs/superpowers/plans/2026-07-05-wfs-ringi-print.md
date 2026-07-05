# WFS 稟議書打印视图 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐 Task 执行。步骤用复选框（`- [ ]`）跟踪。**每个 Task 执行前必读对应 spec 章节**（`docs/superpowers/specs/2026-07-05-wfs-ringi-print-design.md`，唯一权威，含评审修订，不许改设计）。本计划所有产品代码 / 测试代码在任务内逐条给全，测试体为**可编译完整代码**，禁 `{ /* */ }` 骨架。

**Goal:** 给 WFS 电子表单信箱加「稟議書打印」——FormDetail 工具栏「打印」按钮 → 新标签打开独立路由页 `/oa/inbox/print/:instanceId`（`RingiPrintView.vue`），渲染日企稟議書四段排版（表头 / 表单字段表格 / 传签履历表格 / 印章式署名欄），`@media print` A4 纵向排版，用户浏览器打印或另存 PDF。只打**已发生**（Forecast 预计段不打）；会签/并行同关卡多审批人在署名枠内纵列多人各标判定；长流程署名枠按 flex-wrap 折行且每枠 break-inside:avoid。五语 i18n + gstack QA。**零新依赖、零后端改动（i18n seed 除外）、零新端点。**

**Architecture:** RingiPrintView = **独立 standalone 路由页**（仿 Space 编辑器 `/space/editor/:floorId`：`meta.standalone`，不走 LayoutView 壳），组件挂载后复用既有 `inboxApi.detail(instanceId)`（`InboxDetail` 载荷）取全部数据，**零新端点**。所有**可测的渲染逻辑下沉为纯函数** `ringiPrintModel.ts`（`buildHeader` / `buildFieldRows` / `buildHistoryRows` / `buildStampFrames`），`.vue` 为纯函数产出的薄渲染层——沿袭本仓既有模式（`FlowTimeline.vue` 之于 `inboxModel.ts`；本仓 **无 `@vue/test-utils`、全部 vitest 皆为纯函数测试**，故不引入组件挂载，测试落在纯函数）。判定词 100% 复用既有 `oa.formto.*` / `oa.timeline.*` 键（含 `oa.formto.skipped` = スキップ，与 `wfs-version-ops` 计划**同词条键**）；本计划新增 i18n 仅打印视图自有 chrome（表头标签 / 署名欄 / 打印按钮，~16 键）。`@media print` = A4 纵向 + 隐藏应用壳 + 履历表 `thead` 跨页重复（`display:table-header-group`）+ 署名枠 `break-inside:avoid`；屏幕态即打印预览（同排版加纸张阴影）。

**Tech Stack:** Vue3 + `<script setup lang="ts">` + Element Plus + Design System v1.0 tokens（`cp6.web/src/styles/tokens.css`，`--cp-*`）/ vitest（纯函数，`import { describe, it, expect } from 'vitest'`，仿 `inboxModel.test.ts`）/ vue-i18n 运行时键 / vue-router standalone route。后端仅新增一支 i18n seed 类（`CP6.WebApi/Seed`）+ Program.cs concat 一行。

---

## Global Constraints（每个 Task 隐含遵守）

- **零新依赖**：不装 `@vue/test-utils`、不装 PDF 库、不装打印库。浏览器原生打印 + print stylesheet。
- **零后端改动（i18n seed 除外）**：不碰 `InboxService` / `InboxModels` / `InboxController` / 任何 Wf 服务与实体。唯一后端产物 = `Seed/I18nOaRingiPrintScreenSeed.cs` + `Program.cs` 一行 concat。DoD 跑 `dotnet ef migrations has-pending-model-changes ...` 必须 clean（本计划零实体改动，天然 clean）。
- **零新端点**：打印视图数据 100% 复用 `inboxApi.detail(instanceId)` → `InboxDetail`。不加 controller action。
- **零跨模块污染**：只碰 `cp6.web/src/views/oa/inbox/**`（`RingiPrintView.vue` + `ringiPrintModel.ts` + `ringiPrintModel.test.ts` + `FormDetail.vue` 附加式插点）、`cp6.web/src/router/index.ts`（staticRoutes 加一条）、`CP6.WebApi/Seed/`（新 seed）、`CP6.WebApi/Program.cs`（concat 一行）、`docs/superpowers/qa/wfs-ringi-print/**`。不碰 Space/WMS/ERP/FIN/MES 任何文件。每 Task `git show --stat` 复核。
- **五语 i18n**：全部新 UI 文案走 `t('...')` 运行时键；判定词 / 状态词 / 关卡词**复用既有 `oa.formto.*` / `oa.inst.*` / `oa.timeline.*`**（见 `inboxModel.ts`），**不重复 seed**；新键仅打印 chrome，在 P-B-T1 一次性 seed（ZhCN/ZhTW/En/Ja/Ko 五列，日文按稟議書惯例用语）。
- **零硬编码色**：新增 CSS 一律 Design System v1.0 token（`--cp-*`）。黑白打印友好：语义**不依赖底色**（判定用文字 + 边框，非色块）。
- **只打已发生**：打印视图只消费 `detail.timeline`（持久 FormTo 行 = 已发生），**永不消费 `detail.forecast`**——Forecast 不打是结构性保证（纯函数只接 timeline）。
- **测试基线**：前端 `npm run test` 现 320 通过 → +N 全绿，零回归；`npm run type-check` 通过；`npm run build` 通过。后端 `dotnet test` 现 1509 通过（5 skip）→ 零回归（本计划仅加 seed，不加测试到后端）。
- **提交纪律**：TDD（纯函数先失败测试 → 最小实现 → 绿 → commit）；提交信息 `feat(wfs-print): <任务号> <中文描述>`；**只本地 commit 不 push**。

---

## 侦察结论（2026-07-05 实读，各任务代码以此为准）

### R1 数据源形态（FormDetail 复用之，零新端点）

- FormDetail.vue（`views/oa/inbox/`）是**组件**（`defineProps<{ instanceId: string }>`），由 InboxView 抽屉挂载；自身**不渲染实例表头**，只左表单（`DynamicForm` + `readonlyMask`）右时间线（`FlowTimeline`）。数据入口 = `inboxApi.detail(props.instanceId)` → `InboxDetail`。
- `InboxDetail`（`types/oa/inbox.ts:110-120`，后端 `InboxModels.cs:37` record）字段：`instance:any`（= 后端 `Wf_FlowInstance` 整体序列化）、`flowName?`、`formKey?`、`formSchemaJson?`、`currentDataJson`、`timeline:TimelineRow[]`、`snapshots`、`forecast:ForecastStep[]`、`cc:CcRow[]`。
- **`detail.instance` 承载表头四要素（关键结论，均无需后端改动）**：`Wf_FlowInstance : BaseTenantEntity : BaseEntity` →
  - **起案者** = `instance.creator`（`BaseEntity.Creator`，string 创建人，实例由起案者创建时 SaveChanges 盖章）。
  - **起案日** = `instance.createDate`（`BaseEntity.CreateDate`，取 `.slice(0,10)` 作日期）。
  - **状态** = `instance.status`（int：0 进行中 / 1 通过 / 2 驳回 / 3 撤回 / 4 挂起）→ 经既有 `instanceStatusText(status)`（`inboxModel.ts:14`）映射 i18n 键（`oa.inst.running/approved/rejected/withdrawn/suspended/draft`）。
  - **单号** = `instance.bizId`（业务单号，存在时）否则 `instance.id`（实例 GUID）。
  - **FlowCode（人面编号）** = `instance.flowKey`；**稟議書标题** = `detail.flowName`（`def.FlowName`）。
- **表单字段**：`detail.formSchemaJson`（FormSchema，`{fields:[{name,label,type,...}]}`）+ `detail.currentDataJson`（字段值快照，= `inst.VarsJson`）。label 映射 = FormDetail 既有 `safeParseObject` 解析口径。
- **传签履历** = `detail.timeline`（`TimelineRow[]`，`types:69-86`）：`nodeId/nodeName/expectedHandlerName/actualHandlerName/onBehalfOfName/status/comment/sentAt/handledAt/tokenId/stageIndex/stageRound`。**全部为已持久（已发生）行**——`DetailAsync`（`InboxService.cs:238`）由 `Wf_FlowFormTo` 映射，`forecast` 是**独立字段**（`InboxService.cs:247` 仅 Running 时另算）。→ **只消费 timeline 即天然满足「Forecast 不打」（D2）**。

### R2 判定词 / 状态词已存在（复用，禁重复 seed）

- `inboxModel.ts:4` `formToStatusText(s)` → `['oa.formto.pending','oa.formto.approved','oa.formto.rejected','oa.formto.transferred','oa.formto.addsigned','oa.formto.skipped','oa.formto.voided','oa.timeline.sentBack'][s]`。
  - status 值域：0 待办 / 1 承認 / 2 却下 / 3 転送 / 4 加签 / 5 **スキップ** / 6 作废 / 7 退回。
  - **`oa.formto.skipped`（スキップ）已在 `I18nOaInboxScreenSeed.cs` seed**（grep 核实）——即 spec §2.2 要求的「跨 spec 同词条键」。`wfs-version-ops` 计划的强制推进产生 status=5，落纸面复用**同一键 `oa.formto.skipped`**。本计划与 version-ops **不各自 seed 判定词**，共用既有键。
- `instanceStatusText(s)` / `instanceStatusType(s)`（`inboxModel.ts:10-16`）复用于表头状态。

### R3 路由注册方式（standalone 独立页先例）

- `router/index.ts`：`staticRoutes[]`（:178-283）承载 standalone 独立窗口。**先例 = Space 编辑器**（:257-262）`{ path:'/space/editor/:floorId', name:'space-editor', component: ()=>import(...), meta:{ standalone:true, title:'Space 编辑器' } }`——带路由参数、`meta.standalone`、不走 LayoutView 壳、要求登录（不在无鉴权放行白名单内）。**本计划照抄此形态**加 `/oa/inbox/print/:instanceId`。
- 权限 = FormDetail 同口径：standalone 路由绕过菜单权限，但页面挂载即调 `inboxApi.detail(instanceId)`，**服务端 detail 端点是权限硬墙**（能看详情才返回数据；看不到 → 空/错误态）。零额外前端鉴权代码。

### R4 无组件挂载测试基建（决定测试形态）

- 全仓 vitest 皆纯函数（`inboxModel.test.ts` / `designerModel.test.ts` / inbox-ux 的 `notifyMatrixModel.test.ts`），**`@vue/test-utils` 未安装未使用**（grep 零命中）。→ 遵循既有惯例 + 零新依赖：**可测渲染逻辑全下沉 `ringiPrintModel.ts` 纯函数**，vitest 断言纯函数产出；`.vue` 为薄渲染层。纯 CSS 打印效果（A4 / 分页 / 折行视觉）**走 gstack QA 走查**（jsdom 测不了 `@media print`）。

### R5 FormDetail 工具栏插点 + 三期 X-C 边界

- FormDetail 现无工具栏；`<template v-else>`（:13）内首个元素是 `<el-row class="detail-body">`。**打印按钮插点** = 在 `<el-row>` 之前加一行 `<div class="detail-toolbar">`，**纯附加**，不动 `el-row`/`detail-left`/`detail-right`/`action-bar`。
- **三期 inbox-ux Wave X-C**（`plans/2026-07-05-wfs-inbox-ux.md`）改 FormDetail 为移动端响应式：动 `el-col :span` 模板分支 + 尾部 `@media (max-width:767px)` + `TransferDialog`/`SendBackDialog` 全屏化。**边界（本计划排 X-C 之后，spec §4 依赖）**：本计划对 FormDetail 的改动**仅限** ① 顶部 `.detail-toolbar` 附加插入 ② `openPrint()` 方法 ③ `import { useRouter }`。**不触碰** `el-row`/`detail-left/right`/`action-bar`/既有 `@media` 块（X-C 领地）。两计划落点物理不相交，合并顺序无关；若 X-C 先落，本插点加在其（可能移动端重排后的）body 之上，`.detail-toolbar` 自带尾部 `@media (max-width:767px)` 小节仅管自身按钮不干扰 X-C。

### 冲突登记（**不改 spec**，实现取向如下）

| # | 冲突 | 实现取向 |
|---|------|---------|
| C1 | spec §2.2 表头需「起案者」姓名，但 `InboxDetail` 未解析 `StarterId`→姓名 | **用 `instance.creator`**（BaseEntity 审计「创建人」字符串，实例由起案者创建时盖章）——零后端改动即得人名。注：`Creator` 存 SaveChanges 盖章的操作者显示串（多数租户为昵称/用户名）；QA 走查确认渲染为人名，若某租户显用户名属既有审计字段惯例、非打印视图缺陷。**不引入查询参数、不动后端 DTO。** |
| C2 | spec §2.2/D3 subFlow 行显示「子单号」，但 `TimelineRow` 无子实例引用字段（subFlow 特性未合并，其 `wfs-subflow` 计划才引入 `Wf_FlowInstance.ParentInstanceId/ParentTokenId`），且后端冻结 | **防御式渲染**：`buildHistoryRows` 产出可选 `childRef`（今恒 `undefined`，履历表「参照」列留空）；纯函数已预留字段，vitest 断言「无子单号优雅降级」。**Follow-up 票（本计划 backend-freeze 范围外）**：subFlow 落地后由后端把子实例号并入 `InboxDetail.timeline` 行，本视图自动点亮该列。 |
| C3 | spec §3 要求 vitest 覆盖「打印视图渲染」，但本仓无组件挂载基建 | 见 R4：渲染逻辑下沉纯函数测；CSS/分页/折行视觉走 QA 走查。task「会签单/长流程折行断言」按可测粒度落实 = 纯函数 `buildStampFrames` 的枠分组 / 枠内纵列多人 / 枠数量断言（折行本身是 CSS，走 QA）。 |
| C4 | spec §2.3「既有 print 样式先例」——实读全仓仅本 spec 命中 `@media print`，无组件先例 | print stylesheet 为绿地新建；`@page`/`table-header-group`/`break-inside` 为标准 CSS，零依赖。QA README 记录首个 print stylesheet。 |

---

## File Structure（创建/修改清单）

**前端 `cp6.web/src`**
- Create `views/oa/inbox/ringiPrintModel.ts` — 纯函数：`buildHeader` / `buildFieldRows` / `buildHistoryRows` / `buildStampFrames` + 类型。
- Create `views/oa/inbox/ringiPrintModel.test.ts` — vitest（三态 / Forecast 排除 / 字段 label 映射 / 会签枠纵列 / 长流程枠数量 / subFlow 降级）。
- Create `views/oa/inbox/RingiPrintView.vue` — 独立路由页：数据加载 + 四段排版子模板 + `@media print` 样式。
- Modify `router/index.ts` — `staticRoutes` 加 `/oa/inbox/print/:instanceId`（standalone）。
- Modify `views/oa/inbox/FormDetail.vue` — 附加 `.detail-toolbar` 打印按钮 + `openPrint()`（R5 边界内）。

**后端 `CP6.WebApi`**
- Create `Seed/I18nOaRingiPrintScreenSeed.cs` — 五语 ~16 键（仅打印 chrome）。
- Modify `Program.cs` — i18n seed concat 链加一行（仿 inbox-ux R6：`.Concat(I18nOaRingiPrintScreenSeed.Items)`，尾部既有 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重自动兜底）。

**QA**
- Create `docs/superpowers/qa/wfs-ringi-print/{README.md, seed.sql, qa_ringi_print.ps1}`。

---

## 共享契约（所有 Task 用这些**精确**名字）

```ts
// cp6.web/src/views/oa/inbox/ringiPrintModel.ts
import type { InboxDetail, TimelineRow } from '@/types/oa/inbox'

export interface RingiHeader {
  title: string        // 稟議書标题 = detail.flowName ?? flowKey
  bizNo: string        // 单号 = instance.bizId || instance.id
  flowCode: string     // = instance.flowKey
  starter: string      // 起案者 = instance.creator ?? ''
  startedAt: string    // 起案日 = instance.createDate.slice(0,10)
  statusKey: string    // i18n 键 = instanceStatusText(instance.status)
}
export interface FieldRow { label: string; value: string; longText: boolean }
export interface HistoryRow {
  nodeName: string
  handler: string      // actualHandlerName || expectedHandlerName
  onBehalfOf?: string  // 代签
  judgeKey: string     // i18n 键 = formToStatusText(status)
  handledAt: string    // (handledAt||sentAt) 前 19 位
  comment?: string     // 意見
  childRef?: string    // subFlow 子单号（C2：今 undefined）
}
export interface StampPerson { handler: string; judgeKey: string; handledAt: string }
export interface StampFrame { nodeName: string; people: StampPerson[] }  // 一关卡一枠；会签/并行→people 多人纵列

export function buildHeader(detail: InboxDetail): RingiHeader
export function buildFieldRows(schemaJson: string | undefined, dataJson: string | undefined): FieldRow[]
export function buildHistoryRows(timeline: TimelineRow[]): HistoryRow[]
export function buildStampFrames(timeline: TimelineRow[]): StampFrame[]
```

- 路由：`{ path: '/oa/inbox/print/:instanceId', name: 'oa-inbox-print', component: () => import('@/views/oa/inbox/RingiPrintView.vue'), meta: { standalone: true, title: '稟議書' } }`（加入 `staticRoutes`，紧邻既有 standalone 组）。
- 打印新键前缀 `oa.print.*`（P-B-T1 seed，见其键表）；**判定/状态/关卡词复用既有键**（`oa.formto.*` / `oa.inst.*` / `oa.timeline.sentBack`），本计划零重复 seed。
- 打开打印：`FormDetail` 内 `const router = useRouter()`；`openPrint()` = `window.open(router.resolve({ name: 'oa-inbox-print', params: { instanceId: props.instanceId } }).href, '_blank')`。

### 任务波次（spec §4）：**P-A-T1 → P-A-T2 → P-A-T3 → P-B-T1 → P-B-T2**（共 5 Task）

P-A（组件 + 路由 + 排版 + 打印样式）串行；P-B（i18n + QA + DoD）依赖 P-A 全绿。

---

## Wave P-A — 组件 + 路由 + 排版 + 打印样式

### Task P-A-T1: ringiPrintModel 纯函数 + vitest（渲染逻辑内核）

**Files:**
- Create: `cp6.web/src/views/oa/inbox/ringiPrintModel.ts`
- Test: `cp6.web/src/views/oa/inbox/ringiPrintModel.test.ts`

**Interfaces:**
- Consumes: `InboxDetail` / `TimelineRow`（`types/oa/inbox.ts`）、`formToStatusText` / `instanceStatusText`（`inboxModel.ts`）。
- Produces: 共享契约四函数——P-A-T2（`.vue`）全依赖。

- [ ] **Step 1: 写失败 vitest**

```ts
// cp6.web/src/views/oa/inbox/ringiPrintModel.test.ts
import { describe, it, expect } from 'vitest'
import {
  buildHeader,
  buildFieldRows,
  buildHistoryRows,
  buildStampFrames,
} from './ringiPrintModel'
import type { InboxDetail, TimelineRow } from '@/types/oa/inbox'

function tl(p: Partial<TimelineRow>): TimelineRow {
  return {
    stepSeq: 0, nodeId: 'n1', nodeName: '课长审批',
    expectedHandlerId: 'e1', expectedHandlerName: '山田',
    status: 1, sentAt: '2026-07-01T09:00:00', handledAt: '2026-07-01T10:00:00',
    ...p,
  } as TimelineRow
}

function detail(p: Partial<InboxDetail> & { instance?: any }): InboxDetail {
  return {
    instance: { id: 'I1', flowKey: 'ringi-buy', bizId: '', status: 1,
      creator: '铃木一郎', createDate: '2026-07-01T08:30:00', ...(p.instance ?? {}) },
    flowName: '購買稟議', formKey: 'f1',
    formSchemaJson: '{"fields":[]}', currentDataJson: '{}',
    timeline: [], snapshots: [], forecast: [], cc: [],
    ...p,
  } as InboxDetail
}

describe('buildHeader', () => {
  it('决裁済（status=1）四要素 + 状态键', () => {
    const h = buildHeader(detail({}))
    expect(h.title).toBe('購買稟議')
    expect(h.flowCode).toBe('ringi-buy')
    expect(h.starter).toBe('铃木一郎')
    expect(h.startedAt).toBe('2026-07-01')       // 仅日期
    expect(h.statusKey).toBe('oa.inst.approved')
  })

  it('单号：bizId 优先，缺则回落 instance.id', () => {
    expect(buildHeader(detail({ instance: { bizId: 'RINGI-0007' } })).bizNo).toBe('RINGI-0007')
    expect(buildHeader(detail({ instance: { bizId: '', id: 'I-XYZ' } })).bizNo).toBe('I-XYZ')
  })

  it('在途（status=0）/ 却下（status=2）状态键', () => {
    expect(buildHeader(detail({ instance: { status: 0 } })).statusKey).toBe('oa.inst.running')
    expect(buildHeader(detail({ instance: { status: 2 } })).statusKey).toBe('oa.inst.rejected')
  })

  it('title 缺 flowName 回落 flowKey；starter 缺 creator 回落空串', () => {
    const h = buildHeader(detail({ flowName: undefined, instance: { flowKey: 'fk', creator: null } }))
    expect(h.title).toBe('fk')
    expect(h.starter).toBe('')
  })
})

describe('buildFieldRows', () => {
  it('按 schema 字段序映射 label + value；长文本置 longText', () => {
    const schema = '{"fields":[' +
      '{"name":"amount","label":"金額","type":"number"},' +
      '{"name":"reason","label":"理由","type":"textarea"}]}'
    const data = '{"amount":120000,"reason":"設備更新のため"}'
    const rows = buildFieldRows(schema, data)
    expect(rows).toEqual([
      { label: '金額', value: '120000', longText: false },
      { label: '理由', value: '設備更新のため', longText: true },
    ])
  })

  it('缺值 → 空串；缺 label 回落 name；畸形 JSON → 空数组不抛', () => {
    const rows = buildFieldRows('{"fields":[{"name":"memo"}]}', '{}')
    expect(rows).toEqual([{ label: 'memo', value: '', longText: false }])
    expect(buildFieldRows('NOT_JSON{{{', '{}')).toEqual([])
    expect(buildFieldRows(undefined, undefined)).toEqual([])
  })
})

describe('buildHistoryRows', () => {
  it('只消费 timeline（已发生）；实办优先应办；时间截前 19 位；判定键映射', () => {
    const rows = buildHistoryRows([
      tl({ status: 1, actualHandlerName: '山田', handledAt: '2026-07-01T10:00:00.5' }),
      tl({ nodeName: '部长', status: 2, actualHandlerName: undefined, expectedHandlerName: '佐藤', handledAt: undefined, sentAt: '2026-07-02T09:00:00' }),
    ])
    expect(rows[0]).toMatchObject({ nodeName: '课长审批', handler: '山田', judgeKey: 'oa.formto.approved', handledAt: '2026-07-01T10:00:00' })
    expect(rows[1]).toMatchObject({ nodeName: '部长', handler: '佐藤', judgeKey: 'oa.formto.rejected', handledAt: '2026-07-02T09:00:00' })
  })

  it('スキップ（status=5）→ oa.formto.skipped（跨 spec 同键）；退回（7）→ oa.timeline.sentBack', () => {
    const rows = buildHistoryRows([tl({ status: 5 }), tl({ status: 7 })])
    expect(rows[0].judgeKey).toBe('oa.formto.skipped')
    expect(rows[1].judgeKey).toBe('oa.timeline.sentBack')
  })

  it('代签 onBehalfOf + 意見 comment 透传；childRef 今为 undefined（C2 降级）', () => {
    const rows = buildHistoryRows([tl({ onBehalfOfName: '田中', comment: '承認します' })])
    expect(rows[0].onBehalfOf).toBe('田中')
    expect(rows[0].comment).toBe('承認します')
    expect(rows[0].childRef).toBeUndefined()
  })
})

describe('buildStampFrames', () => {
  it('一关卡一枠，顺序保持', () => {
    const frames = buildStampFrames([
      tl({ nodeId: 'a', nodeName: '课长' }),
      tl({ nodeId: 'b', nodeName: '部长' }),
    ])
    expect(frames.map(f => f.nodeName)).toEqual(['课长', '部长'])
    expect(frames.every(f => f.people.length === 1)).toBe(true)
  })

  it('会签/并行同关卡多审批人 → 枠内纵列多人，各带判定', () => {
    const frames = buildStampFrames([
      tl({ nodeId: 'gk', nodeName: '合議', actualHandlerName: '山田', status: 1 }),
      tl({ nodeId: 'gk', nodeName: '合議', actualHandlerName: '佐藤', status: 2 }),
      tl({ nodeId: 'gk', nodeName: '合議', actualHandlerName: '鈴木', status: 1 }),
    ])
    expect(frames).toHaveLength(1)
    expect(frames[0].people).toHaveLength(3)
    expect(frames[0].people.map(p => p.judgeKey)).toEqual([
      'oa.formto.approved', 'oa.formto.rejected', 'oa.formto.approved',
    ])   // 有人承認有人却下如实各标
  })

  it('长流程 15 关卡 → 15 枠（折行本身走 QA 走查，此处断言枠数量）', () => {
    const many: TimelineRow[] = Array.from({ length: 15 }, (_, i) =>
      tl({ nodeId: `n${i}`, nodeName: `关卡${i + 1}` }))
    expect(buildStampFrames(many)).toHaveLength(15)
  })
})
```

- [ ] **Step 2: 跑测试验证 FAIL** — `cd cp6.web && npm run test -- ringiPrintModel`。预期编译失败（模块不存在）。

- [ ] **Step 3: 最小实现**

```ts
// cp6.web/src/views/oa/inbox/ringiPrintModel.ts
import type { InboxDetail, TimelineRow } from '@/types/oa/inbox'
import { formToStatusText, instanceStatusText } from './inboxModel'

export interface RingiHeader {
  title: string; bizNo: string; flowCode: string
  starter: string; startedAt: string; statusKey: string
}
export interface FieldRow { label: string; value: string; longText: boolean }
export interface HistoryRow {
  nodeName: string; handler: string; onBehalfOf?: string
  judgeKey: string; handledAt: string; comment?: string; childRef?: string
}
export interface StampPerson { handler: string; judgeKey: string; handledAt: string }
export interface StampFrame { nodeName: string; people: StampPerson[] }

/** 长文本字段类型（整行显示，署名/字段表用）。 */
const LONG_TEXT_TYPES = new Set(['textarea', 'richtext', 'multiline', 'longtext'])

function safeObj(json: string | undefined): Record<string, any> {
  if (!json) return {}
  try {
    const o = JSON.parse(json)
    return o && typeof o === 'object' && !Array.isArray(o) ? o : {}
  } catch {
    return {}
  }
}

function ts19(s?: string): string {
  return s ? s.replace('T', ' ').slice(0, 19).replace(' ', 'T') : ''
}

export function buildHeader(detail: InboxDetail): RingiHeader {
  const inst = (detail.instance ?? {}) as any
  const created = typeof inst.createDate === 'string' ? inst.createDate : ''
  return {
    title: detail.flowName || inst.flowKey || '',
    bizNo: (inst.bizId && String(inst.bizId)) || (inst.id ? String(inst.id) : ''),
    flowCode: inst.flowKey ? String(inst.flowKey) : '',
    starter: inst.creator ? String(inst.creator) : '',
    startedAt: created.slice(0, 10),
    statusKey: instanceStatusText(typeof inst.status === 'number' ? inst.status : 0),
  }
}

export function buildFieldRows(schemaJson: string | undefined, dataJson: string | undefined): FieldRow[] {
  const schema = safeObj(schemaJson)
  const fields = Array.isArray(schema.fields) ? schema.fields : []
  const data = safeObj(dataJson)
  return fields.map((f: any) => {
    const name = String(f?.name ?? '')
    const raw = data[name]
    return {
      label: String(f?.label ?? '') || name,
      value: raw === undefined || raw === null ? '' : String(raw),
      longText: LONG_TEXT_TYPES.has(String(f?.type ?? '')),
    }
  })
}

export function buildHistoryRows(timeline: TimelineRow[]): HistoryRow[] {
  return (timeline ?? []).map((r) => ({
    nodeName: r.nodeName || r.nodeId,
    handler: r.actualHandlerName || r.expectedHandlerName || '',
    onBehalfOf: r.onBehalfOfName || undefined,
    judgeKey: formToStatusText(r.status ?? 0),
    handledAt: ts19(r.handledAt || r.sentAt),
    comment: r.comment || undefined,
    childRef: undefined,                     // C2：subFlow 子单号未来字段，今降级
  }))
}

export function buildStampFrames(timeline: TimelineRow[]): StampFrame[] {
  const order: string[] = []
  const map = new Map<string, StampFrame>()
  for (const r of timeline ?? []) {
    const key = r.nodeId
    let frame = map.get(key)
    if (!frame) {
      frame = { nodeName: r.nodeName || r.nodeId, people: [] }
      map.set(key, frame)
      order.push(key)
    }
    frame.people.push({
      handler: r.actualHandlerName || r.expectedHandlerName || '',
      judgeKey: formToStatusText(r.status ?? 0),
      handledAt: ts19(r.handledAt || r.sentAt),
    })
  }
  return order.map((k) => map.get(k)!)
}
```

- [ ] **Step 4: 跑测试验证 PASS** — `npm run test -- ringiPrintModel`，预期全绿。
- [ ] **Step 5: type-check + commit**

```bash
cd cp6.web && npm run type-check
git add -A && git commit -m "feat(wfs-print): P-A-T1 ringiPrintModel 纯函数（表头/字段/履历/署名枠）+ vitest 三态·会签枠·Forecast排除"
```

---

### Task P-A-T2: RingiPrintView 组件 + 独立路由 + 四段排版 + FormDetail 打印按钮

**Files:**
- Create: `cp6.web/src/views/oa/inbox/RingiPrintView.vue`
- Modify: `cp6.web/src/router/index.ts`
- Modify: `cp6.web/src/views/oa/inbox/FormDetail.vue`

**Interfaces:**
- Consumes: `ringiPrintModel`（P-A-T1）、`inboxApi.detail`（既有）、`oa.print.*` 键（P-B-T1 seed；开发期键缺失时 `t()` 回落键名，不阻塞——P-B-T1 补齐前 QA 前必须完成）。
- Produces: `/oa/inbox/print/:instanceId` standalone 页 + FormDetail 打印入口。

- [ ] **Step 1: 组件（薄渲染层，四段排版；`@media print` 见 P-A-T3，本 Task 先落屏幕态结构 + token 样式骨架）**

```vue
<!-- cp6.web/src/views/oa/inbox/RingiPrintView.vue -->
<template>
  <div class="ringi-print" :class="{ ready: !loading }">
    <el-skeleton v-if="loading" :rows="8" animated />
    <CpEmpty v-else-if="!detail" :text="t('oa.detail.loadFailed')" />

    <div v-else class="ringi-sheet">
      <!-- ① 表头 -->
      <header class="ringi-head">
        <h1 class="ringi-title">{{ header.title }}</h1>
        <table class="ringi-head-meta">
          <tbody>
            <tr>
              <th>{{ t('oa.print.no') }}</th><td>{{ header.bizNo }}</td>
              <th>{{ t('oa.print.flowCode') }}</th><td>{{ header.flowCode }}</td>
            </tr>
            <tr>
              <th>{{ t('oa.print.starter') }}</th><td>{{ header.starter }}</td>
              <th>{{ t('oa.print.startedAt') }}</th><td>{{ header.startedAt }}</td>
            </tr>
            <tr>
              <th>{{ t('oa.print.status') }}</th>
              <td colspan="3">{{ t(header.statusKey) }}</td>
            </tr>
          </tbody>
        </table>
      </header>

      <!-- ② 表单字段 -->
      <section class="ringi-section">
        <h2 class="ringi-h2">{{ t('oa.print.fields') }}</h2>
        <table class="ringi-fields">
          <tbody>
            <tr v-for="(f, i) in fieldRows" :key="i" :class="{ 'row-long': f.longText }">
              <th>{{ f.label }}</th>
              <td :colspan="f.longText ? 3 : 1">{{ f.value }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- ③ 传签履历 -->
      <section class="ringi-section">
        <h2 class="ringi-h2">{{ t('oa.print.history') }}</h2>
        <table class="ringi-history">
          <thead>
            <tr>
              <th>{{ t('oa.print.stage') }}</th>
              <th>{{ t('oa.print.handler') }}</th>
              <th>{{ t('oa.print.judge') }}</th>
              <th>{{ t('oa.print.date') }}</th>
              <th>{{ t('oa.print.opinion') }}</th>
              <th>{{ t('oa.print.childRef') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(h, i) in historyRows" :key="i">
              <td>{{ h.nodeName }}</td>
              <td>
                {{ h.handler }}
                <span v-if="h.onBehalfOf" class="ringi-behalf">（{{ t('oa.print.onBehalf', { name: h.onBehalfOf }) }}）</span>
              </td>
              <td>{{ t(h.judgeKey) }}</td>
              <td>{{ h.handledAt }}</td>
              <td>{{ h.comment }}</td>
              <td>{{ h.childRef }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- ④ 印章式署名欄 -->
      <section class="ringi-section">
        <h2 class="ringi-h2">{{ t('oa.print.stamps') }}</h2>
        <div class="ringi-stamps">
          <div v-for="(frame, i) in stampFrames" :key="i" class="ringi-frame">
            <div class="frame-node">{{ frame.nodeName }}</div>
            <div v-for="(p, j) in frame.people" :key="j" class="frame-person">
              <div class="person-name">{{ p.handler }}</div>
              <div class="person-judge">{{ t(p.judgeKey) }}</div>
              <div class="person-date">{{ p.handledAt.slice(0, 10) }}</div>
            </div>
          </div>
        </div>
      </section>

      <p class="ringi-hint">{{ t('oa.print.hint') }}</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { inboxApi } from '@/api/oa/inbox'
import type { InboxDetail } from '@/types/oa/inbox'
import { buildHeader, buildFieldRows, buildHistoryRows, buildStampFrames } from './ringiPrintModel'
import CpEmpty from '@/components/base/CpEmpty.vue'

const { t } = useI18n()
const route = useRoute()
const instanceId = String(route.params.instanceId ?? '')

const loading = ref(true)
const detail = ref<InboxDetail | null>(null)

const header = computed(() => (detail.value ? buildHeader(detail.value) : buildHeader({ instance: {} } as any)))
const fieldRows = computed(() => buildFieldRows(detail.value?.formSchemaJson, detail.value?.currentDataJson))
const historyRows = computed(() => buildHistoryRows(detail.value?.timeline ?? []))   // 只 timeline，Forecast 不打
const stampFrames = computed(() => buildStampFrames(detail.value?.timeline ?? []))

onMounted(async () => {
  if (!instanceId) { loading.value = false; return }
  try {
    const res = await inboxApi.detail(instanceId)
    detail.value = (res as any).data as InboxDetail
  } catch {
    // http 拦截器已提示
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
/* 屏幕态即打印预览（纸张阴影）；@media print 覆盖见 P-A-T3 追加块 */
.ringi-print { padding: 24px; background: var(--cp-bg-subtle, #f2f3f5); min-height: 100vh; }
.ringi-sheet {
  width: 210mm; max-width: 100%; margin: 0 auto; padding: 15mm;
  background: var(--cp-bg, #fff); color: var(--cp-ink);
  box-shadow: 0 2px 16px rgba(0, 0, 0, 0.12);
}
.ringi-title { font-size: 20px; font-weight: 700; text-align: center; margin: 0 0 12px; letter-spacing: 4px; }
.ringi-head-meta, .ringi-fields, .ringi-history { width: 100%; border-collapse: collapse; }
.ringi-head-meta th, .ringi-head-meta td,
.ringi-fields th, .ringi-fields td,
.ringi-history th, .ringi-history td {
  border: 1px solid var(--cp-line); padding: 5px 8px; font-size: 12px; text-align: left; vertical-align: top;
}
.ringi-head-meta th, .ringi-fields th, .ringi-history thead th {
  background: var(--cp-fill-light, #f5f7fa); font-weight: 600; white-space: nowrap;
}
.ringi-section { margin-top: 16px; }
.ringi-h2 { font-size: 13px; font-weight: 700; margin: 0 0 8px; padding-left: 6px; border-left: 3px solid var(--cp-brand); }
.ringi-behalf { color: var(--cp-muted); font-size: 11px; }
/* 署名欄：flex-wrap 以枠为单位折行；每枠 break-inside:avoid（打印不跨页劈开） */
.ringi-stamps { display: flex; flex-wrap: wrap; gap: 8px; }
.ringi-frame {
  min-width: 96px; border: 1px solid var(--cp-ink); border-radius: 2px;
  break-inside: avoid; page-break-inside: avoid;
}
.frame-node { text-align: center; font-size: 11px; font-weight: 600; padding: 3px; border-bottom: 1px solid var(--cp-line); background: var(--cp-fill-light, #f5f7fa); }
.frame-person { text-align: center; padding: 6px 4px; border-bottom: 1px dashed var(--cp-line); }
.frame-person:last-child { border-bottom: none; }
.person-name { font-size: 13px; font-weight: 600; min-height: 20px; }
.person-judge { font-size: 10px; color: var(--cp-muted); }
.person-date { font-size: 10px; color: var(--cp-faint); }
.ringi-hint { margin-top: 20px; font-size: 11px; color: var(--cp-faint); text-align: center; }
</style>
```

- [ ] **Step 2: 路由注册**（`router/index.ts` `staticRoutes` 内，紧邻 Space standalone 组，加）：

```ts
  // WFS 稟議書打印（独立 standalone 路由、无侧边栏；instanceId 来自路由参数，数据复用 inboxApi.detail）
  {
    path: '/oa/inbox/print/:instanceId',
    name: 'oa-inbox-print',
    component: () => import('@/views/oa/inbox/RingiPrintView.vue'),
    meta: { standalone: true, title: '稟議書' },
  },
```

> 守卫核对：standalone 路由要求登录（不加入 `beforeEach` 无鉴权白名单，与 Space 编辑器同）；权限硬墙在服务端 `detail` 端点（R3）。若 `beforeEach` 对 standalone 有特殊分支，照 Space 编辑器现状（可达且要求 authed）即可，**不新增守卫代码**。

- [ ] **Step 3: FormDetail 打印按钮（R5 边界内，纯附加）** — `<template v-else>`（:13）首行加：

```vue
      <div class="detail-toolbar">
        <el-button size="small" @click="openPrint">{{ t('oa.print.btn') }}</el-button>
      </div>
```

`<script setup>` 加 `import { useRouter } from 'vue-router'`、`const router = useRouter()`、方法：

```ts
function openPrint() {
  const href = router.resolve({ name: 'oa-inbox-print', params: { instanceId: props.instanceId } }).href
  window.open(href, '_blank')
}
```

`<style scoped>` 尾部加（仅管自身，不动既有块）：

```css
.detail-toolbar { display: flex; justify-content: flex-end; padding: 0 0 8px; }
@media (max-width: 767px) { .detail-toolbar { justify-content: stretch; } .detail-toolbar .el-button { flex: 1; } }
```

- [ ] **Step 4: 验证** — `npm run type-check` 通过；`npm run build` 通过；`npm run test`（P-A-T1 用例仍绿，本 Task 无新纯函数测试——渲染薄层走 P-B-T2 QA 走查，见 C3）。
- [ ] **Step 5: commit**

```bash
git add -A && git commit -m "feat(wfs-print): P-A-T2 RingiPrintView 四段排版 + standalone 路由 + FormDetail 打印入口"
```

---

### Task P-A-T3: `@media print` 打印样式（A4 纵向 / 隐藏壳 / 履历跨页 thead 重复 / 枠避断页）

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/RingiPrintView.vue`（`<style scoped>` 尾部追加 `@media print` 块）

**Interfaces:**
- Consumes: P-A-T2 的 DOM 结构与 class 名。
- Produces: 打印/另存 PDF 的 A4 纵向排版。纯 CSS，无 TS 改动。

- [ ] **Step 1: 追加 `@media print` 块**（`<style scoped>` 末尾）：

```css
/* ── 打印态（A4 纵向；spec §2.3）────────────────────────────────── */
@media print {
  @page { size: A4 portrait; margin: 15mm; }
  .ringi-print { padding: 0; background: #fff; min-height: 0; }
  .ringi-sheet { width: auto; margin: 0; padding: 0; box-shadow: none; }
  .ringi-hint { display: none; }                              /* 屏幕提示不上纸 */
  /* 履历表跨页：thead 每页重复 */
  .ringi-history thead { display: table-header-group; }
  .ringi-history tr { break-inside: avoid; page-break-inside: avoid; }
  /* 署名枠不跨页劈开（会签多人枠亦整体保留） */
  .ringi-frame { break-inside: avoid; page-break-inside: avoid; }
  .ringi-section { break-inside: auto; }
  /* 黑白友好：语义靠边框/文字，去掉表头底色亦可读 */
  .ringi-head-meta th, .ringi-fields th, .ringi-history thead th, .frame-node {
    background: transparent !important; -webkit-print-color-adjust: exact; print-color-adjust: exact;
  }
}
```

> **隐藏应用壳**：RingiPrintView 是 standalone 路由（不挂 LayoutView，无侧栏/顶栏），故打印天然无壳——**无需 `.app-sidebar{display:none}` 之类跨组件 print 规则**（这正是 spec §2.1 选独立路由不选对话框 iframe 的理由）。本块只管稟議书自身元素。

- [ ] **Step 2: 验证** — `npm run build` 通过；`npm run type-check` 通过。视觉走查移交 P-B-T2 QA（jsdom 测不了 `@media print`，见 C3）。
- [ ] **Step 3: commit**

```bash
git add -A && git commit -m "feat(wfs-print): P-A-T3 @media print A4纵向+履历thead跨页重复+署名枠避断页+黑白友好"
```

---

## Wave P-B — i18n seed + QA harness + DoD

### Task P-B-T1: I18nOaRingiPrintScreenSeed 五语（仅打印 chrome，判定词复用既有键）

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaRingiPrintScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（i18n seed concat 一行）

**Interfaces:**
- Produces: `oa.print.*` 键（~16），RingiPrintView / FormDetail 引用。判定/状态/关卡词**不在本 seed**（复用 `oa.formto.*` / `oa.inst.*` / `oa.timeline.*`，已 seed）。

- [ ] **Step 1: seed 类**（键面以 RingiPrintView.vue / FormDetail.vue 实际 `t()` 引用为权威；日文按稟議書惯例用语）

```csharp
// CP6.WebApi/Seed/I18nOaRingiPrintScreenSeed.cs
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>稟議書打印视图词条：oa.print.*（RingiPrintView.vue / FormDetail.vue 引用）。
/// 判定词/状态词/关卡词**不在此**——复用既有 oa.formto.* / oa.inst.* / oa.timeline.*（inboxModel.ts 映射，
/// 其中 oa.formto.skipped=スキップ 与 wfs-version-ops 计划同词条键，两处均不重复 seed）。
/// 去重：本文件 16 个 oa.print.* 在既有 I18nOaInbox/Advanced/Designer/SerialSign/Approver/ServiceTask seed 中均无重复（已 grep 核实）。</summary>
public static class I18nOaRingiPrintScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        new() { LangKey = "oa.print.btn",       ZhCN = "打印",       ZhTW = "列印",       En = "Print",        Ja = "印刷",         Ko = "인쇄" },
        new() { LangKey = "oa.print.title",     ZhCN = "稟議書",     ZhTW = "稟議書",     En = "Ringi Sheet",  Ja = "稟議書",       Ko = "품의서" },
        new() { LangKey = "oa.print.no",        ZhCN = "单号",       ZhTW = "單號",       En = "No.",          Ja = "番号",         Ko = "번호" },
        new() { LangKey = "oa.print.flowCode",  ZhCN = "流程编号",   ZhTW = "流程編號",   En = "Flow Code",    Ja = "フローコード", Ko = "플로우 코드" },
        new() { LangKey = "oa.print.starter",   ZhCN = "起案者",     ZhTW = "起案者",     En = "Originator",   Ja = "起案者",       Ko = "기안자" },
        new() { LangKey = "oa.print.startedAt", ZhCN = "起案日",     ZhTW = "起案日",     En = "Date",         Ja = "起案日",       Ko = "기안일" },
        new() { LangKey = "oa.print.status",    ZhCN = "状态",       ZhTW = "狀態",       En = "Status",       Ja = "状態",         Ko = "상태" },
        new() { LangKey = "oa.print.fields",    ZhCN = "申请内容",   ZhTW = "申請內容",   En = "Content",      Ja = "申請内容",     Ko = "신청 내용" },
        new() { LangKey = "oa.print.history",   ZhCN = "传签履历",   ZhTW = "傳簽履歷",   En = "Routing History", Ja = "回付履歴",  Ko = "전자결재 이력" },
        new() { LangKey = "oa.print.stage",     ZhCN = "关卡",       ZhTW = "關卡",       En = "Stage",        Ja = "審査段階",     Ko = "단계" },
        new() { LangKey = "oa.print.handler",   ZhCN = "審査者",     ZhTW = "審査者",     En = "Reviewer",     Ja = "審査者",       Ko = "심사자" },
        new() { LangKey = "oa.print.judge",     ZhCN = "判定",       ZhTW = "判定",       En = "Decision",     Ja = "判定",         Ko = "판정" },
        new() { LangKey = "oa.print.date",      ZhCN = "日付",       ZhTW = "日付",       En = "Date",         Ja = "日付",         Ko = "일자" },
        new() { LangKey = "oa.print.opinion",   ZhCN = "意见",       ZhTW = "意見",       En = "Comment",      Ja = "意見",         Ko = "의견" },
        new() { LangKey = "oa.print.childRef",  ZhCN = "参照",       ZhTW = "參照",       En = "Ref.",         Ja = "参照",         Ko = "참조" },
        new() { LangKey = "oa.print.stamps",    ZhCN = "署名栏",     ZhTW = "署名欄",     En = "Signatures",   Ja = "署名欄",       Ko = "서명란" },
        new() { LangKey = "oa.print.onBehalf",  ZhCN = "代 {name} 签", ZhTW = "代 {name} 簽", En = "for {name}", Ja = "{name} 代理", Ko = "{name} 대리" },
        new() { LangKey = "oa.print.hint",      ZhCN = "请使用浏览器打印或另存为 PDF（Ctrl/Cmd+P）。", ZhTW = "請使用瀏覽器列印或另存為 PDF（Ctrl/Cmd+P）。", En = "Use your browser to print or save as PDF (Ctrl/Cmd+P).", Ja = "ブラウザの印刷または PDF 保存をご利用ください（Ctrl/Cmd+P）。", Ko = "브라우저 인쇄 또는 PDF 저장을 사용하세요 (Ctrl/Cmd+P)." },
    };
}
```

> 键计数：18 条（含 `onBehalf` / `hint` 两条 chrome 文案）。「~15 键」为 spec 估值，实际以引用为准。

- [ ] **Step 2: Program.cs concat**（i18n seed 拼装链，inbox-ux R6 记载 :1813-1819 一带；在 OA 系列 seed concat 尾部加）：

```csharp
    .Concat(I18nOaRingiPrintScreenSeed.Items)
```

> 拼装链尾部既有 `.Where(x => !existingKeys.Contains(...))` + `GroupBy(LangKey)` 双层去重兜底，重复键不会写入。

- [ ] **Step 3: 验证 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
git add -A && git commit -m "feat(wfs-print): P-B-T1 I18nOaRingiPrintScreenSeed 五语 18 键（判定词复用既有 oa.formto.*）"
```

---

### Task P-B-T2: gstack QA harness + DoD

**Files:**
- Create: `docs/superpowers/qa/wfs-ringi-print/README.md`
- Create: `docs/superpowers/qa/wfs-ringi-print/seed.sql`
- Create: `docs/superpowers/qa/wfs-ringi-print/qa_ringi_print.ps1`

**Interfaces:**
- Consumes: 全 P-A/P-B-T1 产物。
- Produces: 真浏览器打印预览走查脚本 + 五剧本种子。

- [ ] **Step 1: seed.sql** — 建 5 个稟議书实例（同租户、同起案者），每个配 `Wf_FlowFormTo` 履历行，覆盖：
  - **S1 决裁済单**：3 关卡全 status=1（承認），实例 status=1。
  - **S2 在途单**：2 关卡 status=1 + 1 关卡 status=0（待办），实例 status=0；另置 forecast 数据（后端 Running 时自算）——走查确认 **Forecast 段不上纸**。
  - **S3 却下单**：1 关卡 status=1 + 1 关卡 status=2（却下），实例 status=2。
  - **S4 会签单**：同一 nodeId 三行 FormTo（山田 status=1 / 佐藤 status=2 / 鈴木 status=1），实例 status=0 或 1——走查确认署名枠**枠内纵列三人各标判定**（含承認+却下混存）。
  - **S5 长流程单**：15 个不同 nodeId 各一行——走查确认署名枠**以枠为单位 flex-wrap 折行**、每枠 break-inside:avoid（不横向压缩/不跨页劈开）、履历表**跨页 thead 重复**。
  - 字段：`Wf_FlowInstance.VarsJson` 含短字段（金額）+ 长文本字段（理由，schema type=textarea），走查确认**长文本整行**。字段表 label 映射来自对应 `Wf_FormDef.SchemaJson`。
  - Sys_User / Wf_FlowDef / Wf_FormDef 主数据齐备（仿既有 OA QA seed 口径，`creator` 字段填人名如「铃木一郎」以验 C1 起案者渲染）。
- [ ] **Step 2: qa_ringi_print.ps1** — 登录 → 对每个实例 `GET /api/oa/inbox/detail` 冒烟（确认 detail 端点返回）→ 打印路由 URL 清单输出（`/oa/inbox/print/{id}`）供人工浏览器打印预览走查。
- [ ] **Step 3: README.md** — 记录：dev server 启动命令、QA 登录账号、5 剧本对应实例 id、**人工走查清单**：
  1. A4 纵向、页边距 15mm、无侧栏/顶栏（standalone）。
  2. 表头四要素（起案者= `creator` 人名 / 起案日 / 单号 / FlowCode / 状态词五语切换正确）。
  3. 字段表 label 映射；长文本整行。
  4. 履历表六列；判定词五语（承認/却下/転送/スキップ/退回）；**Forecast 段不出现**（S2）；子单号列今留空（C2）。
  5. 署名枠：S4 枠内纵列三人各标判定；S5 折行 + 每枠不跨页劈开；履历跨页 thead 重复。
  6. 移动端不受影响回归（FormDetail 打印按钮在窄屏可点、开新标签）。
  7. 黑白打印可读（语义不靠底色）。
- [ ] **Step 4: commit**

```bash
git add -A && git commit -m "docs(wfs-print): P-B-T2 gstack QA harness（5剧本seed+走查清单+打印预览脚本）"
```

---

## Definition of Done（合并前逐条核对）

- [ ] **spec 逐节覆盖**：§2.1 独立路由页 `/oa/inbox/print/:instanceId` + 不自动 `window.print()` + 权限同 FormDetail（P-A-T2/R3）；§2.2 表头四要素（P-A-T1 buildHeader）/ 字段表 label 双列 + 长文本整行（buildFieldRows）/ 履历只已发生 + 判定词跨 spec 同键 + subFlow 子单号列（buildHistoryRows，C2 降级）/ 印章式署名欄 + **会签枠内纵列多人各标判定**（buildStampFrames）+ **flex-wrap 按枠折行 + break-inside:avoid**（P-A-T2/T3 CSS）；§2.3 `@media print` A4 纵向 + 履历 thead 跨页重复 + 署名枠避断页 + 黑白友好（P-A-T3）；§3 vitest 三态/Forecast 排除/subFlow 降级/字段 label（P-A-T1）+ QA 走查（P-B-T2）；§5 YAGNI 未越界（无服务端 PDF / 无批量 / 无自定义模板）。
- [ ] **只打已发生**：`buildHistoryRows`/`buildStampFrames` 只接 `timeline`，`RingiPrintView` 永不传 `forecast`；vitest + S2 走查双证。
- [ ] **跨 spec 同键**：スキップ = `oa.formto.skipped`（既有键，与 wfs-version-ops 同）；本 seed 零重复判定词。
- [ ] **零依赖/零后端改动/零新端点**：`git show --stat` 确认仅碰 File Structure 清单；`package.json` 无新增；后端仅 seed + Program.cs 一行；`dotnet ef migrations has-pending-model-changes` clean。
- [ ] **基线**：`npm run test` 320+N 全绿；`npm run type-check` 通过；`npm run build` 通过；`dotnet build` 通过；`dotnet test` 1509 零回归。
- [ ] **占位符扫描**：全 diff 无 `TODO`/`待补`/`{ /* */ }`/`throw new NotImplementedException`（测试体均可编译完整代码）。
- [ ] **签名一致**：全部提交 `feat(wfs-print): ...` / `docs(wfs-print): ...` 中文；**只本地 commit 不 push**。
- [ ] **三期 X-C 边界**：FormDetail 改动仅 `.detail-toolbar` 附加 + `openPrint` + `useRouter` import，未触碰 X-C 领地（`el-row`/`detail-left/right`/`action-bar`/既有 `@media`）。

---

*生成于 2026-07-05。纯前端增量（后端仅 i18n seed）；5 Task（P-A ×3 + P-B ×2）；零跨模块污染。*
