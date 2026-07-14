### Task C-T1: 信箱壳 + 三个列表页卡片化 + 筛选抽屉

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/InboxView.vue`
- Modify: `cp6.web/src/views/oa/inbox/InboxPending.vue`
- Modify: `cp6.web/src/views/oa/inbox/InboxRunning.vue`
- Modify: `cp6.web/src/views/oa/inbox/InboxDone.vue`

**Interfaces:**
- Consumes: `useBreakpoint()`（`cp6.web/src/composables/useBreakpoint.ts`，`isMobile = width<=767`）。
- Produces: 无对外契约（纯视图）；InboxPending 的 `selected` 数组语义不变（D-T2/批量条继续复用）。

- [ ] **Step 1: InboxView.vue（壳）**

脚本加：

```ts
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()

/** 移动端文件夹横滑条（含流程管理路由项） */
const folderList = computed(() => [
  { key: 'dashboard',  label: t('oa.inbox.dashboard') },
  { key: 'pending',    label: t('oa.inbox.pending') },
  { key: 'running',    label: t('oa.inbox.running') },
  { key: 'done',       label: t('oa.inbox.done') },
  { key: 'draft',      label: t('oa.inbox.draft') },
  { key: 'flow-admin', label: t('oa.inbox.flowAdmin') },
])
```

模板三处：

(a) `el-aside` 桌面独占：

```html
      <el-aside v-if="!isMobile" width="200px" class="inbox-aside">
```

(b) `el-aside` 结束标签后、`el-main` 前无需插入——横滑条放 `inbox-body` 之上（`el-header` 结束标签之后）：

```html
    <!-- 移动端文件夹横滑条（替代左侧菜单） -->
    <div v-if="isMobile" class="mobile-folder-bar">
      <el-button
        v-for="f in folderList"
        :key="f.key"
        size="small"
        round
        :type="folder === f.key ? 'primary' : 'default'"
        @click="onSelect(f.key)"
      >
        {{ f.label }}<template v-if="f.key === 'pending' && stats?.pendingCount"> ({{ stats.pendingCount }})</template>
      </el-button>
    </div>
```

(c) 详情抽屉移动端全屏：

```html
    <el-drawer
      v-model="drawerVisible"
      :size="isMobile ? '100%' : '60%'"
      :title="t('oa.inbox.detailTitle')"
      destroy-on-close
    >
```

`<style scoped>` 尾部追加：

```css
.mobile-folder-bar {
  display: flex;
  gap: 6px;
  padding: 8px 12px;
  overflow-x: auto;
  background: var(--cp-card);
  border-bottom: 1px solid var(--cp-line-soft);
  flex-shrink: 0;
  -webkit-overflow-scrolling: touch;
}

.mobile-folder-bar .el-button {
  flex-shrink: 0;
  margin-left: 0;
}

@media (max-width: 767px) {
  .inbox-header {
    padding: 0 12px;
  }

  .inbox-title {
    font-size: 14px;
  }

  .inbox-main {
    padding: 10px;
  }
}
```

- [ ] **Step 2: InboxPending.vue（卡片化 + 移动端多选）**

脚本加：

```ts
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()

/** 移动端卡片多选：直接维护同一 selected 数组（批量条 doBatch 复用零改动） */
function isSelected(row: PendingItem): boolean {
  return selected.value.some((r) => r.taskId === row.taskId)
}

function toggleMobileSelect(row: PendingItem) {
  selected.value = isSelected(row)
    ? selected.value.filter((r) => r.taskId !== row.taskId)
    : [...selected.value, row]
}
```

review 面板 `<el-table ...>`（:28-47）加 `v-if="!isMobile"`，其后（`CpEmpty` 之前）插卡片流（spec §4 字段：单号/流程名/当前关卡/时间戳/状态 CpTag）：

```html
        <div v-if="isMobile" class="mobile-list" v-loading="reviewLoading">
          <div
            v-for="row in reviewRows"
            :key="row.taskId"
            class="mobile-row"
            :class="{ 'row-unread': !row.isRead }"
            @click="onReviewRowClick(row)"
          >
            <div class="mobile-main">
              <el-checkbox
                :model-value="isSelected(row)"
                @click.stop
                @change="toggleMobileSelect(row)"
              />
              <span class="mobile-flow">{{ row.flowName }}</span>
              <CpTag tone="info">{{ row.stageName || row.nodeId }}</CpTag>
            </div>
            <div class="mobile-meta">
              <span class="mobile-key">{{ row.flowKey }}</span>
              <span>{{ row.starterName }}</span>
              <span>{{ formatTime(row.sentAt) }}</span>
            </div>
          </div>
        </div>
```

cc 面板同法：`<el-table>`（:57-73）加 `v-if="!isMobile"`，其后插：

```html
        <div v-if="isMobile" class="mobile-list" v-loading="ccLoading">
          <div v-for="row in ccRows" :key="row.ccId" class="mobile-row" @click="onCcRowClick(row)">
            <div class="mobile-main">
              <span class="mobile-flow">{{ row.flowName }}</span>
              <CpTag tone="info">{{ row.atNodeId }}</CpTag>
            </div>
            <div class="mobile-meta">
              <span>{{ row.starterName }}</span>
              <span>{{ formatTime(row.createDate) }}</span>
            </div>
          </div>
        </div>
```

`<style scoped>` 尾部追加（卡片样式照 `StockDwellView.vue:402-443` 词汇）：

```css
.mobile-list {
  display: flex;
  flex-direction: column;
}

.mobile-row {
  padding: 12px 2px;
  border-bottom: 1px solid var(--cp-line);
  cursor: pointer;
}

.mobile-row:last-child {
  border-bottom: none;
}

.mobile-main {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--cp-ink);
  font-size: 14px;
  margin-bottom: 6px;
}

.mobile-flow {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mobile-meta {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  color: var(--cp-muted);
  font-size: 12px;
}

.mobile-key {
  font-family: monospace;
}

.mobile-row.row-unread .mobile-flow {
  font-weight: 650;
}

@media (max-width: 767px) {
  .batch-bar {
    flex-wrap: wrap;
  }

  .batch-bar .el-input {
    width: 100% !important;
    order: 3;
  }
}
```

- [ ] **Step 3: InboxRunning.vue** — 同法：脚本加 `useBreakpoint`；`<el-table>` 加 `v-if="!isMobile"`；其后插：

```html
    <div v-if="isMobile" class="mobile-list" v-loading="loading">
      <div v-for="row in rows" :key="row.instanceId" class="mobile-row" @click="onRowClick(row)">
        <div class="mobile-main">
          <span class="mobile-flow">{{ row.flowName }}</span>
          <CpTag :tone="instanceStatusTone(row.status)">{{ t(instanceStatusText(row.status)) }}</CpTag>
        </div>
        <div class="mobile-meta">
          <span>{{ row.currentNode }} · {{ row.currentHandlers.join('、') }}</span>
          <span>{{ formatTime(row.createDate) }}</span>
        </div>
      </div>
    </div>
```

`<style scoped>` 尾部追加与 InboxPending 相同的 `.mobile-list/.mobile-row/.mobile-main/.mobile-flow/.mobile-meta` 五条规则（scoped 样式不跨组件，需各页自带；逐字同上，无 `.mobile-key`/`.row-unread` 两条）。

- [ ] **Step 4: InboxDone.vue（卡片化 + 筛选收抽屉）**

脚本加：

```ts
import { Filter, Refresh } from '@element-plus/icons-vue'   // 原行只有 Refresh，替换
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()
const filterDrawer = ref(false)
```

模板：`.done-controls`（:4-20）加 `v-if="!isMobile"`；`table-toolbar`（:22-25）内刷新按钮后加移动端筛选入口；`.done-controls` 原块整体复制进底部抽屉（月份选择 + tab 换 `el-radio-group`）：

```html
    <!-- 移动端：筛选入口 + 底部抽屉 -->
    <div class="table-toolbar">
      <CpTag>{{ t('共 {n} 条', { n: rows.length }) }}</CpTag>
      <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
      <el-button v-if="isMobile" :icon="Filter" size="small" round @click="filterDrawer = true">
        {{ t('oa.inbox.mobileFilter') }}
      </el-button>
    </div>

    <el-drawer v-model="filterDrawer" direction="btt" size="40%" :title="t('oa.inbox.mobileFilter')">
      <el-form label-width="90px">
        <el-form-item :label="t('oa.done.allMonths')">
          <el-date-picker v-model="selectedMonth" type="month" value-format="YYYY-MM"
            :placeholder="t('oa.done.allMonths')" clearable style="width: 100%" @change="load" />
        </el-form-item>
        <el-form-item>
          <el-radio-group v-model="activeTab" @change="load">
            <el-radio-button label="mine">{{ t('oa.done.mine') }}</el-radio-button>
            <el-radio-button label="all">{{ t('oa.done.all') }}</el-radio-button>
            <el-radio-button label="cc">{{ t('oa.done.cc') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
    </el-drawer>
```

`<el-table>` 加 `v-if="!isMobile"`，其后插：

```html
    <div v-if="isMobile" class="mobile-list" v-loading="loading">
      <div v-for="row in rows" :key="row.instanceId" class="mobile-row" @click="onRowClick(row)">
        <div class="mobile-main">
          <span class="mobile-flow">{{ row.flowName }}</span>
          <CpTag :tone="formToStatusTone(row.formToStatus)">{{ t(formToStatusText(row.formToStatus)) }}</CpTag>
        </div>
        <div class="mobile-meta">
          <span>{{ row.starterName }}</span>
          <span>{{ formatTime(row.doneAt) }}</span>
        </div>
      </div>
    </div>
```

`<style scoped>` 尾部追加同一组 `.mobile-*` 五条规则（同 Step 3）。

- [ ] **Step 5: 验证 + commit** — 桌面回归：既有 vitest 全绿（列表逻辑零改，纯模板分支）；375px 走查留 E-T2 harness。

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): C-T1 信箱壳+待办/在途/已办列表移动端卡片化+筛选抽屉(767px断点)"
```

---


---
## 附: R6前端现状+全局约束
## Global Constraints（每个 Task 隐含遵守）

- **测试基线**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 1509 通过（5 skip）→ +N 全绿，既有测试零回归；前端 `npm run test` 320 通过 → +N 全绿；`npm run type-check` 大堆参数照常通过；`npm run build` 通过。
- **零 EF 迁移**：本计划**零实体/DbSet/索引改动**（`Wf_InboxPref.PrefsJson` 自由结构承载全部新偏好键）。DoD 跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。
- **零跨模块污染**：只碰 `CP6.Core/Services/Oa`、`CP6.Core/Services/Wf`（只读引用，**不改 `TransferAsync` 等引擎动作**）、`CP6.WebApi`（Controllers/Oa、Services、Seed、Program.cs 定点块）、`cp6.web/src/views/oa/**`、`cp6.web/src/api/oa/**`、`cp6.web/src/types/oa/**`。不碰 Space/WMS/ERP/FIN 任何文件。每 Task 完成 `git show --stat` 复核。
- **五语 i18n**：全部新 UI 文案走 `t('...')` 运行时键；键在 E-T1 一次性 seed（ZhCN/ZhTW/En/Ja/Ko 五列，`Sys_Lang`）。后端业务错误抛 i18n 键字符串（前端 http 拦截器 `t(raw)` 自动本地化，`http.ts:92-95` 既有口径）。
- **零硬编码色**：新增 CSS 一律 Design System v1.0 token（`--cp-*`，`cp6.web/src/styles/tokens.css`）。
- **桌面端像素零回归**：全部移动端适配走 `isMobile` 模板分支或 `@media (max-width: 767px)` 尾部块，≥768px 渲染路径与现状字节等价（QA 双端走查）。
- **审批人策略勿碰**：`ApproverResolver.cs` / `NodePropertyPanel.vue` 审批人段已完成，本计划零接触。
- **提交纪律**：TDD（先失败测试→最小实现→绿→commit）；提交信息 `feat(wfs-inbox): <任务号> <中文描述>`；**只本地 commit 不 push**。

### R6 前端现状

- 三页：`InboxView.vue`（壳：header + `el-aside 200px` 菜单 + `el-drawer size=60%` 详情）/ `InboxPending.vue`（el-table + batch-bar）/ `FormDetail.vue`（`el-col :span=14/10` 左表单右时间线 + `.action-bar` 底部按钮排）。**无独立「Sign Records 弹窗」**——签核记录 = 右栏 `FlowTimeline` 内联（移动端处理为纵向堆叠 + Transfer/SendBack 对话框全屏化）。
- 移动端先例：`useBreakpoint()`（`cp6.web/src/composables/useBreakpoint.ts`，`MOBILE_MAX=767`）+ `v-if="!isMobile"` 表格 / `v-else .mobile-list` 卡片（`StockDwellView.vue:116-170` + `:402-458` CSS）+ 尾部 `@media (max-width: 767px)`。断点 `<768px` = `max-width: 767px`，与既有约定一致。
- 设置页 `InboxSettings.vue` 已有 notify tab（扁平开关堆，:46-73）→ 替换为矩阵卡片。
- i18n seed：`CP6.WebApi/Seed/I18nOa*ScreenSeed.cs`（`Sys_Lang[] Items`，五列 `ZhCN/ZhTW/En/Ja/Ko`）；Program.cs concat 链 :1813-1819，尾部 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重；新 seed 插 :1819 之后。
