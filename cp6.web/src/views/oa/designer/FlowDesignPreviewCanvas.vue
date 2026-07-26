<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { Background } from '@vue-flow/background'
import {
  ConnectionMode,
  Handle,
  MarkerType,
  Position,
  VueFlow,
  useVueFlow,
  type Connection as FlowConnection,
  type Edge,
  type EdgeMouseEvent,
  type EdgeUpdateEvent,
  type Node,
  type NodeDragEvent,
  type NodeMouseEvent,
} from '@vue-flow/core'
import {
  CircleCheck,
  CloseBold,
  Connection as ConnectionIcon,
  Delete,
  DocumentCopy,
  EditPen,
  Flag,
  Money,
  OfficeBuilding,
  Rank,
  Share,
  Stamp,
  SwitchButton,
  Timer,
  Tools,
  User,
  VideoPlay,
} from '@element-plus/icons-vue'

import {
  FLOW_PREVIEW_EDGES,
  FLOW_PREVIEW_NODES,
  type FlowPreviewEdge,
  type FlowPreviewEditableEdge,
  type FlowPreviewNode,
  type FlowPreviewNodeKind,
  type FlowPreviewSelection,
} from './flowDesignConcept'

import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'

type EdgeData = {
  tone: 'default' | 'warning' | 'danger'
  dashed: boolean
  condition: string
  routeType: 'normal' | 'condition' | 'exception'
  isDefault: boolean
  priority: number
  addSignMode: string
  apiMethod: string
  apiPath: string
  apiTimeout: number
  apiRetry: number
  jobCode: string
  jobQueue: string
  jobParameters: string
}

type CanvasStats = { nodes: number; edges: number }
type HistoryState = { canUndo: boolean; canRedo: boolean }
type CanvasPoint = { x: number; y: number }
type ContextMenuState = {
  visible: boolean
  kind: 'node' | 'edge'
  id: string
  x: number
  y: number
}

const props = withDefaults(defineProps<{
  selectedNodeId: string
  selectedEdgeId?: string
  showGrid?: boolean
  showMiniMap?: boolean
  tone?: 'classic' | 'focus' | 'control'
}>(), {
  selectedEdgeId: '',
  showGrid: true,
  showMiniMap: false,
  tone: 'classic',
})

const emit = defineEmits<{
  select: [selection: FlowPreviewSelection]
  stats: [stats: CanvasStats]
  history: [state: HistoryState]
}>()

const iconByKind = {
  start: VideoPlay,
  approval: User,
  gateway: Share,
  finance: Money,
  compliance: Stamp,
  join: ConnectionIcon,
  service: Tools,
  timer: Timer,
  subflow: DocumentCopy,
  end: CircleCheck,
  reject: CloseBold,
} as const

const labelByKind: Record<FlowPreviewNodeKind, string> = {
  start: '开始',
  approval: '审批',
  gateway: '条件分支',
  finance: '财务审批',
  compliance: '合规会签',
  join: '并行汇聚',
  service: '服务任务',
  timer: '定时器',
  subflow: '子流程',
  end: '结束',
  reject: '终止',
}

const nodeDefaults: Record<FlowPreviewNodeKind, Pick<FlowPreviewNode, 'title' | 'subtitle' | 'assignee' | 'sla' | 'status'>> = {
  start: { title: '流程开始', subtitle: '发起业务表单', assignee: '申请人', sla: '即时', status: 'ready' },
  approval: { title: '审批节点', subtitle: '配置审批人及规则', assignee: '待配置', sla: '8 小时', status: 'warning' },
  gateway: { title: '条件分支', subtitle: '按业务条件选择路径', assignee: '系统判断', sla: '即时', status: 'warning' },
  finance: { title: '财务审批', subtitle: '预算与金额检查', assignee: '财务角色', sla: '1 个工作日', status: 'ready' },
  compliance: { title: '合规会签', subtitle: '规则与风险检查', assignee: '合规角色', sla: '1 个工作日', status: 'warning' },
  join: { title: '并行汇聚', subtitle: '等待分支处理完成', assignee: '系统判断', sla: '即时', status: 'ready' },
  service: { title: '接口调用', subtitle: '执行 WebAPI 服务', assignee: '系统服务', sla: '30 秒', status: 'system' },
  timer: { title: '定时等待', subtitle: '按时间继续流程', assignee: '系统定时器', sla: '1 小时', status: 'system' },
  subflow: { title: '调用子流程', subtitle: '复用已发布流程', assignee: '子流程', sla: '即时', status: 'system' },
  end: { title: '流程结束', subtitle: '完成业务处理', assignee: '系统', sla: '即时', status: 'ready' },
  reject: { title: '流程终止', subtitle: '取消或退回申请', assignee: '系统', sla: '即时', status: 'warning' },
}

const handles = [
  { id: 'top', position: Position.Top },
  { id: 'right', position: Position.Right },
  { id: 'bottom', position: Position.Bottom },
  { id: 'left', position: Position.Left },
] as const

function iconFor(kind: FlowPreviewNodeKind) {
  return iconByKind[kind] ?? SwitchButton
}

function labelFor(kind: FlowPreviewNodeKind) {
  return labelByKind[kind] ?? '节点'
}

function edgeColor(tone: EdgeData['tone']) {
  if (tone === 'danger') return '#d95858'
  if (tone === 'warning') return '#d48a22'
  return '#6f8790'
}

function makeEdge(definition: FlowPreviewEdge): Edge {
  const tone = definition.tone ?? 'default'
  const color = edgeColor(tone)
  const data: EdgeData = {
    tone,
    dashed: Boolean(definition.dashed),
    condition: definition.condition ?? '',
    routeType: definition.routeType ?? 'normal',
    isDefault: Boolean(definition.isDefault),
    priority: definition.priority ?? 1,
    addSignMode: '不加签',
    apiMethod: 'POST',
    apiPath: '',
    apiTimeout: 30,
    apiRetry: 3,
    jobCode: '',
    jobQueue: 'default',
    jobParameters: '{}',
  }

  return {
    id: definition.id,
    source: definition.source,
    target: definition.target,
    sourceHandle: definition.sourceHandle,
    targetHandle: definition.targetHandle,
    label: definition.label,
    type: 'smoothstep',
    animated: data.dashed,
    markerEnd: { type: MarkerType.ArrowClosed, color },
    style: {
      stroke: color,
      strokeWidth: tone === 'default' ? 1.45 : 1.8,
      strokeDasharray: data.dashed ? '7 5' : undefined,
    },
    labelStyle: { fill: color, fontSize: 11, fontWeight: 700 },
    labelBgStyle: { fill: '#ffffff', fillOpacity: 0.94 },
    labelBgPadding: [6, 4],
    labelBgBorderRadius: 3,
    data,
  }
}

const nodes = shallowRef<Node[]>(FLOW_PREVIEW_NODES.map(node => ({
  id: node.id,
  type: 'concept',
  position: { x: node.x, y: node.y },
  data: { ...node },
  selected: node.id === props.selectedNodeId,
})))

const edges = shallowRef<Edge[]>(FLOW_PREVIEW_EDGES.map(makeEdge))
const canvasRoot = ref<HTMLDivElement | null>(null)
const contextMenu = ref<ContextMenuState>({ visible: false, kind: 'node', id: '', x: 0, y: 0 })
const priorityPanel = ref({ visible: false, nodeId: '', nodeName: '' })
const draggedRouteId = ref('')
const history = ref<string[]>([])
const historyIndex = ref(-1)
let restoringHistory = false
let fitTimer: ReturnType<typeof setTimeout> | undefined

const {
  fitView,
  project,
  zoomIn,
  zoomOut,
} = useVueFlow()

function edgeData(edge: Edge): EdgeData {
  const data = (edge.data ?? {}) as Partial<EdgeData>
  return {
    tone: data.tone ?? 'default',
    dashed: Boolean(data.dashed),
    condition: data.condition ?? '',
    routeType: data.routeType ?? 'normal',
    isDefault: Boolean(data.isDefault),
    priority: data.priority ?? 1,
    addSignMode: data.addSignMode ?? '不加签',
    apiMethod: data.apiMethod ?? 'POST',
    apiPath: data.apiPath ?? '',
    apiTimeout: data.apiTimeout ?? 30,
    apiRetry: data.apiRetry ?? 3,
    jobCode: data.jobCode ?? '',
    jobQueue: data.jobQueue ?? 'default',
    jobParameters: data.jobParameters ?? '{}',
  }
}

function nodeTitle(id: string) {
  const node = (nodes.value as unknown as Array<{ id: string; data?: { title?: unknown } }>).find(item => item.id === id)
  return String(node?.data?.title ?? id)
}

function toPreviewNode(node: Node): FlowPreviewNode {
  const data = node.data as unknown as FlowPreviewNode
  return {
    ...data,
    id: node.id,
    x: node.position.x,
    y: node.position.y,
  }
}

function toEditableEdge(edge: Edge): FlowPreviewEditableEdge {
  const data = edgeData(edge)
  return {
    id: edge.id,
    source: edge.source,
    target: edge.target,
    sourceHandle: edge.sourceHandle ?? null,
    targetHandle: edge.targetHandle ?? null,
    sourceName: nodeTitle(edge.source),
    targetName: nodeTitle(edge.target),
    label: typeof edge.label === 'string' ? edge.label : '',
    condition: data.condition,
    routeType: data.routeType,
    isDefault: data.isDefault,
    priority: data.priority,
    addSignMode: data.addSignMode,
    apiMethod: data.apiMethod,
    apiPath: data.apiPath,
    apiTimeout: data.apiTimeout,
    apiRetry: data.apiRetry,
    jobCode: data.jobCode,
    jobQueue: data.jobQueue,
    jobParameters: data.jobParameters,
  }
}

function emitStats() {
  emit('stats', { nodes: nodes.value.length, edges: edges.value.length })
}

function emitHistoryState() {
  emit('history', {
    canUndo: historyIndex.value > 0,
    canRedo: historyIndex.value >= 0 && historyIndex.value < history.value.length - 1,
  })
}

function snapshot() {
  const snapshotNodes = nodes.value as unknown as Array<{
    id: string
    type?: string
    position: CanvasPoint
    data?: Record<string, unknown>
  }>
  const snapshotEdges = edges.value as unknown as Array<{
    id: string
    source: string
    target: string
    sourceHandle?: string | null
    targetHandle?: string | null
    label?: unknown
    type?: string
    animated?: boolean
    markerEnd?: unknown
    style?: unknown
    labelStyle?: unknown
    labelBgStyle?: unknown
    labelBgPadding?: [number, number]
    labelBgBorderRadius?: number
    data?: Record<string, unknown>
  }>
  return JSON.stringify({
    nodes: snapshotNodes.map(node => ({
      id: node.id,
      type: node.type,
      position: { ...node.position },
      data: { ...node.data },
    })),
    edges: snapshotEdges.map(edge => ({
      id: edge.id,
      source: edge.source,
      target: edge.target,
      sourceHandle: edge.sourceHandle,
      targetHandle: edge.targetHandle,
      label: edge.label,
      type: edge.type,
      animated: edge.animated,
      markerEnd: edge.markerEnd,
      style: edge.style,
      labelStyle: edge.labelStyle,
      labelBgStyle: edge.labelBgStyle,
      labelBgPadding: edge.labelBgPadding,
      labelBgBorderRadius: edge.labelBgBorderRadius,
      data: { ...edge.data },
    })),
  })
}

function recordHistory() {
  if (restoringHistory) return
  const nextSnapshot = snapshot()
  if (history.value[historyIndex.value] === nextSnapshot) return
  history.value = history.value.slice(0, historyIndex.value + 1)
  history.value.push(nextSnapshot)
  if (history.value.length > 50) history.value.shift()
  historyIndex.value = history.value.length - 1
  emitHistoryState()
}

function commitChange() {
  void nextTick(() => {
    recordHistory()
    emitStats()
  })
}

function restoreSnapshot(value: string) {
  const parsed = JSON.parse(value) as { nodes: Node[]; edges: Edge[] }
  restoringHistory = true
  nodes.value = parsed.nodes
  edges.value = parsed.edges
  void nextTick(() => {
    restoringHistory = false
    syncExternalSelection()
    emitStats()
    emitHistoryState()
  })
}

function undo() {
  if (historyIndex.value <= 0) return
  historyIndex.value -= 1
  restoreSnapshot(history.value[historyIndex.value]!)
}

function redo() {
  if (historyIndex.value >= history.value.length - 1) return
  historyIndex.value += 1
  restoreSnapshot(history.value[historyIndex.value]!)
}

function fit() {
  void fitView({ padding: 0.16, maxZoom: 1.05, duration: 260 })
}

function zoomCanvasIn() {
  void zoomIn({ duration: 160 })
}

function zoomCanvasOut() {
  void zoomOut({ duration: 160 })
}

function selectNode(node: Node) {
  nodes.value = nodes.value.map(item => ({ ...item, selected: item.id === node.id }))
  edges.value = edges.value.map(edge => ({ ...edge, selected: false }))
  emit('select', { kind: 'node', node: toPreviewNode(node) })
}

function selectEdge(edge: Edge) {
  nodes.value = nodes.value.map(node => ({ ...node, selected: false }))
  edges.value = edges.value.map(item => ({ ...item, selected: item.id === edge.id }))
  const current = edges.value.find(item => item.id === edge.id) ?? edge
  emit('select', { kind: 'edge', edge: toEditableEdge(current) })
}

function syncExternalSelection() {
  if (props.selectedEdgeId) {
    nodes.value = nodes.value.map(node => ({ ...node, selected: false }))
    edges.value = edges.value.map(edge => ({ ...edge, selected: edge.id === props.selectedEdgeId }))
    return
  }
  nodes.value = nodes.value.map(node => ({ ...node, selected: node.id === props.selectedNodeId }))
  edges.value = edges.value.map(edge => ({ ...edge, selected: false }))
}

function onNodeClick(event: NodeMouseEvent) {
  closeContextMenu()
  selectNode(event.node as unknown as Node)
}

function onEdgeClick(event: EdgeMouseEvent) {
  closeContextMenu()
  selectEdge(event.edge as unknown as Edge)
}

function normalizeOutgoing(sourceId: string, preferredDefaultId?: string) {
  const outgoing = edges.value.filter(edge => edge.source === sourceId)
  if (!outgoing.length) return

  const fallbackId = preferredDefaultId && outgoing.some(edge => edge.id === preferredDefaultId)
    ? preferredDefaultId
    : outgoing.find(edge => edgeData(edge).isDefault)?.id ?? outgoing.at(-1)!.id
  const ordered = [
    ...outgoing
      .filter(edge => edge.id !== fallbackId)
      .sort((a, b) => edgeData(a).priority - edgeData(b).priority),
    outgoing.find(edge => edge.id === fallbackId)!,
  ]
  const priorityById = new Map(ordered.map((edge, index) => [edge.id, index + 1]))

  edges.value = edges.value.map(edge => {
    if (edge.source !== sourceId) return edge
    const data = edgeData(edge)
    const isDefault = edge.id === fallbackId
    return {
      ...edge,
      data: {
        ...data,
        condition: isDefault ? '' : data.condition,
        routeType: isDefault ? 'normal' : data.routeType,
        isDefault,
        priority: priorityById.get(edge.id) ?? data.priority,
      } satisfies EdgeData,
    }
  })
}

function onConnect(connection: FlowConnection) {
  const duplicate = edges.value.some(edge =>
    edge.source === connection.source
    && edge.target === connection.target
    && edge.sourceHandle === connection.sourceHandle
    && edge.targetHandle === connection.targetHandle,
  )
  if (duplicate) return

  const outgoing = edges.value.filter(edge => edge.source === connection.source)
  const hasFallback = outgoing.some(edge => edgeData(edge).isDefault)
  const edge = makeEdge({
    id: `path-${Date.now().toString(36)}`,
    source: connection.source,
    target: connection.target,
    sourceHandle: (connection.sourceHandle ?? 'bottom') as FlowPreviewEdge['sourceHandle'],
    targetHandle: (connection.targetHandle ?? 'top') as FlowPreviewEdge['targetHandle'],
    label: '新路径',
    routeType: hasFallback ? 'condition' : 'normal',
    condition: hasFallback ? '${condition}' : '',
    isDefault: !hasFallback,
    priority: outgoing.length + 1,
  })
  edges.value = [...edges.value, edge]
  normalizeOutgoing(connection.source, hasFallback ? undefined : edge.id)
  selectEdge(edge)
  commitChange()
}

function onEdgeUpdate(event: EdgeUpdateEvent) {
  const previousSource = edges.value.find(edge => edge.id === event.edge.id)?.source
  edges.value = edges.value.map(edge => edge.id === event.edge.id
    ? {
        ...edge,
        source: event.connection.source,
        target: event.connection.target,
        sourceHandle: event.connection.sourceHandle,
        targetHandle: event.connection.targetHandle,
      }
    : edge)
  if (previousSource) normalizeOutgoing(previousSource)
  normalizeOutgoing(event.connection.source)
  const updated = edges.value.find(edge => edge.id === event.edge.id)
  if (updated) selectEdge(updated)
  commitChange()
}

function updateEdge(id: string, patch: Partial<FlowPreviewEditableEdge>) {
  edges.value = edges.value.map(edge => {
    if (edge.id !== id) return edge
    const currentData = edgeData(edge)
    const nextData: EdgeData = {
      ...currentData,
      condition: patch.condition ?? currentData.condition,
      routeType: patch.routeType ?? currentData.routeType,
      isDefault: patch.isDefault ?? currentData.isDefault,
      priority: patch.priority ?? currentData.priority,
      addSignMode: patch.addSignMode ?? currentData.addSignMode,
      apiMethod: patch.apiMethod ?? currentData.apiMethod,
      apiPath: patch.apiPath ?? currentData.apiPath,
      apiTimeout: patch.apiTimeout ?? currentData.apiTimeout,
      apiRetry: patch.apiRetry ?? currentData.apiRetry,
      jobCode: patch.jobCode ?? currentData.jobCode,
      jobQueue: patch.jobQueue ?? currentData.jobQueue,
      jobParameters: patch.jobParameters ?? currentData.jobParameters,
    }
    return {
      ...edge,
      label: patch.label ?? edge.label,
      data: nextData,
    }
  })
  const changed = edges.value.find(edge => edge.id === id)
  if (changed) normalizeOutgoing(changed.source, patch.isDefault ? id : undefined)
  const updated = edges.value.find(edge => edge.id === id)
  if (updated) selectEdge(updated)
  commitChange()
}

function removeEdge(id: string) {
  const sourceId = edges.value.find(edge => edge.id === id)?.source
  edges.value = edges.value.filter(edge => edge.id !== id)
  if (sourceId) normalizeOutgoing(sourceId)
  emit('select', { kind: 'none' })
  commitChange()
}

function removeNode(id: string) {
  nodes.value = nodes.value.filter(node => node.id !== id)
  edges.value = edges.value.filter(edge => edge.source !== id && edge.target !== id)
  emit('select', { kind: 'none' })
  commitChange()
}

function deleteSelection() {
  if (props.selectedEdgeId) {
    removeEdge(props.selectedEdgeId)
    return
  }
  if (props.selectedNodeId) removeNode(props.selectedNodeId)
}

function createNode(kind: FlowPreviewNodeKind, position: CanvasPoint): Node {
  const index = nodes.value.length + 1
  const defaults = nodeDefaults[kind]
  const id = `${kind}-${Date.now().toString(36)}`
  const data: FlowPreviewNode = {
    id,
    kind,
    code: String(index).padStart(2, '0'),
    ...defaults,
    x: position.x,
    y: position.y,
  }
  return {
    id,
    type: 'concept',
    position,
    data,
  }
}

function addNode(kind: FlowPreviewNodeKind, clientPoint?: CanvasPoint) {
  const root = canvasRoot.value
  let localPoint = { x: 460, y: 320 }
  if (root && clientPoint) {
    const bounds = root.getBoundingClientRect()
    localPoint = { x: clientPoint.x - bounds.left, y: clientPoint.y - bounds.top }
  } else if (root) {
    localPoint = { x: root.clientWidth / 2, y: root.clientHeight / 2 }
  }
  const flowPoint = project(localPoint)
  const node = createNode(kind, { x: flowPoint.x - 92, y: flowPoint.y - 35 })
  nodes.value = [...nodes.value, node]
  selectNode(node)
  commitChange()
}

function duplicateNode(id: string) {
  const original = nodes.value.find(node => node.id === id)
  if (!original) return
  const data = toPreviewNode(original)
  const copyId = `${data.kind}-${Date.now().toString(36)}`
  const copy: Node = {
    id: copyId,
    type: 'concept',
    position: { x: original.position.x + 34, y: original.position.y + 34 },
    data: {
      ...data,
      id: copyId,
      code: `${data.code}C`,
      title: `${data.title} 副本`,
    },
  }
  nodes.value = [...nodes.value, copy]
  selectNode(copy)
  commitChange()
}

function autoLayout() {
  const sorted = [...nodes.value].sort((a, b) => a.position.y - b.position.y || a.position.x - b.position.x)
  const rows: Node[][] = []
  for (const node of sorted) {
    const row = rows.at(-1)
    const rowY = row?.reduce((sum, item) => sum + item.position.y, 0) ?? 0
    const averageY = row?.length ? rowY / row.length : 0
    if (!row || Math.abs(node.position.y - averageY) > 95) rows.push([node])
    else row.push(node)
  }

  const positions = new Map<string, CanvasPoint>()
  rows.forEach((row, rowIndex) => {
    const width = (row.length - 1) * 280
    row.forEach((node, columnIndex) => {
      positions.set(node.id, { x: 460 - width / 2 + columnIndex * 280, y: 34 + rowIndex * 150 })
    })
  })
  nodes.value = nodes.value.map(node => ({ ...node, position: positions.get(node.id) ?? node.position }))
  commitChange()
  void nextTick(() => fit())
}

function onNodeDragStop(_event: NodeDragEvent) {
  commitChange()
}

function onCanvasDrop(event: DragEvent) {
  event.preventDefault()
  const kind = event.dataTransfer?.getData('application/cp6-flow-node') as FlowPreviewNodeKind
  if (!kind || !nodeDefaults[kind]) return
  addNode(kind, { x: event.clientX, y: event.clientY })
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
    x: Math.min(event.clientX - bounds.left, bounds.width - 174),
    y: Math.min(event.clientY - bounds.top, bounds.height - 146),
  }
}

function onNodeContextMenu(event: NodeMouseEvent) {
  if (!(event.event instanceof MouseEvent)) return
  const node = event.node as unknown as Node
  selectNode(node)
  showContextMenu('node', node.id, event.event)
}

function onCanvasContextMenu(event: MouseEvent) {
  const target = event.target
  if (!(target instanceof Element) || target.closest('.vue-flow__node')) return
  const edgeElement = target.closest<SVGGElement>('.vue-flow__edge[data-id]')
  const edgeId = edgeElement?.dataset.id
  const edge = edges.value.find(item => item.id === edgeId)
  if (!edge) return
  event.preventDefault()
  selectEdge(edge)
  showContextMenu('edge', edge.id, event)
}

function priorityRoutes(nodeId: string) {
  return edges.value
    .filter(edge => edge.source === nodeId)
    .sort((a, b) => {
      const aData = edgeData(a)
      const bData = edgeData(b)
      if (aData.isDefault !== bData.isDefault) return aData.isDefault ? 1 : -1
      return aData.priority - bData.priority
    })
}

function routeLabel(edge: Edge) {
  return typeof edge.label === 'string' && edge.label ? edge.label : `${nodeTitle(edge.source)} → ${nodeTitle(edge.target)}`
}

function openPriorityPanel(nodeId: string) {
  priorityPanel.value = {
    visible: true,
    nodeId,
    nodeName: nodeTitle(nodeId),
  }
}

function closePriorityPanel() {
  priorityPanel.value.visible = false
  draggedRouteId.value = ''
}

function onRouteDragStart(event: DragEvent, edge: Edge) {
  if (edgeData(edge).isDefault) {
    event.preventDefault()
    return
  }
  draggedRouteId.value = edge.id
  event.dataTransfer?.setData('application/cp6-flow-route', edge.id)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move'
}

function onRouteDrop(targetEdge: Edge) {
  const sourceId = priorityPanel.value.nodeId
  const routeId = draggedRouteId.value
  if (!routeId || routeId === targetEdge.id) return

  const routes = priorityRoutes(sourceId)
  const conditionalRoutes = routes.filter(edge => !edgeData(edge).isDefault)
  const fromIndex = conditionalRoutes.findIndex(edge => edge.id === routeId)
  const targetOriginalIndex = conditionalRoutes.findIndex(edge => edge.id === targetEdge.id)
  if (fromIndex < 0) return
  const [moved] = conditionalRoutes.splice(fromIndex, 1)
  if (!moved) return

  if (edgeData(targetEdge).isDefault) {
    conditionalRoutes.push(moved)
  } else {
    const targetIndex = conditionalRoutes.findIndex(edge => edge.id === targetEdge.id)
    const insertIndex = fromIndex < targetOriginalIndex ? targetIndex + 1 : targetIndex
    conditionalRoutes.splice(Math.max(0, insertIndex), 0, moved)
  }

  const fallback = routes.find(edge => edgeData(edge).isDefault)
  const ordered = fallback ? [...conditionalRoutes, fallback] : conditionalRoutes
  const priorityById = new Map(ordered.map((edge, index) => [edge.id, index + 1]))
  edges.value = edges.value.map(edge => edge.source === sourceId
    ? { ...edge, data: { ...edgeData(edge), priority: priorityById.get(edge.id) ?? edgeData(edge).priority } satisfies EdgeData }
    : edge)
  draggedRouteId.value = ''
  const selected = edges.value.find(edge => edge.id === props.selectedEdgeId)
  if (selected) selectEdge(selected)
  commitChange()
}

function runContextAction(action: 'edit' | 'priority' | 'copy' | 'default' | 'delete') {
  const menu = contextMenu.value
  closeContextMenu()
  if (action === 'edit') return
  if (menu.kind === 'node') {
    if (action === 'priority') openPriorityPanel(menu.id)
    if (action === 'copy') duplicateNode(menu.id)
    if (action === 'delete') removeNode(menu.id)
    return
  }
  if (action === 'default') updateEdge(menu.id, { isDefault: true })
  if (action === 'delete') removeEdge(menu.id)
}

function onKeydown(event: KeyboardEvent) {
  const target = event.target as HTMLElement
  if (['INPUT', 'TEXTAREA'].includes(target.tagName) || target.isContentEditable) return
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
    event.preventDefault()
    event.shiftKey ? redo() : undo()
    return
  }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') {
    event.preventDefault()
    redo()
    return
  }
  if (event.key === 'Delete' || event.key === 'Backspace') {
    event.preventDefault()
    deleteSelection()
  }
  if (event.key === 'Escape') {
    closeContextMenu()
    closePriorityPanel()
  }
}

watch(
  [() => props.selectedNodeId, () => props.selectedEdgeId],
  syncExternalSelection,
)

onMounted(() => {
  recordHistory()
  emitStats()
  void nextTick(() => {
    fitTimer = setTimeout(fit, 80)
  })
})

onBeforeUnmount(() => {
  if (fitTimer) clearTimeout(fitTimer)
})

defineExpose({
  addNode,
  autoLayout,
  deleteSelection,
  fit,
  redo,
  undo,
  updateEdge,
  zoomIn: zoomCanvasIn,
  zoomOut: zoomCanvasOut,
})
</script>

<template>
  <div
    ref="canvasRoot"
    class="flow-preview-canvas"
    :class="`tone-${tone}`"
    tabindex="0"
    @click.self="closeContextMenu"
    @contextmenu="onCanvasContextMenu"
    @dragover.prevent
    @drop="onCanvasDrop"
    @keydown="onKeydown"
  >
    <VueFlow
      v-model:nodes="nodes"
      v-model:edges="edges"
      class="concept-vue-flow"
      :connection-mode="ConnectionMode.Loose"
      :delete-key-code="null"
      :min-zoom="0.2"
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
      @pane-click="closeContextMenu"
    >
      <template #node-concept="{ data, selected }">
        <div
          class="concept-node"
          :class="[
            `kind-${data.kind}`,
            { selected, warning: data.status === 'warning' },
          ]"
        >
          <Handle
            v-for="handle in handles"
            :id="handle.id"
            :key="handle.id"
            type="source"
            :position="handle.position"
            :class="`anchor-${handle.id}`"
          />
          <div class="node-accent" />
          <span class="node-icon">
            <el-icon><component :is="iconFor(data.kind)" /></el-icon>
          </span>
          <span class="node-copy">
            <small>{{ data.code }} · {{ labelFor(data.kind) }}</small>
            <strong>{{ data.title }}</strong>
            <em>{{ data.assignee }} · {{ data.sla }}</em>
          </span>
          <i class="node-state" />
        </div>
      </template>

      <Background
        v-if="showGrid"
        variant="lines"
        :gap="20"
        :size="1"
        pattern-color="#dce7e9"
      />
    </VueFlow>

    <div
      v-if="contextMenu.visible"
      class="flow-context-menu"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      @click.stop
    >
      <button type="button" @click="runContextAction('edit')">
        <el-icon><EditPen /></el-icon>{{ contextMenu.kind === 'node' ? '编辑节点' : '编辑路径' }}
      </button>
      <button v-if="contextMenu.kind === 'node'" type="button" data-testid="route-priority-action" @click="runContextAction('priority')">
        <el-icon><Rank /></el-icon>审核线优先级
      </button>
      <button v-if="contextMenu.kind === 'node'" type="button" @click="runContextAction('copy')">
        <el-icon><DocumentCopy /></el-icon>复制节点
      </button>
      <button v-else type="button" @click="runContextAction('default')">
        <el-icon><Flag /></el-icon>设为默认路径
      </button>
      <i />
      <button type="button" class="danger" @click="runContextAction('delete')">
        <el-icon><Delete /></el-icon>{{ contextMenu.kind === 'node' ? '删除节点' : '删除路径' }}
      </button>
    </div>

    <div v-if="priorityPanel.visible" class="priority-layer" @mousedown.self="closePriorityPanel">
      <section class="priority-panel" data-testid="route-priority-panel">
        <header>
          <span><el-icon><Rank /></el-icon></span>
          <div><small>节点审核线</small><strong>{{ priorityPanel.nodeName }}</strong></div>
          <button type="button" title="关闭" @click="closePriorityPanel"><el-icon><CloseBold /></el-icon></button>
        </header>
        <div class="priority-heading">
          <span>优先级</span><strong>流出路径</strong><em>匹配规则</em>
        </div>
        <div v-if="priorityRoutes(priorityPanel.nodeId).length" class="priority-list">
          <div
            v-for="route in priorityRoutes(priorityPanel.nodeId)"
            :key="route.id"
            class="priority-route"
            :class="{ fallback: edgeData(route).isDefault, dragging: draggedRouteId === route.id }"
            :draggable="!edgeData(route).isDefault"
            :data-edge-id="route.id"
            @dragstart="onRouteDragStart($event, route)"
            @dragend="draggedRouteId = ''"
            @dragover.prevent
            @drop.prevent="onRouteDrop(route)"
          >
            <span class="route-rank">{{ edgeData(route).priority }}</span>
            <span class="route-main">
              <strong>{{ routeLabel(route) }}</strong>
              <small>{{ nodeTitle(route.target) }}</small>
            </span>
            <span v-if="edgeData(route).isDefault" class="route-rule fallback-rule"><el-icon><Flag /></el-icon>无条件 · 兜底</span>
            <span v-else class="route-rule">{{ edgeData(route).condition || (edgeData(route).routeType === 'exception' ? '异常触发' : '待配置条件') }}</span>
            <el-icon class="route-drag"><Rank /></el-icon>
          </div>
        </div>
        <div v-else class="priority-empty"><el-icon><ConnectionIcon /></el-icon><strong>暂无流出审核线</strong></div>
        <footer><span>{{ priorityRoutes(priorityPanel.nodeId).length }} 条审核线</span><b><el-icon><Flag /></el-icon>末位无条件兜底</b></footer>
      </section>
    </div>

    <div v-if="showMiniMap" class="concept-minimap" aria-label="流程缩略图">
      <span class="mini-line vertical" />
      <span class="mini-node n1" />
      <span class="mini-node n2" />
      <span class="mini-node n3 amber" />
      <span class="mini-node n4" />
      <span class="mini-node n5" />
      <span class="mini-node n6" />
      <span class="mini-node n7 teal" />
      <i />
    </div>
  </div>
</template>

<style scoped>
.flow-preview-canvas {
  position: relative;
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  outline: none;
  background: #f8fbfb;
}

.tone-focus { background: #f5f8f9; }
.tone-control { background: #f7fafa; }

.concept-vue-flow {
  width: 100%;
  height: 100%;
}

.concept-node {
  position: relative;
  width: 184px;
  min-height: 70px;
  padding: 10px 13px 10px 12px;
  display: grid;
  grid-template-columns: 34px minmax(0, 1fr) 7px;
  align-items: center;
  gap: 10px;
  border: 1px solid #b9c9cd;
  border-radius: 6px;
  background: #fff;
  color: #324b53;
  box-shadow: 0 3px 10px rgb(40 69 77 / 7%);
  cursor: grab;
  transition: border-color .16s, box-shadow .16s, transform .16s;
}

.concept-node:hover {
  border-color: #6caeb2;
  box-shadow: 0 7px 18px rgb(40 69 77 / 12%);
}

.concept-node.selected {
  border-color: #129da3;
  box-shadow: 0 0 0 3px rgb(18 157 163 / 14%), 0 8px 20px rgb(40 69 77 / 13%);
}

.node-accent {
  position: absolute;
  top: -1px;
  bottom: -1px;
  left: -1px;
  width: 4px;
  border-radius: 6px 0 0 6px;
  background: #4c83d6;
}

.node-icon {
  width: 34px;
  height: 34px;
  display: grid;
  place-items: center;
  border-radius: 5px;
  background: #eaf1fb;
  color: #3c73c2;
  font-size: 17px;
}

.node-copy { min-width: 0; }

.node-copy small,
.node-copy strong,
.node-copy em {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.node-copy small {
  margin-bottom: 3px;
  color: #829398;
  font-size: 9px;
  font-weight: 700;
}

.node-copy strong {
  font-size: 12px;
  line-height: 1.25;
}

.node-copy em {
  margin-top: 4px;
  color: #7b8e93;
  font-size: 9px;
  font-style: normal;
}

.node-state {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #26a877;
}

.concept-node.warning .node-state { background: #df972b; }

.kind-start,
.kind-end,
.kind-reject { width: 164px; }

.kind-start .node-accent,
.kind-end .node-accent { background: #2ca574; }
.kind-start .node-icon,
.kind-end .node-icon { background: #e4f5ed; color: #218860; }

.kind-gateway .node-accent,
.kind-join .node-accent { background: #d38b22; }
.kind-gateway .node-icon,
.kind-join .node-icon { background: #fff2dc; color: #b87516; }

.kind-finance .node-accent { background: #7b62c7; }
.kind-finance .node-icon { background: #f0ebfb; color: #6f55bd; }

.kind-compliance .node-accent { background: #d06d4f; }
.kind-compliance .node-icon { background: #fbece7; color: #bb593c; }

.kind-service .node-accent,
.kind-timer .node-accent { background: #159aa0; }
.kind-service .node-icon,
.kind-timer .node-icon { background: #e1f3f3; color: #10868b; }

.kind-subflow .node-accent { background: #6478bf; }
.kind-subflow .node-icon { background: #eaedf8; color: #586daf; }

.kind-reject { border-color: #e1b2ae; }
.kind-reject .node-accent { background: #d65b55; }
.kind-reject .node-icon { background: #fbe9e8; color: #c54d48; }
.kind-reject .node-state { background: #d65b55; }

.flow-context-menu {
  position: absolute;
  z-index: 12;
  width: 166px;
  padding: 5px;
  border: 1px solid #d4dfe2;
  border-radius: 6px;
  background: #fff;
  box-shadow: 0 12px 30px rgb(38 64 72 / 18%);
}

.flow-context-menu button {
  width: 100%;
  min-height: 34px;
  padding: 0 9px;
  display: flex;
  align-items: center;
  gap: 9px;
  border: 0;
  border-radius: 4px;
  background: transparent;
  color: #405960;
  font-size: 11px;
  text-align: left;
  cursor: pointer;
}

.flow-context-menu button:hover { background: #eff5f5; }
.flow-context-menu button.danger { color: #c64f4b; }
.flow-context-menu > i { display: block; margin: 4px 3px; border-top: 1px solid #e4eaec; }

.priority-layer {
  position: absolute;
  z-index: 11;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 20px;
  background: rgb(38 59 65 / 10%);
}

.priority-panel {
  width: min(520px, 100%);
  max-height: min(560px, calc(100% - 20px));
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid #ccd9dc;
  border-radius: 7px;
  background: #fff;
  box-shadow: 0 18px 48px rgb(28 54 62 / 22%);
}

.priority-panel > header {
  min-height: 66px;
  padding: 11px 13px;
  display: grid;
  grid-template-columns: 38px minmax(0, 1fr) 30px;
  align-items: center;
  gap: 10px;
  border-bottom: 1px solid #e0e7e9;
}

.priority-panel > header > span {
  width: 38px;
  height: 38px;
  display: grid;
  place-items: center;
  border-radius: 6px;
  background: #fff0dc;
  color: #b87517;
  font-size: 18px;
}

.priority-panel > header small,
.priority-panel > header strong { display: block; }
.priority-panel > header small { margin-bottom: 3px; color: #829399; font-size: 9px; }
.priority-panel > header strong { color: #334d54; font-size: 13px; }
.priority-panel > header button {
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  border: 1px solid #dce5e7;
  border-radius: 5px;
  background: #fff;
  color: #71858b;
  cursor: pointer;
}

.priority-heading {
  min-height: 34px;
  padding: 0 13px;
  display: grid;
  grid-template-columns: 46px minmax(130px, 1fr) minmax(130px, .9fr) 18px;
  align-items: center;
  gap: 9px;
  border-bottom: 1px solid #e6ecee;
  background: #f7f9fa;
  color: #7c8e93;
  font-size: 9px;
  font-style: normal;
}
.priority-heading em { font-style: normal; }

.priority-list { min-height: 0; padding: 7px; overflow-y: auto; }
.priority-route {
  min-height: 58px;
  margin-bottom: 5px;
  padding: 7px 9px;
  display: grid;
  grid-template-columns: 32px minmax(130px, 1fr) minmax(130px, .9fr) 18px;
  align-items: center;
  gap: 9px;
  border: 1px solid #dbe4e6;
  border-radius: 5px;
  background: #fff;
  cursor: grab;
  transition: border-color .14s, background .14s, opacity .14s;
}
.priority-route:hover { border-color: #8ab7ba; background: #f7fbfb; }
.priority-route.dragging { opacity: .42; }
.priority-route.fallback { border-color: #cfe2d9; background: #f4faf7; cursor: default; }
.route-rank {
  width: 28px;
  height: 28px;
  display: grid;
  place-items: center;
  border-radius: 50%;
  background: #e8f1f2;
  color: #497078;
  font-size: 11px;
  font-weight: 800;
}
.priority-route.fallback .route-rank { background: #dff1e8; color: #277e5f; }
.route-main { min-width: 0; }
.route-main strong,
.route-main small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.route-main strong { color: #405a61; font-size: 10px; }
.route-main small { margin-top: 4px; color: #84969b; font-size: 8px; }
.route-rule {
  min-width: 0;
  overflow: hidden;
  color: #a46c1a;
  font-size: 9px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.fallback-rule { display: flex; align-items: center; gap: 4px; color: #2d8063; font-weight: 700; }
.route-drag { color: #8fa0a4; }
.priority-route.fallback .route-drag { visibility: hidden; }

.priority-empty {
  min-height: 150px;
  display: grid;
  place-items: center;
  align-content: center;
  gap: 8px;
  color: #89999e;
}
.priority-empty .el-icon { font-size: 24px; }
.priority-empty strong { font-size: 11px; }

.priority-panel > footer {
  min-height: 46px;
  padding: 0 14px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-top: 1px solid #e0e7e9;
  background: #f9fbfb;
  color: #72868b;
  font-size: 9px;
}
.priority-panel > footer b { display: flex; align-items: center; gap: 5px; color: #2c7e61; }

.concept-minimap {
  position: absolute;
  right: 14px;
  bottom: 14px;
  width: 126px;
  height: 88px;
  border: 1px solid #cbd8db;
  border-radius: 5px;
  background: rgb(255 255 255 / 94%);
  box-shadow: 0 5px 18px rgb(35 66 74 / 10%);
  pointer-events: none;
}

.concept-minimap > i {
  position: absolute;
  inset: 8px 18px;
  border: 1px solid rgb(18 157 163 / 45%);
  background: rgb(18 157 163 / 5%);
}

.mini-line { position: absolute; background: #b7c5c8; }
.mini-line.vertical { top: 12px; bottom: 12px; left: 62px; width: 1px; }

.mini-node {
  position: absolute;
  z-index: 1;
  width: 18px;
  height: 7px;
  border: 1px solid #7f9fcf;
  border-radius: 2px;
  background: #e8eef8;
}

.mini-node.n1 { top: 10px; left: 53px; }
.mini-node.n2 { top: 22px; left: 53px; }
.mini-node.n3 { top: 34px; left: 53px; }
.mini-node.n4 { top: 48px; left: 21px; }
.mini-node.n5 { top: 48px; left: 53px; }
.mini-node.n6 { top: 48px; left: 85px; }
.mini-node.n7 { top: 67px; left: 53px; }
.mini-node.amber { border-color: #d29a48; background: #fff0d8; }
.mini-node.teal { border-color: #4aaeb1; background: #e2f3f3; }

:deep(.vue-flow__node-concept) {
  width: auto;
  padding: 0;
  border: 0;
  background: transparent;
}

:deep(.vue-flow__handle) {
  width: 9px;
  height: 9px;
  border: 2px solid #fff;
  background: #657d84;
  opacity: .72;
  transition: width .14s, height .14s, background .14s, opacity .14s;
}

:deep(.concept-node:hover .vue-flow__handle),
:deep(.concept-node.selected .vue-flow__handle) {
  width: 11px;
  height: 11px;
  background: #159da3;
  opacity: 1;
}

:deep(.vue-flow__handle.connecting) { background: #d68a21; }
:deep(.vue-flow__handle.valid) { background: #26966c; }

:deep(.vue-flow__edge-text) { letter-spacing: 0; }

:deep(.vue-flow__edge.selected .vue-flow__edge-path) {
  stroke: #d48720;
  stroke-width: 2.5;
}

:deep(.vue-flow__edge.selected .vue-flow__edge-text) { fill: #b96f0f; }

:deep(.vue-flow__selection) {
  border-color: #159da3;
  background: rgb(21 157 163 / 8%);
}
</style>
