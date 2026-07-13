### Task E-T2: 前端「触发器」tab（API + 分型表单 + 流水抽屉 + key 一次性显示）+ vitest

> 纪律：视图全 `t()`（键在 F-T2 seed）；零硬编码色（CpTag tone / Design System token）；每步 `npm run test` + `npm run type-check`。

**Files:**
- Create: `cp6.web/src/api/oa/flowTrigger.ts`
- Create: `cp6.web/src/views/oa/admin/flowTriggerModel.ts`（纯逻辑，vitest 可测）
- Create: `cp6.web/src/views/oa/admin/__tests__/flowTriggerModel.spec.ts`
- Modify: `cp6.web/src/views/oa/admin/FlowAdmin.vue`（el-tabs 包裹）
- Create: `cp6.web/src/views/oa/admin/FlowTriggerPanel.vue`
- Create: `cp6.web/src/views/oa/admin/FlowTriggerDialog.vue`

- [ ] **Step 1: 写失败 vitest（纯逻辑先行）**

```ts
// cp6.web/src/views/oa/admin/__tests__/flowTriggerModel.spec.ts
import { describe, it, expect } from 'vitest'
import { TRIGGER_TYPES, CRON_PRESETS, typeTone, validateTriggerForm, buildConfigJson } from '../flowTriggerModel'

describe('flowTriggerModel', () => {
  it('three trigger types with stable codes', () => {
    expect(TRIGGER_TYPES.map(t => t.value)).toEqual([0, 1, 2])
  })
  it('cron presets include daily/monday/day25/monthEnd(≈28th)', () => {
    const crons = CRON_PRESETS.map(p => p.cron)
    expect(crons).toContain('0 9 * * *')
    expect(crons).toContain('0 9 * * 1')
    expect(crons).toContain('0 9 25 * *')
    expect(crons).toContain('0 9 28 * *')   // 每月末近似（NCrontab 无 L，映射表③）
  })
  it('typeTone maps to Cp tones (no hardcoded colors)', () => {
    expect(['ok', 'info', 'warn', 'muted']).toContain(typeTone(0))
  })
  it('validateTriggerForm flags missing per-type fields', () => {
    expect(validateTriggerForm({ triggerType: 0, flowKey: 'fk', starterUserId: 'u', cron: '' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 1, flowKey: 'fk', starterUserId: 'u', eventKey: '' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 0, flowKey: '', starterUserId: 'u', cron: '0 9 * * *' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 0, flowKey: 'fk', starterUserId: 'u', cron: '0 9 * * *' })).toEqual([])
  })
  it('buildConfigJson per type', () => {
    expect(JSON.parse(buildConfigJson({ triggerType: 0, cron: '0 9 * * *', varsJson: '{"a":1}' }))).toEqual({ cron: '0 9 * * *', varsJson: '{"a":1}' })
    expect(JSON.parse(buildConfigJson({ triggerType: 1, varsMap: { orderNo: '$.No' } }))).toEqual({ varsMap: { orderNo: '$.No' } })
    expect(JSON.parse(buildConfigJson({ triggerType: 2, varsSchema: ['orderNo'] }))).toEqual({ varsSchema: ['orderNo'] })
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- flowTriggerModel`。

- [ ] **Step 3: 实现纯逻辑 + API**

```ts
// cp6.web/src/views/oa/admin/flowTriggerModel.ts
export interface TriggerFormState {
  triggerType: number
  flowKey?: string
  starterUserId?: string
  cron?: string
  varsJson?: string
  eventKey?: string
  varsMap?: Record<string, string>
  varsSchema?: string[]
}

export const TRIGGER_TYPES = [
  { value: 0, labelKey: 'oa.flowtrigger.type.timer' },
  { value: 1, labelKey: 'oa.flowtrigger.type.event' },
  { value: 2, labelKey: 'oa.flowtrigger.type.message' },
] as const

/** cron 常用预设（spec §4；「每月末」按 28 日近似——NCrontab 无 L 语义，映射表③，文案已注明） */
export const CRON_PRESETS = [
  { labelKey: 'oa.flowtrigger.preset.daily', cron: '0 9 * * *' },
  { labelKey: 'oa.flowtrigger.preset.monday', cron: '0 9 * * 1' },
  { labelKey: 'oa.flowtrigger.preset.day25', cron: '0 9 25 * *' },
  { labelKey: 'oa.flowtrigger.preset.monthEnd', cron: '0 9 28 * *' },
] as const

/** CpTag tone（零硬编码色）：timer=info / event=ok / message=warn */
export function typeTone(triggerType: number): 'ok' | 'info' | 'warn' | 'muted' {
  return triggerType === 0 ? 'info' : triggerType === 1 ? 'ok' : triggerType === 2 ? 'warn' : 'muted'
}

/** 客户端镜像校验（后端权威 E-WF-022/023）；返回 i18n 键数组，空=通过 */
export function validateTriggerForm(f: TriggerFormState): string[] {
  const errs: string[] = []
  if (!f.flowKey) errs.push('oa.flowtrigger.err.flowKey')
  if (!f.starterUserId) errs.push('oa.flowtrigger.err.starter')
  if (f.triggerType === 0 && !f.cron) errs.push('oa.flowtrigger.err.cron')
  if (f.triggerType === 1 && !f.eventKey) errs.push('oa.flowtrigger.err.eventKey')
  return errs
}

export function buildConfigJson(f: Partial<TriggerFormState> & { triggerType: number }): string {
  if (f.triggerType === 0) return JSON.stringify({ cron: f.cron ?? '', ...(f.varsJson ? { varsJson: f.varsJson } : {}) })
  if (f.triggerType === 1) return JSON.stringify({ varsMap: f.varsMap ?? {} })
  return JSON.stringify({ varsSchema: f.varsSchema ?? [] })
}
```

```ts
// cp6.web/src/api/oa/flowTrigger.ts —— 范式照 designer.ts/flowAdmin.ts（http + 剥壳 res.data ?? res）
import http from '../http'

export interface FlowTriggerItem {
  id: string
  flowKey: string
  triggerType: number
  enabled: boolean
  eventKey?: string | null
  starterUserId: string
  nextDueUtc?: string | null
  lastFiredUtc?: string | null
  hasApiKey: boolean
  configJson: string
}

export interface TriggerFireItem {
  id: string
  idempotencyKey: string
  firedUtc: string
  instanceId?: string | null
  source: number
  error?: string | null
}

export interface FlowTriggerSaveBody {
  flowKey: string
  triggerType: number
  configJson: string
  enabled: boolean
  eventKey?: string | null
  starterUserId: string
}

const unwrap = (res: any) => res?.data ?? res

export const flowTriggerApi = {
  list: async (): Promise<FlowTriggerItem[]> => unwrap(await http.get('/oa/flow-triggers/list')) ?? [],
  get: async (id: string): Promise<FlowTriggerItem> => unwrap(await http.get(`/oa/flow-triggers/${id}`)),
  create: async (body: FlowTriggerSaveBody): Promise<{ id: string; apiKeyPlain?: string | null }> =>
    unwrap(await http.post('/oa/flow-triggers', body)),
  update: (id: string, body: FlowTriggerSaveBody) => http.put(`/oa/flow-triggers/${id}`, body),
  enable: (id: string, enabled: boolean) => http.post(`/oa/flow-triggers/${id}/enable`, { enabled }),
  resetKey: async (id: string): Promise<{ apiKeyPlain: string }> =>
    unwrap(await http.post(`/oa/flow-triggers/${id}/reset-key`)),
  manualFire: async (id: string): Promise<{ instanceId?: string }> =>
    unwrap(await http.post(`/oa/flow-triggers/${id}/manual-fire`)),
  fires: async (id: string, take = 20): Promise<TriggerFireItem[]> =>
    unwrap(await http.get(`/oa/flow-triggers/${id}/fires`, { params: { take } })) ?? [],
  cronPreview: async (cron: string): Promise<{ next: string[] }> =>
    unwrap(await http.post('/oa/flow-triggers/cron-preview', { cron })),
}
```

- [ ] **Step 4: FlowAdmin.vue 加 tab**（既有流程列表内容**原样整体移入**第一个 tab-pane，行为零变；`:count` 只在流程 tab 生效）：

```vue
<!-- template 改造骨架（script 追加 activeTab + FlowTriggerPanel import，其余既有代码不动） -->
<CpPageShell :title="t('oa.flowadmin.title')" :count="activeTab === 'flows' ? total : undefined">
  <template #actions>
    <el-button v-if="activeTab === 'flows'" :icon="Refresh" circle :loading="refreshing" @click="refresh" />
  </template>
  <el-tabs v-model="activeTab">
    <el-tab-pane :label="t('oa.flowadmin.tab.flows')" name="flows">
      <!-- 既有 el-alert + CpListPage 原样移入 -->
    </el-tab-pane>
    <el-tab-pane :label="t('oa.flowtrigger.tab')" name="triggers" lazy>
      <FlowTriggerPanel />
    </el-tab-pane>
  </el-tabs>
</CpPageShell>
```

```ts
// script setup 追加
import FlowTriggerPanel from './FlowTriggerPanel.vue'
const activeTab = ref<'flows' | 'triggers'>('flows')
```

- [ ] **Step 5: FlowTriggerPanel.vue**（列表 + 操作 + 流水抽屉 + key 一次性弹窗；骨架级完整）：

```vue
<template>
  <div class="flow-trigger-panel">
    <div class="panel-actions">
      <el-button type="primary" @click="openCreate">{{ t('oa.flowtrigger.new') }}</el-button>
      <el-button :icon="Refresh" circle @click="reload" />
    </div>

    <el-table :data="rows" v-loading="loading" :empty-text="t('oa.flowtrigger.empty')">
      <el-table-column prop="triggerType" :label="t('oa.flowtrigger.col.type')" width="110">
        <template #default="{ row }">
          <CpTag :tone="typeTone(row.triggerType)">{{ t(typeLabelKey(row.triggerType)) }}</CpTag>
        </template>
      </el-table-column>
      <el-table-column prop="flowKey" :label="t('oa.flowtrigger.col.flowKey')" min-width="160" />
      <el-table-column prop="eventKey" :label="t('oa.flowtrigger.col.eventKey')" min-width="180">
        <template #default="{ row }">{{ row.eventKey ?? '—' }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.flowtrigger.col.enabled')" width="90">
        <template #default="{ row }">
          <el-switch :model-value="row.enabled" :loading="toggling.has(row.id)"
                     @change="(v: boolean | string | number) => toggleEnable(row, v as boolean)" />
        </template>
      </el-table-column>
      <el-table-column prop="nextDueUtc" :label="t('oa.flowtrigger.col.nextDue')" width="170">
        <template #default="{ row }">{{ fmtUtc(row.nextDueUtc) }}</template>
      </el-table-column>
      <el-table-column prop="lastFiredUtc" :label="t('oa.flowtrigger.col.lastFired')" width="170">
        <template #default="{ row }">{{ fmtUtc(row.lastFiredUtc) }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.flowtrigger.col.actions')" width="280" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">{{ t('common.edit') }}</el-button>
          <el-button link type="primary" @click="manualFire(row)">{{ t('oa.flowtrigger.manualFire') }}</el-button>
          <el-button link @click="openFires(row)">{{ t('oa.flowtrigger.fires') }}</el-button>
          <el-button v-if="row.triggerType === 2" link type="danger" @click="resetKey(row)">
            {{ t('oa.flowtrigger.resetKey') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <FlowTriggerDialog v-model="dialogVisible" :editing="editing" @saved="onSaved" />

    <!-- 流水抽屉（spec §4：最近 N 条 时间/结果/实例链接/错误） -->
    <el-drawer v-model="firesVisible" :title="t('oa.flowtrigger.fires')" size="480px">
      <el-table :data="fires" v-loading="firesLoading">
        <el-table-column prop="firedUtc" :label="t('oa.flowtrigger.fire.time')" width="170">
          <template #default="{ row }">{{ fmtUtc(row.firedUtc) }}</template>
        </el-table-column>
        <el-table-column :label="t('oa.flowtrigger.fire.result')" width="90">
          <template #default="{ row }">
            <CpTag :tone="row.instanceId ? 'ok' : row.error ? 'warn' : 'muted'">
              {{ row.instanceId ? t('oa.flowtrigger.fire.ok') : row.error ? t('oa.flowtrigger.fire.fail') : t('oa.flowtrigger.fire.pending') }}
            </CpTag>
          </template>
        </el-table-column>
        <el-table-column :label="t('oa.flowtrigger.fire.instance')" min-width="140">
          <template #default="{ row }">
            <router-link v-if="row.instanceId" :to="`/oa/inbox?instanceId=${row.instanceId}`">{{ row.instanceId.slice(0, 8) }}…</router-link>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="error" :label="t('oa.flowtrigger.fire.error')" min-width="160" show-overflow-tooltip />
      </el-table>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import CpTag from '@/components/base/CpTag.vue'
import FlowTriggerDialog from './FlowTriggerDialog.vue'
import { flowTriggerApi, type FlowTriggerItem, type TriggerFireItem } from '@/api/oa/flowTrigger'
import { typeTone, TRIGGER_TYPES } from './flowTriggerModel'

const { t } = useI18n()
const rows = ref<FlowTriggerItem[]>([])
const loading = ref(false)
const toggling = reactive(new Set<string>())
const dialogVisible = ref(false)
const editing = ref<FlowTriggerItem | null>(null)
const firesVisible = ref(false)
const fires = ref<TriggerFireItem[]>([])
const firesLoading = ref(false)

const typeLabelKey = (v: number) => TRIGGER_TYPES.find(x => x.value === v)?.labelKey ?? 'oa.flowtrigger.type.timer'
const fmtUtc = (s?: string | null) => (s ? new Date(s).toLocaleString() : '—')

async function reload() {
  loading.value = true
  try { rows.value = await flowTriggerApi.list() } finally { loading.value = false }
}
onMounted(reload)

function openCreate() { editing.value = null; dialogVisible.value = true }
function openEdit(row: FlowTriggerItem) { editing.value = row; dialogVisible.value = true }

async function onSaved(apiKeyPlain?: string | null) {
  if (apiKeyPlain) showKeyOnce(apiKeyPlain)
  await reload()
}

/** key 一次性显示（spec §3.4：明文只此一次） */
function showKeyOnce(plain: string) {
  ElMessageBox.alert(plain, t('oa.flowtrigger.keyTitle'), {
    confirmButtonText: t('common.ok'),
    message: `${t('oa.flowtrigger.keyOnce')}\n\n${plain}`,
  })
}

async function toggleEnable(row: FlowTriggerItem, v: boolean) {
  if (toggling.has(row.id)) return
  toggling.add(row.id)
  try { await flowTriggerApi.enable(row.id, v); row.enabled = v } 
  catch {
    // http 拦截器已 toast，无需重复提示
  }
  finally { toggling.delete(row.id); await reload() }
}

async function manualFire(row: FlowTriggerItem) {
  try {
    const r = await flowTriggerApi.manualFire(row.id)
    ElMessage.success(`${t('oa.flowtrigger.fired')}: ${r.instanceId ?? ''}`)
    await reload()
  } catch {
    // http 拦截器已 toast，无需重复提示
  }
}

async function resetKey(row: FlowTriggerItem) {
  await ElMessageBox.confirm(t('oa.flowtrigger.resetKeyConfirm'), t('oa.flowtrigger.resetKey'))
  const r = await flowTriggerApi.resetKey(row.id)
  showKeyOnce(r.apiKeyPlain)
}

async function openFires(row: FlowTriggerItem) {
  firesVisible.value = true
  firesLoading.value = true
  try { fires.value = await flowTriggerApi.fires(row.id, 20) } finally { firesLoading.value = false }
}
</script>

<style scoped>
.panel-actions { display: flex; justify-content: flex-end; gap: 8px; margin-bottom: 12px; }
</style>
```

- [ ] **Step 6: FlowTriggerDialog.vue**（分型表单：timer=cron+预设+预览 / event=eventKey+varsMap 键值编辑 / message=varsSchema 白名单；el-dialog 范式照 `SendBackDialog.vue`）：

```vue
<template>
  <el-dialog :model-value="modelValue" :title="editing ? t('common.edit') : t('oa.flowtrigger.new')"
             width="560px" @close="onClose">
    <el-form label-width="120px">
      <el-form-item :label="t('oa.flowtrigger.form.type')">
        <el-radio-group v-model="form.triggerType" :disabled="!!editing">
          <el-radio v-for="ty in TRIGGER_TYPES" :key="ty.value" :value="ty.value">{{ t(ty.labelKey) }}</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item :label="t('oa.flowtrigger.form.flowKey')">
        <el-input v-model="form.flowKey" />
      </el-form-item>
      <el-form-item :label="t('oa.flowtrigger.form.starter')">
        <el-input v-model="form.starterUserId" :placeholder="t('oa.flowtrigger.form.starterHint')" />
      </el-form-item>

      <!-- timer -->
      <template v-if="form.triggerType === 0">
        <el-form-item :label="t('oa.flowtrigger.form.cronPreset')">
          <el-select v-model="preset" clearable @change="applyPreset">
            <el-option v-for="p in CRON_PRESETS" :key="p.cron" :value="p.cron" :label="t(p.labelKey)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.cron')">
          <el-input v-model="form.cron" placeholder="0 9 * * *" @change="loadPreview" />
          <div class="cron-preview">
            <div>{{ t('oa.flowtrigger.form.previewTz') }}</div>
            <div v-for="d in preview" :key="d">{{ new Date(d).toLocaleString() }}</div>
          </div>
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.varsJson')">
          <el-input v-model="form.varsJson" type="textarea" :rows="2" placeholder='{"a":1}' />
        </el-form-item>
      </template>

      <!-- event -->
      <template v-if="form.triggerType === 1">
        <el-form-item :label="t('oa.flowtrigger.form.eventKey')">
          <el-input v-model="form.eventKey" placeholder="WMS|OnShipmentConfirmedAsync" />
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.varsMap')">
          <div v-for="(pair, i) in varsMapPairs" :key="i" class="kv-row">
            <el-input v-model="pair.k" :placeholder="t('oa.flowtrigger.form.varName')" />
            <el-input v-model="pair.v" placeholder="$.OutboundNo" />
            <el-button link type="danger" @click="varsMapPairs.splice(i, 1)">✕</el-button>
          </div>
          <el-button link type="primary" @click="varsMapPairs.push({ k: '', v: '' })">+ {{ t('common.add') }}</el-button>
        </el-form-item>
      </template>

      <!-- message -->
      <template v-if="form.triggerType === 2">
        <el-form-item :label="t('oa.flowtrigger.form.varsSchema')">
          <el-input v-model="varsSchemaText" :placeholder="t('oa.flowtrigger.form.varsSchemaHint')" />
        </el-form-item>
        <el-alert v-if="!editing" type="info" :closable="false" show-icon>{{ t('oa.flowtrigger.keyCreateHint') }}</el-alert>
      </template>

      <el-form-item :label="t('oa.flowtrigger.col.enabled')">
        <el-switch v-model="form.enabled" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <el-button type="primary" :loading="saving" @click="onSave">{{ t('common.save') }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { flowTriggerApi, type FlowTriggerItem } from '@/api/oa/flowTrigger'
import { TRIGGER_TYPES, CRON_PRESETS, validateTriggerForm, buildConfigJson } from './flowTriggerModel'

const props = defineProps<{ modelValue: boolean; editing: FlowTriggerItem | null }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; saved: [string | null | undefined] }>()
const { t } = useI18n()

const form = reactive({ triggerType: 0, flowKey: '', starterUserId: '', cron: '', varsJson: '', eventKey: '', enabled: true })
const varsMapPairs = ref<{ k: string; v: string }[]>([])
const varsSchemaText = ref('')
const preset = ref('')
const preview = ref<string[]>([])
const saving = ref(false)

watch(() => props.modelValue, open => { if (open) hydrate() })

function hydrate() {
  preview.value = []; preset.value = ''
  const e = props.editing
  if (!e) {
    Object.assign(form, { triggerType: 0, flowKey: '', starterUserId: '', cron: '', varsJson: '', eventKey: '', enabled: true })
    varsMapPairs.value = []; varsSchemaText.value = ''
    return
  }
  const cfg = JSON.parse(e.configJson || '{}')
  Object.assign(form, {
    triggerType: e.triggerType, flowKey: e.flowKey, starterUserId: e.starterUserId,
    cron: cfg.cron ?? '', varsJson: cfg.varsJson ?? '', eventKey: e.eventKey ?? '', enabled: e.enabled,
  })
  varsMapPairs.value = Object.entries(cfg.varsMap ?? {}).map(([k, v]) => ({ k, v: String(v) }))
  varsSchemaText.value = (cfg.varsSchema ?? []).join(',')
  if (form.cron) loadPreview()
}

function applyPreset(cron: string) { if (cron) { form.cron = cron; loadPreview() } }

async function loadPreview() {
  if (!form.cron) { preview.value = []; return }
  try { preview.value = (await flowTriggerApi.cronPreview(form.cron)).next } catch { preview.value = [] }
}

function onClose() { emit('update:modelValue', false) }

async function onSave() {
  const errs = validateTriggerForm({ ...form, triggerType: form.triggerType })
  if (errs.length) { ElMessage.warning(t(errs[0])); return }
  const body = {
    flowKey: form.flowKey, triggerType: form.triggerType, enabled: form.enabled,
    eventKey: form.triggerType === 1 ? form.eventKey : null,
    starterUserId: form.starterUserId,
    configJson: buildConfigJson({
      triggerType: form.triggerType, cron: form.cron, varsJson: form.varsJson || undefined,
      varsMap: Object.fromEntries(varsMapPairs.value.filter(p => p.k).map(p => [p.k, p.v])),
      varsSchema: varsSchemaText.value.split(',').map(s => s.trim()).filter(Boolean),
    }),
  }
  saving.value = true
  try {
    if (props.editing) { await flowTriggerApi.update(props.editing.id, body); emit('saved', null) }
    else { const r = await flowTriggerApi.create(body); emit('saved', r.apiKeyPlain) }
    onClose()
  } catch {
    // http 拦截器已 toast，无需重复提示
  }
  finally { saving.value = false }
}
</script>

<style scoped>
.kv-row { display: flex; gap: 8px; margin-bottom: 6px; }
.cron-preview { font-size: 12px; opacity: 0.75; margin-top: 4px; }
</style>
```

- [ ] **Step 7: 验证 + commit**

```bash
cd cp6.web && npm run test -- flowTriggerModel && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-trigger): E-T2 流程管理页触发器 tab+分型表单+流水抽屉+key 一次性显示+vitest"
```

---


---
## 附: 前端现状锚点
| 前端 | 流程管理页=`cp6.web/src/views/oa/admin/FlowAdmin.vue`（97 行，CpPageShell+CpListPage，**当前无 tab**）。API 范式 `cp6.web/src/api/oa/*.ts`（`import http from '../http'`，导出 `xxxApi` 字面量，剥壳 `res.data ?? res`）。CpTag 用 `:tone="'ok'\|'muted'"`；对话框直接 el-dialog（`SendBackDialog.vue` 范本）。 |
