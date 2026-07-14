<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { VueFlow, useVueFlow, Handle, Position } from '@vue-flow/core'
import type {
  Connection, NodeMouseEvent, EdgeMouseEvent,
  Node, Edge, GraphNode, GraphEdge,
} from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import { useI18n } from 'vue-i18n'

import { schemaToGraph, graphToSchema, NODE_PALETTE } from './designerModel'
import type { FlowSchemaDto } from './designerModel'
import StartNode from './nodes/StartNode.vue'
import ApprovalNode from './nodes/ApprovalNode.vue'
import GatewayNode from './nodes/GatewayNode.vue'
import InclusiveGatewayNode from './nodes/InclusiveGatewayNode.vue'
import EndNode from './nodes/EndNode.vue'
import ServiceTaskNode from './nodes/ServiceTaskNode.vue'
import SubFlowNode from './nodes/SubFlowNode.vue'

// ── Props & emits ──────────────────────────────────────────────────
const props = defineProps<{ modelValue: FlowSchemaDto }>()
const emit = defineEmits<{
  'update:modelValue': [value: FlowSchemaDto]
  'select': [val: { kind: 'node' | 'edge' | null; id: string | null }]
}>()

const { t } = useI18n()

// ── Vue Flow store ─────────────────────────────────────────────────
// Calling useVueFlow() here creates the store and provides it in context;
// the <VueFlow> in the template will inject the same store automatically.
const {
  nodes, edges,
  setNodes, setEdges,
  addNodes, addEdges,
  removeNodes, removeEdges,
  getSelectedNodes, getSelectedEdges,
  project, fitView,
} = useVueFlow()

// ── Initialize ─────────────────────────────────────────────────────
{
  const init = schemaToGraph(props.modelValue)
  setNodes(init.nodes as Node[])
  setEdges(init.edges as Edge[])
}

// ── UI state ───────────────────────────────────────────────────────
const showGrid = ref(true)
const vfWrapperRef = ref<HTMLElement | null>(null)

// ── History (undo / redo) ──────────────────────────────────────────
// Store history as serialized JSON strings to avoid deep-type inference issues.
const history = ref<string[]>([])
const histIdx = ref(-1)

function snapshotJson(): string {
  return JSON.stringify({ nodes: nodes.value, edges: edges.value })
}
function restoreSnapshot(json: string) {
  const parsed = JSON.parse(json) as { nodes: Node[]; edges: Edge[] }
  setNodes(parsed.nodes)
  setEdges(parsed.edges)
}

function pushHistory() {
  history.value.splice(histIdx.value + 1)
  history.value.push(snapshotJson())
  histIdx.value = history.value.length - 1
}

const canUndo = computed(() => histIdx.value > 0)
const canRedo = computed(() => histIdx.value < history.value.length - 1)

let skipEmit = false

function undo() {
  if (!canUndo.value) return
  histIdx.value--
  const entry = history.value[histIdx.value]
  if (entry === undefined) return
  skipEmit = true
  restoreSnapshot(entry)
  nextTick(() => { skipEmit = false })
}

function redo() {
  if (!canRedo.value) return
  histIdx.value++
  const entry = history.value[histIdx.value]
  if (entry === undefined) return
  skipEmit = true
  restoreSnapshot(entry)
  nextTick(() => { skipEmit = false })
}

// ── Emit update:modelValue (debounced) ────────────────────────────
let emitTimer: ReturnType<typeof setTimeout> | null = null
let ignoreExternal = false

function scheduleEmit() {
  if (skipEmit || ignoreExternal) return
  if (emitTimer) clearTimeout(emitTimer)
  emitTimer = setTimeout(() => {
    emit('update:modelValue', graphToSchema(nodes.value as Node[], edges.value as Edge[]))
  }, 150)
}

watch([nodes, edges], scheduleEmit, { deep: true })

// Re-init when parent updates modelValue externally
watch(
  () => props.modelValue,
  (val) => {
    ignoreExternal = true
    const g = schemaToGraph(val)
    setNodes(g.nodes as Node[])
    setEdges(g.edges as Edge[])
    nextTick(() => { ignoreExternal = false })
  },
  { deep: true },
)

// ── Connect ────────────────────────────────────────────────────────
function onConnect(conn: Connection) {
  if (!conn.source || !conn.target) return
  pushHistory()
  addEdges([{
    id: `e${conn.source}-${conn.target}-${Date.now()}`,
    source: conn.source,
    target: conn.target,
  }])
}

// ── Selection ──────────────────────────────────────────────────────
function onNodeClick({ node }: NodeMouseEvent) {
  emit('select', { kind: 'node', id: node.id })
}
function onEdgeClick({ edge }: EdgeMouseEvent) {
  emit('select', { kind: 'edge', id: edge.id })
}
function onPaneClick() {
  emit('select', { kind: null, id: null })
}

// ── Delete selected ────────────────────────────────────────────────
function deleteSelected() {
  const selNodes: GraphNode[] = getSelectedNodes.value
  const selEdges: GraphEdge[] = getSelectedEdges.value
  if (!selNodes.length && !selEdges.length) return
  pushHistory()
  if (selNodes.length) {
    removeNodes(selNodes as Node[], true)
  } else if (selEdges.length) {
    removeEdges(selEdges as Edge[])
  }
}

// ── Keyboard shortcuts ─────────────────────────────────────────────
function onKeydown(e: KeyboardEvent) {
  const tag = (e.target as HTMLElement | null)?.tagName?.toLowerCase()
  if (tag === 'input' || tag === 'textarea') return
  if (e.key === 'Delete' || e.key === 'Backspace') {
    deleteSelected()
  } else if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
    e.preventDefault()
    undo()
  } else if ((e.ctrlKey || e.metaKey) && (e.key === 'y' || (e.shiftKey && e.key === 'z'))) {
    e.preventDefault()
    redo()
  }
}

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  pushHistory()
})
onUnmounted(() => {
  document.removeEventListener('keydown', onKeydown)
})

// ── Palette drag-and-drop ──────────────────────────────────────────
// dragKey uniquely identifies a palette entry: `type` for single-type nodes,
// `serviceTask:<kind>` for the three serviceTask entries (same type, distinct kind).
let dragKey = ''

function paletteKey(item: (typeof NODE_PALETTE)[number]): string {
  return 'kind' in item ? `${item.type}:${item.kind}` : item.type
}

function onPaletteDragStart(e: DragEvent, item: (typeof NODE_PALETTE)[number]) {
  dragKey = paletteKey(item)
  e.dataTransfer?.setData('text/plain', dragKey)
}

function onCanvasDragOver(e: DragEvent) {
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy'
}

function onCanvasDrop(e: DragEvent) {
  e.preventDefault()
  const key = dragKey || e.dataTransfer?.getData('text/plain') || ''
  if (!key) return
  const el = vfWrapperRef.value
  if (!el) return
  const rect = el.getBoundingClientRect()
  // project(): converts VueFlow-container-relative coordinates to flow coordinates
  const pos = project({ x: e.clientX - rect.left, y: e.clientY - rect.top })
  const palette = NODE_PALETTE.find((p) => paletteKey(p) === key)
  if (!palette) { dragKey = ''; return }
  const type = palette.type
  // serviceTask 落点预置 serviceKind（D-T1 graphToSchema 会随 data 透传，保证 round-trip）。
  const serviceKind = 'kind' in palette ? palette.kind : undefined
  pushHistory()
  addNodes([
    {
      id: `n${Date.now()}`,
      type,
      position: pos,
      label: palette.label,
      data: { type, name: palette.label, ...(serviceKind ? { serviceKind } : {}) },
    } satisfies Node,
  ])
  dragKey = ''
}

// ── Auto layout (BFS from start node) ─────────────────────────────
function autoLayout() {
  const ns = nodes.value
  const es = edges.value
  if (!ns.length) return

  const startNode = ns.find((n) => n.type === 'start') ?? ns[0]
  if (!startNode) return                     // guarded by ns.length check above; keeps TS happy
  const adj = new Map<string, string[]>()
  ns.forEach((n) => adj.set(n.id, []))
  es.forEach((e) => adj.get(e.source)?.push(e.target))

  const level = new Map<string, number>()
  const queue = [startNode.id]
  level.set(startNode.id, 0)
  while (queue.length) {
    const nid = queue.shift()!
    const lvl = (level.get(nid) ?? 0) + 1
    for (const tid of adj.get(nid) ?? []) {
      if (!level.has(tid)) {
        level.set(tid, lvl)
        queue.push(tid)
      }
    }
  }
  ns.forEach((n) => { if (!level.has(n.id)) level.set(n.id, 0) })

  const byLevel = new Map<number, string[]>()
  level.forEach((lvl, id) => {
    const g = byLevel.get(lvl) ?? []
    g.push(id)
    byLevel.set(lvl, g)
  })

  const xGap = 200
  const yGap = 130
  const maxGroupLen = Math.max(...Array.from(byLevel.values()).map((g) => g.length))
  const canvasWidth = maxGroupLen * xGap

  pushHistory()
  setNodes(
    ns.map((n) => {
      const lvl = level.get(n.id) ?? 0
      const group = byLevel.get(lvl) ?? []
      const idx = group.indexOf(n.id)
      const groupWidth = group.length * xGap
      const offsetX = (canvasWidth - groupWidth) / 2 + idx * xGap
      return { ...n, position: { x: offsetX, y: lvl * yGap + 60 } }
    }) as Node[],
  )
}

// ── Search state ───────────────────────────────────────────────────
const searchQuery = ref('')

const searchResults = computed(() => {
  const q = searchQuery.value.trim().toLowerCase()
  if (!q) return [] as GraphNode[]
  return nodes.value.filter((n) => {
    const data = n.data as Record<string, unknown> | undefined
    return (
      n.id.toLowerCase().includes(q) ||
      String(data?.name ?? '').toLowerCase().includes(q) ||
      String(data?.code ?? '').toLowerCase().includes(q)
    )
  })
})

function focusNode(nodeId: string) {
  fitView({ nodes: [nodeId], maxZoom: 1.5 })
  emit('select', { kind: 'node', id: nodeId })
}
</script>

<template>
  <div class="designer-canvas-root">
    <!-- ── Toolbar ────────────────────────────────────────────────── -->
    <div class="canvas-toolbar">
      <el-button size="small" :disabled="!canUndo" @click="undo" :title="t('oa.designer.undo')">
        ↩ {{ t('oa.designer.undo') }}
      </el-button>
      <el-button size="small" :disabled="!canRedo" @click="redo" :title="t('oa.designer.redo')">
        ↪ {{ t('oa.designer.redo') }}
      </el-button>
      <el-button size="small" @click="autoLayout" :title="t('oa.designer.autoLayout')">
        ⚡ {{ t('oa.designer.autoLayout') }}
      </el-button>
      <el-button size="small" @click="showGrid = !showGrid" :title="t('oa.designer.toggleGrid')">
        ▦ {{ showGrid ? t('oa.designer.hideGrid') : t('oa.designer.showGrid') }}
      </el-button>
      <el-button size="small" type="danger" @click="deleteSelected" :title="t('oa.designer.deleteSelected')">
        🗑 {{ t('oa.designer.deleteSelected') }}
      </el-button>
    </div>

    <!-- ── Main area: palette + canvas ───────────────────────────── -->
    <div class="canvas-body">
      <!-- Left palette -->
      <div class="canvas-palette">
        <!-- Search state -->
        <div class="palette-search">
          <el-input
            v-model="searchQuery"
            size="small"
            clearable
            :placeholder="t('oa.designer.searchState')"
          />
          <div v-if="searchResults.length" class="search-results">
            <div
              v-for="n in searchResults"
              :key="n.id"
              class="search-result-item"
              @click="focusNode(n.id)"
            >
              <span class="sr-id">{{ n.id }}</span>
              <span class="sr-name">{{ (n.data as Record<string, unknown>)?.name as string }}</span>
            </div>
          </div>
        </div>

        <div class="palette-label">{{ t('oa.designer.palette') }}</div>

        <!-- Palette node types -->
        <div
          v-for="item in NODE_PALETTE"
          :key="item.label"
          class="palette-item"
          draggable="true"
          @dragstart="onPaletteDragStart($event, item)"
        >
          <span class="palette-dot" :class="`dot-${item.type}`" />
          {{ item.label }}
        </div>
      </div>

      <!-- VueFlow canvas wrapper -->
      <div
        ref="vfWrapperRef"
        class="canvas-flow-wrap"
        @dragover="onCanvasDragOver"
        @drop="onCanvasDrop"
      >
        <VueFlow
          :delete-key-code="null"
          fit-view-on-init
          @connect="onConnect"
          @node-click="onNodeClick"
          @edge-click="onEdgeClick"
          @pane-click="onPaneClick"
        >
          <!-- Custom node slots -->
          <template #node-start="nodeProps">
            <StartNode v-bind="nodeProps" />
          </template>
          <template #node-approval="nodeProps">
            <ApprovalNode v-bind="nodeProps" />
          </template>
          <template #node-parallelSplit="nodeProps">
            <GatewayNode v-bind="nodeProps" />
          </template>
          <template #node-parallelJoin="nodeProps">
            <GatewayNode v-bind="nodeProps" />
          </template>
          <template #node-inclusiveSplit="nodeProps">
            <InclusiveGatewayNode v-bind="nodeProps" />
          </template>
          <template #node-inclusiveJoin="nodeProps">
            <InclusiveGatewayNode v-bind="nodeProps" />
          </template>
          <template #node-end="nodeProps">
            <EndNode v-bind="nodeProps" />
          </template>
          <template #node-serviceTask="nodeProps">
            <ServiceTaskNode v-bind="nodeProps" />
          </template>
          <template #node-subFlow="nodeProps">
            <SubFlowNode v-bind="nodeProps" />
          </template>

          <!-- Background & Controls -->
          <Background v-if="showGrid" variant="lines" />
          <Controls />
        </VueFlow>
      </div>
    </div>
  </div>
</template>

<style scoped>
.designer-canvas-root {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.canvas-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  background: var(--cp-card);
  border-bottom: 1px solid var(--cp-line);
  flex-shrink: 0;
}

.canvas-body {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.canvas-palette {
  width: 160px;
  flex-shrink: 0;
  background: var(--cp-bg-th);
  border-right: 1px solid var(--cp-line);
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 8px 6px;
  overflow-y: auto;
}

.palette-search {
  margin-bottom: 6px;
}

.search-results {
  margin-top: 4px;
  background: var(--cp-card);
  border: 1px solid var(--cp-line);
  border-radius: var(--cp-r-sm);
  max-height: 120px;
  overflow-y: auto;
}

.search-result-item {
  padding: 4px 8px;
  cursor: pointer;
  font-size: 12px;
  display: flex;
  flex-direction: column;
}
.search-result-item:hover {
  background: var(--cp-bg-hover);
}
.sr-id {
  color: var(--cp-muted);
  font-size: 11px;
}
.sr-name {
  color: var(--cp-text);
}

.palette-label {
  font-size: 11px;
  color: var(--cp-muted);
  padding: 2px 4px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.palette-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  border: 1px solid var(--cp-line);
  border-radius: var(--cp-r-sm);
  background: var(--cp-card);
  cursor: grab;
  font-size: 12px;
  user-select: none;
  transition: box-shadow 0.15s;
}
.palette-item:hover {
  box-shadow: var(--cp-shadow-1);
}
.palette-item:active {
  cursor: grabbing;
}

.palette-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}
/* Palette legend swatches — node-type semantic colors, kept in lockstep
   with the rendered node borders (StartNode/ApprovalNode/GatewayNode/EndNode). */
.dot-start { background: var(--cp-ok); }
.dot-approval { background: var(--cp-info); }
.dot-parallelSplit,
.dot-parallelJoin { background: var(--cp-warn); }
/* inclusive 网关：空心 dot 区分 parallel 实心（BPMN 记号一致性）。 */
.dot-inclusiveSplit,
.dot-inclusiveJoin { background: transparent; border: 2px solid var(--cp-warn); }
.dot-end { background: var(--cp-muted); }
/* serviceTask 三 kind 共用 brand 青，与 ServiceTaskNode 节点身份一致（虚线机器节点）。 */
.dot-serviceTask { background: var(--cp-brand); }
/* subFlow 子流程：info 蓝 + 方形 dot（border-radius 小），与 SubFlowNode 双线容器节点身份一致，
   方形记号区分 approval 同色圆形 dot（形状双重编码，不发新色相）。 */
.dot-subFlow { background: var(--cp-info); border-radius: 2px; }

.canvas-flow-wrap {
  flex: 1;
  min-width: 0;
  min-height: 0;
  position: relative;
}

/* VueFlow fills the wrapper */
.canvas-flow-wrap :deep(.vue-flow) {
  width: 100%;
  height: 100%;
}
</style>
