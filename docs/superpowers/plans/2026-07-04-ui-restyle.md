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

- [ ] **Step 2: 实现 CpListPage.vue**——组合结构：`CpStatusStrip?` → `CpFilterBar?` → 表格卡（toolbar slot + el-table + el-pagination）。内部状态 `page/size/filters/statusKey/rows/total/loading`；统一 `load()` 调 `props.fetch`，catch 里 `ElMessage.error((e as Error)?.message ?? String(e))`（原弱形式 `(e as Error).message` 已在终审 hardening commit 修复为与 CpFormDialog 一致的硬化形式；Milestone C brief 不得再抄弱形式）。列渲染按 `kind` 分支（`tag`→`<CpTag :status="row[prop]" />`；`mono`→`class="cp-mono"`；`num`→`class="num" align=right`）；`col-<prop>` slot 存在时优先。视觉值抄 mockup-final-b `.tcard/.toolbar/.pager`。
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

### WMS 迁移批次1 复盘（编号接续 Task 11 试点，从 #9 起）

批次1（Placeholder/InboundOrderList/Expiry/CrossDock/WarehouseList/StockTakeList）迁移完成，功能零丢失、真栈验收通过（5 个已路由页；WmsPlaceholderView 为无路由孤儿代码，仅 token 化）。以下缺口均用逃生舱/旧机制代偿保功能，按批次规则「不改模板组件本体」记录待后续扩契约：

9. **CpFilterBar `daterange` 无 `value-format` 透传 → 返回 `Date` 对象**（Minor）—— ✅ 已实现（契约扩展二轮 commit）：FilterField 增 `valueFormat?` 透传 el-date-picker（date/daterange 通用，opt-in 无默认值以免静默改变既有消费者返回类型）；InboundOrderList 已改独立 date 字段直接拿字符串，ymd() 代偿已删。
   - 现象：InboundOrderList 原「予定入荷 从/至」两个 `el-date-picker value-format="YYYY-MM-DD"`（返回字符串）。CpFilterBar 无单日 `date` type，只能合并为 `daterange`；而 CpFilterBar 的 `el-date-picker` 未设 `value-format`，返回 `[Date, Date]`，与后端 `arrivalFrom/arrivalTo: string` 契约不符。
   - 代偿：fetch 包装内 `ymd()` 本地时区格式化（避免 `toISOString` UTC 偏移）为 `YYYY-MM-DD`。
   - 建议：FilterField 增 `valueFormat?`（透传 el-date-picker），或 CpFilterBar 对 daterange 默认 `value-format="YYYY-MM-DD"`；并补单日 `date` type。

10. **CpFilterBar 无 `number` 字段类型（min/max/step）**（Minor）—— ✅ 已实现（契约扩展二轮 commit）：FilterField 增 `type:'number'` + `min/max/step` 透传 el-input-number；Expiry「N日以内」已改 number(1..365)（spinner 恢复；字段初值空，fetch 侧缺省 30 语义保留）。
    - 现象：Expiry 原「N 日以内」为 `el-input-number :min="1" :max="365"`。CpFilterBar 仅 text/select/daterange。
    - 代偿：用 `text` + `placeholder="30"`；fetch 内 `Number()` 解析并 clamp 到 1..365，缺省 30。丢失 spinner 与初值回填（字段初值空，查询按 30）。
    - 建议：FilterField 增 `type:'number'` + `min/max/step`。

11. **CpListPage 强制分页，无法关闭 → 全量勾选跨页失效**（Minor）—— ✅ 已实现（契约扩展二轮 commit）：CpListPage 增 `paginated?: boolean`（默认 true）；false 时隐藏 pager、page 锁 1、fetch 收 size=UNPAGED_SIZE(1000)；Expiry 已挂 `:paginated="false"`，跨全量勾选一括廃棄恢复。
    - 现象：Expiry 原为单表 `max-height` 滚动（无分页），`type="selection"` 跨全量勾选后一括廃棄。CpListPage 始终渲染 pager 且无 `:paginated=false` 开关，勾选降为「当页范围」。
    - 影响：数据完整性不受影响（概览指标在 fetch 包装内按全量结果计算）；仅跨页批量勾选丢失。廃棄为低频操作，可接受。
    - 建议：CpListPage 增 `paginated?: boolean`（false 时隐藏 pager、fetch 传大 size）或 `pageSizes` 定制。

12. **CpListPage 无命令式 `reload()` / 无外部刷新触发**（Important，影响 3 页）—— ✅ 已实现（契约扩展二轮 commit）：`defineExpose({ reload })`（仅此一项），reload() 保留 filters/page/statusKey 重查；Expiry/CrossDock/Warehouse 三页 `reloadKey` + `:key` 重挂载方案已删，搜索/翻页上下文在变更后不再丢失（删空当前页不自动收拢页码，与原页行为一致，记录在案）。
    - 现象：CpListPage 仅在 mounted/search/reset/翻页/切卡内部 fetch，不 watch 任何外部信号，也未 `defineExpose`。而 Expiry(廃棄)、CrossDock(新建/実行/取消)、Warehouse(新建/編集/削除) 均需「页内 in-place 变更后刷新列表」（原页调用 `reload()`）。
    - 代偿：父级持 `reloadKey` ref，`:key="reloadKey"` 绑定 CpListPage，变更成功后 `reloadKey++` 强制重挂载重查。**副作用**：重挂载会把 CpListPage 内部 `filters`（重置为 `{}`）与 `page`（重置为 1）清空 → 用户当前搜索/翻页上下文丢失（Warehouse 编辑后列表回到未筛选首页最明显）。数据正确性优先，故采用；记为已知降级。
    - 建议：CpListPage `defineExpose({ reload })`（父级 `ref` 命令式刷新，保留 filters/page），或增 `refreshKey?: number` prop 内部 `watch` 触发 `load()`（不重置状态）。这是本批最值得回填的契约扩展。

13. **FilterField 无单日期 `date` 类型**（Minor，批次1评审补记）—— ✅ 已实现（契约扩展二轮 commit）：FilterField 增 `type:'date'`（单日 el-date-picker 透传，配 valueFormat 用）；InboundOrderList「予定入荷 从/至」已拆回两个独立字段，单侧开区间查询恢复。
    - 现象：InboundOrderList 原「予定入荷 从/至」为两个**独立**单日期 `el-date-picker`（可只填一侧做开区间查询）。FilterField 仅 text/select/daterange，被并成一个 daterange —— 单侧开区间查询能力丢失（daterange 必须成对选起止）。
    - 代偿：合并为 daterange（documented compensation，见批次1报告盘点）；fetch 内拆回 arrivalFrom/arrivalTo。
    - 建议：FilterField 增 `type:'date'`（单日 el-date-picker 透传），恢复独立起/止字段形态。

（注：所有页 CpFilterBar `expand/collapse` 仍留组件内中文默认「展开更多/收起」、CpEmpty 空态仍为中文「暂无数据」——沿用 Task 11 试点约定，属 follow-up #6，非本批新增。）

### WMS 迁移批次2 复盘（编号接续，从 #14 起）

批次2（LotTrace/Replenish/InboundReceipt/Slotting/ProductionInbound）迁移完成，功能零丢失、真栈验收通过（5 页全路由直达）。分类：Replenish=查询列表页（CpListPage+2×CpFormDialog）；Slotting=一覧/明细双态同组件（CpListPage v-show 常挂 + CpDetailPanel + 分析 CpFormDialog）；LotTrace/ProductionInbound/InboundReceipt=非表格特殊页（token 化 + 基础件替换）。以下为唯一新增缺口：

14. **CpDetailPanel 的 tag 值无 tone 映射（不支持 ListColumn.map 式码值→tone）**（Minor）
    - 现象：Slotting 明细「基本情報」用 CpDetailPanel 铺 6 项；其中「状態」是数字码需 码→文案 + 码→tone 两步映射。CpDetailPanel 的 `kind:'tag'` 与 CpTag 一样只认「已是状态词」的原始值（走 STATUS_TONE，日文标签命不中→muted），无 `map?: (val)=>{label,tone?}`（对照 ListColumn 已有 map）。
    - 代偿：把带 tone 的状態 CpTag 放到明细卡 `CpSectionHeader` 的 `#extra` 槽（原页状态也在标题行），CpDetailPanel 只铺纯文本/数字项——功能与视觉均保全，未丢 tone。
    - 建议：CpDetailPanel.items 增可选 `tone?: Tone`（或 `map`），与 ListColumn.map 对齐，让详情面板码值状态也能声明式着色。

（注：CpListPage 的 el-pagination 触发 element-plus 内部 `[el-pagination] small … deprecated` 警告——EP 库内部 size-changer 自带，warning 级、非本批引入、CpListPage 本体未改，记录在案非阻塞。InboundReceipt 为全页可编辑录入页，原本即已 token 化合规（el-card overrides + var() 令牌，零禁用硬编码、无状态 pill 可替换），本批审计后维持原结构。）

### WMS 迁移批次3 复盘（编号接续，从 #15 起）

批次3（InboundOrder/SampleStock/Pallet/LocationList/PaperRoll）迁移完成，功能零丢失、真栈验收通过（5 页全路由直达）。分类：SampleStock/Pallet/PaperRoll=查询列表页（CpListPage + 2~3×CpFormDialog default slot）；InboundOrder=全页可编辑录入页（token 化 + 状態 el-tag→CpTag）；LocationList=master-detail 双表联动特殊页（token 化 + CpTag，保留双栏 + el-dialog）。以下为唯一新增缺口：

15. **CpFilterBar 无 boolean/checkbox 字段类型**（Minor）
    - 现象：SampleStock 原「未返却(超過)」为 el-checkbox 查询条件（`overdueOnly: boolean`）。FilterField 仅 text/select/date/daterange/number，无法渲染复选。
    - 代偿：`overdueOnly` 提为页级 `ref`，放 CpListPage **toolbar slot** 复选，`fetchList` 闭包读取 + `@change` 触发 `listRef.reload()`——控件与功能完整保全（未降级为 select 下拉）。
    - 建议：FilterField 增 `type:'boolean'`（透传 el-checkbox / el-switch），或 CpFilterBar 增字段级插槽，让布尔查询条件声明式进查询区。

（注：LocationList 为 master-detail 双表联动，CpListPage 单表卡形态不表达，按「特殊页不强套模板」保留双栏 el-table + 编辑 el-dialog，未计入模板缺口——与批次2 LotTrace/InboundReceipt 处置一致。）

### WMS 迁移批次4 复盘（编号接续，从 #16 起）

批次4（WcsTask/StockTake/IotMonitor/ReportCenter/Carrier）迁移完成，功能零丢失、真栈验收通过（5 页全路由直达）。分类：WcsTask/Carrier=查询列表页（CpPageShell+CpListPage+CpFilterBar+3×CpFormDialog，码值状態列 kind:'tag'+map，客户端分页）；StockTake=棚卸明細/编辑特殊页（token 化：el-tag→CpTag+tone、内联 #aaa/el-var→--cp-* token、保留 el-descriptions+可编辑 el-table+el-affix action-bar）；IotMonitor=监控仪表盘特殊页（token 化 + CpTag/CpEmpty 基础件替换，保留 30s 轮询/アラート/行クリック履歴，新建/投入弹窗迁 CpFormDialog）；ReportCenter=帳票中心特殊页（token 化 + CpTag/CpEmpty，保留動的表单/多结果表/CSV）。真栈：WcsTask 5 行 + 状態/優先度 pill + 新建 CpFormDialog；Carrier 空态（Total 0）+ 新建必填标记；IotMonitor 1 alert/3 sensors + 投入/履歴弹窗；ReportCenter 在庫月報 16 行 + 件数 pill；StockTake 无数据故直达验证 default 渲染（計画/全棚卸 pill + action-bar）。以下为唯一新增缺口：

16. **CpListPage 无 `@row-click` 透传（整行点击事件）**（Minor）
    - 现象：Carrier 原表格 `@row-click` 整行点击打开「イベント履歴」详情弹窗。CpListPage 的 el-table 未透传 row-click，业务侧拿不到行点击事件。
    - 代偿：详情能力下沉到操作列常驻「詳細」link 按钮（`openDetail(row)`），功能与 timeline 弹窗完整保全；仅丢失「整行可点」这一 UX affordance（highlight-current-row 仍默认开启，视觉高亮不受影响）。
    - 建议：CpListPage 增 `@row-click(row)` 透传 el-table 同名事件，恢复整行点击进详情的交互。

（注：StockTake/IotMonitor/ReportCenter 三页为特殊页，按「非表格特殊页只做 token 化 + 基础件替换，不强套模板」处置，未计入模板缺口——与批次2/3 处置一致。CpFormDialog 采用 `label-position:top`（设计系统标准），与原页 label-width 左标签的差异属既定契约，非缺口。Carrier/StockTake/stock-take-list 三处后端无种子数据，Carrier 验证空态 + 新建弹窗、StockTake 直达验证 default 渲染，已在报告注明。）

### WMS 迁移批次7 复盘（编号接续，从 #17 起）

批次7（BridgeHealth/OutboundRouting/MaterialShortage/WmsDashboard/InkLot）迁移完成，功能零丢失、真栈验收通过（5 页全路由直达）。分类：OutboundRouting/MaterialShortage=查询列表页（CpPageShell+CpListPage，码值列 map，前者 paginated=false、后者服务端分页，复合 create/action ダイアログ保持 el-dialog）；BridgeHealth=监控仪表盘特殊页（KPI×3→CpStatCard、パネルヘッダ→CpSectionHeader、状態 el-tag→CpTag，保留 30s ポーリング/timer cleanup、el-progress/el-table）；WmsDashboard=仪表盘特殊页（KPI×8→CpStatCard、カードヘッダ→CpSectionHeader、状態/TXN el-tag→CpTag，保留 SignalR リアルタイム/棒グラフ/タイムライン/明細テーブル；棒グラフ系列色→--cp-ok/danger/muted token）；InkLot=タブ式ワークベンチ特殊页（2 リスト+4 ダイアログ、tabs は模板契約外——token 化 + 状態 el-tag→CpTag、expiry 色→--cp-danger/warn，el-tabs/el-form/el-table/dialogs 保持）。真栈：BridgeHealth KPI(0.0%/0/0)+2 パネル No Data；OutboundRouting count 0 空态+新規ルール ダイアログ+プレビューカード；MaterialShortage KPI(未対応 0)+検索/クリア reload+Total 0 分页；WmsDashboard KPI×8+未接続 pill+トレンド/倉庫別/アラート表；InkLot 2 タブ切替+検索フォーム No Data。console 无新 error（WmsDashboard 的 SignalR CSRF 403 negotiate 失败=環境既有基础设施问题，SignalR コード原様保持未改，非本批引入，未接続 pill 正确反映）。以下为唯一新增缺口：

17. **CpListPage / CpFilterBar 无初始 filter 值（无法 seed 默认查询条件）**（Minor）
    - 现象：MaterialShortage 原 `query.status` 初期値 = 'OPEN'（欠品トリアージのため既定で未対応のみ表示）、resetQuery も 'OPEN' に戻す。CpFilterBar 的 filters 内部初始化为 `{}`，各字段起始 undefined，无 prop 可 seed 初始值。
    - 代偿：`fetchList` 内 `status = filters.status === undefined ? 'OPEN' : filters.status`——初回/リセット(undefined)→OPEN、''→全件、明示選択はそのまま。功能等价保全（初期表示=未対応、リセット=未対応、全件は ALL 選択で到達）；唯一の齟齬は初回に status セレクトが空表示（実データは OPEN 絞込）——cosmetic、記录在案。
    - 建议：CpListPage 增 `initialFilters?: Record<string,unknown>`（或 CpFilterBar `defaultValue`）透传初始 filters，恢复默认查询条件的可视回填。

（注：MaterialShortage 采用 CpListPage 标准分页，page-sizes 从原 [50,100,200]/默认 50 变为模板 [20,50,100]/默认 20——属既定模板契约，非缺口。BridgeHealth/WmsDashboard 系监控·仪表盘特殊页，InkLot 系 tabs 工作台特殊页，均按「非表格特殊页只做 token 化 + 基础件替换，不强套模板」处置，未计入模板缺口——与批次2/3/4 处置一致。WmsDashboard SignalR CSRF negotiate 403 为測試環境基础设施既有问题，非本批引入。）

### WMS 迁移批次8 复盘（模块收尾，无新增模板缺口）

批次8（模块收尾）迁移完成，功能零丢失、真栈验收通过。本批 = Part A 末两页（模块最大）+ Part B 模块级硬编码清扫 + Part C 累积清理。分类与处置：

- **KitView**（キット，420 行）= el-tabs 双模块（マスタ / 組立指示）× list+detail 双态。list 态迁 CpListPage（状態/種別/ON-OFF=kind:'tag'+map；数量/実行日時=col slot；新規=toolbar slot #15），list 用 `v-if` 随 mode 切换卸载 → 戻る时重挂 auto-fetch（RmaView 先例のフレッシュネス）；detail 态 = 新規/閲覧兼用の編集フォーム + BOM 編集テーブル（特殊エディタ領域，保留 el-card/el-form/el-table/el-affix），ヘッダ状態を CpTag 化、action-bar/txn-list を token 化。組立指示の kitSku ドロップダウンはマスタ一覧と別ソース（`activeMasters` を onMounted＋マスタ変更後にロード）で疎結合化。el-tabs は模板契約外の特殊ナビ——CpPageShell は被せず、原页无页头を踏襲。
- **StockDwellView**（在庫滞留レポート，456 行）= 仪表盘/分析特殊页（KPI×4 + 滞留バケット横棒グラフ + 明細テーブル + モバイル表示）。按「非表格特殊页只做 token 化 + 基础件替换」处置：el-tag→CpTag、el-empty→CpEmpty、内联全色値（#303133/#606266/#409eff/#f56c6c/#e6a23c/#67c23a/#d93026/#eef2f7/#ebeef5）→ `--cp-ink/muted/info/danger/warn/ok/line/line-soft` token 化。滞留バケット 4 色は「意味づけ色（新鮮→期限超過）」で設計トークンに 1:1 対応するため §2.5 図表色免除は使わずトークン化（grep が `/* cp-chart-color */` 免除行ゼロ＝完全クリーンで返る）。

**Part B 模块硬编码清扫**：全 `#hex`/`rgba()` 真彩值仅 StockDwell + KitView 内联残留（其余 wms 页 grep 命中均为 `template #default` 正则误报——"defa" 4 位十六进制字符，非色值），已随两页迁移清零。`var(--el-*)` 残留 5 处一并 token 化：InboundReceipt/InboundOrder/Kit 的 action-bar（`--el-bg-color`/`--el-border-color-lighter`→`--cp-card`/`--cp-line-soft`，与 Slotting 统一）、LotTrace 的 `.qty-in/.qty-out`（`--el-color-success`/`--el-color-danger`→`--cp-ok`/`--cp-danger`）。最终 grep：非 `#default` 误报行 = 0。

**Part C 累积清理**（并入 fix commit ②）：① SlottingView 删除死 CSS `.wms-slotting{padding:16px}`（模板根为 CpPageShell，该类无宿主）；`listRef` 从悬空改为接线——新增 `listDirty` 脏标记，onApprove/onCancel 成功后置位，`backToList()` 在返回一覧时命令式 `reload()`（CpListPage 为 `v-show` 常挂不自动重取，故手动刷新，等价 RmaView `v-if` 重挂的フレッシュネス；真栈证实：承認後戻る→`GET /wms/slotting` 触发、一覧显示 SLP2026070001/承認済/admin）。② 删除 detail action-bar 重复「戻る」（保留 header #actions 版）。③ **CrossDock xDockNo 大小写修正**：后端实体 `XDockNo` camelCase 序列化为 `xDockNo`（大写 D），前端行读取用了 `xdockNo`（小写 d）→ 単号列空白 + 実行/取消 POST `/cross-dock/undefined/execute`。修正 CrossDockView 列 prop + onExecute/onCancel 行读取 + `CrossDockOrder` 类型定义三处为 `xDockNo`（create 响应体后端返回的是字面 `{ xdockNo }` 匿名对象，保持不变；search 过滤键 `xdockNo` 走后端大小写不敏感模型绑定，工作正常，未动）。真栈证实：単号列显示 XD2026070001、`POST /api/wms/cross-dock/XD2026070001/execute → 200`（原为 `/undefined/execute → 400`）。

真栈证据（截图存 `.superpowers/sdd/shots/`）：Kit マスタ一覧（Total 2 + ON pill + 開く/削除）/マスタ detail（フォーム+BOM 編集+行追加）/組立一覧（Total 3 + 方向/状態 pill）/組立 detail（下書き=muted・組立=ok の CpTag ヘッダ + 実行/取消 action-bar）；StockDwell（KPI×4 トークン枠色 + バケット横棒 --cp-ok/info + 基準日 CpTag）；CrossDock（単号 XD2026070001 + 実行 200）；Slotting（承認→戻る→一覧リロード=承認済 反映）。console 无本批新 error（SignalR CSRF 403 / EP small·label 弃用警告 = 環境·既有基础设施，非本批引入）。type-check 0 error、`npm run test` 304 全绿。

**本批无新增模板缺口**——两页均落在既有契约（CpListPage toolbar/col slot #15/#16、default-filter-in-fetch #17）与「特殊页 token 化」处置内，未触发新的模板扩展需求。

### ERP批次1 复盘（Milestone C 首模块，编号接续，从 #18 起）

批次1（FxRate/FscChecklist/SheetUnitPrice/BusinessPartnerList/OrderPriceCorrection）迁移完成，功能零丢失、真栈验收通过（5 页均无菜单入口，localStorage.menus 注入 + route 直达）。ERP 页 i18n 用 erp.*/sales.* 键族（非 wms.common.*），filter-labels 与 CpTag 文案均取原页现有键、未臆造。**诚实分类判定**（CpListPage 契约 = onMounted 自动 fetch + 单一 fetch；auto-load 无害且无必须检索条件的照会一覧才模板化，余者按「特殊页 token 化不强套」）：

- **FxRateView**（照会 CRUD 一覧，194 行）= 唯一素直に載る查询列表页 → **CpPageShell + CpListPage + CpFormDialog**。rateDate=kind:'date'、rate 6 桁固定=col-rate slot、subtitle+「基軸:JPY」CpTag=toolbar slot、新規/編集=CpFormDialog default slot（uppercase/input-number precision6 step0.5/textarea 保全）、削除=ElMessageBox+listRef.reload()。filterLabels={ search:erp.fxRate.btn.refresh(「通貨で再読込」語義の既存キー), reset:sales.btn.clear }。
- **FscChecklistView**（206 行）= 検索先行（拠点必須・自動取得なし・FROM≤TO/フォーマット必須のクロス検証・出力フォーマットはアクション引数）→ **token 化**。CpListPage の onMounted 自動 fetch 契約と相反するため強套せず、el-form/el-table/el-pagination/発行フロー原様、状態・件数・発行済 el-tag→CpTag(+tone)。
- **SheetUnitPriceView**（206 行）= Excel アップロード + 登録/参照デュアルモード + 行内選択グリッド + 一括更新（検索駆動の単一 fetch 形態でない）→ **token 化**。件数 el-tag→CpTag、選択ファイル名 #606266→--cp-muted。
- **BusinessPartnerListView**（219 行）= サーバサイド列ソート（@sort-change、CpListPage 未透過＝強套すると機能喪失）+ 属性 FLG×11 + 詳細検索コラプス（分類 1〜10）の構造化検索（CpFilterBar 平坦フィールドでは表現不能）→ **token 化**。ソート/CSV/行選択 原様、状態・件数 el-tag→CpTag(+tone)、FlgIcon 色 #67c23a/#dcdfe6→--cp-ok/--cp-line。WMS LocationList（双表→token 化）同处置。
- **OrderPriceCorrectionView**（237 行）= type=selection 連動の行内編集グリッド（変更後単価/特値/理由）+ 拠点必須・自動取得なし → **token 化**。WMS StockTake（編集テーブル→token 化）同处置。状態・件数・選択中 el-tag→CpTag(+tone)、仮単価警告 #e6a23c→--cp-warn。

真栈证据（截图存 `.superpowers/sdd/shots/erp-*.png`）：FxRate（為替レート管理 +count 0 pill / CpFilterBar / toolbar「基軸:JPY」CpTag / CpEmpty / 新規=CpFormDialog 必須マーク+precision6 input-number）；FscChecklist（拠点必須*+ステータスチェック+出力フォーマット select+発行(0)+「合計 0 件」CpTag）；SheetUnitPrice（基準日/拠点必須+取込区分/操作種別 radio+Excel 選択+全選択/全解除+CpTag）；BusinessPartnerList（FLG×11 グリーンチェック --cp-ok+ソート可列+CSV+「合計 0 件」CpTag、検索実行=0 行空態）；OrderPriceCorrection（数量/金額 FROM-TO number+仮単価+selection+行内編集列+選択行を更新(0)+CpTag）。5 页种子データ無しのため空態レンダリング検証（BP は検索で 0 行確認）；表内ステータス CpTag tone は純関数+FxRate toolbar タグで描画済のため低リスク、記录在案。console 无本批新 error（残存 intlify object-flatten / Vue Router deprecation・No-match=menus 注入リロード由来＝既有基础设施）。type-check 0 error、`npm run test` 46 files/304 全绿（baseline 304 保持）。

本批新增模板缺口 2 项（复盘评审补记：token 化处置本身各有依据并获维持，但反复出现的形态缺口应起票而非仅记录——「0 缺口」判定系本批缺陷）：

18. **CpListPage 无 search-first/lazy 模式**（Minor）—— ✅ 已实现（契约扩展三轮 commit）：`lazy?: boolean`（默认 false），true 时 onMounted 不自动 fetch、空态起步（CpEmpty 可见/total=0 分页器自然惰性），首查由 search/切卡/reload()/分页/排序等显式手势触发，首查成功前不 emit total-change。
    - 现象：现状 onMounted 必自动 fetch；ERP 反复出现「先选必填条件再查询」形态（本批 3/5 页有此形态：FscChecklist/OrderPriceCorrection 拠点必須・自動取得なし，SheetUnitPrice 基準日+拠点必須），与该契约相反，只能整页 token 化放弃模板。
    - 建议契约：`lazy?: boolean`（默认 false），true 时抑制 onMounted(load)，首查仅由显式 search/reload() 触发。
19. **CpListPage 无服务端排序透传**（Minor）—— ✅ 已实现（契约扩展三轮 commit）：ListColumn 增 `sortable?: 'custom'`（仅服务端语义），CpListPage 接 el-table @sort-change：order 规范化 asc/desc、page 重置 1、`sortField?/sortOrder?` 并入 ListFetch query（取消排序两键移除）并 emit sort-change({field,order})；lazy 未加载时排序亦触发首查。
    - 现象：BusinessPartnerList 的 @sort-change 服务端排序是本批该页「decisive token-only reason」却未起票——CpListPage 未透传 el-table 同名事件，强套即丢排序功能。
    - 建议契约：ListColumn 增 `sortable?: 'custom'`，CpListPage 接 el-table @sort-change，把 `sortField?/sortOrder?` 并入 ListFetch query（并 emit sort-change）。

## Self-Review 记录

- 规范覆盖：设计系统 §1~§11 全部有对应任务（§2~§7→Task 1；§8 图标→Task 2/3 沿用 EP 图标；§9.1→Task 6/9/10 + overrides；§9.2→Task 7~10；§10→Task 1；§11 命名→各任务文件路径；§12 暗色→Task 1 Step 2 占位；§13→Milestone 结构本身）。CpInput/CpSelect/CpDatePicker 薄封装按 YAGNI 暂缓：全局 overrides 已覆盖其视觉，待模板出现重复默认值需求时再引入（此为对设计系统 §9.1 的显式偏差，记录在案）。
- 占位符扫描：无 TBD/TODO；「抄 mockup 对应 class」均指向已入库的具体文件与类名，属引用而非占位。
- 类型一致性：FilterField 定义于 CpFilterBar 并被 CpListPage/CpFormDialog 复用；ListColumn.kind 与 CpDetailPanel.items.kind 枚举一致（text/num/mono/tag）。

## 模板缺口（Task 11 试点复盘）

第一位真实消费者 `views/wms/OutboundOrderListView.vue`（出庫指示一覧）迁移完成，功能零丢失、真栈验收通过。CpPageShell + CpListPage + CpFilterBar + CpTag 的组合（本页状态筛选沿用原下拉，无 statusTabs，未用到 CpStatusStrip）足以承载「搜索区 + 表格卡 + 分页 + 行操作 + 头部动作 + 自定义列」这套标准查询页形态，未改动任何已评审模板组件。以下为发现的缺口，建议进 Milestone C 前先扩 CpListPage 契约：

1. **total 不外露 → PageShell 计数 pill 无法接线**（本次影响最大）—— ✅ 已实现（契约扩展 commit）：CpListPage 新增 `@total-change(n)`，仅最新请求（seq 守卫）成功后 emit；试点页已接 CpPageShell `:count`（真栈 37）。
   - 页面需要：mockup 头部「28 单」计数 pill（CpPageShell `:count`）。
   - 模板缺：CpListPage 内部持有 `total` 但既不 emit 也不暴露，父级 CpPageShell 拿不到值，只能省略 `:count`。
   - 建议契约扩展：CpListPage 增 `@total-change(n:number)`（或 `v-model:total` / `#count` 作用域插槽），让业务页把总数回填到 PageShell。本次为遵守「不擅改已评审组件」而省略计数 pill，记为缺口而非页内 hack。

2. **ListColumn / 表格级配置缺字段：`minWidth`、`overflowTooltip`、`fixed`、`highlight-current-row`**—— ✅ 已实现（契约扩展 commit）：ListColumn 增 `minWidth/overflowTooltip/fixed` 透传 el-table-column；CpListPage 增 `highlightCurrentRow`（默认 true）透传 el-table；试点页三项行为已回补。
   - 页面需要：客先名列原为 `min-width:160 + show-overflow-tooltip`（长客户名省略号 + 悬浮全文）；操作列原为 `fixed="right"`（横向滚动时行按钮钉在右侧）；表格原有 `highlight-current-row`（点击行高亮当前行）。
   - 模板缺：ListColumn 仅有 `width`，无最小宽 / 无溢出 tooltip / 无 `fixed`；CpListPage 也不透传表格级 `highlight-current-row`。迁移后这三项行为丢失（undocumented behavior change，本次试点复盘补记）：横向滚动时操作按钮不再钉住、当前行不再高亮。
   - 建议契约扩展：ListColumn 增 `minWidth?: number`、`overflowTooltip?: boolean`、`fixed?: 'left' | 'right'`，透传 el-table-column 的 `min-width` / `show-overflow-tooltip` / `fixed`；CpListPage 增 `highlightCurrentRow?: boolean`（或直接默认开启）透传 el-table。

3. **kind:'tag' 仅认「已是 CpTag 状态词」的原始值，码值状态列仍需自绘插槽**—— ✅ 已实现（契约扩展 commit）：ListColumn 增 `map?: (val,row)=>{label,tone?}`——label 替换任意 kind 的单元格文案，`kind:'tag'` 时按 tone 渲染 CpTag；`col-<prop>` 插槽仍优先。试点页 区分/ステータス 改 `kind:'tag'`+map、優先度 改纯 map（原页即纯文本无 tone），三个插槽已删。
   - 页面需要：区分 / ステータス / 優先度 三列是数字码（0..9），要「码→i18n 文案」+「码→语义 tone」两步映射。
   - 模板缺：kind:'tag' 直接把单元格原值当 CpTag `status`，对数字码既显不出文案也命不中 tone；只能改用 `col-<prop>` 插槽 + `<CpTag :tone>` 自绘（功能已保全，但每个码值列都要重复样板）。
   - 建议契约扩展：ListColumn 增可选 `map?: (val, row) => { label: string; tone?: Tone }`（或 `valueMap` 字典），让码值状态/枚举列声明式着色，免去逐列插槽。

4. **CpTag 文本在窄列内换行**（小视觉缺陷）—— ✅ 已修复（终审 hardening commit）。
   - 现象：区分列 90px / 状态列 110px 下，「ピッキング」被折成「ピッキン グ」，pill 竖向撑高。
   - 修复：CpTag `.cp-tag` 已加 `white-space:nowrap`（纯样式微调，未改契约）。

5. **数据源无 total，ListFetch 只能客户端分页**（记录而非模板缺口——受「不改后端/API 契约」约束）。
   - 现象：`outboundOrderApi.search` 返回扁平数组无总数，而 ListFetch 契约要求 `{ rows, total }`。
   - 适配：fetch 包装以 `pageSize:500` 取一批，`total = 数组长度`，按 page/size 客户端切片（真栈验证 37 条 → 20/页 2 页、翻页正确）。
   - 后续：待后端补 `WmsPaged<T>`（total/page/pageSize 已有类型）后，fetch 包装可直接透传 page/size 做服务端分页；无需改模板。

6. **CpFilterBar 按钮文案硬编码中文 → i18n 回归**（Important，评审补记）—— ⏳ 部分修复（终审 hardening commit）。
   - 页面需要：原页面查询/重置按钮走 `t('wms.common.search')` 等词条，随语言切换；默认语言为 ja。
   - 已做：CpFilterBar 增 `labels?: { search?; reset?; expand?; collapse? }`，CpFormDialog 增 `labels?: { cancel?; confirm? }` + `requiredMessage?`，CpEmpty 沿用既有 `text?`；CpListPage 透传 `filterLabels?` / `emptyText?`。试点页 `OutboundOrderListView.vue` 已就现有词条接线（`search→wms.common.search`、`reset→wms.common.clear`）；`expand/collapse` 无对应 key，保留组件内中文默认（未臆造 Sys_Langs 词条）。
   - 剩余 follow-up（Milestone C）：采用共享词条（如 `common.search/reset/expandMore/collapse`）或组件内直接 `t()`，让 expand/collapse 等也随语言切换、免每页手动传 labels。

7. **共享 `Tone` 类型导出 + STATUS_TONE 强类型**（终审提出，Milestone C 票）—— ✅ 已实现（契约扩展 commit）：CpTag.vue 导出 `export type Tone`，`STATUS_TONE: Record<string, Tone>`；CpStatusStrip.items.tone、CpListPage StatusTab.tone / ListColumn.map、试点页 statusTone() 全部复用，库公开类型不再残留 string tone。
   - 现状：`CpTag` 的 tone 联合类型 `'ok'|'warn'|'danger'|'info'|'muted'` 内联在 props；`STATUS_TONE` 值为宽松 `Record<string,string>`；`ListColumn.map`（缺口#3 提案）、`StatusTab.tone`、页面 `statusTone()` 等各处对 tone 各写各的字面量，无单一事实来源。
   - 建议：从 `CpTag.vue` 导出 `export type Tone = ...`，`STATUS_TONE: Record<string, Tone>`，各消费点复用该类型，编译期约束非法 tone。

8. **`kind:'date'` 死词汇**（终审提出，Milestone C 前实现或删除）—— ✅ 已实现（契约扩展 commit）：date 分支落地为 `String(val).slice(0,10)`（null/undefined 渲染空，与试点页原插槽约定一致）；头注已去「date→暂原样」；试点页 plannedDate 改 `kind:'date'` 并删插槽。
   - 现状：`ListColumn.kind` 声明含 `'date'`，但 CpListPage 列渲染分支未实现 date 格式化（落到 `<template v-else>` 原样输出），头注也写「date→暂原样」。试点页日期列走 `col-plannedDate` 插槽自行 `slice`，`kind:'date'` 从未生效——属死词汇。
   - 建议：Milestone C 前二选一——要么实现 date 格式化分支（按 i18n `d()`/`format`），要么从 `kind` 联合类型删除 `'date'`，避免误导后续迁移页声明无效 kind。

模板本次「够用」的原因：CpListPage 的 `col-<prop>` 具名插槽是逃生舱——凡 kind 表达不了的列（码值 tag、日期截断、行操作按钮、自定义 tone）都能落到插槽里保功能，因此上述缺口都不是阻塞项，而是「省样板 / 补计数」的契约增强。
