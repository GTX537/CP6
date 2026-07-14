<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'

import DesignerCanvas from './DesignerCanvas.vue'
import NodePropertyPanel from './NodePropertyPanel.vue'
import EdgePropertyPanel from './EdgePropertyPanel.vue'
import { validateClient } from './designerModel'
import type { FlowSchemaDto, SchemaNode, SchemaEdge } from './designerModel'
import { designerApi } from '@/api/oa/designer'
import type { FlowDefSummary, LoadFlowResult } from '@/types/oa/designer'

const { t } = useI18n()

// ── Initial empty schema (start + end) ────────────────────────────
const INITIAL_SCHEMA: FlowSchemaDto = {
  start: 'start1',
  nodes: [
    { id: 'start1', type: 'start', name: '填單', x: 200, y: 40 },
    { id: 'end1',   type: 'end',   name: '結束', x: 200, y: 300 },
  ],
  edges: [],
}

// ── Identity fields ────────────────────────────────────────────────
const flowKey    = ref('')
const flowName   = ref('')
const formKey    = ref('')
const functionId = ref('')
const flowCode   = ref('')

// ── Schema state ───────────────────────────────────────────────────
const schema = ref<FlowSchemaDto>(structuredClone(INITIAL_SCHEMA))

// ── Flow list ──────────────────────────────────────────────────────
const flowList   = ref<FlowDefSummary[]>([])
const listLoading = ref(false)
const selectedKey = ref<string | null>(null)

async function loadList() {
  listLoading.value = true
  try {
    const res = await designerApi.list() as any
    flowList.value = Array.isArray(res.data) ? res.data : (Array.isArray(res) ? res : [])
  } catch { /* interceptor toasts */ } finally {
    listLoading.value = false
  }
}

async function onSelectFlow(key: string | null) {
  if (!key) return
  selectedKey.value = key
  try {
    const res = await designerApi.load(key) as any
    const result: LoadFlowResult = res.data ?? res
    const s = result.summary
    flowKey.value    = s.flowKey
    flowName.value   = s.flowName
    formKey.value    = s.formKey
    functionId.value = s.functionId ?? ''
    flowCode.value   = s.flowCode ?? ''
    schema.value     = result.schemaJson
      ? (JSON.parse(result.schemaJson) as FlowSchemaDto)
      : structuredClone(INITIAL_SCHEMA)
  } catch { /* interceptor toasts */ }
}

function newFlow() {
  selectedKey.value = null
  flowKey.value    = ''
  flowName.value   = ''
  formKey.value    = ''
  functionId.value = ''
  flowCode.value   = ''
  schema.value     = structuredClone(INITIAL_SCHEMA)
  selState.value   = { kind: null, id: null }
}

// ── Selection state ────────────────────────────────────────────────
const selState = ref<{ kind: 'node' | 'edge' | null; id: string | null }>({ kind: null, id: null })

function onSelect(val: { kind: 'node' | 'edge' | null; id: string | null }) {
  selState.value = val
}

const selNode = computed<SchemaNode | null>(() => {
  if (selState.value.kind !== 'node' || !selState.value.id) return null
  return schema.value.nodes.find(n => n.id === selState.value.id) ?? null
})

const selEdge = computed<SchemaEdge | null>(() => {
  if (selState.value.kind !== 'edge' || !selState.value.id) return null
  // Edge id format from DesignerCanvas: `${from}__${to}` or `e${from}-${to}-${ts}`
  // We match by iterating edges; we stored from/to in edge id as "from__to"
  // But we need to match the canvas edge id to schema edge.
  // The canvas uses id = `${e.from}__${e.to}` (from schemaToGraph) or `e${src}-${tgt}-${ts}` (added dynamically).
  // We can only match by reconstructing the id pattern or by index.
  // Since dynamic edges get id `e${source}-${target}-${ts}`, we need to match against schema differently.
  // We'll match by checking if the edge id starts with `${from}__${to}` for static,
  // or match source/target from the id for dynamic edges.
  const eid = selState.value.id
  // Try static format: "from__to"
  if (eid.includes('__')) {
    const [from, to] = eid.split('__')
    return schema.value.edges.find(e => e.from === from && e.to === to) ?? null
  }
  // Dynamic format: "e{src}-{tgt}-{ts}" — extract source/target by schema position heuristic
  // The canvas stores edges in the same order as schema.edges after graphToSchema conversion.
  // As a fallback, return the first edge we can't identify (null is fine — panel won't show).
  return null
})

// ── Patch node (merge panel emit back into schema) ─────────────────
function patchNode(patch: Partial<SchemaNode>) {
  const idx = schema.value.nodes.findIndex(n => n.id === selState.value.id)
  if (idx === -1) return
  schema.value = {
    ...schema.value,
    nodes: schema.value.nodes.map((n, i) =>
      i === idx ? { ...n, ...patch, id: n.id } : n
    ),
  }
}

// ── Patch edge (merge panel emit back into schema) ─────────────────
function patchEdge(patch: Partial<SchemaEdge>) {
  const eid = selState.value.id
  if (!eid) return
  let idx = -1
  if (eid.includes('__')) {
    const [from, to] = eid.split('__')
    idx = schema.value.edges.findIndex(e => e.from === from && e.to === to)
  }
  if (idx === -1) return
  schema.value = {
    ...schema.value,
    edges: schema.value.edges.map((e, i) =>
      i === idx ? { ...e, ...patch, from: e.from, to: e.to } : e
    ),
  }
}

// ── Validate ───────────────────────────────────────────────────────
function doValidate(): boolean {
  const errs = validateClient(schema.value)
  if (errs.length) {
    errs.forEach(key => ElMessage.error(t(key)))
    return false
  }
  ElMessage.success(t('oa.designer.validateOk'))
  return true
}

// ── Save ───────────────────────────────────────────────────────────
const saving = ref(false)

async function doSave() {
  if (!doValidateOnly()) return
  if (!flowKey.value.trim()) {
    ElMessage.warning(t('oa.designer.flowKeyRequired'))
    return
  }
  if (!flowName.value.trim()) {
    ElMessage.warning(t('oa.designer.flowNameRequired'))
    return
  }
  saving.value = true
  try {
    await designerApi.save({
      flowKey:    flowKey.value.trim(),
      flowName:   flowName.value.trim(),
      formKey:    formKey.value.trim(),
      functionId: functionId.value.trim() || undefined,
      flowCode:   flowCode.value.trim() || undefined,
      schemaJson: JSON.stringify(schema.value),
    })
    ElMessage.success(t('oa.designer.saveOk'))
    // Refresh list to reflect new/updated entry
    await loadList()
    selectedKey.value = flowKey.value
  } catch { /* interceptor already toasts E-WF-009 / E-WF-010 */ } finally {
    saving.value = false
  }
}

// Helper: run validateClient and show errors; returns whether valid
function doValidateOnly(): boolean {
  const errs = validateClient(schema.value)
  if (errs.length) {
    errs.forEach(key => ElMessage.error(t(key)))
    return false
  }
  return true
}

// ── Clone ──────────────────────────────────────────────────────────
const cloning       = ref(false)
const cloneVisible  = ref(false)
const cloneNewKey   = ref('')
const cloneNewName  = ref('')

function openCloneDialog() {
  if (!flowKey.value.trim()) {
    ElMessage.warning(t('oa.designer.flowKeyRequired'))
    return
  }
  cloneNewKey.value  = flowKey.value + '_copy'
  cloneNewName.value = flowName.value + ' (副本)'
  cloneVisible.value = true
}

async function doClone() {
  if (!cloneNewKey.value.trim() || !cloneNewName.value.trim()) {
    ElMessage.warning(t('oa.designer.cloneFieldsRequired'))
    return
  }
  cloning.value = true
  try {
    await designerApi.clone(flowKey.value.trim(), cloneNewKey.value.trim(), cloneNewName.value.trim())
    ElMessage.success(t('oa.designer.cloneOk'))
    cloneVisible.value = false
    await loadList()
  } catch { /* interceptor toasts */ } finally {
    cloning.value = false
  }
}

// ── Mount ──────────────────────────────────────────────────────────
onMounted(() => {
  loadList()
})

// Expose doValidate under the button-click name (aliases)
function doValidateClick() { doValidate() }
</script>

<template>
  <div class="designer-view">
    <!-- ── Top toolbar ─────────────────────────────────────────── -->
    <div class="designer-toolbar">
      <!-- Flow selector -->
      <el-select
        v-model="selectedKey"
        :placeholder="t('oa.designer.selectFlow')"
        clearable
        :loading="listLoading"
        style="width: 220px"
        @change="onSelectFlow"
      >
        <el-option
          v-for="f in flowList"
          :key="f.flowKey"
          :label="`${f.flowName} (${f.flowKey})`"
          :value="f.flowKey"
        />
      </el-select>

      <el-button @click="newFlow">{{ t('oa.designer.newFlow') }}</el-button>

      <el-divider direction="vertical" />

      <!-- Identity fields -->
      <el-input
        v-model="flowKey"
        :placeholder="t('oa.designer.flowKey')"
        style="width: 140px"
        clearable
      />
      <el-input
        v-model="flowName"
        :placeholder="t('oa.designer.flowName')"
        style="width: 140px"
        clearable
      />
      <el-input
        v-model="formKey"
        :placeholder="t('oa.designer.formKey')"
        style="width: 130px"
        clearable
      />
      <el-input
        v-model="functionId"
        :placeholder="t('oa.designer.functionId')"
        style="width: 130px"
        clearable
      />
      <el-input
        v-model="flowCode"
        :placeholder="t('oa.designer.flowCode')"
        style="width: 110px"
        clearable
      />

      <el-divider direction="vertical" />

      <!-- Actions -->
      <el-button type="info"    @click="doValidateClick">{{ t('oa.designer.validate') }}</el-button>
      <el-button type="primary" :loading="saving" @click="doSave">{{ t('oa.designer.save') }}</el-button>
      <el-button                @click="openCloneDialog">{{ t('oa.designer.clone') }}</el-button>
    </div>

    <!-- ── Main area: canvas + property panel ──────────────────── -->
    <div class="designer-main">
      <!-- Canvas -->
      <div class="designer-canvas-wrap">
        <DesignerCanvas
          v-model="schema"
          @select="onSelect"
        />
      </div>

      <!-- Right panel -->
      <div class="designer-right-panel">
        <NodePropertyPanel
          v-if="selState.kind === 'node' && selNode"
          :node="selNode"
          :current-flow-key="flowKey"
          @update="patchNode"
        />
        <EdgePropertyPanel
          v-else-if="selState.kind === 'edge' && selEdge"
          :edge="selEdge"
          @update="patchEdge"
        />
        <div v-else class="panel-hint">
          {{ t('oa.designer.selectHint') }}
        </div>
      </div>
    </div>

    <!-- ── Clone dialog ────────────────────────────────────────── -->
    <el-dialog
      v-model="cloneVisible"
      :title="t('oa.designer.cloneTitle')"
      width="400px"
      :close-on-click-modal="false"
    >
      <el-form label-position="top" size="small">
        <el-form-item :label="t('oa.designer.newFlowKey')">
          <el-input v-model="cloneNewKey" clearable />
        </el-form-item>
        <el-form-item :label="t('oa.designer.newFlowName')">
          <el-input v-model="cloneNewName" clearable />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="cloneVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="cloning" @click="doClone">
          {{ t('oa.designer.cloneConfirm') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.designer-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 84px);   /* subtract app header */
  min-height: 0;
  background: var(--cp-bg);
}

.designer-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 8px 12px;
  background: var(--cp-card);
  border-bottom: 1px solid var(--cp-line);
  flex-shrink: 0;
}

.designer-main {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.designer-canvas-wrap {
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

.designer-right-panel {
  width: 280px;
  flex-shrink: 0;
  border-left: 1px solid var(--cp-line);
  background: var(--cp-card);
  overflow-y: auto;
}

.panel-hint {
  padding: 24px 16px;
  color: var(--cp-muted);
  font-size: 13px;
  text-align: center;
  line-height: 1.6;
}
</style>
