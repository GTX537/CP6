### Task B-T3: 批量改派对话框 UI（流程管理入口 + 预览 + 结果报告 + 单条重试）

**Files:**
- Create: `cp6.web/src/views/oa/admin/BatchTransferDialog.vue`
- Modify: `cp6.web/src/views/oa/admin/FlowAdmin.vue`（#actions 加入口按钮）
- Modify: `cp6.web/src/api/oa/inbox.ts`
- Modify: `cp6.web/src/types/oa/inbox.ts`

**Interfaces:**
- Consumes: `POST /oa/inbox/batch-transfer` / `.../preview`（B-T2）；用户远程搜索 `userApi.getList`（照 `TransferDialog.vue:74-92` 逐字模式）。
- Produces: `inboxApi.batchTransfer(p)` / `inboxApi.batchTransferPreview(p)`；单条重试 = **同端点 + `filter.taskIds:[id]` + 同失败明细口径**（spec §3.2：任务被他人办结/转走等结果同样以明细行呈现，不特殊处理）。

- [ ] **Step 1: API + 类型**

`cp6.web/src/api/oa/inbox.ts` 追加两行（对象内）：

```ts
  batchTransfer: (p: BatchTransferReq) => http.post('/oa/inbox/batch-transfer', p),
  batchTransferPreview: (p: BatchTransferReq) => http.post('/oa/inbox/batch-transfer/preview', p),
```

文件头加 `import type { BatchTransferReq } from '@/types/oa/inbox'`。

`cp6.web/src/types/oa/inbox.ts` 末尾追加：

```ts
// ── 在途批量转单（wfs-inbox-ux §3）──
export interface BatchTransferReq {
  fromUserId: string
  toUserId: string
  comment?: string
  filter?: { flowKey?: string; beforeUtc?: string; taskIds?: string[] }
}

export interface BatchTransferItemResult {
  taskId: string
  flowKey: string
  ok: boolean
  error?: string
}

export interface BatchTransferReport {
  total: number
  succeeded: number
  failed: BatchTransferItemResult[]
}

export interface BatchTransferPreview {
  total: number
  sample: PendingItem[]
}
```

- [ ] **Step 2: 对话框组件**（完整代码；用户搜索段与 `TransferDialog.vue` 同模式）

```html
<!-- cp6.web/src/views/oa/admin/BatchTransferDialog.vue
     在途批量改派（wfs-inbox-ux §3.2）：选 from/to → 预览待转清单 → 确认 → 结果报告（失败行单条重试）。
     重试走同一 batch-transfer 端点 + filter.taskIds（同 TransferAsync、同失败明细口径）。 -->
<template>
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.bt.title')"
    :width="isMobile ? '100vw' : '640px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
    <!-- Step 1: 表单 + 预览 -->
    <template v-if="!report">
      <el-form label-width="100px">
        <el-form-item :label="t('oa.bt.fromUser')">
          <el-select v-model="fromUserId" filterable remote :remote-method="searchFrom"
            :loading="fromLoading" :placeholder="t('oa.transfer.userHint')" style="width: 100%" clearable
            @change="preview = null">
            <el-option v-for="u in fromOptions" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.bt.toUser')">
          <el-select v-model="toUserId" filterable remote :remote-method="searchTo"
            :loading="toLoading" :placeholder="t('oa.transfer.userHint')" style="width: 100%" clearable>
            <el-option v-for="u in toOptions" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.bt.filterFlowKey')">
          <el-input v-model="filterFlowKey" clearable @change="preview = null" />
        </el-form-item>
        <el-form-item :label="t('oa.bt.filterBefore')">
          <el-date-picker v-model="filterBefore" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss"
            style="width: 100%" clearable @change="preview = null" />
        </el-form-item>
        <el-form-item :label="t('oa.bt.comment')">
          <el-input v-model="comment" type="textarea" :rows="2" :placeholder="t('oa.bt.commentHint')" />
        </el-form-item>
      </el-form>

      <div v-if="preview" class="bt-preview">
        <CpTag tone="info">{{ t('oa.bt.previewTotal', { n: preview.total }) }}</CpTag>
        <el-table v-if="preview.sample.length" :data="preview.sample" size="small" border max-height="220">
          <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="140" />
          <el-table-column prop="starterName" :label="t('oa.col.starter')" width="110" />
          <el-table-column :label="t('oa.col.sentAt')" width="160">
            <template #default="{ row }">{{ formatTime(row.sentAt) }}</template>
          </el-table-column>
        </el-table>
        <CpEmpty v-else :text="t('oa.bt.previewEmpty')" />
      </div>
    </template>

    <!-- Step 2: 结果报告 -->
    <template v-else>
      <div class="bt-result">
        <CpTag :tone="report.failed.length ? 'warn' : 'ok'">
          {{ t('oa.bt.resultSummary', { total: report.total, ok: report.succeeded, fail: report.failed.length }) }}
        </CpTag>
        <el-table v-if="report.failed.length" :data="report.failed" size="small" border max-height="260">
          <el-table-column :label="t('oa.bt.colTask')" width="120">
            <template #default="{ row }">{{ row.taskId.slice(0, 8) }}</template>
          </el-table-column>
          <el-table-column prop="flowKey" :label="t('oa.bt.colFlow')" min-width="110" />
          <el-table-column :label="t('oa.bt.colError')" min-width="140">
            <template #default="{ row }">{{ t(row.error ?? '') }}</template>
          </el-table-column>
          <el-table-column width="90" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link type="primary" :loading="retrying.has(row.taskId)"
                @click="retryOne(row)">
                {{ t('oa.bt.retry') }}
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </template>

    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <template v-if="!report">
        <el-button :disabled="!fromUserId" :loading="previewing" @click="doPreview">
          {{ t('oa.bt.preview') }}
        </el-button>
        <el-button type="warning" :loading="submitting"
          :disabled="!fromUserId || !toUserId || !preview || preview.total === 0"
          @click="doTransfer">
          {{ t('oa.bt.confirm') }}
        </el-button>
      </template>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { inboxApi } from '@/api/oa/inbox'
import { userApi } from '@/api/sys/user'
import { useBreakpoint } from '@/composables/useBreakpoint'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { BatchTransferItemResult, BatchTransferPreview, BatchTransferReport, BatchTransferReq } from '@/types/oa/inbox'

defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [val: boolean] }>()

const { t } = useI18n()
const { isMobile } = useBreakpoint()

const fromUserId = ref('')
const toUserId = ref('')
const comment = ref('')
const filterFlowKey = ref('')
const filterBefore = ref('')
const preview = ref<BatchTransferPreview | null>(null)
const report = ref<BatchTransferReport | null>(null)
const previewing = ref(false)
const submitting = ref(false)
const retrying = reactive(new Set<string>())

// ── 用户远程搜索（同 TransferDialog.vue 模式）──
interface UserOption { label: string; value: string }
const fromOptions = ref<UserOption[]>([])
const toOptions = ref<UserOption[]>([])
const fromLoading = ref(false)
const toLoading = ref(false)

async function searchUsers(keyword: string, into: typeof fromOptions, loading: typeof fromLoading) {
  if (!keyword) { into.value = []; return }
  loading.value = true
  try {
    const res: any = await userApi.getList({ page: 1, pageSize: 20, keyword })
    into.value = (res.rows ?? []).map((u: any) => ({ label: u.nickName || u.userName, value: u.id }))
  } catch {
    // HTTP interceptor already toasts the error
  } finally {
    loading.value = false
  }
}
const searchFrom = (kw: string) => searchUsers(kw, fromOptions, fromLoading)
const searchTo = (kw: string) => searchUsers(kw, toOptions, toLoading)

function buildReq(taskIds?: string[]): BatchTransferReq {
  return {
    fromUserId: fromUserId.value,
    toUserId: toUserId.value,
    comment: comment.value || undefined,
    filter: {
      flowKey: filterFlowKey.value || undefined,
      beforeUtc: filterBefore.value || undefined,
      taskIds,
    },
  }
}

async function doPreview() {
  previewing.value = true
  try {
    const res: any = await inboxApi.batchTransferPreview(buildReq())
    preview.value = res.data as BatchTransferPreview
  } catch {
    // 400（errSameUser/errTargetInvalid 等）由拦截器 t(raw) 自动 toast
  } finally {
    previewing.value = false
  }
}

async function doTransfer() {
  submitting.value = true
  try {
    const res: any = await inboxApi.batchTransfer(buildReq())
    report.value = res.data as BatchTransferReport
    if (!report.value.failed.length) ElMessage.success(t('oa.bt.allOk'))
  } catch {
    // 拦截器 toast（含 oa.bt.errTooMany 分批提示）
  } finally {
    submitting.value = false
  }
}

/** 单条重试：同端点 + filter.taskIds=[id]（同 TransferAsync、同失败明细口径，spec §3.2） */
async function retryOne(row: BatchTransferItemResult) {
  if (retrying.has(row.taskId) || !report.value) return
  retrying.add(row.taskId)
  try {
    const res: any = await inboxApi.batchTransfer(buildReq([row.taskId]))
    const r = res.data as BatchTransferReport
    if (r.succeeded === 1) {
      report.value = {
        ...report.value,
        succeeded: report.value.succeeded + 1,
        failed: report.value.failed.filter((f) => f.taskId !== row.taskId),
      }
      ElMessage.success(t('oa.bt.retryOk'))
    } else {
      // 重试仍失败（可能已被他人办结/转走）→ 用最新明细行替换（同口径呈现）
      const latest = r.failed.find((f) => f.taskId === row.taskId)
      report.value = {
        ...report.value,
        failed: report.value.failed.map((f) =>
          f.taskId === row.taskId && latest ? latest : f),
      }
      if (r.total === 0) {
        // 已不在 from 名下（他人办结/已转走）→ 从失败清单移除并提示
        report.value = { ...report.value, failed: report.value.failed.filter((f) => f.taskId !== row.taskId) }
        ElMessage.info(t('oa.bt.retryGone'))
      }
    }
  } finally {
    retrying.delete(row.taskId)
  }
}

function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

function onClose() {
  emit('update:modelValue', false)
  fromUserId.value = ''
  toUserId.value = ''
  comment.value = ''
  filterFlowKey.value = ''
  filterBefore.value = ''
  preview.value = null
  report.value = null
  fromOptions.value = []
  toOptions.value = []
}
</script>

<style scoped>
.bt-preview,
.bt-result {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 4px;
}
</style>
```

- [ ] **Step 3: FlowAdmin 入口**（`FlowAdmin.vue`）：

模板 `#actions` 内、刷新按钮之前加：

```html
      <el-button type="warning" plain @click="batchTransferVisible = true">
        {{ t('oa.bt.entry') }}
      </el-button>
```

`</CpListPage>` 与 `</CpPageShell>` 之间加：

```html
    <BatchTransferDialog v-model="batchTransferVisible" />
```

脚本加：

```ts
import BatchTransferDialog from './BatchTransferDialog.vue'

const batchTransferVisible = ref(false)
```

（权限由后端 403 强制：未授权用户点击确认时拦截器 toast「无权限：oa-inbox:batch-transfer」；按钮不做前端隐藏——OA 前端当前无权限位可查，R4。）

- [ ] **Step 4: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): B-T3 批量改派对话框(预览+结果报告+单条重试)+流程管理入口"
```

---


---
## 附: 共享契约(plan全局)
## 共享契约（所有 Task 用这些**精确**名字）

```csharp
// CP6.Core/Services/Oa/NotifyMatrix.cs
public record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported);
public static class NotifyMatrix
{
    public const string ChannelInApp = "inApp";
    public const string ChannelEmail = "email";
    public static bool IsEnabled(string prefsJson, string type, string channel);
    public static IReadOnlyList<NotifyMatrixRow> Rows();
}

// IPrefService 新增
Task<bool> IsEnabledAsync(Guid userId, string type, string channel);  // per-request 缓存（Scoped 实例内字典）
Task SaveMergeAsync(Guid userId, string partialJson);                 // 顶层键合并；patch 值为 null → 删除该键
Task<string> GetRowModeAsync(Guid userId);                            // "merged" | "expanded"，缺省 merged

// IInboxService 变更/新增
Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null);
Task<BatchTransferReport> BatchTransferAsync(Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null);
Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null);

// InboxModels.cs 新增（批量上限常量在 InboxService：private const int MaxBatchTransfer = 500;）
public record BatchTransferFilter(string? FlowKey = null, DateTime? BeforeUtc = null, IReadOnlyList<Guid>? TaskIds = null);
public record BatchTransferItemResult(Guid TaskId, string FlowKey, bool Ok, string? Error);
public record BatchTransferReport(int Total, int Succeeded, IReadOnlyList<BatchTransferItemResult> Failed);
public record BatchTransferPreview(int Total, IReadOnlyList<InboxPendingItem> Sample);   // Sample = 前 10 条
```

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.ts
export interface NotifyMatrixRow { typeKey: string; typeValue: number; inAppSupported: boolean; emailSupported: boolean }
export type MatrixState = Record<string, { inApp: boolean; email: boolean }>
export function buildMatrixState(prefsJson: string, rows: NotifyMatrixRow[]): MatrixState
export function toNotifyPatch(state: MatrixState): string        // → '{"notify":{...}}'

// cp6.web/src/views/oa/inbox/inboxModel.ts 新增
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded'
```

- 端点：`POST /api/oa/pref/save`（`SavePrefReq(string PrefsJson, bool Merge = false)`）、`GET /api/oa/pref/notify-matrix`、`GET /api/oa/inbox/pending?rowMode=&page=&pageSize=`、`POST /api/oa/inbox/batch-transfer`、`POST /api/oa/inbox/batch-transfer/preview`。
- 业务错误 i18n 键（不占 E-WF 码，走既有「message=键、前端 t(raw)」口径）：`oa.bt.errSameUser` / `oa.bt.errTargetInvalid` / `oa.bt.errTooMany` / `oa.pref.errBadJson`。
- 通知类型键（camelCase 枚举名）：`todoCreated` / `flowApproved` / `flowRejected` / `timeout` / （`branchPruned` 若枚举已合入）。

## 附: R6前端现状
### R6 前端现状

- 三页：`InboxView.vue`（壳：header + `el-aside 200px` 菜单 + `el-drawer size=60%` 详情）/ `InboxPending.vue`（el-table + batch-bar）/ `FormDetail.vue`（`el-col :span=14/10` 左表单右时间线 + `.action-bar` 底部按钮排）。**无独立「Sign Records 弹窗」**——签核记录 = 右栏 `FlowTimeline` 内联（移动端处理为纵向堆叠 + Transfer/SendBack 对话框全屏化）。
- 移动端先例：`useBreakpoint()`（`cp6.web/src/composables/useBreakpoint.ts`，`MOBILE_MAX=767`）+ `v-if="!isMobile"` 表格 / `v-else .mobile-list` 卡片（`StockDwellView.vue:116-170` + `:402-458` CSS）+ 尾部 `@media (max-width: 767px)`。断点 `<768px` = `max-width: 767px`，与既有约定一致。
- 设置页 `InboxSettings.vue` 已有 notify tab（扁平开关堆，:46-73）→ 替换为矩阵卡片。
- i18n seed：`CP6.WebApi/Seed/I18nOa*ScreenSeed.cs`（`Sys_Lang[] Items`，五列 `ZhCN/ZhTW/En/Ja/Ko`）；Program.cs concat 链 :1813-1819，尾部 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重；新 seed 插 :1819 之后。
