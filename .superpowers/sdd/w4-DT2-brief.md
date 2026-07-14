### Task D-T2: 列表工具栏 rowMode 切换开关（写回偏好）

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/inboxModel.ts`（+ 既有 `inboxModel.test.ts` 追加用例）
- Modify: `cp6.web/src/api/oa/inbox.ts`
- Modify: `cp6.web/src/views/oa/inbox/InboxPending.vue`
- Modify: `cp6.web/src/views/oa/inbox/FormDetail.vue`（pending 调用固定 expanded，行为保真）

**Interfaces:**
- Consumes: `GET /oa/inbox/pending?rowMode=`（D-T1）、`prefApi.saveMerge`（A-T4）、`prefApi.get`。
- Produces: `parseRowMode(prefsJson): 'merged'|'expanded'`（共享契约）；`inboxApi.pending(rowMode?)`。

- [ ] **Step 1: 写失败 vitest** — `cp6.web/src/views/oa/inbox/inboxModel.test.ts` 追加：

```ts
import { parseRowMode } from './inboxModel'

describe('parseRowMode', () => {
  it('缺省/缺键/非法/畸形 → merged', () => {
    expect(parseRowMode(undefined)).toBe('merged')
    expect(parseRowMode('')).toBe('merged')
    expect(parseRowMode('{}')).toBe('merged')
    expect(parseRowMode('{"rowMode":"weird"}')).toBe('merged')
    expect(parseRowMode('NOT_JSON{{{')).toBe('merged')
  })
  it('expanded 显式识别', () => {
    expect(parseRowMode('{"rowMode":"expanded"}')).toBe('expanded')
    expect(parseRowMode('{"rowMode":"merged"}')).toBe('merged')
  })
})
```

（`describe/it/expect` 该文件既有 import 复用。）

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- inboxModel`。

- [ ] **Step 3: 实现**

`inboxModel.ts` 末尾追加：

```ts
/** rowMode 显示偏好解析（wfs-inbox-ux §5）：PrefsJson 顶层 rowMode 键；缺省/非法/畸形 → merged。 */
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded' {
  if (!prefsJson) return 'merged'
  try {
    const parsed = JSON.parse(prefsJson)
    return parsed?.rowMode === 'expanded' ? 'expanded' : 'merged'
  } catch {
    return 'merged'
  }
}
```

`api/oa/inbox.ts` 的 `pending` 行替换：

```ts
  pending:   (rowMode?: 'merged' | 'expanded') => http.get('/oa/inbox/pending', { params: { rowMode } }),
```

（axios 自动省略 undefined 参数 → 既有无参调用点走后端偏好回落，零变化。）

`InboxPending.vue`：

(a) review 面板 `.table-toolbar`（:6-9）追加开关（刷新按钮之后）：

```html
          <el-radio-group v-model="rowMode" size="small" class="rowmode-toggle" @change="onRowModeChange">
            <el-radio-button label="merged">{{ t('oa.inbox.rowMode.merged') }}</el-radio-button>
            <el-radio-button label="expanded">{{ t('oa.inbox.rowMode.expanded') }}</el-radio-button>
          </el-radio-group>
```

(b) 脚本：

```ts
import { prefApi } from '@/api/oa/pref'
import { parseRowMode } from '@/views/oa/inbox/inboxModel'

// ── rowMode（wfs-inbox-ux §5：切换即写回偏好 + 重载列表）──
const rowMode = ref<'merged' | 'expanded'>('merged')

async function initRowMode() {
  try {
    const res: any = await prefApi.get()
    rowMode.value = parseRowMode(res.data?.prefsJson)
  } catch {
    // 默认 merged
  }
}

async function onRowModeChange() {
  try {
    await prefApi.saveMerge(JSON.stringify({ rowMode: rowMode.value }))   // 顶层键合并：不碰 notify/pageSize 等
  } catch {
    // HTTP interceptor auto-toasts；写回失败不阻塞本次切换显示
  }
  await loadReview()
}
```

(c) `loadReview` 的取数行改为 `const res = await inboxApi.pending(rowMode.value)`；`onMounted(loadReview)` 改为：

```ts
onMounted(async () => {
  await initRowMode()
  await loadReview()
})
```

(d) `<style scoped>` 追加：

```css
.rowmode-toggle {
  margin-left: auto;
}
```

`FormDetail.vue`：`loadDetail` 内 `inboxApi.pending()`（:172）改为 `inboxApi.pending('expanded')`——详情页找「我的可办任务」需逐任务粒度，不随显示偏好合并（行为保真）。

- [ ] **Step 4: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): D-T2 待办工具栏rowMode切换开关+偏好写回+详情页expanded保真"
```

---


---
## 附: 共享契约(parseRowMode行)
// cp6.web/src/views/oa/inbox/inboxModel.ts 新增
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded'
```
