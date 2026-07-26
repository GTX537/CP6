<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { Background } from '@vue-flow/background'
import {
  ConnectionMode,
  MarkerType,
  VueFlow,
  useVueFlow,
  type Connection,
  type Edge,
  type EdgeMouseEvent,
  type EdgeUpdateEvent,
  type GraphEdge,
  type GraphNode,
  type Node,
  type NodeDragEvent,
  type NodeMouseEvent,
} from '@vue-flow/core'
import {
  Aim,
  CircleCheck,
  CloseBold,
  Connection as ConnectionIcon,
  Delete,
  DocumentCopy,
  EditPen,
  Flag,
  Grid,
  Link,
  MagicStick,
  Plus,
  Rank,
  RefreshLeft,
  RefreshRight,
  Search,
  Share,
  Timer,
  Tools,
  User,
  VideoPlay,
  ZoomIn,
  ZoomOut,
} from '@element-plus/icons-vue'

import {
  NODE_PALETTE,
  graphToSchema,
  isFallbackEdge,
  schemaToGraph,
  type FlowSchemaDto,
  type SchemaEdge,
} from './designerModel'
import ApprovalNode from './nodes/ApprovalNode.vue'
import EndNode from './nodes/EndNode.vue'
import GatewayNode from './nodes/GatewayNode.vue'
import InclusiveGatewayNode from './nodes/InclusiveGatewayNode.vue'
import ServiceTaskNode from './nodes/ServiceTaskNode.vue'
import StartNode from './nodes/StartNode.vue'
import SubFlowNode from './nodes/SubFlowNode.vue'

import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'

type PaletteItem = (typeof NODE_PALETTE)[number]
type ContextMenuState = {
  visible: boolean
  kind: 'node' | 'edge'
  id: string
  x: number
  y: number
}

const props = defineProps<{ modelValue: FlowSchemaDto }>()
const emit = defineEmits<{
  'update:modelValue': [value: FlowSchemaDto]
  select: [value: { kind: 'node' | 'edge' | null; id: string | null }]
}>()

const {
  nodes,
  edges,
  setNodes,
  setEdges,
  addNodes,
  addEdges,
  removeNodes,
  removeEdges,
  getSelectedNodes,
  getSelectedEdges,
  project,
  fitView,
  zoomIn,
  zoomOut,
} = useVueFlow()

const canvasRoot = ref<HTMLElement | null>(null)
const flowWrap = ref<HTMLElement | null>(null)
const showGrid = ref(true)
const searchQuery = ref('')
const contextMenu = ref<ContextMenuState>({ visible: false, kind: 'node', id: '', x: 0, y: 0 })
const priorityPanel = ref({ visible: false, nodeId: '', nodeName: '' })
const draggedRouteId = ref('')
let dragKey = ''
let emitTimer: ReturnType<typeof setTimeout> | undefined
let ignoreGraphChanges = false
let lastEmitted = ''

const iconByPaletteKey: Record<string, typeof VideoPlay> = {
  start: VideoPlay,
  approval: User,
  parallelSplit: Share,
  parallelJoin: ConnectionIcon,
  inclusiveSplit: Share,
  inclusiveJoin: ConnectionIcon,
  end: CircleCheck,
  'serviceTask:dataWriteback': Tools,
  'serviceTask:webApi': Link,
  'serviceTask:timer': Timer,
  subFlow: DocumentCopy,
}

function paletteKey(item: PaletteItem): string {
  return 'kind' in item ? `${item.type}:${item.kind}` : item.type
}

function paletteIcon(item: PaletteItem) {
  return iconByPaletteKey[paletteKey(item)] ?? Plus
}

function edgeData(edge: Edge): SchemaEdge {
  const data = (edge.data ?? {}) as Partial<SchemaEdge>
  return {
    id: data.id || edge.id,
    from: edge.source,
    to: edge.target,
    name: data.name,
    condition: data.condition,
    priority: Number.isFinite(data.priority) ? Number(data.priority) : undefined,
    sourceHandle: (edge.sourceHandle ?? data.sourceHandle ?? undefined) as SchemaEdge['sourceHandle'],
    targetHandle: (edge.targetHandle ?? data.targetHandle ?? undefined) as SchemaEdge['targetHandle'],
    ccUsers: data.ccUsers,
    isError: data.isError,
  }
}

function decorateEdge(edge: Edge): Edge {
  const data = edgeData(edge)
  const color = data.isError ? 'var(--cp-danger)' : 'var(--cp-muted)'
  return {
    ...edge,
    type: 'smoothstep',
    data,
    label: data.name || data.condition || undefined,
    markerEnd: { type: MarkerType.ArrowClosed, color },
    style: {
      stroke: color,
      strokeWidth: data.isError ? 2 : 1.6,
      strokeDasharray: data.isError ? '6 4' : undefined,
    },
    class: data.isError ? 'edge-error' : undefined,
  }
}

function loadGraph(schema: FlowSchemaDto, resetHistoryAfter = false) {
  ignoreGraphChanges = true
  const graph = schemaToGraph(schema)
  setNodes(graph.nodes as Node[])
  setEdges(graph.edges.map(edge => decorateEdge(edge as Edge)))
  nextTick(() => {
    ignoreGraphChanges = false
    if (resetHistoryAfter) {
      resetHistory()
      window.setTimeout(fitCanvas, 80)
    }
  })
}

loadGraph(props.modelValue)

function currentSchema(): FlowSchemaDto {
  return graphToSchema(nodes.value as Node[], edges.value as Edge[])
}

function emitSchemaNow() {
  if (ignoreGraphChanges) return
  const value = currentSchema()
  lastEmitted = JSON.stringify(value)
  emit('update:modelValue', value)
}

function scheduleEmit() {
  if (ignoreGraphChanges) return
  if (emitTimer) clearTimeout(emitTimer)
  emitTimer = setTimeout(emitSchemaNow, 120)
}

watch([nodes, edges], scheduleEmit, { deep: true })

watch(
  () => props.modelValue,
  value => {
    const serialized = JSON.stringify(value)
    if (serialized === lastEmitted) {
      lastEmitted = ''
      return
    }
    loadGraph(value, true)
  },
  { deep: true },
)

const history = ref<string[]>([])
const historyIndex = ref(-1)
const canUndo = computed(() => historyIndex.value > 0)
const canRedo = computed(() => historyIndex.value >= 0 && historyIndex.value < history.value.length - 1)

function graphSnapshot(): string {
  return JSON.stringify(currentSchema())
}

function resetHistory() {
  history.value = [graphSnapshot()]
  historyIndex.value = 0
}

function recordHistory() {
  const snapshot = graphSnapshot()
  if (history.value[historyIndex.value] === snapshot) return
  history.value = history.value.slice(0, historyIndex.value + 1)
  history.value.push(snapshot)
  if (history.value.length > 60) history.value.shift()
  historyIndex.value = history.value.length - 1
}

function commitChange() {
  nextTick(() => {
    recordHistory()
    emitSchemaNow()
  })
}

function restoreHistory(index: number) {
  const snapshot = history.value[index]
  if (!snapshot) return
  historyIndex.value = index
  ignoreGraphChanges = true
  const graph = schemaToGraph(JSON.parse(snapshot) as FlowSchemaDto)
  setNodes(graph.nodes as Node[])
  setEdges(graph.edges.map(edge => decorateEdge(edge as Edge)))
  nextTick(() => {
    ignoreGraphChanges = false
    emitSchemaNow()
    emit('select', { kind: null, id: null })
  })
}

function undo() {
  if (canUndo.value) restoreHistory(historyIndex.value - 1)
}

function redo() {
  if (canRedo.value) restoreHistory(historyIndex.value + 1)
}

function nodeName(nodeId: string) {
  const node = nodes.value.find(item => item.id === nodeId)
  return String((node?.data as { name?: string } | undefined)?.name || nodeId)
}

function selectNode(node: Node) {
  emit('select', { kind: 'node', id: node.id })
}

function selectEdge(edge: Edge) {
  emit('select', { kind: 'edge', id: edge.id })
}

function onNodeClick({ node }: NodeMouseEvent) {
  closeContextMenu()
  selectNode(node as Node)
}

function onEdgeClick({ edge }: EdgeMouseEvent) {
  closeContextMenu()
  selectEdge(edge as Edge)
}

function onPaneClick() {
  closeContextMenu()
  emit('select', { kind: null, id: null })
}

function normalizeOutgoing(sourceId: string, preferredFallbackId?: string) {
  const outgoing = edges.value.filter(edge => edge.source === sourceId && !edgeData(edge).isError)
  if (!outgoing.length) return

  const fallbackId = preferredFallbackId && outgoing.some(edge => edge.id === preferredFallbackId)
    ? preferredFallbackId
    : outgoing.find(edge => isFallbackEdge(edgeData(edge)))?.id ?? outgoing.at(-1)!.id
  const conditional = outgoing
    .filter(edge => edge.id !== fallbackId)
    .sort((a, b) => (edgeData(a).priority ?? Number.MAX_SAFE_INTEGER) - (edgeData(b).priority ?? Number.MAX_SAFE_INTEGER))
  const fallback = outgoing.find(edge => edge.id === fallbackId)!
  const priorityById = new Map([...conditional, fallback].map((edge, index) => [edge.id, index + 1]))

  setEdges(edges.value.map(edge => {
    if (edge.source !== sourceId || edgeData(edge).isError) return edge
    const data = edgeData(edge)
    const isFallback = edge.id === fallbackId
    return decorateEdge({
      ...edge,
      data: {
        ...data,
        condition: isFallback ? undefined : (data.condition?.trim() || 'false'),
        priority: priorityById.get(edge.id),
      },
    })
  }) as Edge[])
}

function onConnect(connection: Connection) {
  if (!connection.source || !connection.target || connection.source === connection.target) return
  const duplicate = edges.value.some(edge =>
    edge.source === connection.source
    && edge.target === connection.target
    && edge.sourceHandle === connection.sourceHandle
    && edge.targetHandle === connection.targetHandle,
  )
  if (duplicate) return

  const outgoing = edges.value.filter(edge => edge.source === connection.source && !edgeData(edge).isError)
  const hasFallback = outgoing.some(edge => isFallbackEdge(edgeData(edge)))
  const id = `path-${Date.now().toString(36)}`
  const data: SchemaEdge = {
    id,
    from: connection.source,
    to: connection.target,
    name: hasFallback ? '新条件路径' : '默认路径',
    condition: hasFallback ? 'false' : undefined,
    priority: outgoing.length + 1,
    sourceHandle: (connection.sourceHandle ?? 'bottom') as SchemaEdge['sourceHandle'],
    targetHandle: (connection.targetHandle ?? 'top') as SchemaEdge['targetHandle'],
  }
  const edge = decorateEdge({
    id,
    source: connection.source,
    target: connection.target,
    sourceHandle: connection.sourceHandle ?? 'bottom',
    targetHandle: connection.targetHandle ?? 'top',
    data,
  })
  addEdges([edge])
  normalizeOutgoing(connection.source, hasFallback ? undefined : id)
  selectEdge(edge)
  commitChange()
}

function onEdgeUpdate(event: EdgeUpdateEvent) {
  const { source, target } = event.connection
  if (!source || !target) return
  const previousSource = edges.value.find(edge => edge.id === event.edge.id)?.source
  setEdges(edges.value.map(edge => edge.id === event.edge.id
    ? decorateEdge({
        ...edge,
        source,
        target,
        sourceHandle: event.connection.sourceHandle,
        targetHandle: event.connection.targetHandle,
        data: {
          ...edgeData(edge),
          from: source,
          to: target,
          sourceHandle: (event.connection.sourceHandle ?? undefined) as SchemaEdge['sourceHandle'],
          targetHandle: (event.connection.targetHandle ?? undefined) as SchemaEdge['targetHandle'],
        },
      })
    : edge) as Edge[])
  if (previousSource) normalizeOutgoing(previousSource)
  normalizeOutgoing(source)
  const updated = edges.value.find(edge => edge.id === event.edge.id)
  if (updated) selectEdge(updated)
  commitChange()
}

function newNode(item: PaletteItem, position: { x: number; y: number }): Node {
  const type = item.type
  const serviceKind = 'kind' in item ? item.kind : undefined
  return {
    id: `node-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`,
    type,
    position,
    label: item.label,
    data: { type, name: item.label, ...(serviceKind ? { serviceKind } : {}) },
  }
}

function addPaletteNode(item: PaletteItem, clientPoint?: { x: number; y: number }) {
  const wrap = flowWrap.value
  if (!wrap) return
  const rect = wrap.getBoundingClientRect()
  const point = clientPoint ?? { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
  const position = project({ x: point.x - rect.left, y: point.y - rect.top })
  const node = newNode(item, position)
  addNodes([node])
  selectNode(node)
  commitChange()
}

function onPaletteDragStart(event: DragEvent, item: PaletteItem) {
  dragKey = paletteKey(item)
  event.dataTransfer?.setData('application/cp6-flow-node', dragKey)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
}

function onCanvasDragOver(event: DragEvent) {
  event.preventDefault()
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy'
}

function onCanvasDrop(event: DragEvent) {
  event.preventDefault()
  const key = dragKey || event.dataTransfer?.getData('application/cp6-flow-node') || ''
  const item = NODE_PALETTE.find(entry => paletteKey(entry) === key)
  dragKey = ''
  if (item) addPaletteNode(item, { x: event.clientX, y: event.clientY })
}

function deleteSelected() {
  const selectedNodes: GraphNode[] = getSelectedNodes.value
  const selectedEdges: GraphEdge[] = getSelectedEdges.value
  if (!selectedNodes.length && !selectedEdges.length) return
  if (selectedNodes.length) removeNodes(selectedNodes as Node[], true)
  else removeEdges(selectedEdges as Edge[])
  emit('select', { kind: null, id: null })
  commitChange()
}

function removeNode(nodeId: string) {
  const node = nodes.value.find(item => item.id === nodeId)
  if (!node) return
  removeNodes([node as Node], true)
  emit('select', { kind: null, id: null })
  commitChange()
}

function removeEdgeById(edgeId: string) {
  const edge = edges.value.find(item => item.id === edgeId)
  if (!edge) return
  removeEdges([edge as Edge])
  emit('select', { kind: null, id: null })
  commitChange()
}

function duplicateNode(nodeId: string) {
  const original = nodes.value.find(item => item.id === nodeId)
  if (!original) return
  const copy: Node = {
    ...original,
    id: `node-${Date.now().toString(36)}`,
    position: { x: original.position.x + 36, y: original.position.y + 36 },
    data: { ...(original.data as Record<string, unknown>), name: `${nodeName(nodeId)} 副本` },
  }
  addNodes([copy])
  selectNode(copy)
  commitChange()
}

function autoLayout() {
  if (!nodes.value.length) return
  const start = nodes.value.find(node => node.type === 'start') ?? nodes.value[0]
  if (!start) return
  const adjacency = new Map(nodes.value.map(node => [node.id, [] as string[]]))
  edges.value.filter(edge => !edgeData(edge).isError).forEach(edge => adjacency.get(edge.source)?.push(edge.target))
  const level = new Map<string, number>([[start.id, 0]])
  const queue = [start.id]
  while (queue.length) {
    const current = queue.shift()!
    for (const target of adjacency.get(current) ?? []) {
      if (level.has(target)) continue
      level.set(target, (level.get(current) ?? 0) + 1)
      queue.push(target)
    }
  }
  nodes.value.forEach(node => { if (!level.has(node.id)) level.set(node.id, 0) })
  const rows = new Map<number, Node[]>()
  nodes.value.forEach(node => {
    const row = rows.get(level.get(node.id) ?? 0) ?? []
    row.push(node as Node)
    rows.set(level.get(node.id) ?? 0, row)
  })
  const maxWidth = Math.max(...[...rows.values()].map(row => row.length)) * 240
  setNodes(nodes.value.map(node => {
    const rowIndex = level.get(node.id) ?? 0
    const row = rows.get(rowIndex) ?? []
    const index = row.findIndex(item => item.id === node.id)
    const rowWidth = row.length * 240
    return { ...node, position: { x: (maxWidth - rowWidth) / 2 + index * 240 + 80, y: rowIndex * 145 + 55 } }
  }) as Node[])
  commitChange()
  nextTick(() => fitCanvas())
}

function fitCanvas() {
  void fitView({ padding: 0.18, maxZoom: 1.15, duration: 240 })
}

function onNodeDragStop(_event: NodeDragEvent) {
  commitChange()
}

const searchResults = computed(() => {
  const query = searchQuery.value.trim().toLowerCase()
  if (!query) return [] as GraphNode[]
  return nodes.value.filter(node => {
    const data = node.data as { name?: string; code?: string } | undefined
    return node.id.toLowerCase().includes(query)
      || String(data?.name ?? '').toLowerCase().includes(query)
      || String(data?.code ?? '').toLowerCase().includes(query)
  })
})

function focusNode(nodeId: string) {
  void fitView({ nodes: [nodeId], maxZoom: 1.45, duration: 220 })
  emit('select', { kind: 'node', id: nodeId })
  searchQuery.value = ''
}

function closeContextMenu() {
  contextMenu.value.visible = false
}

function showContextMenu(kind: ContextMenuState['kind'], id: string, event: MouseEvent) {
  event.preventDefault()
  const bounds = canvasRoot.value?.getBoundingClientRect()
  if (!bounds) return
  contextMenu.value = {
    visible: true,
    kind,
    id,
    x: Math.max(6, Math.min(event.clientX - bounds.left, bounds.width - 180)),
    y: Math.max(50, Math.min(event.clientY - bounds.top, bounds.height - 156)),
  }
}

function onNodeContextMenu(event: NodeMouseEvent) {
  if (!(event.event instanceof MouseEvent)) return
  selectNode(event.node as Node)
  showContextMenu('node', event.node.id, event.event)
}

function onCanvasContextMenu(event: MouseEvent) {
  const target = event.target
  if (!(target instanceof Element) || target.closest('.vue-flow__node')) return
  const edgeElement = target.closest<SVGGElement>('.vue-flow__edge[data-id]')
  const edge = edges.value.find(item => item.id === edgeElement?.dataset.id)
  if (!edge) return
  selectEdge(edge as Edge)
  showContextMenu('edge', edge.id, event)
}

function priorityRoutes(nodeId: string) {
  return edges.value
    .filter(edge => edge.source === nodeId && !edgeData(edge).isError)
    .slice()
    .sort((a, b) => {
      const aFallback = isFallbackEdge(edgeData(a))
      const bFallback = isFallbackEdge(edgeData(b))
      if (aFallback !== bFallback) return aFallback ? 1 : -1
      return (edgeData(a).priority ?? Number.MAX_SAFE_INTEGER) - (edgeData(b).priority ?? Number.MAX_SAFE_INTEGER)
    })
}

function openPriorityPanel(nodeId: string) {
  priorityPanel.value = { visible: true, nodeId, nodeName: nodeName(nodeId) }
}

function closePriorityPanel() {
  priorityPanel.value.visible = false
  cleanupRoutePointerDrag()
  draggedRouteId.value = ''
}

function cleanupRoutePointerDrag() {
  window.removeEventListener('pointermove', onRoutePointerMove)
  window.removeEventListener('pointerup', onRoutePointerUp)
  window.removeEventListener('pointercancel', onRoutePointerCancel)
}

function onRoutePointerMove(event: PointerEvent) {
  if (draggedRouteId.value) event.preventDefault()
}

function onRoutePointerUp(event: PointerEvent) {
  const targetElement = document.elementFromPoint(event.clientX, event.clientY)?.closest<HTMLElement>('.priority-route')
  const targetId = targetElement?.dataset.edgeId
  const targetEdge = priorityRoutes(priorityPanel.value.nodeId).find(edge => edge.id === targetId)
  if (targetEdge) onRouteDrop(targetEdge)
  draggedRouteId.value = ''
  cleanupRoutePointerDrag()
}

function onRoutePointerCancel() {
  draggedRouteId.value = ''
  cleanupRoutePointerDrag()
}

function onRoutePointerDragStart(event: PointerEvent, edge: Edge) {
  if (isFallbackEdge(edgeData(edge)) || event.button !== 0) return
  cleanupRoutePointerDrag()
  draggedRouteId.value = edge.id
  window.addEventListener('pointermove', onRoutePointerMove, { passive: false })
  window.addEventListener('pointerup', onRoutePointerUp)
  window.addEventListener('pointercancel', onRoutePointerCancel)
}

function onRouteDrop(targetEdge: Edge) {
  const sourceId = priorityPanel.value.nodeId
  const routeId = draggedRouteId.value
  if (!routeId || routeId === targetEdge.id) return
  const routes = priorityRoutes(sourceId)
  const conditional = routes.filter(edge => !isFallbackEdge(edgeData(edge)))
  const fromIndex = conditional.findIndex(edge => edge.id === routeId)
  const targetOriginalIndex = conditional.findIndex(edge => edge.id === targetEdge.id)
  if (fromIndex < 0) return
  const [moved] = conditional.splice(fromIndex, 1)
  if (!moved) return
  if (isFallbackEdge(edgeData(targetEdge))) conditional.push(moved)
  else {
    const targetIndex = conditional.findIndex(edge => edge.id === targetEdge.id)
    const insertIndex = fromIndex < targetOriginalIndex ? targetIndex + 1 : targetIndex
    conditional.splice(Math.max(0, insertIndex), 0, moved)
  }
  const fallback = routes.find(edge => isFallbackEdge(edgeData(edge)))
  const ordered = fallback ? [...conditional, fallback] : conditional
  const priorityById = new Map(ordered.map((edge, index) => [edge.id, index + 1]))
  setEdges(edges.value.map(edge => edge.source === sourceId && !edgeData(edge).isError
    ? decorateEdge({ ...edge, data: { ...edgeData(edge), priority: priorityById.get(edge.id) } })
    : edge) as Edge[])
  draggedRouteId.value = ''
  commitChange()
}

function setDefaultEdge(edgeId: string) {
  const edge = edges.value.find(item => item.id === edgeId)
  if (!edge || edgeData(edge).isError) return
  setEdges(edges.value.map(item => item.id === edgeId
    ? decorateEdge({ ...item, data: { ...edgeData(item), condition: undefined } })
    : item) as Edge[])
  normalizeOutgoing(edge.source, edge.id)
  selectEdge(edge as Edge)
  commitChange()
}

function runContextAction(action: 'edit' | 'priority' | 'copy' | 'default' | 'delete') {
  const menu = { ...contextMenu.value }
  closeContextMenu()
  if (action === 'edit') return
  if (menu.kind === 'node') {
    if (action === 'priority') openPriorityPanel(menu.id)
    if (action === 'copy') duplicateNode(menu.id)
    if (action === 'delete') removeNode(menu.id)
    return
  }
  if (action === 'default') setDefaultEdge(menu.id)
  if (action === 'delete') removeEdgeById(menu.id)
}

function onKeydown(event: KeyboardEvent) {
  const target = event.target as HTMLElement | null
  if (target && (['INPUT', 'TEXTAREA'].includes(target.tagName) || target.isContentEditable)) return
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
    event.preventDefault()
    event.shiftKey ? redo() : undo()
  } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') {
    event.preventDefault()
    redo()
  } else if (event.key === 'Delete' || event.key === 'Backspace') {
    event.preventDefault()
    deleteSelected()
  }
}

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  resetHistory()
})

onUnmounted(() => {
  document.removeEventListener('keydown', onKeydown)
  cleanupRoutePointerDrag()
  if (emitTimer) clearTimeout(emitTimer)
})
</script>

<template>
  <div ref="canvasRoot" class="designer-canvas-root" @contextmenu="onCanvasContextMenu">
    <div class="canvas-toolbar">
      <div class="tool-group">
        <el-tooltip content="撤销" placement="bottom">
          <el-button text :disabled="!canUndo" aria-label="撤销" @click="undo"><el-icon><RefreshLeft /></el-icon></el-button>
        </el-tooltip>
        <el-tooltip content="重做" placement="bottom">
          <el-button text :disabled="!canRedo" aria-label="重做" @click="redo"><el-icon><RefreshRight /></el-icon></el-button>
        </el-tooltip>
      </div>
      <i />
      <div class="tool-group">
        <el-tooltip content="自动布局" placement="bottom">
          <el-button text aria-label="自动布局" @click="autoLayout"><el-icon><MagicStick /></el-icon></el-button>
        </el-tooltip>
        <el-tooltip :content="showGrid ? '隐藏网格' : '显示网格'" placement="bottom">
          <el-button text :class="{ active: showGrid }" aria-label="切换网格" @click="showGrid = !showGrid"><el-icon><Grid /></el-icon></el-button>
        </el-tooltip>
        <el-tooltip content="缩小" placement="bottom">
          <el-button text aria-label="缩小" @click="zoomOut({ duration: 160 })"><el-icon><ZoomOut /></el-icon></el-button>
        </el-tooltip>
        <el-tooltip content="放大" placement="bottom">
          <el-button text aria-label="放大" @click="zoomIn({ duration: 160 })"><el-icon><ZoomIn /></el-icon></el-button>
        </el-tooltip>
        <el-tooltip content="适应画布" placement="bottom">
          <el-button text aria-label="适应画布" @click="fitCanvas"><el-icon><Aim /></el-icon></el-button>
        </el-tooltip>
      </div>
      <i />
      <el-tooltip content="删除选中" placement="bottom">
        <el-button text class="danger-tool" aria-label="删除选中" @click="deleteSelected"><el-icon><Delete /></el-icon></el-button>
      </el-tooltip>
      <div class="canvas-search">
        <el-input v-model="searchQuery" clearable size="small" placeholder="搜索节点名称或编号" :prefix-icon="Search" />
        <div v-if="searchResults.length" class="search-results">
          <button v-for="node in searchResults" :key="node.id" type="button" @click="focusNode(node.id)">
            <strong>{{ (node.data as Record<string, unknown>)?.name || node.id }}</strong>
            <span>{{ node.id }}</span>
          </button>
        </div>
      </div>
      <span class="graph-count">{{ nodes.length }} 节点 · {{ edges.length }} 路径</span>
    </div>

    <div class="canvas-body">
      <aside class="canvas-palette" aria-label="节点工具箱">
        <strong>节点</strong>
        <el-tooltip
          v-for="item in NODE_PALETTE"
          :key="paletteKey(item)"
          :content="item.label"
          placement="right"
        >
          <button
            type="button"
            draggable="true"
            :data-node-type="paletteKey(item)"
            @click="addPaletteNode(item)"
            @dragstart="onPaletteDragStart($event, item)"
          >
            <el-icon><component :is="paletteIcon(item)" /></el-icon>
            <span>{{ item.label }}</span>
          </button>
        </el-tooltip>
      </aside>

      <div ref="flowWrap" class="canvas-flow-wrap" @dragover="onCanvasDragOver" @drop="onCanvasDrop">
        <VueFlow
          :connection-mode="ConnectionMode.Loose"
          :delete-key-code="null"
          :min-zoom="0.18"
          :max-zoom="2"
          :nodes-connectable="true"
          :edges-updatable="true"
          :connect-on-click="true"
          fit-view-on-init
          @connect="onConnect"
          @edge-click="onEdgeClick"
          @edge-update="onEdgeUpdate"
          @node-click="onNodeClick"
          @node-context-menu="onNodeContextMenu"
          @node-drag-stop="onNodeDragStop"
          @pane-click="onPaneClick"
        >
          <template #node-start="nodeProps"><StartNode v-bind="nodeProps" /></template>
          <template #node-approval="nodeProps"><ApprovalNode v-bind="nodeProps" /></template>
          <template #node-parallelSplit="nodeProps"><GatewayNode v-bind="nodeProps" /></template>
          <template #node-parallelJoin="nodeProps"><GatewayNode v-bind="nodeProps" /></template>
          <template #node-inclusiveSplit="nodeProps"><InclusiveGatewayNode v-bind="nodeProps" /></template>
          <template #node-inclusiveJoin="nodeProps"><InclusiveGatewayNode v-bind="nodeProps" /></template>
          <template #node-end="nodeProps"><EndNode v-bind="nodeProps" /></template>
          <template #node-serviceTask="nodeProps"><ServiceTaskNode v-bind="nodeProps" /></template>
          <template #node-subFlow="nodeProps"><SubFlowNode v-bind="nodeProps" /></template>
          <Background v-if="showGrid" variant="lines" :gap="20" :size="1" pattern-color="#dce7e9" />
        </VueFlow>
        <div class="canvas-help">拖动节点排列 · 从任一连接点拖出路径 · 右击节点管理审核线</div>
      </div>
    </div>

    <div v-if="contextMenu.visible" class="flow-context-menu" :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }" @click.stop>
      <button type="button" @click="runContextAction('edit')"><el-icon><EditPen /></el-icon>{{ contextMenu.kind === 'node' ? '编辑节点' : '编辑路径' }}</button>
      <button v-if="contextMenu.kind === 'node'" type="button" data-testid="route-priority-action" @click="runContextAction('priority')"><el-icon><Rank /></el-icon>审核线优先级</button>
      <button v-if="contextMenu.kind === 'node'" type="button" @click="runContextAction('copy')"><el-icon><DocumentCopy /></el-icon>复制节点</button>
      <button v-else type="button" @click="runContextAction('default')"><el-icon><Flag /></el-icon>设为无条件兜底</button>
      <i />
      <button type="button" class="danger" @click="runContextAction('delete')"><el-icon><Delete /></el-icon>{{ contextMenu.kind === 'node' ? '删除节点' : '删除路径' }}</button>
    </div>

    <div v-if="priorityPanel.visible" class="priority-layer" @mousedown.self="closePriorityPanel">
      <section class="priority-panel" data-testid="route-priority-panel">
        <header>
          <span><el-icon><Rank /></el-icon></span>
          <div><small>节点审核线</small><strong>{{ priorityPanel.nodeName }}</strong></div>
          <button type="button" title="关闭" @click="closePriorityPanel"><el-icon><CloseBold /></el-icon></button>
        </header>
        <div class="priority-heading"><span>顺序</span><strong>流出路径</strong><em>匹配规则</em></div>
        <div v-if="priorityRoutes(priorityPanel.nodeId).length" class="priority-list">
          <div
            v-for="route in priorityRoutes(priorityPanel.nodeId)"
            :key="route.id"
            class="priority-route"
            :class="{ fallback: isFallbackEdge(edgeData(route)), dragging: draggedRouteId === route.id }"
            :data-edge-id="route.id"
          >
            <span class="route-rank">{{ edgeData(route).priority }}</span>
            <span class="route-main"><strong>{{ edgeData(route).name || `${nodeName(route.source)} → ${nodeName(route.target)}` }}</strong><small>{{ nodeName(route.target) }}</small></span>
            <span v-if="isFallbackEdge(edgeData(route))" class="route-rule fallback-rule"><el-icon><Flag /></el-icon>无条件 · 兜底</span>
            <span v-else class="route-rule">{{ edgeData(route).condition || '待配置条件' }}</span>
            <button
              v-if="!isFallbackEdge(edgeData(route))"
              type="button"
              class="route-drag"
              title="拖动调整优先级"
              @pointerdown.stop.prevent="onRoutePointerDragStart($event, route as Edge)"
            ><el-icon><Rank /></el-icon></button>
            <span v-else class="route-drag-spacer" />
          </div>
        </div>
        <div v-else class="priority-empty"><el-icon><ConnectionIcon /></el-icon><strong>暂无流出审核线</strong></div>
        <footer><span>{{ priorityRoutes(priorityPanel.nodeId).length }} 条审核线</span><b><el-icon><Flag /></el-icon>无条件路径始终置底</b></footer>
      </section>
    </div>
  </div>
</template>

<style scoped>
.designer-canvas-root { position: relative; display: flex; flex-direction: column; width: 100%; height: 100%; min-height: 0; overflow: hidden; background: #f7fafb; }
.canvas-toolbar { position: relative; z-index: 4; min-height: 44px; padding: 0 10px; display: flex; align-items: center; gap: 3px; flex-shrink: 0; border-bottom: 1px solid var(--cp-line); background: var(--cp-card); }
.canvas-toolbar > i { width: 1px; height: 20px; margin: 0 5px; background: var(--cp-line); }
.tool-group { display: flex; align-items: center; }
.canvas-toolbar :deep(.el-button) { width: 32px; height: 32px; margin: 0; color: var(--cp-text); }
.canvas-toolbar :deep(.el-button.active) { background: var(--cp-brand-bg); color: var(--cp-brand); }
.canvas-toolbar :deep(.el-button.danger-tool) { color: var(--cp-danger); }
.canvas-search { position: relative; width: min(210px, 24vw); margin-left: 8px; }
.search-results { position: absolute; z-index: 18; top: 36px; left: 0; width: 100%; max-height: 220px; padding: 4px; overflow-y: auto; border: 1px solid var(--cp-line); border-radius: 5px; background: var(--cp-card); box-shadow: var(--cp-shadow-2); }
.search-results button { width: 100%; min-height: 42px; padding: 6px 8px; display: flex; flex-direction: column; align-items: flex-start; border: 0; border-radius: 4px; background: transparent; color: var(--cp-text); cursor: pointer; }
.search-results button:hover { background: var(--cp-bg-hover); }
.search-results strong { max-width: 100%; overflow: hidden; font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.search-results span { color: var(--cp-muted); font-size: 10px; }
.graph-count { margin-left: auto; color: var(--cp-muted); font-size: 11px; white-space: nowrap; }
.canvas-body { display: flex; flex: 1; min-height: 0; overflow: hidden; }
.canvas-palette { width: 76px; padding: 9px 6px; display: flex; flex-direction: column; align-items: stretch; gap: 4px; flex-shrink: 0; overflow-y: auto; border-right: 1px solid var(--cp-line); background: #f3f7f8; }
.canvas-palette > strong { padding: 2px 0 5px; color: var(--cp-muted); font-size: 10px; text-align: center; }
.canvas-palette button { min-height: 50px; padding: 5px 3px; display: grid; place-items: center; align-content: center; gap: 3px; border: 1px solid transparent; border-radius: 5px; background: transparent; color: #536d74; cursor: grab; }
.canvas-palette button:hover { border-color: #c7dadd; background: var(--cp-card); color: var(--cp-brand); }
.canvas-palette button:active { cursor: grabbing; }
.canvas-palette .el-icon { font-size: 17px; }
.canvas-palette span { max-width: 100%; overflow: hidden; font-size: 9px; line-height: 1.15; text-align: center; text-overflow: ellipsis; white-space: nowrap; }
.canvas-flow-wrap { position: relative; flex: 1; min-width: 0; min-height: 0; }
.canvas-flow-wrap :deep(.vue-flow) { width: 100%; height: 100%; }
.canvas-help { position: absolute; right: 12px; bottom: 10px; z-index: 3; padding: 5px 8px; border: 1px solid rgb(199 216 219 / 80%); border-radius: 4px; background: rgb(255 255 255 / 88%); color: #7b8f94; font-size: 9px; pointer-events: none; }
.flow-context-menu { position: absolute; z-index: 20; width: 174px; padding: 5px; border: 1px solid #d4dfe2; border-radius: 6px; background: #fff; box-shadow: 0 12px 30px rgb(38 64 72 / 18%); }
.flow-context-menu button { width: 100%; min-height: 34px; padding: 0 9px; display: flex; align-items: center; gap: 9px; border: 0; border-radius: 4px; background: transparent; color: #405960; font-size: 11px; text-align: left; cursor: pointer; }
.flow-context-menu button:hover { background: #eff5f5; }
.flow-context-menu button.danger { color: #c64f4b; }
.flow-context-menu > i { display: block; margin: 4px 3px; border-top: 1px solid #e4eaec; }
.priority-layer { position: absolute; z-index: 19; inset: 44px 0 0 76px; padding: 20px; display: grid; place-items: center; background: rgb(38 59 65 / 11%); }
.priority-panel { width: min(540px, 100%); max-height: min(570px, calc(100% - 12px)); display: flex; flex-direction: column; overflow: hidden; border: 1px solid #ccd9dc; border-radius: 7px; background: #fff; box-shadow: 0 18px 48px rgb(28 54 62 / 22%); }
.priority-panel > header { min-height: 66px; padding: 11px 13px; display: grid; grid-template-columns: 38px minmax(0, 1fr) 30px; align-items: center; gap: 10px; border-bottom: 1px solid #e0e7e9; }
.priority-panel > header > span { width: 38px; height: 38px; display: grid; place-items: center; border-radius: 6px; background: #fff0dc; color: #b87517; font-size: 18px; }
.priority-panel > header small, .priority-panel > header strong { display: block; }
.priority-panel > header small { margin-bottom: 3px; color: #829399; font-size: 9px; }
.priority-panel > header strong { color: #334d54; font-size: 13px; }
.priority-panel > header button { width: 30px; height: 30px; display: grid; place-items: center; border: 1px solid #dce5e7; border-radius: 5px; background: #fff; color: #71858b; cursor: pointer; }
.priority-heading, .priority-route { display: grid; grid-template-columns: 44px minmax(130px, 1fr) minmax(130px, .9fr) 18px; align-items: center; gap: 9px; }
.priority-heading { min-height: 34px; padding: 0 13px; border-bottom: 1px solid #e6ecee; background: #f7f9fa; color: #7c8e93; font-size: 9px; }
.priority-heading em { font-style: normal; }
.priority-list { min-height: 0; padding: 7px; overflow-y: auto; }
.priority-route { min-height: 58px; margin-bottom: 5px; padding: 7px 9px; border: 1px solid #dbe4e6; border-radius: 5px; background: #fff; }
.priority-route:hover { border-color: #8ab7ba; background: #f7fbfb; }
.priority-route.dragging { opacity: .42; }
.priority-route.fallback { border-color: #cfe2d9; background: #f4faf7; cursor: default; }
.route-rank { width: 28px; height: 28px; display: grid; place-items: center; border-radius: 50%; background: #e8f1f2; color: #497078; font-size: 11px; font-weight: 800; }
.priority-route.fallback .route-rank { background: #dff1e8; color: #277e5f; }
.route-main { min-width: 0; }
.route-main strong, .route-main small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.route-main strong { color: #405a61; font-size: 10px; }
.route-main small { margin-top: 4px; color: #84969b; font-size: 8px; }
.route-rule { min-width: 0; overflow: hidden; color: #a46c1a; font-size: 9px; text-overflow: ellipsis; white-space: nowrap; }
.fallback-rule { display: flex; align-items: center; gap: 4px; color: #2d8063; font-weight: 700; }
.route-drag { width: 20px; height: 30px; padding: 0; display: grid; place-items: center; border: 0; background: transparent; color: #8fa0a4; cursor: grab; touch-action: none; }
.route-drag:active { cursor: grabbing; }
.route-drag-spacer { width: 20px; }
.priority-empty { min-height: 150px; display: grid; place-items: center; align-content: center; gap: 8px; color: #89999e; }
.priority-empty .el-icon { font-size: 24px; }
.priority-empty strong { font-size: 11px; }
.priority-panel > footer { min-height: 46px; padding: 0 14px; display: flex; align-items: center; justify-content: space-between; border-top: 1px solid #e0e7e9; background: #f9fbfb; color: #72868b; font-size: 9px; }
.priority-panel > footer b { display: flex; align-items: center; gap: 5px; color: #2c7e61; }
:deep(.vue-flow__node) { cursor: grab; }
:deep(.vue-flow__handle) { width: 9px; height: 9px; border: 2px solid #fff; background: #657d84; opacity: .72; transition: width .14s, height .14s, background .14s, opacity .14s; }
:deep(.vue-flow__node:hover .vue-flow__handle), :deep(.vue-flow__node.selected .vue-flow__handle) { width: 11px; height: 11px; background: #159da3; opacity: 1; }
:deep(.vue-flow__handle.connecting) { background: #d68a21; }
:deep(.vue-flow__handle.valid) { background: #26966c; }
:deep(.vue-flow__edge.selected .vue-flow__edge-path) { stroke: #d48720; stroke-width: 2.5; }
:deep(.vue-flow__selection) { border-color: #159da3; background: rgb(21 157 163 / 8%); }
@media (max-width: 980px) {
  .canvas-search { width: 150px; }
  .graph-count { display: none; }
  .canvas-help { display: none; }
}
</style>
