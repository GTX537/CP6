<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import {
  Check,
  Close,
  Connection,
  CopyDocument,
  Document,
  DocumentAdd,
  EditPen,
  Search,
  Setting,
} from '@element-plus/icons-vue'

import { designerApi } from '@/api/oa/designer'
import type { FlowDefSummary, LoadFlowResult } from '@/types/oa/designer'
import DesignerCanvas from './DesignerCanvas.vue'
import EdgePropertyPanel from './EdgePropertyPanel.vue'
import NodePropertyPanel from './NodePropertyPanel.vue'
import { isFallbackEdge, validateClient, type FlowSchemaDto, type SchemaEdge, type SchemaNode } from './designerModel'

const { t } = useI18n()

const INITIAL_SCHEMA: FlowSchemaDto = {
  start: 'start1',
  nodes: [
    { id: 'start1', type: 'start', name: '填单（发起）', x: 260, y: 60 },
    { id: 'end1', type: 'end', name: '结束', x: 260, y: 330 },
  ],
  edges: [],
}

const flowKey = ref('')
const flowName = ref('')
const formKey = ref('')
const functionId = ref('')
const flowCode = ref('')
const schema = ref<FlowSchemaDto>(structuredClone(INITIAL_SCHEMA))
const flowList = ref<FlowDefSummary[]>([])
const listLoading = ref(false)
const selectedKey = ref<string | null>(null)
const loadedVersion = ref(0)
const flowSearch = ref('')
const saving = ref(false)
const publishing = ref(false)
const draftRowVersion = ref<string>()
const savedSnapshot = ref('')

const selState = ref<{ kind: 'node' | 'edge' | null; id: string | null }>({ kind: null, id: null })

function editorSnapshot() {
  return JSON.stringify({
    flowKey: flowKey.value,
    flowName: flowName.value,
    formKey: formKey.value,
    functionId: functionId.value,
    flowCode: flowCode.value,
    schema: schema.value,
  })
}

function markSaved() {
  savedSnapshot.value = editorSnapshot()
}

const isDirty = computed(() => savedSnapshot.value !== '' && savedSnapshot.value !== editorSnapshot())
const filteredFlows = computed(() => {
  const query = flowSearch.value.trim().toLowerCase()
  if (!query) return flowList.value
  return flowList.value.filter(flow =>
    flow.flowName.toLowerCase().includes(query)
    || flow.flowKey.toLowerCase().includes(query)
    || String(flow.flowCode ?? '').toLowerCase().includes(query),
  )
})

function hydrateSchema(value: FlowSchemaDto): FlowSchemaDto {
  return {
    ...value,
    nodes: value.nodes ?? [],
    edges: (value.edges ?? []).map((edge, index) => ({
      ...edge,
      id: edge.id || `${edge.from}__${edge.to}__${index}`,
    })),
  }
}

async function loadList() {
  listLoading.value = true
  try {
    const response = await designerApi.list() as any
    flowList.value = Array.isArray(response.data) ? response.data : (Array.isArray(response) ? response : [])
  } catch {
    // HTTP interceptor displays the request error.
  } finally {
    listLoading.value = false
  }
}

async function onSelectFlow(key: string | null) {
  if (!key) return
  selectedKey.value = key
  try {
    const response = await designerApi.load(key) as any
    const result: LoadFlowResult = response.data ?? response
    const summary = result.summary
    flowKey.value = summary.flowKey
    flowName.value = summary.flowName
    formKey.value = summary.formKey ?? ''
    functionId.value = summary.functionId ?? ''
    flowCode.value = summary.flowCode ?? ''
    loadedVersion.value = summary.version
    draftRowVersion.value = result.draft?.rowVersion
    schema.value = result.schemaJson
      ? hydrateSchema(JSON.parse(result.schemaJson) as FlowSchemaDto)
      : structuredClone(INITIAL_SCHEMA)
    selState.value = { kind: null, id: null }
    markSaved()
  } catch {
    // HTTP interceptor displays the request error.
  }
}

function newFlow() {
  selectedKey.value = null
  flowKey.value = ''
  flowName.value = ''
  formKey.value = ''
  functionId.value = ''
  flowCode.value = ''
  loadedVersion.value = 0
  draftRowVersion.value = undefined
  schema.value = structuredClone(INITIAL_SCHEMA)
  selState.value = { kind: null, id: null }
  markSaved()
}

function onSelect(value: { kind: 'node' | 'edge' | null; id: string | null }) {
  selState.value = value
}

function clearSelection() {
  selState.value = { kind: null, id: null }
}

const selNode = computed<SchemaNode | null>(() => {
  if (selState.value.kind !== 'node' || !selState.value.id) return null
  return schema.value.nodes.find(node => node.id === selState.value.id) ?? null
})

function selectedEdgeIndex() {
  if (selState.value.kind !== 'edge' || !selState.value.id) return -1
  const selectedId = selState.value.id
  return schema.value.edges.findIndex((edge, index) =>
    edge.id === selectedId || `${edge.from}__${edge.to}__${index}` === selectedId,
  )
}

const selEdge = computed<SchemaEdge | null>(() => {
  const index = selectedEdgeIndex()
  return index >= 0 ? schema.value.edges[index] ?? null : null
})

function patchNode(patch: Partial<SchemaNode>) {
  const index = schema.value.nodes.findIndex(node => node.id === selState.value.id)
  if (index < 0) return
  schema.value = {
    ...schema.value,
    nodes: schema.value.nodes.map((node, nodeIndex) =>
      nodeIndex === index ? { ...node, ...patch, id: node.id } : node,
    ),
  }
}

function normalizeSchemaOutgoing(input: SchemaEdge[], sourceId: string, preferredFallbackId?: string): SchemaEdge[] {
  const outgoing = input.filter(edge => edge.from === sourceId && edge.isError !== true)
  if (!outgoing.length) return input
  const fallbackId = preferredFallbackId && outgoing.some(edge => edge.id === preferredFallbackId)
    ? preferredFallbackId
    : outgoing.find(isFallbackEdge)?.id ?? outgoing.at(-1)?.id
  const conditional = outgoing
    .filter(edge => edge.id !== fallbackId)
    .sort((a, b) => (a.priority ?? Number.MAX_SAFE_INTEGER) - (b.priority ?? Number.MAX_SAFE_INTEGER))
  const fallback = outgoing.find(edge => edge.id === fallbackId)
  const ordered = fallback ? [...conditional, fallback] : conditional
  const priorityById = new Map(ordered.map((edge, index) => [edge.id, index + 1]))

  return input.map(edge => {
    if (edge.from !== sourceId || edge.isError === true) return edge
    const fallbackEdge = edge.id === fallbackId
    return {
      ...edge,
      condition: fallbackEdge ? undefined : (edge.condition?.trim() || 'false'),
      priority: priorityById.get(edge.id),
    }
  })
}

function patchEdge(patch: Partial<SchemaEdge>) {
  const index = selectedEdgeIndex()
  if (index < 0) return
  const current = schema.value.edges[index]!
  let nextEdges = schema.value.edges.map((edge, edgeIndex) =>
    edgeIndex === index
      ? { ...edge, ...patch, id: edge.id, from: edge.from, to: edge.to }
      : edge,
  )
  const next = nextEdges[index]!
  const wantsFallback = next.isError !== true && !next.condition?.trim()
  const wasOnlyFallback = isFallbackEdge(current)
    && !nextEdges.some((edge, edgeIndex) => edgeIndex !== index && edge.from === current.from && isFallbackEdge(edge))
  if (!wantsFallback && wasOnlyFallback) {
    nextEdges[index] = { ...next, condition: undefined }
  }
  nextEdges = normalizeSchemaOutgoing(nextEdges, current.from, wantsFallback ? current.id : undefined)
  schema.value = { ...schema.value, edges: nextEdges }
}

function doValidateOnly(): boolean {
  const errors = validateClient(schema.value)
  if (errors.length) {
    errors.forEach(key => ElMessage.error(t(key)))
    return false
  }
  return true
}

function doValidate() {
  if (doValidateOnly()) ElMessage.success(t('oa.designer.validateOk'))
}

async function doSave(): Promise<boolean> {
  if (!doValidateOnly()) return false
  if (!flowKey.value.trim()) {
    ElMessage.warning(t('oa.designer.flowKeyRequired'))
    return false
  }
  if (!flowName.value.trim()) {
    ElMessage.warning(t('oa.designer.flowNameRequired'))
    return false
  }
  saving.value = true
  try {
    const response = await designerApi.save({
      flowKey: flowKey.value.trim(),
      flowName: flowName.value.trim(),
      formKey: formKey.value.trim(),
      functionId: functionId.value.trim() || undefined,
      flowCode: flowCode.value.trim() || undefined,
      schemaJson: JSON.stringify(schema.value),
      rowVersion: draftRowVersion.value,
    })
    draftRowVersion.value = (response as any)?.data?.rowVersion
    ElMessage.success(t('oa.designer.saveOk'))
    await loadList()
    selectedKey.value = flowKey.value
    const summary = flowList.value.find(flow => flow.flowKey === flowKey.value)
    loadedVersion.value = summary?.version ?? loadedVersion.value
    markSaved()
    return true
  } catch {
    // HTTP interceptor displays validation and conflict errors.
    return false
  } finally {
    saving.value = false
  }
}

async function doPublish() {
  if (isDirty.value && !await doSave()) return
  if (!flowKey.value || !draftRowVersion.value) {
    ElMessage.warning(t('请先保存草稿'))
    return
  }
  publishing.value = true
  try {
    const response = await designerApi.publish(flowKey.value, draftRowVersion.value) as any
    loadedVersion.value = response?.data?.version ?? loadedVersion.value
    ElMessage.success(t('发布成功'))
    await onSelectFlow(flowKey.value)
  } finally {
    publishing.value = false
  }
}

const cloning = ref(false)
const cloneVisible = ref(false)
const cloneNewKey = ref('')
const cloneNewName = ref('')

function openCloneDialog() {
  if (!flowKey.value.trim()) {
    ElMessage.warning(t('oa.designer.flowKeyRequired'))
    return
  }
  cloneNewKey.value = `${flowKey.value}_copy`
  cloneNewName.value = `${flowName.value}（副本）`
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
  } catch {
    // HTTP interceptor displays the request error.
  } finally {
    cloning.value = false
  }
}

onMounted(async () => {
  markSaved()
  await loadList()
})
</script>

<template>
  <div class="designer-view">
    <header class="designer-header">
      <div class="header-identity">
        <span class="header-icon"><el-icon><Connection /></el-icon></span>
        <div>
          <small>OA 工作流 · 流程设计器</small>
          <div class="title-line">
            <h1>{{ flowName || '新建流程' }}</h1>
            <span v-if="loadedVersion">V{{ loadedVersion }}</span>
            <em :class="{ dirty: isDirty }">{{ isDirty ? '有未保存更改' : '已同步' }}</em>
          </div>
        </div>
      </div>
      <div class="header-actions">
        <el-button :icon="Check" @click="doValidate">校验</el-button>
        <el-button v-permission="'oa-designer:add'" :icon="CopyDocument" @click="openCloneDialog">另存副本</el-button>
        <el-button v-permission="'oa-designer:edit'" :loading="saving" @click="doSave">保存草稿</el-button>
        <el-button v-permission="'oa-designer:publish'" type="primary" :loading="publishing" @click="doPublish">发布</el-button>
      </div>
    </header>

    <section class="identity-strip" aria-label="流程基础信息">
      <label><span>流程标识 *</span><el-input v-model="flowKey" clearable placeholder="例如 leave_request" /></label>
      <label><span>流程名称 *</span><el-input v-model="flowName" clearable placeholder="请输入流程名称" /></label>
      <label><span>表单标识</span><el-input v-model="formKey" clearable placeholder="关联业务表单" /></label>
      <label><span>功能 ID</span><el-input v-model="functionId" clearable placeholder="可选" /></label>
      <label><span>流程编号</span><el-input v-model="flowCode" clearable placeholder="可选" /></label>
    </section>

    <main class="designer-workbench">
      <aside class="flow-library">
        <div class="library-heading">
          <div><small>流程资产</small><strong>流程库</strong></div>
          <el-tooltip content="新建流程" placement="bottom">
            <el-button circle type="primary" :icon="DocumentAdd" aria-label="新建流程" @click="newFlow" />
          </el-tooltip>
        </div>
        <el-input v-model="flowSearch" clearable :prefix-icon="Search" placeholder="搜索名称、标识或编号" />
        <div v-loading="listLoading" class="flow-list">
          <button
            v-for="flow in filteredFlows"
            :key="flow.flowKey"
            type="button"
            :class="{ active: selectedKey === flow.flowKey }"
            @click="onSelectFlow(flow.flowKey)"
          >
            <span class="flow-file"><el-icon><Document /></el-icon></span>
            <span class="flow-copy"><strong>{{ flow.flowName }}</strong><small>{{ flow.flowKey }}</small></span>
            <span class="flow-meta"><b>V{{ flow.version }}</b><i :class="{ enabled: flow.enable }" /></span>
          </button>
          <div v-if="!listLoading && !filteredFlows.length" class="flow-empty">没有匹配的流程</div>
        </div>
        <footer><span>{{ flowList.length }} 个流程</span><b><i />已启用</b></footer>
      </aside>

      <section class="designer-canvas-wrap">
        <DesignerCanvas v-model="schema" @select="onSelect" />
      </section>

      <aside class="designer-right-panel" :class="{ active: selState.kind }">
        <div class="inspector-heading">
          <span><el-icon><component :is="selState.kind === 'edge' ? Connection : (selState.kind === 'node' ? EditPen : Setting)" /></el-icon></span>
          <div>
            <small>{{ selState.kind === 'edge' ? '路径配置' : (selState.kind === 'node' ? '节点配置' : '属性面板') }}</small>
            <strong>{{ selEdge?.name || selNode?.name || '选择节点或路径' }}</strong>
          </div>
          <el-button text :icon="Close" aria-label="关闭属性面板" @click="clearSelection" />
        </div>
        <div class="inspector-content">
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
            <el-icon><Setting /></el-icon>
            <strong>选择节点或路径</strong>
            <span>在画布中选中对象后配置详细属性</span>
          </div>
        </div>
      </aside>
    </main>

    <el-dialog v-model="cloneVisible" :title="t('oa.designer.cloneTitle')" width="400px" :close-on-click-modal="false">
      <el-form label-position="top" size="small">
        <el-form-item :label="t('oa.designer.newFlowKey')"><el-input v-model="cloneNewKey" clearable /></el-form-item>
        <el-form-item :label="t('oa.designer.newFlowName')"><el-input v-model="cloneNewName" clearable /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="cloneVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="cloning" @click="doClone">{{ t('oa.designer.cloneConfirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.designer-view { display: flex; flex-direction: column; width: 100%; height: 100%; min-height: 0; overflow: hidden; background: #eef4f5; color: var(--cp-text); }
.designer-header { min-height: 66px; padding: 9px 18px; display: flex; align-items: center; justify-content: space-between; gap: 18px; flex-shrink: 0; border-bottom: 1px solid var(--cp-line); background: var(--cp-card); }
.header-identity { min-width: 0; display: flex; align-items: center; gap: 11px; }
.header-icon { width: 40px; height: 40px; display: grid; place-items: center; flex-shrink: 0; border-radius: 6px; background: var(--cp-brand-bg); color: var(--cp-brand); font-size: 20px; }
.header-identity small { display: block; margin-bottom: 2px; color: var(--cp-muted); font-size: 10px; }
.title-line { display: flex; align-items: center; gap: 8px; min-width: 0; }
.title-line h1 { max-width: min(420px, 35vw); margin: 0; overflow: hidden; color: var(--cp-ink); font-size: 18px; line-height: 1.25; text-overflow: ellipsis; white-space: nowrap; }
.title-line > span { padding: 2px 5px; border: 1px solid #c7d9dc; border-radius: 3px; color: #5e7b82; font-size: 9px; font-weight: 700; }
.title-line em { display: flex; align-items: center; gap: 4px; color: #2b8965; font-size: 10px; font-style: normal; white-space: nowrap; }
.title-line em::before { width: 6px; height: 6px; border-radius: 50%; background: #31a77a; content: ''; }
.title-line em.dirty { color: #ad741e; }
.title-line em.dirty::before { background: #dc962b; }
.header-actions { display: flex; align-items: center; flex-shrink: 0; }
.identity-strip { padding: 7px 12px 8px; display: grid; grid-template-columns: 1.1fr 1.15fr 1fr 1fr .85fr; gap: 8px; flex-shrink: 0; border-bottom: 1px solid var(--cp-line); background: #f8fbfb; }
.identity-strip label { min-width: 0; }
.identity-strip label > span { display: block; margin: 0 0 3px 2px; color: #6d8389; font-size: 9px; font-weight: 600; }
.identity-strip :deep(.el-input__wrapper) { min-height: 30px; box-shadow: 0 0 0 1px #dbe5e7 inset; }
.designer-workbench { position: relative; display: grid; grid-template-columns: 228px minmax(500px, 1fr) 320px; flex: 1; min-height: 0; overflow: hidden; }
.flow-library { min-width: 0; min-height: 0; display: flex; flex-direction: column; overflow: hidden; border-right: 1px solid var(--cp-line); background: #f8fbfb; }
.library-heading { min-height: 63px; padding: 10px 12px; display: flex; align-items: center; justify-content: space-between; }
.library-heading small, .library-heading strong { display: block; }
.library-heading small { margin-bottom: 2px; color: var(--cp-muted); font-size: 9px; }
.library-heading strong { color: var(--cp-ink); font-size: 14px; }
.flow-library > :deep(.el-input) { width: calc(100% - 20px); margin: 0 10px 9px; }
.flow-list { min-height: 0; padding: 0 7px; flex: 1; overflow-y: auto; }
.flow-list button { width: 100%; min-height: 58px; margin-bottom: 4px; padding: 7px 7px; display: grid; grid-template-columns: 32px minmax(0, 1fr) auto; align-items: center; gap: 7px; border: 1px solid transparent; border-radius: 5px; background: transparent; color: var(--cp-text); text-align: left; cursor: pointer; }
.flow-list button:hover { border-color: #d2e0e2; background: #fff; }
.flow-list button.active { border-color: #8dcfd1; background: #e9f7f7; }
.flow-file { width: 32px; height: 32px; display: grid; place-items: center; border-radius: 5px; background: #e8f1f2; color: #4d7880; }
.flow-list button.active .flow-file { background: #d4eeee; color: var(--cp-brand); }
.flow-copy { min-width: 0; }
.flow-copy strong, .flow-copy small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.flow-copy strong { font-size: 11px; }
.flow-copy small { margin-top: 4px; color: var(--cp-muted); font-size: 9px; }
.flow-meta { display: grid; justify-items: end; gap: 7px; }
.flow-meta b { color: #7f9297; font-size: 8px; }
.flow-meta i { width: 7px; height: 7px; border-radius: 50%; background: #b8c3c6; }
.flow-meta i.enabled { background: #2eaa7d; }
.flow-empty { padding: 40px 10px; color: var(--cp-muted); font-size: 11px; text-align: center; }
.flow-library footer { min-height: 38px; padding: 0 11px; display: flex; align-items: center; justify-content: space-between; flex-shrink: 0; border-top: 1px solid var(--cp-line); color: var(--cp-muted); font-size: 9px; }
.flow-library footer b { display: flex; align-items: center; gap: 5px; color: #4f7770; }
.flow-library footer i { width: 6px; height: 6px; border-radius: 50%; background: #2eaa7d; }
.designer-canvas-wrap { min-width: 0; min-height: 0; overflow: hidden; }
.designer-right-panel { min-width: 0; min-height: 0; height: 100%; display: flex; flex-direction: column; overflow: hidden; border-left: 1px solid var(--cp-line); background: var(--cp-card); }
.inspector-heading { min-height: 63px; padding: 9px 10px; display: grid; grid-template-columns: 36px minmax(0, 1fr) 30px; align-items: center; gap: 8px; flex-shrink: 0; border-bottom: 1px solid var(--cp-line); }
.inspector-heading > span { width: 36px; height: 36px; display: grid; place-items: center; border-radius: 5px; background: var(--cp-brand-bg); color: var(--cp-brand); }
.inspector-heading small, .inspector-heading strong { display: block; }
.inspector-heading small { margin-bottom: 3px; color: var(--cp-muted); font-size: 9px; }
.inspector-heading strong { overflow: hidden; color: var(--cp-ink); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.inspector-content { min-height: 0; display: flex; flex: 1; overflow: hidden; }
.panel-hint { height: 100%; padding: 28px; display: grid; place-items: center; align-content: center; gap: 8px; color: var(--cp-muted); text-align: center; }
.panel-hint .el-icon { font-size: 27px; }
.panel-hint strong { color: #637a80; font-size: 12px; }
.panel-hint span { font-size: 10px; }
@media (max-width: 1420px) {
  .designer-workbench { grid-template-columns: 206px minmax(460px, 1fr) 300px; }
  .title-line h1 { max-width: 300px; }
}
@media (max-width: 1180px) {
  .designer-workbench { grid-template-columns: minmax(0, 1fr) 300px; }
  .flow-library { display: none; }
  .identity-strip { grid-template-columns: repeat(3, minmax(120px, 1fr)); }
}
@media (max-width: 900px) {
  .designer-view { height: 100%; min-height: 0; }
  .designer-header { min-height: 58px; padding: 7px 10px; }
  .header-icon { width: 34px; height: 34px; }
  .header-identity small, .title-line > span, .title-line em { display: none; }
  .title-line h1 { max-width: 30vw; font-size: 15px; }
  .header-actions .el-button:first-child { display: none; }
  .identity-strip { grid-template-columns: repeat(2, minmax(100px, 1fr)); max-height: 118px; overflow-y: auto; }
  .designer-workbench { display: block; }
  .designer-canvas-wrap { width: 100%; height: 100%; }
  .designer-right-panel { position: absolute; z-index: 25; top: 0; right: 0; bottom: 0; width: min(330px, 88vw); display: none; box-shadow: -12px 0 30px rgb(31 58 66 / 14%); }
  .designer-right-panel.active { display: flex; }
}
</style>
