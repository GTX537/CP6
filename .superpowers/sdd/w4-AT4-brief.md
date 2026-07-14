### Task A-T4: 设置页「通知设定」矩阵卡片（前端）

**Files:**
- Create: `cp6.web/src/views/oa/settings/notifyMatrixModel.ts`
- Test: `cp6.web/src/views/oa/settings/notifyMatrixModel.test.ts`
- Modify: `cp6.web/src/api/oa/pref.ts`
- Modify: `cp6.web/src/views/oa/settings/InboxSettings.vue`

**Interfaces:**
- Consumes: `GET /api/oa/pref/notify-matrix`（A-T2）、`POST /api/oa/pref/save`（Merge=true）。
- Produces: `buildMatrixState(prefsJson, rows)` / `toNotifyPatch(state)`（共享契约签名）、`prefApi.saveMerge(partialJson)` / `prefApi.notifyMatrix()`——D-T2 复用 `saveMerge`。

- [ ] **Step 1: 写失败 vitest**

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.test.ts
import { describe, it, expect } from 'vitest'
import { buildMatrixState, toNotifyPatch, type NotifyMatrixRow } from './notifyMatrixModel'

const rows: NotifyMatrixRow[] = [
  { typeKey: 'todoCreated',  typeValue: 1, inAppSupported: true,  emailSupported: true },
  { typeKey: 'flowApproved', typeValue: 2, inAppSupported: true,  emailSupported: true },
  { typeKey: 'flowRejected', typeValue: 3, inAppSupported: true,  emailSupported: true },
  { typeKey: 'timeout',      typeValue: 4, inAppSupported: false, emailSupported: false },
]

describe('notifyMatrixModel', () => {
  it('三态坍缩：空/缺键/畸形 → 全 true', () => {
    for (const json of ['', '{}', '{"notify":{}}', 'NOT_JSON{{{']) {
      const s = buildMatrixState(json, rows)
      expect(s.todoCreated).toEqual({ inApp: true, email: true })
      expect(s.timeout).toEqual({ inApp: true, email: true })
    }
  })

  it('新矩阵形态逐格解析（仅字面 false 为关）', () => {
    const s = buildMatrixState('{"notify":{"flowRejected":{"inApp":true,"email":false}}}', rows)
    expect(s.flowRejected).toEqual({ inApp: true, email: false })
    expect(s.flowApproved).toEqual({ inApp: true, email: true })
  })

  it('遗留扁平形态回落（镜像后端 NotifyMatrix.IsEnabled）', () => {
    const s = buildMatrixState('{"notify":{"todo":false,"email":false,"approved":true}}', rows)
    expect(s.todoCreated).toEqual({ inApp: false, email: false })   // 事件关 → 双关
    expect(s.flowApproved).toEqual({ inApp: true, email: false })   // 全局 email 关 → 仅邮件关
  })

  it('toNotifyPatch 产出可回读的 notify patch', () => {
    const s = buildMatrixState('{}', rows)
    s.flowRejected.email = false
    const patch = JSON.parse(toNotifyPatch(s))
    expect(patch.notify.flowRejected).toEqual({ inApp: true, email: false })
    expect(patch.notify.todoCreated).toEqual({ inApp: true, email: true })
    expect(Object.keys(patch)).toEqual(['notify'])                  // 只 patch notify 顶层键
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- notifyMatrixModel`。预期：模块不存在。

- [ ] **Step 3: 实现纯模型**

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.ts
/** 通知矩阵纯函数（wfs-inbox-ux §2.3）。解析语义逐位镜像后端 NotifyMatrix.IsEnabled。 */
export interface NotifyMatrixRow {
  typeKey: string
  typeValue: number
  inAppSupported: boolean
  emailSupported: boolean
}

export type MatrixState = Record<string, { inApp: boolean; email: boolean }>

const LEGACY_KEY: Record<string, string> = {
  todoCreated: 'todo',
  flowApproved: 'approved',
  flowRejected: 'rejected',
  timeout: 'timeout',
}

export function buildMatrixState(prefsJson: string, rows: NotifyMatrixRow[]): MatrixState {
  let notify: Record<string, unknown> = {}
  try {
    const parsed = JSON.parse(prefsJson || '{}')
    if (parsed && typeof parsed.notify === 'object' && parsed.notify !== null) notify = parsed.notify
  } catch {
    notify = {} // 畸形 → 全默认 true
  }
  const state: MatrixState = {}
  for (const r of rows) {
    const cell = notify[r.typeKey]
    if (cell && typeof cell === 'object') {
      const c = cell as Record<string, unknown>
      state[r.typeKey] = { inApp: c.inApp !== false, email: c.email !== false }
    } else {
      const legacyKey = LEGACY_KEY[r.typeKey]
      const eventOn = legacyKey ? notify[legacyKey] !== false : true
      const emailOn = notify['email'] !== false
      state[r.typeKey] = { inApp: eventOn, email: eventOn && emailOn }
    }
  }
  return state
}

/** 序列化为顶层 notify patch（配 prefApi.saveMerge，服务端合并保他键）。 */
export function toNotifyPatch(state: MatrixState): string {
  const notify: Record<string, { inApp: boolean; email: boolean }> = {}
  for (const [k, v] of Object.entries(state)) notify[k] = { inApp: v.inApp, email: v.email }
  return JSON.stringify({ notify })
}
```

- [ ] **Step 4: 跑验证 PASS** — `npm run test -- notifyMatrixModel`。

- [ ] **Step 5: API + 设置页接线**

`cp6.web/src/api/oa/pref.ts` 全文替换为：

```ts
import http from '../http'

export const prefApi = {
  get:  ()                     => http.get('/oa/pref/get'),
  save: (prefsJson: string)    => http.post('/oa/pref/save', { prefsJson }),
  /** 服务端顶层键合并写（保他键；值 null=删键恢复默认） */
  saveMerge: (partialJson: string) => http.post('/oa/pref/save', { prefsJson: partialJson, merge: true }),
  /** 通知矩阵元数据（类型轴 + 通道支持标志） */
  notifyMatrix: () => http.get('/oa/pref/notify-matrix'),
}
```

`InboxSettings.vue` 改造（三处）：

**(a) 模板**：notify tab（:46-73 的 `el-card` 内容）整体替换为矩阵表格：

```html
      <!-- Tab 3: 通知设定（类型×通道矩阵，wfs-inbox-ux §2.3） -->
      <el-tab-pane :label="t('oa.notify.settings.tab')" name="notify">
        <el-card shadow="never" style="max-width: 640px; margin-top: 16px">
          <el-table :data="matrixRows" size="small" border>
            <el-table-column :label="t('oa.notify.matrix.colType')" min-width="180">
              <template #default="{ row }">{{ t('oa.notify.type.' + row.typeKey) }}</template>
            </el-table-column>
            <el-table-column :label="t('oa.notify.matrix.colInApp')" width="110" align="center">
              <template #default="{ row }">
                <el-tooltip :disabled="row.inAppSupported" :content="t('oa.notify.matrix.unsupported')">
                  <el-switch
                    v-model="matrixState[row.typeKey].inApp"
                    :disabled="!row.inAppSupported"
                  />
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column :label="t('oa.notify.matrix.colEmail')" width="110" align="center">
              <template #default="{ row }">
                <el-tooltip :disabled="row.emailSupported" :content="t('oa.notify.matrix.unsupported')">
                  <el-switch
                    v-model="matrixState[row.typeKey].email"
                    :disabled="!row.emailSupported"
                  />
                </el-tooltip>
              </template>
            </el-table-column>
          </el-table>
          <div class="matrix-actions">
            <el-button type="primary" :loading="notifySaving" @click="saveNotifyMatrix">
              {{ t('common.save') }}
            </el-button>
            <el-button @click="resetNotifyMatrix">{{ t('oa.notify.matrix.reset') }}</el-button>
          </div>
        </el-card>
      </el-tab-pane>
```

**(b) 脚本**：删 `NotifyPrefs` 接口、`notifyPrefs` ref、`saveNotifyPref`；加：

```ts
import { buildMatrixState, toNotifyPatch, type MatrixState, type NotifyMatrixRow } from './notifyMatrixModel'

// ─── Notify matrix tab ───────────────────────────────────────────────────────
const matrixRows = ref<NotifyMatrixRow[]>([])
const matrixState = ref<MatrixState>({})
const notifySaving = ref(false)

async function loadNotifyMatrix(prefsJson: string) {
  try {
    const res = await prefApi.notifyMatrix()
    matrixRows.value = (((res as any).data as NotifyMatrixRow[]) || [])
    matrixState.value = buildMatrixState(prefsJson, matrixRows.value)
  } catch {
    // HTTP interceptor auto-toasts
  }
}

async function saveNotifyMatrix() {
  notifySaving.value = true
  try {
    await prefApi.saveMerge(toNotifyPatch(matrixState.value))   // 服务端合并：保 pageSize/rowMode 等他键
    ElMessage.success(t('oa.notify.matrix.saveOk'))
    await loadPref()
  } finally {
    notifySaving.value = false
  }
}

async function resetNotifyMatrix() {
  try {
    await prefApi.saveMerge('{"notify":null}')                  // 删键 = 恢复默认全开（三态坍缩）
    ElMessage.success(t('oa.notify.matrix.resetOk'))
    await loadPref()
  } catch {
    // HTTP interceptor auto-toasts
  }
}
```

`loadPref()` 内：删除 `notifyPrefs.value = {...}` 赋值段（:299-306），在解析出 `prefsJson` 后追加 `await loadNotifyMatrix(prefsJson ?? '{}')`（无 prefsJson 时传 `'{}'` 也要调，保证矩阵行渲染）。`savePref()` 的 `prefApi.save(JSON.stringify(merged))` 改为 `prefApi.saveMerge(JSON.stringify(prefs.value))`（显示偏好三键顶层合并，`storedRaw` spread 保留兜底不删）。

**(c) 样式**（`<style scoped>` 追加）：

```css
.matrix-actions {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}
```

- [ ] **Step 6: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): A-T4 设置页通知矩阵卡片(格子禁用+恢复默认+服务端合并写)"
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
