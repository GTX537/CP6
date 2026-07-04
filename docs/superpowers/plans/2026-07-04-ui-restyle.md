# CP6 UI 风格翻新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按《CP6 Design System v1.0》把 cp6.web 全站翻新为「专业化科技感柔和 SaaS」风格：token 层 → 布局壳/仪表盘 → 模板组件 → 分模块迁移。

**Architecture:** 三层收敛——第 1 层 CSS 设计 token（`--cp-*`）+ Element Plus 变量映射全局换肤；第 2 层 Cp 前缀基础件与业务模板（CpListPage 收敛 130+ 手写查询页）；第 3 层业务页只写布局。每个 Milestone 独立可合并上线。

**Tech Stack:** Vue 3.5 `<script setup lang="ts">` + Element Plus 2.13.6（CSS 变量换肤，不引入 SCSS 定制）+ Vite 8 + Vitest 4（组件测试用 jsdom pragma）+ @fontsource/nunito。

## 必读文档（实现者先看）

| 文档 | 作用 |
|---|---|
| `docs/CP6_Design_System_v1.0.md` | 唯一视觉事实来源：全部 token 值、组件规范、命名规范。**本计划中所有色值/圆角/阴影以它为准** |
| `picture/mockup-final-a-dashboard.html` | 仪表盘视觉基准（已入库）。LayoutView 侧栏/顶栏与 Dashboard 的 CSS 精确值直接从此文件对应 class 抄 |
| `picture/mockup-final-b-wms-list.html` | 列表页视觉基准。CpListPage/CpFilterBar/CpStatusStrip/表格样式的 CSS 精确值来源 |
| `docs/superpowers/specs/2026-07-04-ui-restyle-design.md` | 决策背景与非目标 |

## Global Constraints

- 分支：全部工作在 `feat/ui-restyle` 分支（从 main 切出）；Milestone C 每模块单独分支 `feat/ui-migrate-<module>` + PR
- 视觉值唯一来源 = tokens：**业务页（src/views/**）新代码禁止硬编码色值/阴影/圆角**（图表系列色按设计系统 §2.5 豁免）
- 不改任何后端、不改 API 契约、不改 `localStorage.menus` / i18n 词条机制 / 路由结构
- 必须保留的现有功能：语言切换（`langOptions`/`changeLang`）、NotificationBell、ImpersonationBanner、平台区入口（`showPlatformEntry`）、移动端抽屉（`useBreakpoint().isMobile`）、登录入场动画（`enterFromLogin`）
- 组件命名：`Cp` 前缀 PascalCase；基础件放 `src/components/base/`，模板放 `src/components/templates/`
- 每个 Cp 组件文件头部必须有注释：用途 + props/slots 一览 + ≤10 行使用示例
- 验证命令：`npm run type-check`、`npm run test`、`npm run build`（在 `cp6.web/` 下执行）；视觉验收用 gstack 截图对照 mockup
- Commit 规约：小步频繁提交，消息前缀 `feat(ui):`/`refactor(ui):`/`test(ui):`，结尾 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- `prefers-reduced-motion: reduce` 下关停 transform 过渡（transitions.css 统一处理）

---

# Milestone A：Token 层 + 布局壳 + 仪表盘（全站观感变 70~80%）

### Task 1: 样式基建四件套 + main.ts 接线 + 清理脚手架残留

**Files:**
- Create: `cp6.web/src/styles/tokens.css`
- Create: `cp6.web/src/styles/tokens-dark.css`
- Create: `cp6.web/src/styles/element-overrides.css`
- Create: `cp6.web/src/styles/transitions.css`
- Modify: `cp6.web/src/main.ts:1-6`（import 块）
- Delete: `cp6.web/src/assets/base.css`、`cp6.web/src/components/HelloWorld.vue`、`TheWelcome.vue`、`WelcomeItem.vue`、`cp6.web/src/components/icons/`（整目录）

**Interfaces:**
- Produces: 全局 CSS 变量 `--cp-*`（完整清单=设计系统附录 A）；`.num` 工具类；Element Plus 全局观感。后续所有任务消费这些变量。

- [ ] **Step 1: 创建 tokens.css**

内容 = 设计系统 `docs/CP6_Design_System_v1.0.md` **附录 A 代码块原样拷贝**，并在文件末尾追加工具类与 body 氛围背景：

```css
/* 附录 A 的 :root{...} 原样在上方 …… 以下为追加部分 */
.num { font-variant-numeric: tabular-nums; }

body {
  font-family: var(--cp-font);
  color: var(--cp-text);
  background:
    radial-gradient(1000px 520px at 92% -8%, rgba(43,212,205,.10), transparent 55%),
    var(--cp-bg);
}
```

- [ ] **Step 2: 创建 tokens-dark.css（只建结构，v1.0 空实现）**

```css
/* CP6 Design System §12：暗色模式覆盖层。v1.0 仅占位——
   业务代码只许消费 semantic 变量，未来在此覆盖即可零重构上暗色。 */
html.dark {
  /* 预留：--cp-bg / --cp-card / --cp-ink / --cp-text / --cp-line ... */
}
```

- [ ] **Step 3: 创建 element-overrides.css**

内容 = 设计系统 **§10.1 代码块原样拷贝**，并补齐语义色 light 变体与集中微调：

```css
/* §10.1 的 :root{...} 映射在上方 …… 以下为追加部分 */
:root {
  --el-color-success-light-9: var(--cp-ok-bg);
  --el-color-warning-light-9: var(--cp-warn-bg);
  --el-color-danger-light-9: var(--cp-danger-bg);
  --el-color-info-light-9: var(--cp-info-bg);
}
/* 表头：设计系统 §9.1 CpTable 规范 */
.el-table th.el-table__cell {
  background: var(--cp-bg-th);
  color: var(--cp-muted);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: .8px;
}
.el-table .el-table__row:hover > td.el-table__cell { background: var(--cp-bg-hover); }
/* 主按钮品牌渐变 + glow */
.el-button--primary {
  background: var(--cp-brand-grad);
  border: none;
  box-shadow: var(--cp-brand-glow);
  font-weight: 800;
}
.el-button--primary:hover { transform: translateY(-1px); }
/* 卡片 */
.el-card { border-radius: var(--cp-r-lg); border: none; box-shadow: var(--cp-shadow-1); }
.el-dialog { border-radius: var(--cp-r-lg); }
```

- [ ] **Step 4: 创建 transitions.css**

```css
.cp-hover-lift { transition: transform var(--cp-t-base), box-shadow var(--cp-t-base); }
.cp-hover-lift:hover { transform: translateY(-3px); box-shadow: var(--cp-shadow-2); }

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { transition-property: opacity, color, background-color, border-color !important; }
}
```

- [ ] **Step 5: 安装字体并接线 main.ts**

```bash
cd cp6.web && npm i @fontsource/nunito
```

`main.ts` import 块改为（顺序即层叠优先级）：

```ts
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import '@fontsource/nunito/600.css'
import '@fontsource/nunito/700.css'
import '@fontsource/nunito/800.css'
import './styles/tokens.css'
import './styles/tokens-dark.css'
import './styles/element-overrides.css'
import './styles/transitions.css'
import './assets/main.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
```

- [ ] **Step 6: 删除脚手架残留**

```bash
cd cp6.web
git rm src/assets/base.css src/components/HelloWorld.vue src/components/TheWelcome.vue src/components/WelcomeItem.vue
git rm -r src/components/icons
```

先 `grep -rn "base.css\|HelloWorld\|TheWelcome\|WelcomeItem\|components/icons" src/ --include=*.vue --include=*.ts` 确认除待删文件互引外无业务引用；若有则该引用一并清除。

- [ ] **Step 7: 验证**

Run: `npm run type-check && npm run build`
Expected: 均 0 error。`npm run dev` 后 gstack 打开 http://localhost:5173（或 vite 实际端口）登录页——应看到青绿主色按钮与新底色（Element Plus 变量已生效）。

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(ui): design tokens + Element Plus 全局换肤 + 清理脚手架残留"
```

---

### Task 2: LayoutView 重做（悬浮侧栏 + 毛玻璃顶栏）

**Files:**
- Modify: `cp6.web/src/views/LayoutView.vue`（template 40% 重排 + style 全换；script 逻辑不动）
- Modify: `cp6.web/src/components/MenuTreeItem.vue`（仅样式适配浅色侧栏，结构不动）

**Interfaces:**
- Consumes: Task 1 的全部 token
- Produces: 新布局壳。业务页无感（`<el-main>` 容器契约不变）

**关键约束（Global Constraints 重申）**：script setup 内全部逻辑保持原样——`menuTree`/`buildTree`、`langOptions`/`onChangeLang`、`handleLogout`、`showPlatformEntry`/`platformLinks`、`pageTitle`、`enterFromLogin`、移动抽屉。只改视觉。

- [ ] **Step 1: 桌面侧栏改造**

`el-aside` 宽 220px→238px；删除 `el-menu` 上的 `background-color="#304156" text-color="#bfcbd9" active-text-color="#409eff"` 三个硬编码属性。`.layout-logo` 换成 mockup 品牌区结构：

```html
<div class="cp-brand">
  <span class="cp-brand-logo">CP</span>
  <div><b>{{ $t('app.title') }}</b><small>MANUFACTURING</small></div>
</div>
```

侧栏底部加环境徽标 `<div class="cp-env"><i />{{ $t('app.title') }} · v2.4</div>`。
CSS 精确值从 `picture/mockup-final-a-dashboard.html` 的 `.sidebar/.brand/.env` 类抄，色值一律替换为对应 `--cp-*` 变量（如 `#10343C`→`var(--cp-ink)`）。

- [ ] **Step 2: 浅色菜单样式（scoped + :deep）**

```css
.layout-aside { background: transparent; box-shadow: none; padding: 12px 10px; }
.layout-aside :deep(.el-menu) { background: transparent; border-right: none; }
.layout-aside :deep(.el-menu-item),
.layout-aside :deep(.el-sub-menu__title) {
  color: var(--cp-text); font-weight: 700; font-size: 13.5px;
  border-radius: var(--cp-r-md); margin: 2px 0; height: 42px;
}
.layout-aside :deep(.el-menu-item:hover),
.layout-aside :deep(.el-sub-menu__title:hover) { background: var(--cp-brand-bg); color: var(--cp-ink); }
.layout-aside :deep(.el-menu-item.is-active) {
  background: var(--cp-brand-grad); color: #fff; box-shadow: var(--cp-brand-glow);
}
.layout-aside :deep(.el-sub-menu .el-menu) { background: transparent; }
```

MenuTreeItem.vue 若含硬编码色，同法替换为变量（先 `grep -n "#[0-9a-fA-F]" src/components/MenuTreeItem.vue` 检查）。

- [ ] **Step 3: 顶栏改造**

`.layout-header` 换 mockup `.topbar` 规格：`background: rgba(242,250,251,.72); backdrop-filter: blur(14px); border-bottom: 1px solid var(--cp-line);`。右侧顺序：语言切换（保留 el-select，加 `.cp-lang` 圆角 11px 白底样式）→ NotificationBell → 用户胶囊（`.cp-me`：渐变圆头像 = 昵称首字 + 昵称，样式抄 mockup `.me`）→ 登出改图标按钮。手机端汉堡/标题逻辑不动。

- [ ] **Step 4: 移动抽屉配色适配**

`.drawer-content` 背景 `#304156` 渐变→`var(--cp-card)`；`.drawer-header/.drawer-footer` 边框换 `var(--cp-line)`；drawer 内 el-menu 同 Step 2 样式（提为共用 class）。全局块里 `.layout-drawer .el-drawer__body { background: #304156 }`→`var(--cp-card)`。

- [ ] **Step 5: 验证（gstack）**

```
goto http://localhost:5173 → 登录（admin）→ screenshot
点开两级菜单 → 进任一 WMS 页 → screenshot
viewport 375x812 → 汉堡开抽屉 → screenshot
```
对照 mockup-final-a：侧栏选中态渐变+glow、顶栏毛玻璃。检查 `console` 无新增错误；语言切换成日文再切回，菜单文案正常。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(ui): LayoutView 悬浮侧栏+毛玻璃顶栏，菜单/i18n/平台入口逻辑不变"
```

---

### Task 3: Dashboard 重做

**Files:**
- Modify: `cp6.web/src/views/dashboard/DashboardView.vue`（视觉层重写；数据获取逻辑与 API 调用保留）
- Create: `cp6.web/src/views/dashboard/components/KpiCard.vue`
- Create: `cp6.web/src/views/dashboard/components/DonutChart.vue`

**Interfaces:**
- Consumes: Task 1 token；现有 dashboard API（先 `grep -n "api\." src/views/dashboard/DashboardView.vue` 列出全部数据源，一个不落地保留）
- Produces: `KpiCard`（props: `label:string, value:number|string, suffix?:string, tone?:'brand'|'info'|'warn'|'danger', trend?:number[], sub?:string`）；`DonutChart`（props: `segments:{label:string,value:number,color:string}[], centerLabel:string`）。Milestone B Task 9 会把 KpiCard 抽为 CpStatCard。

- [ ] **Step 1: 实现 KpiCard.vue（含 sparkline）**

```vue
<!-- KpiCard：仪表盘 KPI 卡。props 见下；trend 传 7 日数值数组渲染 sparkline。
     用法：<KpiCard label="在制指令" :value="10" suffix="件" tone="brand" :trend="[3,4,2,5,6,5,7]" sub="完成率 36.4%" /> -->
<script setup lang="ts">
const props = withDefaults(defineProps<{
  label: string; value: number | string; suffix?: string
  tone?: 'brand' | 'info' | 'warn' | 'danger'; trend?: number[]; sub?: string
}>(), { tone: 'brand' })
const toneVar = { brand: 'var(--cp-brand)', info: 'var(--cp-info)', warn: 'var(--cp-warn)', danger: 'var(--cp-danger)' }
function points(t: number[]): string {
  const max = Math.max(...t, 1), min = Math.min(...t, 0)
  return t.map((v, i) => `${(i / (t.length - 1)) * 100},${28 - ((v - min) / (max - min || 1)) * 24}`).join(' ')
}
</script>
<template>
  <div class="kpi cp-hover-lift" :class="tone === 'danger' ? 'alert' : ''">
    <div class="top"><span class="lbl">{{ label }}</span><slot name="icon" /></div>
    <div class="val num">{{ value }}<small v-if="suffix"> {{ suffix }}</small></div>
    <div v-if="sub" class="sub">{{ sub }}</div>
    <svg v-if="trend?.length" class="spark" viewBox="0 0 100 30" preserveAspectRatio="none" aria-hidden="true">
      <polyline :points="points(trend)" :stroke="toneVar[tone]" stroke-width="2" fill="none" stroke-linecap="round" />
    </svg>
  </div>
</template>
```

`<style scoped>` 从 mockup-final-a `.kpi` 系列类抄，色值换变量。

- [ ] **Step 2: 实现 DonutChart.vue**

SVG 三段圆环算法照 mockup-final-a 的 donut（r=46，C=289.0，`stroke-dasharray="len 289" stroke-dashoffset="-累计+缝隙3"`），由 `segments` 动态计算；右侧图例行 = 色块+label+数量+百分比。分段间保留 3px 弧长缝隙，`segments` 为空时渲染灰色整环。

- [ ] **Step 3: DashboardView 模板重排**

结构照 mockup-final-a：page-head（问候+日期+预警摘要，右侧操作钮）→ 4×KpiCard → 双列（左：出货进度条卡/最近受注表/快捷入口；右：预警条幅/DonutChart 制造进度/通知 feed）。**所有现有数据绑定、路由跳转、i18n key 原样保留**；快捷入口沿用现有入口配置数组。表格若已是 el-table 则只调整容器 class（新表头样式来自 Task 1 overrides）。

- [ ] **Step 4: 验证 + Commit**

gstack 截图对照 mockup-final-a；`console` 无错误；`npm run type-check && npm run build` 通过。

```bash
git add -A && git commit -m "feat(ui): Dashboard 重做——KPI sparkline + 制造进度环图 + 新布局"
```

---

### Task 4: Milestone A 收尾——main.css 瘦身与回归

**Files:**
- Modify: `cp6.web/src/assets/main.css`（把 `#409eff`/`#304156` 等硬编码替换为 `--cp-*` 变量；响应式规则保留）

- [ ] **Step 1:** `grep -n "#[0-9a-fA-F]\{3,8\}" src/assets/main.css` 逐条替换：`#409eff`→`var(--cp-brand)`、`#304156/#26364a`→已废弃的侧栏色直接删除对应规则、灰阶→就近中性 token。
- [ ] **Step 2:** 回归四个代表页（gstack）：登录页、Dashboard、一个 WMS 列表页、一个 OA 页——截图检查无样式崩坏、无控制台错误；`npm run test` 全绿（现有 space-editor 等单测不受影响）。
- [ ] **Step 3:** Commit + 合并请求：`refactor(ui): main.css 硬编码色值 token 化`。**Milestone A 可在此合并回 main 上线。**

---

# Milestone B：模板组件层 + WMS 试点

### Task 5: 组件测试基建

**Files:**
- Modify: `cp6.web/package.json`（devDeps）

- [ ] **Step 1:** `npm i -D @vue/test-utils jsdom`
- [ ] **Step 2:** 写冒烟测试 `src/components/base/__tests__/smoke.spec.ts`：

```ts
// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
describe('component test infra', () => {
  it('mounts a component under jsdom', () => {
    const w = mount(defineComponent({ template: '<p>ok</p>' }))
    expect(w.text()).toBe('ok')
  })
})
```

- [ ] **Step 3:** Run `npm run test -- smoke` → PASS。Commit：`test(ui): 组件测试基建（@vue/test-utils + jsdom pragma）`。

---

### Task 6: CpTag（状态 pill）+ CpSectionHeader

**Files:**
- Create: `cp6.web/src/components/base/CpTag.vue`、`cp6.web/src/components/base/CpSectionHeader.vue`
- Test: `cp6.web/src/components/base/__tests__/CpTag.spec.ts`

**Interfaces:**
- Produces: `CpTag` props `{ status?: string; tone?: 'ok'|'warn'|'danger'|'info'|'muted' }`——传 `status` 走集中映射，传 `tone` 直接指定；slot 为文案。`STATUS_TONE` 映射表 export 供业务查询。`CpSectionHeader` props `{ title: string }` + slot `extra`。

- [ ] **Step 1: 失败测试**

```ts
// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CpTag, { STATUS_TONE } from '../CpTag.vue'
describe('CpTag', () => {
  it('maps known status to tone class', () => {
    const w = mount(CpTag, { props: { status: '已出库' }, slots: { default: '已出库' } })
    expect(w.classes()).toContain('t-ok')
  })
  it('falls back to muted for unknown status', () => {
    const w = mount(CpTag, { props: { status: '莫名状态' } })
    expect(w.classes()).toContain('t-muted')
  })
  it('explicit tone overrides status', () => {
    const w = mount(CpTag, { props: { status: '已出库', tone: 'danger' } })
    expect(w.classes()).toContain('t-danger')
  })
  it('exports STATUS_TONE map', () => { expect(STATUS_TONE['拣货中']).toBe('info') })
})
```

Run: `npm run test -- CpTag` → FAIL（模块不存在）。

- [ ] **Step 2: 实现 CpTag.vue**

```vue
<!-- CpTag：状态 pill（圆点+文字，设计系统 §9.1）。
     <CpTag status="已出库">已出库</CpTag> 或 <CpTag tone="warn">未出库</CpTag> -->
<script lang="ts">
export const STATUS_TONE: Record<string, string> = {
  '已出库': 'ok', '已出货': 'ok', '已完成': 'ok', '已对账': 'ok', '已批准': 'ok',
  '未出库': 'warn', '未出货': 'warn', '待审批': 'warn', '待处理': 'warn',
  '拣货中': 'info', '进行中': 'info', '已发行': 'info',
  '已取消': 'muted', '已作废': 'muted',
  '超期': 'danger', '今日': 'danger', '已驳回': 'danger'
}
</script>
<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ status?: string; tone?: 'ok'|'warn'|'danger'|'info'|'muted' }>()
const t = computed(() => props.tone ?? (props.status && STATUS_TONE[props.status]) ?? 'muted')
</script>
<template><span class="cp-tag" :class="`t-${t}`"><slot>{{ status }}</slot></span></template>
<style scoped>
.cp-tag { display:inline-flex; align-items:center; gap:6px; font-size:11.5px; font-weight:800;
  padding:3px 10px; border-radius:999px; }
.cp-tag::before { content:""; width:6px; height:6px; border-radius:50%; background:currentColor; }
.t-ok { background:var(--cp-ok-bg); color:var(--cp-ok); }
.t-warn { background:var(--cp-warn-bg); color:var(--cp-warn); }
.t-danger { background:var(--cp-danger-bg); color:var(--cp-danger); }
.t-info { background:var(--cp-info-bg); color:var(--cp-info); }
.t-muted { background:var(--cp-line-soft); color:var(--cp-muted); }
</style>
```

- [ ] **Step 3:** 实现 CpSectionHeader.vue（标题 14.5px/800/ink + 右侧 `extra` slot，样式抄 mockup `.card-head`/`.sec-head`）。
- [ ] **Step 4:** Run `npm run test -- CpTag` → PASS。Commit：`feat(ui): CpTag 状态 pill + CpSectionHeader`。

---

### Task 7: CpPageShell + CpEmpty

**Files:**
- Create: `cp6.web/src/components/templates/CpPageShell.vue`、`cp6.web/src/components/base/CpEmpty.vue`
- Test: `cp6.web/src/components/templates/__tests__/CpPageShell.spec.ts`

**Interfaces:**
- Produces: `CpPageShell` props `{ title: string; count?: number }`，slots：`actions`（右上按钮组）、默认（内容区，纵向 16px 间距 flex）。`CpEmpty` props `{ text?: string }` + slot `action`。

- [ ] **Step 1: 失败测试**（渲染 title/count pill、actions slot 有出口、默认 slot 有出口——三条 it，写法同 Task 6 模式）
- [ ] **Step 2: 实现**

```vue
<!-- CpPageShell：业务页标准壳（设计系统 §9.2）。
     <CpPageShell title="出庫指示一覧" :count="28"><template #actions>…按钮…</template>…内容…</CpPageShell> -->
<script setup lang="ts">
defineProps<{ title: string; count?: number }>()
</script>
<template>
  <div class="cp-page">
    <div class="cp-page-head">
      <h1>{{ title }}<span v-if="count !== undefined" class="cnt num">{{ count }}</span></h1>
      <div class="cp-page-actions"><slot name="actions" /></div>
    </div>
    <slot />
  </div>
</template>
<style scoped>
.cp-page { display:flex; flex-direction:column; gap:16px; max-width:1420px; margin:0 auto; }
.cp-page-head { display:flex; align-items:center; justify-content:space-between; gap:14px; }
.cp-page-head h1 { font-size:var(--cp-fs-2xl); font-weight:800; color:var(--cp-ink);
  display:flex; align-items:center; gap:11px; }
.cp-page-head .cnt { font-size:12px; font-weight:800; color:var(--cp-brand-deep);
  background:var(--cp-brand-bg); border-radius:999px; padding:3px 11px; }
.cp-page-actions { display:flex; gap:10px; }
</style>
```

CpEmpty：居中 muted 图标+文案+`action` slot。

- [ ] **Step 3:** 测试 PASS → Commit：`feat(ui): CpPageShell 页面壳 + CpEmpty 空状态`。

---

### Task 8: CpStatusStrip + CpFilterBar

**Files:**
- Create: `cp6.web/src/components/templates/CpStatusStrip.vue`、`CpFilterBar.vue`
- Test: `cp6.web/src/components/templates/__tests__/CpStatusStrip.spec.ts`、`CpFilterBar.spec.ts`

**Interfaces:**
- Produces:
  - `CpStatusStrip` props `{ items: { key:string; label:string; count:number; tone?:string }[]; modelValue:string }`，emit `update:modelValue`（点击 pill 卡切换筛选）
  - `CpFilterBar` props `{ fields: FilterField[]; modelValue: Record<string,unknown> }`，emit `update:modelValue`、`search`、`reset`；`FilterField = { key:string; label:string; type:'text'|'select'|'daterange'; options?:{label:string;value:unknown}[]; placeholder?:string }`，超过 4 个字段自动折叠出「展开更多」。类型 export 自 `CpFilterBar.vue`

- [ ] **Step 1:** 失败测试：Strip 渲染 N 张卡/点击 emit key/active 卡有 `on` class；FilterBar 渲染字段/点查询 emit `search`/点重置清空 model 并 emit `reset`/第 5 个字段默认隐藏。
- [ ] **Step 2:** 实现。视觉抄 mockup-final-b `.stat-strip/.ss` 与 `.filter/.fld`（色值换变量）；内部控件用 el-input/el-select/el-date-picker（观感由 overrides 保证）。
- [ ] **Step 3:** 测试 PASS → Commit：`feat(ui): CpStatusStrip 状态速览条 + CpFilterBar 查询区`。

---

### Task 9: CpListPage（核心模板）+ CpStatCard 抽取

**Files:**
- Create: `cp6.web/src/components/templates/CpListPage.vue`
- Create: `cp6.web/src/components/templates/CpStatCard.vue`（把 Task 3 的 KpiCard 逻辑搬来，dashboard 引用改指向这里并删除 KpiCard.vue）
- Test: `cp6.web/src/components/templates/__tests__/CpListPage.spec.ts`

**Interfaces:**
- Consumes: CpPageShell/CpStatusStrip/CpFilterBar/CpTag/CpEmpty
- Produces: `CpListPage`——**这是 130+ 查询页的目标模板**：

```ts
type ListColumn = { prop:string; label:string; width?:number; align?:'left'|'right'|'center';
  kind?:'text'|'num'|'mono'|'tag'|'date' }   // kind 控制格式化：num→.num 右对齐；mono→单号样式；tag→CpTag(status=值)
type ListFetch = (q: { page:number; size:number; filters:Record<string,unknown>; statusKey?:string })
  => Promise<{ rows:unknown[]; total:number }>
// props: { columns:ListColumn[]; fetch:ListFetch; searchFields?:FilterField[];
//          statusTabs?:{key,label,count,tone}[]; selectable?:boolean; rowKey?:string }
// slots: toolbar（批量操作区）、col-<prop>（自定义列）、expand
// emits: selection-change(rows)
// 行为：mounted 自动 fetch；search/reset/翻页/切状态卡重新 fetch；fetch 期间 el-table v-loading；
//       fetch reject → ElMessage.error(错误消息) 且表格保留旧数据；rows 空 → CpEmpty。
```

- [ ] **Step 1: 失败测试**（fetch mock 驱动，覆盖行为契约的 6 条）：

```ts
// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import CpListPage from '../CpListPage.vue'
const cols = [{ prop: 'no', label: '单号', kind: 'mono' }, { prop: 'qty', label: '数量', kind: 'num' }]
function makeFetch(rows = [{ no: 'SHP-1', qty: 1000 }]) {
  return vi.fn().mockResolvedValue({ rows, total: rows.length })
}
describe('CpListPage', () => {
  it('mounted 自动调用 fetch(page=1)', async () => {
    const f = makeFetch(); mount(CpListPage, { props: { columns: cols, fetch: f } })
    await flushPromises()
    expect(f).toHaveBeenCalledWith(expect.objectContaining({ page: 1 }))
  })
  it('渲染行数据与列格式', async () => {
    const w = mount(CpListPage, { props: { columns: cols, fetch: makeFetch() } })
    await flushPromises()
    expect(w.text()).toContain('SHP-1')
  })
  it('fetch 空结果显示 CpEmpty', async () => {
    const w = mount(CpListPage, { props: { columns: cols, fetch: makeFetch([]) } })
    await flushPromises()
    expect(w.findComponent({ name: 'CpEmpty' }).exists()).toBe(true)
  })
  it('fetch 失败保留 UI 不崩', async () => {
    const f = vi.fn().mockRejectedValue(new Error('boom'))
    const w = mount(CpListPage, { props: { columns: cols, fetch: f } })
    await flushPromises()
    expect(w.find('.cp-list').exists()).toBe(true)
  })
  it('切换状态卡以 statusKey 重新 fetch', async () => {
    const f = makeFetch()
    const w = mount(CpListPage, { props: { columns: cols, fetch: f,
      statusTabs: [{ key: 'all', label: '全部', count: 1 }, { key: 'wait', label: '未出库', count: 1 }] } })
    await flushPromises()
    await w.findAll('.ss')[1].trigger('click'); await flushPromises()
    expect(f).toHaveBeenLastCalledWith(expect.objectContaining({ statusKey: 'wait' }))
  })
  it('selection-change 透传', async () => {
    const w = mount(CpListPage, { props: { columns: cols, fetch: makeFetch(), selectable: true } })
    await flushPromises()
    w.findComponent({ name: 'ElTable' }).vm.$emit('selection-change', [{ no: 'SHP-1' }])
    expect(w.emitted('selection-change')![0][0]).toHaveLength(1)
  })
})
```

- [ ] **Step 2: 实现 CpListPage.vue**——组合结构：`CpStatusStrip?` → `CpFilterBar?` → 表格卡（toolbar slot + el-table + el-pagination）。内部状态 `page/size/filters/statusKey/rows/total/loading`；统一 `load()` 调 `props.fetch`，catch 里 `ElMessage.error((e as Error).message)`。列渲染按 `kind` 分支（`tag`→`<CpTag :status="row[prop]" />`；`mono`→`class="cp-mono"`；`num`→`class="num" align=right`）；`col-<prop>` slot 存在时优先。视觉值抄 mockup-final-b `.tcard/.toolbar/.pager`。
- [ ] **Step 3:** 抽 CpStatCard：KpiCard.vue 内容平移 + 文件头注释，`DashboardView` import 路径改 `@/components/templates/CpStatCard.vue`，删 `views/dashboard/components/KpiCard.vue`。
- [ ] **Step 4:** Run `npm run test` → 全 PASS；`npm run type-check` 0 error。
- [ ] **Step 5:** Commit：`feat(ui): CpListPage 查询页模板 + CpStatCard 抽取`。

---

### Task 10: CpFormDialog + CpDetailPanel

**Files:**
- Create: `cp6.web/src/components/templates/CpFormDialog.vue`、`CpDetailPanel.vue`
- Test: `cp6.web/src/components/templates/__tests__/CpFormDialog.spec.ts`

**Interfaces:**
- Produces:
  - `CpFormDialog` props `{ modelValue:boolean; title:string; fields?:FormField[]; form:Record<string,unknown>; rules?:FormRules; submit:(form)=>Promise<void>; width?:string }`，emits `update:modelValue`、`saved`；`FormField = { key,label,type:'text'|'number'|'select'|'date'|'textarea',options?,required? }`；默认 slot 替代 fields 自组复杂表单。行为：提交前 `elFormRef.validate()`；`submit` resolve → emit saved + 关闭；reject → ElMessage.error 且不关闭；提交期间确认钮 loading。
  - `CpDetailPanel` props `{ items: { label:string; value:unknown; kind?:'text'|'num'|'mono'|'tag' }[]; cols?:number }`（描述栅格，默认 2 列）
- [ ] **Step 1:** 失败测试：打开渲染 title/fields；必填空提交不触发 submit；submit resolve 后 emit saved + update:modelValue(false)；reject 不关闭。
- [ ] **Step 2:** 实现（el-dialog + el-form 组合，视觉由 overrides 保证，footer = ghost 取消 + primary 确认）。
- [ ] **Step 3:** 测试 PASS → Commit：`feat(ui): CpFormDialog 表单弹窗 + CpDetailPanel 详情面板`。

---

### Task 11: WMS 试点——出库指示一览迁移到 CpListPage

**Files:**
- Modify: `cp6.web/src/views/wms/` 下出库指示一览页（执行时 `grep -rn "出庫指示\|出库指示" src/views/wms src/router` 定位精确文件）

- [ ] **Step 1:** 读原页面，列出：API 调用、列定义、搜索字段、批量操作、行操作、权限指令（v-permission）——迁移中一项不许丢。
- [ ] **Step 2:** 用 CpPageShell + CpListPage 重写模板：columns 数组（单号 kind=mono、数量 kind=num、状态 kind=tag）、searchFields、statusTabs（若原页有状态筛选）、`fetch` 包装原 API、toolbar slot 放批量按钮、col-操作 slot 放行按钮。scoped style 只剩布局（若还有视觉规则=模板缺口，回补模板而不是页内写死）。
- [ ] **Step 3:** gstack 真栈验收：查询/翻页/状态切换/多选/批量按钮/行跳转全走一遍，`console` 无错误，截图对照 mockup-final-b。
- [ ] **Step 4:** Commit：`refactor(ui): WMS 出库指示一览迁移 CpListPage（模板首个真实消费者）`。
- [ ] **Step 5:** 试点复盘：模板 API 不够用的地方（列合并、行内编辑等）记入 `docs/superpowers/plans/2026-07-04-ui-restyle.md` 末尾「模板缺口」清单，扩 CpListPage 后再进 Milestone C。**Milestone B 在此合并回 main。**

---

# Milestone C：分模块批量迁移（每模块一分支一 PR）

### Task 12: 迁移手册（对每个模块重复执行）

模块顺序与规模：**WMS(39) → ERP(42) → OA(25) → FIN(22) → PMS(22) → MES(18) → PUR(8)/WF(6)/PLAN(2)/PLATFORM(5)/SPACE(11)**。每模块：

- [ ] **Step 1:** `git checkout -b feat/ui-migrate-<module>`（从最新 main）
- [ ] **Step 2:** 盘点：`grep -ln "el-table" src/views/<module>/*.vue` 分类——查询列表页→CpListPage；表单弹窗→CpFormDialog；详情页→CpDetailPanel；特殊页（画布/3D/设计器）→只换 token 与基础件
- [ ] **Step 3:** 逐页迁移（Task 11 的流程与验收标准原样适用）；每 3~5 页一个 commit
- [ ] **Step 4:** 硬编码清零：`grep -rn "#[0-9a-fA-F]\{3,8\}" src/views/<module> --include=*.vue`，除图表系列色（§2.5 豁免，行尾加 `/* cp-chart-color */` 标记）外全部替换为 token
- [ ] **Step 5:** 模块回归：`npm run type-check && npm run test && npm run build`；gstack 走查该模块全部菜单入口截图
- [ ] **Step 6:** PR：标题 `refactor(ui): <module> 模块迁移到 CP6 Design System`，附截图；合并后删分支
- [ ] **Step 7:** 全部模块完成后收尾 commit：删除 `VolTable.vue`、`VolForm.vue`（确认 `grep -rln "VolTable\|VolForm" src/` 为 0）；在 `cp6.web/README` 或 CLAUDE.md 加一行「新页面必须使用 components/templates，视觉值必须用 --cp- token」

---

## 模板缺口（试点与迁移中回填）

（初始为空；发现 CpListPage/CpFormDialog 覆盖不了的形态时记录于此并先扩模板）

## Self-Review 记录

- 规范覆盖：设计系统 §1~§11 全部有对应任务（§2~§7→Task 1；§8 图标→Task 2/3 沿用 EP 图标；§9.1→Task 6/9/10 + overrides；§9.2→Task 7~10；§10→Task 1；§11 命名→各任务文件路径；§12 暗色→Task 1 Step 2 占位；§13→Milestone 结构本身）。CpInput/CpSelect/CpDatePicker 薄封装按 YAGNI 暂缓：全局 overrides 已覆盖其视觉，待模板出现重复默认值需求时再引入（此为对设计系统 §9.1 的显式偏差，记录在案）。
- 占位符扫描：无 TBD/TODO；「抄 mockup 对应 class」均指向已入库的具体文件与类名，属引用而非占位。
- 类型一致性：FilterField 定义于 CpFilterBar 并被 CpListPage/CpFormDialog 复用；ListColumn.kind 与 CpDetailPanel.items.kind 枚举一致（text/num/mono/tag）。
