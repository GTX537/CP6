### Task C-T2: FormDetail 堆叠 + 审批操作钉底栏 + 对话框全屏化

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/FormDetail.vue`
- Modify: `cp6.web/src/views/oa/inbox/TransferDialog.vue`
- Modify: `cp6.web/src/views/oa/inbox/SendBackDialog.vue`

**Interfaces:**
- Consumes: `useBreakpoint()`。
- Produces: 无对外契约（纯视图）。签核记录=右栏 FlowTimeline 内联（C6）→ 堆叠即「全屏化」落点；Transfer/SendBack 对话框移动端 fullscreen。

- [ ] **Step 1: FormDetail.vue 左右列堆叠** — `el-col` 换响应式栅格（el-col 原生 xs/sm 属性，≥768px 走 sm 值与现状 span 等价）：

```html
        <el-col :xs="24" :sm="14" class="detail-left">
```

```html
        <el-col :xs="24" :sm="10" class="detail-right">
```

（原 `:span="14"` / `:span="10"` 删除。）

- [ ] **Step 2: 操作钉底栏 + 样式** — `.action-bar` 模板不动（`v-if="myTaskId"` 保留）；`<style scoped>` 尾部追加：

```css
@media (max-width: 767px) {
  .detail-left {
    border-right: none;
    padding-right: 0;
  }

  .detail-right {
    max-height: none;
    padding-left: 0;
    margin-top: 16px;
    overflow-y: visible;
  }

  /* 审批操作钉底栏（安全区适配，spec §4） */
  .action-bar {
    position: sticky;
    bottom: 0;
    z-index: 5;
    flex-wrap: wrap;
    background: var(--cp-card);
    box-shadow: var(--cp-shadow-up);
    margin: 16px -16px 0;
    padding: 10px 12px calc(10px + env(safe-area-inset-bottom));
  }

  .action-bar .el-input {
    width: 100% !important;   /* 覆盖行内 280px（同 OtdReportView 既有 !important 口径） */
  }
}
```

（`margin: 0 -16px` 抵消 `el-drawer` body 内边距使钉底栏贴满；FormDetail 挂在 InboxView 抽屉内，抽屉移动端已 100% 全屏——C-T1。）

- [ ] **Step 3: TransferDialog.vue / SendBackDialog.vue 全屏化** — 两文件同改：

脚本加：

```ts
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()
```

`TransferDialog.vue` 的 `<el-dialog ... width="440px">` 改：

```html
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.transfer.title')"
    :width="isMobile ? '100vw' : '440px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
```

`SendBackDialog.vue` 的 `<el-dialog ... width="440px">`（:5，实读现值 440px）同法改：

```html
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.detail.sendback')"
    :width="isMobile ? '100vw' : '440px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
```

- [ ] **Step 4: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): C-T2 详情页移动端堆叠+审批操作钉底栏(安全区)+转交/退回对话框全屏化"
```

---


---
## 附: R6前端现状+冲突C6
### R6 前端现状

- 三页：`InboxView.vue`（壳：header + `el-aside 200px` 菜单 + `el-drawer size=60%` 详情）/ `InboxPending.vue`（el-table + batch-bar）/ `FormDetail.vue`（`el-col :span=14/10` 左表单右时间线 + `.action-bar` 底部按钮排）。**无独立「Sign Records 弹窗」**——签核记录 = 右栏 `FlowTimeline` 内联（移动端处理为纵向堆叠 + Transfer/SendBack 对话框全屏化）。
- 移动端先例：`useBreakpoint()`（`cp6.web/src/composables/useBreakpoint.ts`，`MOBILE_MAX=767`）+ `v-if="!isMobile"` 表格 / `v-else .mobile-list` 卡片（`StockDwellView.vue:116-170` + `:402-458` CSS）+ 尾部 `@media (max-width: 767px)`。断点 `<768px` = `max-width: 767px`，与既有约定一致。
- 设置页 `InboxSettings.vue` 已有 notify tab（扁平开关堆，:46-73）→ 替换为矩阵卡片。
- i18n seed：`CP6.WebApi/Seed/I18nOa*ScreenSeed.cs`（`Sys_Lang[] Items`，五列 `ZhCN/ZhTW/En/Ja/Ko`）；Program.cs concat 链 :1813-1819，尾部 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重；新 seed 插 :1819 之后。
| C6 | spec §4「Sign Records 弹窗全屏化」 vs 无独立签核弹窗 | 对应现状落点 = FlowTimeline 堆叠 + TransferDialog/SendBackDialog 移动端全屏（`width 100vw`） |
